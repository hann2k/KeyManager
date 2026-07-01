using System.Text.Json;
using KeyManager.App; // Loc (linked)

namespace KeyManager.Server;

/// <summary>
/// 상주 서버 트레이(설계 §9). 시작 시 ServerHost.StartDefault로 TLS 리슨 기동.
/// 최초 실행이면 admin 토큰 A를 1회 표시. 트레이 우클릭 메뉴는 딱 2개: "Key 목록 보기" / "종료".
/// 서버는 unlock 개념이 없다(암호 불필요) — 부팅 후 바로 서비스.
/// </summary>
internal sealed class ServerTrayContext : ApplicationContext
{
    private NotifyIcon? _icon;
    private readonly Control _marshal = new();

    private ServerHost? _host;
    private ServerKeyListForm? _listForm;

    public bool Initialized { get; private set; }

    public ServerTrayContext()
    {
        _ = _marshal.Handle;

        // 1) 언어: App과 같은 %APPDATA%\KeyManager\settings.json의 Language를 읽되, 없으면 영어.
        Loc.Init(Loc.Parse(ReadLanguageCode()));

        // 2) 최초 실행 감지는 StartDefault(내부에서 store를 생성) 전에 해야 한다.
        bool firstRun = !File.Exists(ServerStore.DefaultPath);

        // 3) 서버 기동.
        try
        {
            _host = ServerHost.StartDefault();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("srv.startError", ex.Message), Loc.T("srv.title"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Initialized = false;
            return;
        }

        // 4) 트레이 아이콘 + 메뉴(딱 2개).
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(Loc.T("srv.menu.keys"), null, (_, _) => ShowKeyList()));
        menu.Items.Add(new ToolStripMenuItem(Loc.T("srv.menu.exit"), null, (_, _) => ExitApp()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = Trunc(Loc.T("srv.tipListening", _host.Port)),
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowKeyList();

        // 5) 최초 실행이면 admin 토큰 A를 1회 표시.
        if (firstRun)
        {
            using var f = new AdminTokenForm(_host.Store.GetAdminTokenBase64(), _host.Port);
            f.ShowDialog();
        }

        Initialized = true;
    }

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

    private void ShowKeyList()
    {
        if (_host is null) return;
        if (_listForm is null || _listForm.IsDisposed)
        {
            _listForm = new ServerKeyListForm(_host.Store);
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
        // 트레이 아이콘 즉시 제거.
        try { if (_icon is not null) _icon.Visible = false; } catch { }
        try { _icon?.Dispose(); } catch { }
        _icon = null;

        // 서버 정지는 best-effort로 하되 무한 대기 금지(accept 루프 대기가 UI 스레드를 막아
        // 프로세스가 좀비로 남는 것 방지). DisposeAsync는 ConfigureAwait(false)라 UI 데드락 없음.
        try { _host?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); } catch { }
        _host = null;

        // 상주 프로세스를 확실히 종료(포트 점유/좀비 방지). 1단계 App과 동일한 방식.
        Environment.Exit(0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon?.Dispose();
            _marshal.Dispose();
            try { _host?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        }
        base.Dispose(disposing);
    }
}
