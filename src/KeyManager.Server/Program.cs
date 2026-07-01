using KeyManager.App; // Loc (linked)

namespace KeyManager.Server;

// 상주 TCP/TLS 금고 서버 진입점(설계 §4, §5, §9). 트레이 상주 + 리슨.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 단일 인스턴스 보장(상주 서버).
        using var mutex = new Mutex(true, @"Local\KeyManager.Server.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "KeyManager Server is already running. / KeyManager 서버가 이미 실행 중입니다.",
                "KeyManager Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        using var ctx = new ServerTrayContext();
        if (ctx.Initialized)
            Application.Run(ctx);
    }
}
