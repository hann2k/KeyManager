using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using KeyManager.Protocol;

namespace KeyManager.Core;

/// <summary>
/// Named Pipe 위에서 §9 프로토콜을 서비스하는 브로커 서버(설계 §7).
/// 파이프 ACL을 현재 사용자로 제한하고, 연결마다 challenge nonce를 발급한다.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PipeBrokerServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly BrokerService _broker;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PipeBrokerServer(BrokerService broker, string? pipeName = null)
    {
        _broker = broker;
        _pipeName = pipeName ?? ProtocolConstants.DefaultPipeName;
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        // ConfigureAwait(false): 호출자가 UI 스레드에서 동기 대기(.GetResult())해도
        // 컨티뉴에이션이 UI 스레드로 복귀하지 않게 하여 데드락 방지.
        await _cts.CancelAsync().ConfigureAwait(false);
        // 대기 중인 WaitForConnectionAsync를 깨우기 위해 더미 연결 시도
        try
        {
            using var wake = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            await wake.ConnectAsync(200).ConfigureAwait(false);
        }
        catch { /* 무시 */ }

        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); } catch { /* 무시 */ }
        }
        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = CreateInstance();
            }
            catch (Exception)
            {
                // 인스턴스 한도 초과 등 — 잠시 후 재시도
                try { await Task.Delay(50, ct); } catch { }
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                break;
            }
            catch
            {
                await server.DisposeAsync();
                continue;
            }

            _ = HandleConnectionAsync(server, ct);
        }
    }

    private NamedPipeServerStream CreateInstance()
    {
        var security = new PipeSecurity();
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User!;
        // 현재 사용자만 읽기/쓰기 + 인스턴스 생성 허용. 그 외에는 권한 없음.
        security.AddAccessRule(new PipeAccessRule(
            user,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        try
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(ProtocolConstants.NonceLengthBytes);
            await Framing.WriteMessageAsync(server, new ServerHello { Nonce = Convert.ToBase64String(nonce) }, ct);

            var req = await Framing.ReadMessageAsync<ClientRequest>(server, ct);
            string? clientPath = TryGetClientProcessPath(server);

            ServerResponse resp = _broker.Handle(nonce, req, clientPath);
            await Framing.WriteMessageAsync(server, resp, ct);
        }
        catch
        {
            // 연결 단위 오류는 삼킨다(서버 계속 운영).
        }
        finally
        {
            try { await server.DisposeAsync(); } catch { }
        }
    }

    private static string? TryGetClientProcessPath(NamedPipeServerStream server)
    {
        try
        {
            if (GetNamedPipeClientProcessId(server.SafePipeHandle, out uint pid))
            {
                using Process p = Process.GetProcessById((int)pid);
                return p.MainModule?.FileName;
            }
        }
        catch
        {
            // 일부 프로세스는 MainModule 접근 불가 — 바인딩 미사용이면 무방
        }
        return null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle Pipe, out uint ClientProcessId);
}
