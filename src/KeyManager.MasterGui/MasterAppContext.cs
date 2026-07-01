using KeyManager.App; // Loc + reused forms (linked)
using KeyManager.Core;

namespace KeyManager.MasterGui;

/// <summary>
/// 비상주 마스터 GUI 오케스트레이터(설계 §8). 트레이 없음.
/// 흐름: 접속 설정 → 서버 pull → (금고 있으면)unlock / (없으면)create+push → MainForm 편집 →
/// 편집마다 디바운스 후 서버 push → 창 X = 프로세스 종료(임시 금고 파일 정리).
/// </summary>
internal sealed class MasterAppContext : ApplicationContext
{
    private const int PushDebounceMs = 300;

    private readonly MasterSettings _settings;
    private readonly string _tempVaultPath;
    private readonly System.Windows.Forms.Timer _debounce;

    private MasterServerConnection? _conn;
    private VaultStore? _store;
    private MainForm? _main;
    private bool _pushPending;
    private bool _rebuilding;       // 언어 변경 재구성 중(창 닫힘을 종료로 취급하지 않음)

    public bool Initialized { get; private set; }

    public MasterAppContext()
    {
        _settings = MasterSettings.Load();
        _tempVaultPath = Path.Combine(Path.GetTempPath(), $"km-mastergui-{Guid.NewGuid():N}.vault.json");

        _debounce = new System.Windows.Forms.Timer { Interval = PushDebounceMs };
        _debounce.Tick += (_, _) => { _debounce.Stop(); FlushPush(); };

        // 1) 언어.
        Loc.Init(Loc.Parse(_settings.Language));

        // 2) 접속 설정(최초 실행 또는 정보 미비면 프롬프트).
        if (!EnsureConnectionSettings()) { Initialized = false; return; }

        // 3) 서버 pull → unlock/create.
        if (!ConnectAndLoad()) { Initialized = false; CleanupTemp(); return; }

        // 4) MainForm 표시.
        ShowMain();
        Initialized = true;
    }

    // ---- 접속 설정 ----

    private bool EnsureConnectionSettings()
    {
        if (_settings.IsConfigured) return true;

        using var f = new ConnectionSetupForm(_settings.Host, _settings.Port, _settings.AdminTokenBase64);
        if (f.ShowDialog() != DialogResult.OK) return false;

        _settings.Host = f.Host;
        _settings.Port = f.Port;
        _settings.AdminTokenBase64 = f.TokenBase64;
        _settings.Save();
        return true;
    }

    // ---- pull → unlock / create ----

    private bool ConnectAndLoad()
    {
        byte[] token;
        try { token = Convert.FromBase64String(_settings.AdminTokenBase64); }
        catch (FormatException)
        {
            MessageBox.Show(Loc.T("mg.setup.errToken"), Loc.T("mg.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _conn = new MasterServerConnection(_settings.Host, _settings.Port, token, _settings.PinFilePath);

        // 접속 진행 창: 먼저 "접속 중"을 띄우고 pull을 비동기로 수행. 실패하면 창이
        // "서버 접속 불가"로 바뀌고 닫기 버튼을 노출한다(UI를 블록하지 않음).
        using var cf = new ConnectingForm(_conn, _settings.Host, _settings.Port);
        cf.ShowDialog();
        if (!cf.Succeeded) return false;   // 사용자가 접속 불가 창을 닫음

        if (cf.VaultJson is not null)
            return LoadExisting(cf.VaultJson);
        return CreateFirstTime();
    }

    /// <summary>서버에 금고가 있음: 임시 파일에 기록 → Load → unlock 반복.</summary>
    private bool LoadExisting(string vaultJson)
    {
        try { File.WriteAllText(_tempVaultPath, vaultJson); }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("err.fmt", ex.Message), Loc.T("mg.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _store = VaultStore.Load(_tempVaultPath, TimeSpan.Zero);
        if (!PromptUnlock()) return false;

        // unlock 시점의 StateChanged 이후에 구독하므로 pull 직후 중복 push 없음.
        _store.StateChanged += OnStoreStateChanged;
        return true;
    }

    /// <summary>
    /// 서버가 비어 있음(첫 설정). 기존 1단계 로컬 금고(vault.json)가 있으면 가져오기를 제안하고,
    /// 아니면 새 마스터 암호로 빈 금고를 생성한다. 두 경우 모두 즉시 서버로 push.
    /// </summary>
    private bool CreateFirstTime()
    {
        // 기존 데이터 마이그레이션: 1단계 Named Pipe 앱의 로컬 금고가 있으면 서버로 가져오기 제안.
        string legacyPath = LegacyVaultPath();
        if (File.Exists(legacyPath))
        {
            var choice = MessageBox.Show(Loc.T("mg.importPrompt", legacyPath), Loc.T("mg.title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice == DialogResult.Yes)
                return ImportLegacy(legacyPath);
        }

        MessageBox.Show(Loc.T("mg.firstSetup"), Loc.T("mg.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);

        using var f = new MasterPasswordForm(create: true);
        if (f.ShowDialog() != DialogResult.OK) return false;

        _store = VaultStore.CreateNew(_tempVaultPath, f.Password, TimeSpan.Zero);
        if (!_store.Unlock(f.Password)) return false;

        _store.StateChanged += OnStoreStateChanged;

        // 서버가 금고를 갖도록 즉시 push(빈 금고라도).
        if (!PushNow(closing: false)) return false;
        return true;
    }

    /// <summary>
    /// 기존 로컬 금고(vault.json)를 서버로 이관. 원본은 건드리지 않고 임시 파일로 복사해
    /// 기존 마스터 암호로 unlock한 뒤, 마스터 금고 + (봉투 재생성)을 서버로 push한다.
    /// 봉투 생성은 Kd가 필요하므로 반드시 이 마스터 GUI에서만 가능(서버는 Kd 없음).
    /// </summary>
    private bool ImportLegacy(string legacyPath)
    {
        try { File.Copy(legacyPath, _tempVaultPath, overwrite: true); }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("err.fmt", ex.Message), Loc.T("mg.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        try { _store = VaultStore.Load(_tempVaultPath, TimeSpan.Zero); }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("err.fmt", ex.Message), Loc.T("mg.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (!PromptUnlock()) return false;   // 기존 마스터 암호로 해제

        _store.StateChanged += OnStoreStateChanged;

        if (!PushNow(closing: false)) return false;   // 마스터 금고 + 봉투 서버로 이관
        MessageBox.Show(Loc.T("mg.importDone"), Loc.T("mg.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }

    /// <summary>1단계 Named Pipe 앱이 쓰던 로컬 금고 경로(%APPDATA%\KeyManager\vault.json).</summary>
    private static string LegacyVaultPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KeyManager", "vault.json");

    private bool PromptUnlock()
    {
        if (_store!.IsUnlocked) return true;
        while (true)
        {
            using var f = new MasterPasswordForm(create: false);
            if (f.ShowDialog() != DialogResult.OK) return false;
            if (_store.Unlock(f.Password)) return true;
            MessageBox.Show(Loc.T("mp.errWrong"), Loc.T("mp.titleUnlock"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---- MainForm ----

    private void ShowMain()
    {
        if (_store is null) return;
        _main = new MainForm(_store, ApplyLanguage, ChangeMasterPassword);
        _main.Text = Loc.T("mg.title");
        _main.FormClosed += (_, _) => OnMainClosed();
        _main.Show();
    }

    private void OnMainClosed()
    {
        if (_rebuilding) return; // 언어 변경 재구성 중의 프로그램적 닫기는 종료가 아님.

        // 종료 백스톱: 잠금 해제 상태면 마지막 상태를 한 번 더 push.
        _debounce.Stop();
        if (_store is { IsUnlocked: true } && _pushPending)
            PushNow(closing: true);

        CleanupTemp();
        Environment.Exit(0);
    }

    private void ApplyLanguage(Lang lang)
    {
        if (Loc.Current == lang) return;
        Loc.Set(lang);
        _settings.Language = Loc.Code(lang);
        _settings.Save();

        // 현재 이벤트(라디오 변경) 처리 이후로 미뤄 재진입 방지.
        _main?.BeginInvoke(new Action(RebuildMain));
    }

    private void RebuildMain()
    {
        if (_main is { IsDisposed: false })
        {
            _rebuilding = true;
            _main.Close();
            _main = null;
            _rebuilding = false;
        }
        ShowMain();
    }

    private void ChangeMasterPassword()
    {
        if (_store is null || !_store.IsUnlocked) return;

        using var f = new ChangeMasterPasswordForm();
        if (f.ShowDialog() != DialogResult.OK) return;

        bool ok;
        try { ok = _store.ChangeMasterPassword(f.CurrentPassword, f.NewPassword); }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("err.fmt", ex.Message), Loc.T("cpw.title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!ok)
        {
            MessageBox.Show(Loc.T("cpw.errWrongCurrent"), Loc.T("cpw.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool same = string.Equals(f.CurrentPassword, f.NewPassword, StringComparison.Ordinal);
        MessageBox.Show(Loc.T(same ? "cpw.reencrypted" : "cpw.success"), Loc.T("cpw.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        // ChangeMasterPassword가 StateChanged를 발생 → 디바운스가 전체 재봉인 push 처리.
    }

    // ---- 서버 push (디바운스) ----

    private void OnStoreStateChanged()
    {
        if (_marshalDisposed) return;
        // UI 스레드에서 디바운스 재시작(편집은 UI 스레드에서 발생).
        _pushPending = true;
        _debounce.Stop();
        _debounce.Start();
    }

    private void FlushPush()
    {
        if (_store is not { IsUnlocked: true }) return; // 잠금 상태면 push 안 함
        PushNow(closing: false);
    }

    private bool PushNow(bool closing)
    {
        if (_conn is null || _store is not { IsUnlocked: true }) return false;
        try
        {
            ServerPushData data = _store.BuildServerPush();
            RunSync(() => _conn.PushAsync(data.MasterVaultJson, data.Envelopes));
            _pushPending = false;
            return true;
        }
        catch (Exception ex)
        {
            string key = closing ? "mg.closePushError" : "mg.pushError";
            MessageBox.Show(Loc.T(key, ex.Message), Loc.T("mg.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    // ---- helpers ----

    private bool _marshalDisposed;

    /// <summary>비동기 서버 호출을 UI 흐름에서 동기적으로 대기(짧은 라운드트립).</summary>
    private static T RunSync<T>(Func<Task<T>> fn) => Task.Run(fn).GetAwaiter().GetResult();
    private static void RunSync(Func<Task> fn) => Task.Run(fn).GetAwaiter().GetResult();

    private void CleanupTemp()
    {
        try { if (File.Exists(_tempVaultPath)) File.Delete(_tempVaultPath); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _marshalDisposed = true;
            _debounce.Dispose();
            _store?.Dispose();
            CleanupTemp();
        }
        base.Dispose(disposing);
    }
}
