namespace KeyManager.App;

static class Program
{
    [STAThread]
    static void Main()
    {
        // 단일 인스턴스 보장(트레이 상주 앱).
        using var mutex = new Mutex(true, @"Local\KeyManager.SingleInstance", out bool isNew);
        if (!isNew)
        {
            // 언어 결정 전이라 이중 언어로 표기.
            MessageBox.Show("KeyManager is already running. / KeyManager가 이미 실행 중입니다.", "KeyManager",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        using var ctx = new TrayContext();
        if (ctx.Initialized)
            Application.Run(ctx);
    }
}
