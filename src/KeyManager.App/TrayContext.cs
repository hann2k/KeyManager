using KeyManager.Core;

namespace KeyManager.App;

/// <summary>
/// 트레이 상주 컨텍스트. (최초 실행 시 언어 선택 →) vault 로드/생성 → unlock → Named Pipe 브로커 기동.
/// 시작 시 관리 창 자동 표시. 유휴 자동 잠금은 비활성(설계 §15).
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private static readonly TimeSpan AutoLock = TimeSpan.Zero; // 자동 잠금 비활성

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _lockItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly Control _marshal = new();
    private readonly AppSettings _settings;

    private VaultStore? _store;
    private PipeBrokerServer? _server;
    private MainForm? _main;

    public bool Initialized { get; private set; }

    public TrayContext()
    {
        _ = _marshal.Handle; // UI 스레드 마샬링용 핸들

        // 1) 언어 결정 — 최초 실행이면 선택 창, 이후엔 설정에서 로드.
        _settings = AppSettings.Load();
        if (!AppSettings.Exists)
        {
            using var lf = new LanguageSelectForm();
            lf.ShowDialog();
            Loc.Init(lf.Selected);
            _settings.Language = Loc.Code(lf.Selected);
            _settings.Save();
        }
        else
        {
            Loc.Init(Loc.Parse(_settings.Language));
        }

        // 2) 트레이 메뉴
        _openItem = new ToolStripMenuItem(Loc.T("tray.open"), null, (_, _) => ShowMain());
        _lockItem = new ToolStripMenuItem(Loc.T("tray.lock"), null, (_, _) => ToggleLock());
        _exitItem = new ToolStripMenuItem(Loc.T("tray.exit"), null, (_, _) => ExitApp());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_openItem);
        menu.Items.Add(_lockItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = Loc.T("app.title"),
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowMain();

        // 3) vault 초기화 + 브로커
        Initialized = InitializeVault();
        if (!Initialized) { _icon.Visible = false; return; }

        UpdateState();
        _marshal.BeginInvoke(new Action(ShowMain)); // 시작 시 관리 창 표시
    }

    private bool InitializeVault()
    {
        AppPaths.EnsureDir();

        if (!File.Exists(AppPaths.VaultPath))
        {
            using var f = new MasterPasswordForm(create: true);
            if (f.ShowDialog() != DialogResult.OK) return false;
            _store = VaultStore.CreateNew(AppPaths.VaultPath, f.Password, AutoLock);
            _store.Unlock(f.Password);
        }
        else
        {
            _store = VaultStore.Load(AppPaths.VaultPath, AutoLock);
            if (!PromptUnlock()) return false;
        }

        _store.StateChanged += OnStoreStateChanged;
        _server = new PipeBrokerServer(new BrokerService(_store));
        _server.Start();
        return true;
    }

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

    private void ShowMain()
    {
        if (_store is null) return;
        if (!PromptUnlock()) return;

        if (_main is null || _main.IsDisposed)
        {
            _main = new MainForm(_store, ApplyLanguage);
            _main.FormClosed += (_, _) => _main = null;
            _main.Show();
        }
        _main.WindowState = FormWindowState.Normal;
        _main.Activate();
        _main.BringToFront();
    }

    /// <summary>Language 탭에서 언어 변경 시: 저장 + 메뉴 갱신 + 관리 창 재구성.</summary>
    private void ApplyLanguage(Lang lang)
    {
        if (Loc.Current == lang) return;
        Loc.Set(lang);
        _settings.Language = Loc.Code(lang);
        _settings.Save();

        _openItem.Text = Loc.T("tray.open");
        _exitItem.Text = Loc.T("tray.exit");
        UpdateState();

        // 현재 이벤트(라디오 변경) 처리 이후로 미뤄 재진입 방지.
        _marshal.BeginInvoke(new Action(RebuildMain));
    }

    private void RebuildMain()
    {
        if (_main is { IsDisposed: false }) { _main.Close(); _main = null; }
        ShowMain();
    }

    private void ToggleLock()
    {
        if (_store is null) return;
        if (_store.IsUnlocked) _store.Lock();
        else PromptUnlock();
    }

    private void OnStoreStateChanged()
    {
        if (_marshal.IsDisposed) return;
        if (_marshal.InvokeRequired) { _marshal.BeginInvoke(UpdateState); return; }
        UpdateState();
    }

    private void UpdateState()
    {
        bool unlocked = _store?.IsUnlocked == true;
        _lockItem.Text = unlocked ? Loc.T("tray.lock") : Loc.T("tray.unlock");
        _icon.Text = unlocked ? Loc.T("tray.tipUnlocked") : Loc.T("tray.tipLocked");

        if (!unlocked && _main is { IsDisposed: false })
        {
            _main.Close();
            _main = null;
        }
    }

    private void ExitApp()
    {
        try { _icon.Visible = false; } catch { }
        try { _ = _server?.StopAsync(); } catch { }
        try { _store?.Dispose(); } catch { }
        Environment.Exit(0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Dispose();
            _marshal.Dispose();
            _server?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _store?.Dispose();
        }
        base.Dispose(disposing);
    }
}
