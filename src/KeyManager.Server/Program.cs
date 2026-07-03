using KeyManager.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// 헤드리스 TCP/TLS 금고 서버(설계 §4·§5·§9). 콘솔로 실행하면 상주 콘솔 서비스,
// SCM이 띄우면 Windows 서비스로 동작한다(UseWindowsService). 트레이 UI는 KeyManager.Tray 동반앱.
//   - 단일 인스턴스: Global\ 뮤텍스(트레이가 "실행중" 감지에도 사용).
//   - 종료: Ctrl+C / SCM 정지(Generic Host 기본) 또는 트레이의 StopSignal 이벤트(VaultServerService가 처리).

// 단일 인스턴스 보장(세션 무관 — 서비스/사용자 세션 공유를 위해 Global\).
using var mutex = new Mutex(initiallyOwned: true, StopSignal.ServerMutexName, out bool isNew);
if (!isNew)
{
    Console.Error.WriteLine("KeyManager Server is already running. / KeyManager 서버가 이미 실행 중입니다.");
    return;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// 콘솔이면 콘솔 로거, 서비스로 뜨면 이벤트 로그로(UseWindowsService가 전환).
builder.Services.AddWindowsService(options => options.ServiceName = "KeyManager Server");
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });

builder.Services.AddHostedService<VaultServerService>();

IHost host = builder.Build();
await host.RunAsync();
