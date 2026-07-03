using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KeyManager.Server;

/// <summary>
/// 헤드리스 서버 호스트의 상주 워커(Generic Host / <see cref="BackgroundService"/>).
/// <see cref="ServerHost.StartDefault"/>로 TCP/TLS 리슨을 기동하고, 다음 중 하나가 오면 정지한다:
///   - <see cref="StopSignal"/> 이벤트(트레이 동반앱의 "종료" 요청)
///   - stoppingToken(Ctrl+C / SCM 정지 / 호스트 셧다운)
/// 기존 서버가 연결 오류를 침묵으로 삼키던 것을 <see cref="ILogger"/> 로깅으로 보완한다.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VaultServerService : BackgroundService
{
    private readonly ILogger<VaultServerService> _log;
    private readonly IHostApplicationLifetime _lifetime;
    private ServerHost? _host;

    public VaultServerService(ILogger<VaultServerService> log, IHostApplicationLifetime lifetime)
    {
        _log = log;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 최초 실행 감지는 StartDefault(스토어 생성) 전에.
        bool firstRun = !File.Exists(ServerStore.DefaultPath);
        int port = ServerSettings.Load().Port;

        try
        {
            _host = ServerHost.StartDefault(port);
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex, "Failed to start KeyManager server on port {Port}.", port);
            _lifetime.StopApplication();
            return;
        }

        _log.LogInformation("KeyManager server listening on 127.0.0.1:{Port}.", _host.Port);

        if (firstRun)
        {
            // 최초 실행: admin 토큰 A는 마스터 GUI 최초 설정에 필요하고 이후 다시 못 본다.
            // 트레이가 있으면 트레이가 창으로도 보여주지만, 순수 헤드리스 시나리오를 위해 콘솔에도 출력.
            // 로거(LogWarning) 대신 Console: 서비스 호스팅 시 EventLog 소스 생성(관리자 필요)을 건드리지 않고,
            // 콘솔에서 토큰을 그대로 복사하기도 쉽다.
            _log.LogInformation("First run — admin token printed to console (shown once).");
            Console.WriteLine();
            Console.WriteLine("=== KeyManager first run — admin token (A), store it securely (shown once) ===");
            Console.WriteLine(_host.Store.GetAdminTokenBase64());
            Console.WriteLine("============================================================================");
            Console.WriteLine();
        }

        // 트레이의 종료 이벤트를 대기(동시에 stoppingToken도 감시). 이벤트가 오면 호스트 전체 종료.
        using EventWaitHandle stopEvent = StopSignal.CreateForHost();
        await StopSignal.WaitAsync(stopEvent, stoppingToken).ConfigureAwait(false);

        if (!stoppingToken.IsCancellationRequested)
        {
            _log.LogInformation("Stop signal received (tray). Shutting down.");
            _lifetime.StopApplication();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
            _log.LogInformation("KeyManager server stopped.");
        }
    }
}
