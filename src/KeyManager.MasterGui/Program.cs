namespace KeyManager.MasterGui;

// 비상주 관리 GUI 진입점(설계 §8). 서버 pull → unlock → 편집 → push. 창 X = 프로세스 종료.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var ctx = new MasterAppContext();
        if (ctx.Initialized)
            Application.Run(ctx);
    }
}
