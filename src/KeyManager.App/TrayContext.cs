using KeyManager.Core;

namespace KeyManager.App;

/// <summary>
/// 트레이 상주 컨텍스트. vault 로드/생성 → unlock → Named Pipe 브로커 기동.
/// 더블클릭 시 관리 UI 표시(설계 §12). 유휴 자동 잠금은 VaultStore가 처리.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    // 자동 잠금 비활성(0). 소비 앱이 수시로 키를 꺼내 써야 하므로 유휴 잠금은 끈다.
    // 수동 잠금(우클릭 → 잠금)은 그대로 동작. 필요 시 값만 바꾸면 자동 잠금 부활.
    private static readonly TimeSpan AutoLock = TimeSpan.Zero;

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _lockItem;
    private readonly Control _marshal = new();

    private VaultStore? _store;
    private PipeBrokerServer? _server;
    private MainForm? _main;

    public bool Initialized { get; private set; }

    public TrayContext()
    {
        _ = _marshal.Handle; // UI 스레드 마샬링용 핸들 강제 생성

        _lockItem = new ToolStripMenuItem("잠금", null, (_, _) => ToggleLock());
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("열기", null, (_, _) => ShowMain()));
        menu.Items.Add(_lockItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("종료", null, (_, _) => ExitApp()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = "KeyManager",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowMain();

        Initialized = InitializeVault();
        if (!Initialized) { _icon.Visible = false; return; }

        UpdateState();

        // 시작 시 해제까지 마쳤으면 관리 창을 바로 띄운다(트레이에만 들어가 보이지 않던 혼동 방지).
        // 메시지 루프 시작 후 실행되도록 마샬링.
        _marshal.BeginInvoke(new Action(ShowMain));
    }

    private bool InitializeVault()
    {
        AppPaths.EnsureDir();

        if (!File.Exists(AppPaths.VaultPath))
        {
            using var f = new MasterPasswordForm(create: true);
            if (f.ShowDialog() != DialogResult.OK) return false; // 생성 취소 → 종료
            _store = VaultStore.CreateNew(AppPaths.VaultPath, f.Password, AutoLock);
            _store.Unlock(f.Password);
        }
        else
        {
            _store = VaultStore.Load(AppPaths.VaultPath, AutoLock);
            if (!PromptUnlock()) return false; // 해제 취소 → 종료
        }

        _store.StateChanged += OnStoreStateChanged;
        _server = new PipeBrokerServer(new BrokerService(_store));
        _server.Start();
        return true;
    }

    /// <summary>잠금 상태일 때 마스터 암호로 해제. 성공 시 true. 취소 시 false.</summary>
    private bool PromptUnlock()
    {
        if (_store!.IsUnlocked) return true;
        while (true)
        {
            using var f = new MasterPasswordForm(create: false);
            if (f.ShowDialog() != DialogResult.OK) return false;
            if (_store.Unlock(f.Password)) return true;
            MessageBox.Show("암호가 올바르지 않습니다.", "잠금 해제", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowMain()
    {
        if (_store is null) return;
        if (!PromptUnlock()) return;

        if (_main is null || _main.IsDisposed)
        {
            _main = new MainForm(_store);
            _main.FormClosed += (_, _) => _main = null;
            _main.Show();
        }
        _main.WindowState = FormWindowState.Normal;
        _main.Activate();
        _main.BringToFront();
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
        _lockItem.Text = unlocked ? "잠금" : "잠금 해제";
        _icon.Text = unlocked ? "KeyManager — 해제됨" : "KeyManager — 잠김";

        // 잠기면 열려있는 관리 창을 닫는다(값 노출 방지).
        if (!unlocked && _main is { IsDisposed: false })
        {
            _main.Close();
            _main = null;
        }
    }

    private void ExitApp()
    {
        // UI 스레드를 절대 블로킹하지 않는다(동기 대기 시 데드락 → 종료 불가).
        // 1) 트레이 아이콘 즉시 제거  2) 정리는 best-effort  3) 프로세스 종료 보장.
        try { _icon.Visible = false; } catch { }
        try { _ = _server?.StopAsync(); } catch { } // 취소 신호만 보내고 대기하지 않음
        try { _store?.Dispose(); } catch { }        // 동기·즉시 완료(타이머 정리 + Kd 소거)
        // 백그라운드 수락 루프 등이 남아도 확실히 죽도록 보장. vault는 변경 시마다
        // 즉시 저장되므로 미저장 상태가 없어 강제 종료가 안전하다.
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
