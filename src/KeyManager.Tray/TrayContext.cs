using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using KeyManager.App; // Loc (linked)

namespace KeyManager.Server;

/// <summary>
/// 서버 트레이 동반앱(설계 §9). 헤드리스 <c>KeyManager.Server</c>(별도 프로세스)를 기동/감시하고
/// 읽기전용 UI를 제공한다. 서버 런타임은 이 프로세스에 없다(TCP/TLS는 서버 exe가 담당).
///   - 시작 시 서버가 안 떠 있으면 같은 폴더의 KeyManager.Server.exe를 자식으로 기동(현행 "한 번 실행" UX).
///   - 최초 실행이면 서버가 만든 admin 토큰 A를 스토어에서 읽어 1회 표시.
///   - 우클릭 메뉴 2개: "Key 목록 보기"(읽기전용) / "종료". 상태(실행중·포트)를 툴팁/아이콘에 반영.
///   - "종료"는 <see cref="StopSignal"/>로 서버를 우아하게 정지시킨 뒤 트레이도 종료.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private NotifyIcon? _icon;
    private readonly Control _marshal = new();
    private System.Windows.Forms.Timer? _statusTimer;

    private Process? _serverProcess; // 트레이가 직접 기동한 서버(외부 기동이면 null)
    private ServerKeyListForm? _listForm;
    private readonly int _port;

    public bool Initialized { get; private set; }

    public TrayContext()
    {
        _ = _marshal.Handle;

        // 1) 언어: App과 같은 %APPDATA%\KeyManager\settings.json의 Language를 읽되, 없으면 영어.
        Loc.Init(Loc.Parse(ReadLanguageCode()));

        // 2) 포트(서버-설정 파일/환경변수). 상태 확인·토큰 표시에 사용.
        _port = ServerSettings.Load().Port;

        // 3) 최초 실행 감지는 서버가 스토어를 만들기 전에.
        bool firstRun = !File.Exists(ServerStore.DefaultPath);

        // 4) 서버가 안 떠 있으면 기동(같은 폴더의 헤드리스 exe).
        EnsureServerRunning();

        // 5) 트레이 아이콘 + 메뉴(딱 2개).
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(Loc.T("srv.menu.keys"), null, (_, _) => ShowKeyList()));
        menu.Items.Add(new ToolStripMenuItem(Loc.T("srv.menu.exit"), null, (_, _) => ExitApp()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = Trunc(Loc.T("srv.tipListening", _port)),
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowKeyList();

        // 6) 상태 갱신 타이머(실행중/중지 → 아이콘·툴팁).
        _statusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();

        // 7) 최초 실행이면 서버가 만든 admin 토큰 A를 1회 표시.
        if (firstRun)
            ShowAdminTokenOnce();

        Initialized = true;
    }

    // ---- 서버 기동/감시 -------------------------------------------------

    private void EnsureServerRunning()
    {
        if (StopSignal.IsServerRunning() || IsListening()) return;

        string exe = Path.Combine(AppContext.BaseDirectory, "KeyManager.Server.exe");
        if (!File.Exists(exe))
        {
            // 감시 전용 모드: 서버 실행파일이 옆에 없음(개발 중 별도 실행 등). 트레이는 계속 뜬다.
            MessageBox.Show(Loc.T("srv.tray.serverExeMissing", exe), Loc.T("srv.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _serverProcess = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("srv.startError", ex.Message), Loc.T("srv.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        WaitForListening(TimeSpan.FromSeconds(6));
    }

    /// <summary>loopback 포트에 접속되면 서버가 리슨 중.</summary>
    private bool IsListening()
    {
        try
        {
            using var c = new TcpClient();
            Task t = c.ConnectAsync(IPAddress.Loopback, _port);
            return t.Wait(TimeSpan.FromMilliseconds(300)) && c.Connected;
        }
        catch { return false; }
    }

    private void WaitForListening(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (IsListening()) return;
            Thread.Sleep(150);
        }
    }

    private void RefreshStatus()
    {
        if (_icon is null) return;
        bool running = StopSignal.IsServerRunning();
        _icon.Icon = running ? SystemIcons.Shield : SystemIcons.Warning;
        _icon.Text = Trunc(running ? Loc.T("srv.status.running", _port) : Loc.T("srv.status.stopped"));
    }

    // ---- UI 흐름 --------------------------------------------------------

    private void ShowAdminTokenOnce()
    {
        // 서버가 스토어(+토큰)를 만들 때까지 잠깐 대기.
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(6) && !File.Exists(ServerStore.DefaultPath))
            Thread.Sleep(150);

        ServerStore? store = ServerStore.TryLoad();
        if (store is null) return;
        using var f = new AdminTokenForm(store.GetAdminTokenBase64(), _port);
        f.ShowDialog();
    }

    private void ShowKeyList()
    {
        if (_listForm is null || _listForm.IsDisposed)
        {
            // 열 때·새로 고침 때마다 디스크에서 최신 스냅샷을 다시 읽는다.
            _listForm = new ServerKeyListForm(() => ServerStore.TryLoad()?.GetMetadata());
            _listForm.FormClosed += (_, _) => _listForm = null;
            _listForm.Show();
        }
        else
        {
            _listForm.WindowState = FormWindowState.Normal;
        }
        _listForm.Activate();
        _listForm.BringToFront();
    }

    private void ExitApp()
    {
        _statusTimer?.Stop();

        // 트레이 아이콘 즉시 제거.
        try { if (_icon is not null) _icon.Visible = false; } catch { }
        try { _icon?.Dispose(); } catch { }
        _icon = null;

        // 서버 우아한 종료 요청(트레이가 기동했든 외부 기동이든). 이벤트 신호 후 잠깐 대기.
        StopSignal.Signal();
        try
        {
            if (_serverProcess is { HasExited: false })
            {
                if (!_serverProcess.WaitForExit(3000))
                    try { _serverProcess.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch { }

        // 트레이 프로세스 확실히 종료.
        Environment.Exit(0);
    }

    // ---- 유틸 -----------------------------------------------------------

    private static string ReadLanguageCode()
    {
        try
        {
            string path = Path.Combine(ServerStore.DefaultDir, "settings.json");
            if (File.Exists(path))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
                if (doc.RootElement.TryGetProperty("Language", out var lang) && lang.ValueKind == JsonValueKind.String)
                    return lang.GetString() ?? "en";
            }
        }
        catch { /* 손상/부재 시 기본 영어 */ }
        return "en";
    }

    // NotifyIcon.Text는 최대 63자.
    private static string Trunc(string s) => s.Length <= 63 ? s : s[..63];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer?.Dispose();
            _icon?.Dispose();
            _marshal.Dispose();
        }
        base.Dispose(disposing);
    }
}
