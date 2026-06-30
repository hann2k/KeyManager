using System.Text.Json;
using System.IO.Pipes;
using KeyManager.Protocol;

namespace KeyManager.Client;

/// <summary>브로커가 요청을 거부했거나 통신에 실패했을 때.</summary>
public sealed class KeyManagerException : Exception
{
    public KeyManagerException(string message) : base(message) { }
}

/// <summary>
/// 소비 앱용 클라이언트 SDK(설계 §10.1). 등록 시 받은 공유 시드 S만 있으면
/// 핸드셰이크·전송 복호화를 내부에서 처리하고 평문 값을 돌려준다.
/// 마스터 키는 절대 다루지 않는다 — 전송 암호(②)만 푼다.
/// </summary>
public sealed class KeyManagerClient
{
    private readonly string _clientName;
    private readonly byte[] _seed;
    private readonly string _pipeName;
    private readonly int _connectTimeoutMs;

    public KeyManagerClient(string clientName, byte[] seed, string? pipeName = null, int connectTimeoutMs = 3000)
    {
        if (string.IsNullOrEmpty(clientName)) throw new ArgumentException("clientName 필요", nameof(clientName));
        if (seed is null || seed.Length == 0) throw new ArgumentException("seed 필요", nameof(seed));
        _clientName = clientName;
        _seed = seed;
        _pipeName = pipeName ?? ProtocolConstants.DefaultPipeName;
        _connectTimeoutMs = connectTimeoutMs;
    }

    /// <summary>base64로 보관한 시드로 생성하는 편의 생성자.</summary>
    public static KeyManagerClient FromBase64Seed(string clientName, string base64Seed, string? pipeName = null)
        => new(clientName, Convert.FromBase64String(base64Seed), pipeName);

    /// <summary>키 평문 값을 가져온다.</summary>
    public async Task<string> GetAsync(string key, CancellationToken ct = default)
    {
        var result = await RoundtripAsync("get", key, ct).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<GetResult>(result);
        return parsed?.Value ?? throw new KeyManagerException("응답 파싱 실패");
    }

    /// <summary>이 클라이언트가 접근 가능한 키 이름 목록.</summary>
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        var result = await RoundtripAsync("list", null, ct).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<ListResult>(result);
        return parsed?.Keys ?? throw new KeyManagerException("응답 파싱 실패");
    }

    /// <summary>
    /// 콜론(:) 그룹 prefix 아래의 모든 키-값을 한 번에 가져온다(권한 있는 것만).
    /// 반환 키는 전체 콜론 이름이라 .NET <c>IConfiguration</c>의 AddInMemoryCollection에 그대로 넣을 수 있다.
    /// 예: GetGroupAsync("LsOpenApi:Simulation").
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetGroupAsync(string groupName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(groupName)) throw new ArgumentException("groupName 필요", nameof(groupName));
        var result = await RoundtripAsync("getGroup", groupName, ct).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<GroupResult>(result);
        return parsed?.Entries ?? throw new KeyManagerException("응답 파싱 실패");
    }

    private async Task<byte[]> RoundtripAsync(string op, string? key, CancellationToken ct)
    {
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(_connectTimeoutMs, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new KeyManagerException("브로커에 연결할 수 없습니다(에이전트 미실행?).");
        }

        // 1. 서버 challenge 수신
        var hello = await Framing.ReadMessageAsync<ServerHello>(pipe, ct).ConfigureAwait(false);
        if (hello.Protocol != ProtocolConstants.Version)
            throw new KeyManagerException($"프로토콜 버전 불일치: {hello.Protocol}");
        byte[] nonce = Convert.FromBase64String(hello.Nonce);

        // 2. authCode 계산(S로) 후 요청 전송
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
        await Framing.WriteMessageAsync(pipe, req, ct).ConfigureAwait(false);

        // 3. 응답 수신 + 전송 복호화(sessionKey는 S로 직접 유도)
        var resp = await Framing.ReadMessageAsync<ServerResponse>(pipe, ct).ConfigureAwait(false);
        if (!resp.Ok)
            throw new KeyManagerException(resp.Error ?? "거부되었습니다.");

        byte[] sessionKey = TransportCrypto.DeriveSessionKey(_seed, nonce);
        byte[] iv = Convert.FromBase64String(resp.Iv!);
        byte[] tag = Convert.FromBase64String(resp.Tag!);
        byte[] payload = Convert.FromBase64String(resp.Payload!);
        return TransportCrypto.Open(sessionKey, iv, tag, payload);
    }
}
