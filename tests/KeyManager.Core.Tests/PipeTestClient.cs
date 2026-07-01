using System.IO.Pipes;
using System.Text.Json;
using KeyManager.Protocol;

namespace KeyManager.Core.Tests;

/// <summary>
/// 1단계(Named Pipe) 브로커를 end-to-end로 검증하기 위한 테스트 전용 클라이언트.
/// (실 SDK KeyManagerClient는 TCP 버전으로 전환되었으므로, 파이프 브로커 커버리지는
///  이 헬퍼가 예전 클라이언트의 파이프 왕복 로직을 그대로 재현해 유지한다.)
/// </summary>
internal sealed class PipeTestClient
{
    private readonly string _clientName;
    private readonly byte[] _seed;
    private readonly string _pipeName;

    public PipeTestClient(string clientName, byte[] seed, string pipeName)
    {
        _clientName = clientName;
        _seed = seed;
        _pipeName = pipeName;
    }

    public async Task<string> GetAsync(string key, CancellationToken ct = default)
    {
        byte[] result = await RoundtripAsync("get", key, ct);
        return JsonSerializer.Deserialize<GetResult>(result)?.Value
            ?? throw new PipeTestException("응답 파싱 실패");
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        byte[] result = await RoundtripAsync("list", null, ct);
        return JsonSerializer.Deserialize<ListResult>(result)?.Keys
            ?? throw new PipeTestException("응답 파싱 실패");
    }

    public async Task<IReadOnlyDictionary<string, string>> GetGroupAsync(string groupName, CancellationToken ct = default)
    {
        byte[] result = await RoundtripAsync("getGroup", groupName, ct);
        return JsonSerializer.Deserialize<GroupResult>(result)?.Entries
            ?? throw new PipeTestException("응답 파싱 실패");
    }

    private async Task<byte[]> RoundtripAsync(string op, string? key, CancellationToken ct)
    {
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(3000, ct); }
        catch (TimeoutException) { throw new PipeTestException("브로커에 연결할 수 없습니다."); }

        var hello = await Framing.ReadMessageAsync<ServerHello>(pipe, ct);
        byte[] nonce = Convert.FromBase64String(hello.Nonce);

        long timeStep = TransportCrypto.ComputeTimeStep(DateTimeOffset.UtcNow);
        byte[] authCode = TransportCrypto.ComputeAuthCode(_seed, nonce, timeStep, op, key);
        var req = new ClientRequest
        {
            Client = _clientName,
            Op = op,
            Key = key,
            TimeStep = timeStep,
            AuthCode = Convert.ToBase64String(authCode),
        };
        await Framing.WriteMessageAsync(pipe, req, ct);

        var resp = await Framing.ReadMessageAsync<ServerResponse>(pipe, ct);
        if (!resp.Ok) throw new PipeTestException(resp.Error ?? "거부되었습니다.");

        byte[] sessionKey = TransportCrypto.DeriveSessionKey(_seed, nonce);
        return TransportCrypto.Open(sessionKey,
            Convert.FromBase64String(resp.Iv!),
            Convert.FromBase64String(resp.Tag!),
            Convert.FromBase64String(resp.Payload!));
    }
}

/// <summary>파이프 테스트 클라이언트 거부/실패.</summary>
internal sealed class PipeTestException : Exception
{
    public PipeTestException(string message) : base(message) { }
}
