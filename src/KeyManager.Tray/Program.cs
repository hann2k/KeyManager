using KeyManager.App; // Loc (linked)

namespace KeyManager.Server;

// 서버 트레이 동반앱 진입점. 헤드리스 KeyManager.Server를 기동/감시하고
// 읽기전용 UI(키 목록)와 상태를 트레이에 제공한다(설계 §9). 서버 런타임은 별도 프로세스.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 트레이 자체 단일 인스턴스(사용자 세션). 서버 단일 인스턴스는 서버 프로세스가 Global\로 따로 보장.
        using var mutex = new Mutex(true, @"Local\KeyManager.Tray.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                Loc.T("srv.alreadyRunning"),
                Loc.T("srv.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        using var ctx = new TrayContext();
        if (ctx.Initialized)
            Application.Run(ctx);
    }
}
