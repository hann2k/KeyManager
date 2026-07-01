using System.Net.Security;
using System.Net.Sockets;
using KeyManager.Protocol;

namespace KeyManager.Core;

/// <summary>서버 접속/인증/응답 처리 중 실패 시 던지는 예외(마스터 GUI가 사용자에게 안내).</summary>
public sealed class MasterServerException : Exception
{
    public MasterServerException(string message) : base(message) { }
    public MasterServerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// 마스터 GUI(admin)측 TCP/TLS 클라이언트(설계 §5, §8). 서버에서 마스터 금고를 pull하고,
/// 편집 후 (금고 + 재생성 봉투)를 push한다. 인증은 admin 토큰 A 기반 authCode:
///   authCode = HMAC(A, "auth" ‖ nonce ‖ timeStep ‖ op ‖ "").
/// A·Kd·값 평문은 절대 와이어로 흐르지 않는다(금고 JSON은 Kd 암호 상태 그대로 흐름).
/// TLS 신뢰는 TOFU thumbprint pin(설계 §7).
/// </summary>
public sealed class MasterServerConnection
{
    private readonly string _host;
    private readonly int _port;
    private readonly byte[] _adminToken;
    private readonly string _pinFilePath;
    private readonly int _connectTimeoutMs;

    /// <param name="host">서버 호스트(예: 127.0.0.1).</param>
    /// <param name="port">TCP 포트(기본 <see cref="ProtocolConstants.DefaultTcpPort"/>).</param>
    /// <param name="adminToken">admin 토큰 A(32바이트). AppSettings에 base64로 보관한 것을 디코딩해 전달.</param>
    /// <param name="pinFilePath">TOFU thumbprint pin 파일 경로.</param>
    public MasterServerConnection(string host, int port, byte[] adminToken, string pinFilePath, int connectTimeoutMs = 5000)
    {
        if (string.IsNullOrEmpty(host)) throw new ArgumentException("host 필요", nameof(host));
        if (adminToken is null || adminToken.Length == 0) throw new ArgumentException("adminToken 필요", nameof(adminToken));
        _host = host;
        _port = port;
        _adminToken = adminToken;
        _pinFilePath = pinFilePath;
        _connectTimeoutMs = connectTimeoutMs;
    }

    /// <summary>
    /// 마스터 금고 JSON을 서버에서 받아온다. 서버에 금고가 아직 없으면 null(최초 설정 흐름).
    /// </summary>
    public async Task<string?> PullVaultAsync(CancellationToken ct = default)
    {
        VaultResponse resp = await RoundtripAsync(VaultOps.PullVault, req => req, ct).ConfigureAwait(false);
        return resp.MasterVaultJson; // 금고 없으면 Ok=true인데 null
    }

    /// <summary>마스터 금고 + 봉투를 원자적으로 서버에 올린다.</summary>
    public async Task PushAsync(string masterVaultJson, IReadOnlyList<EnvelopeRecord> envelopes, CancellationToken ct = default)
    {
        var push = new PushPayload
        {
            MasterVaultJson = masterVaultJson,
            Envelopes = envelopes as EnvelopeRecord[] ?? envelopes.ToArray(),
        };
        await RoundtripAsync(VaultOps.PushVault, req => req with { Push = push }, ct).ConfigureAwait(false);
    }

    private async Task<VaultResponse> RoundtripAsync(string op, Func<VaultRequest, VaultRequest> build, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(_connectTimeoutMs);
            await tcp.ConnectAsync(_host, _port, connectCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            throw new MasterServerException($"서버에 연결할 수 없습니다({_host}:{_port}).", ex);
        }

        var validator = TlsSupport.CreateTofuValidator(_pinFilePath);
        await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, validator);
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = _host,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new MasterServerException("TLS 핸드셰이크 실패(인증서 pin 불일치 가능).", ex);
        }

        // ServerHello → nonce
        ServerHello hello;
        try { hello = await Framing.ReadMessageAsync<ServerHello>(ssl, ct).ConfigureAwait(false); }
        catch (Exception ex) { throw new MasterServerException("서버 핸드셰이크 수신 실패.", ex); }
        if (hello.Protocol != ProtocolConstants.Version)
            throw new MasterServerException($"프로토콜 버전 불일치: {hello.Protocol}");

        byte[] nonce = Convert.FromBase64String(hello.Nonce);
        long timeStep = TransportCrypto.ComputeTimeStep(DateTimeOffset.UtcNow);
        // pull/push의 arg는 빈 문자열(설계 §3, §5).
        byte[] authCode = TransportCrypto.ComputeAuthCode(_adminToken, nonce, timeStep, op, null);

        var req = build(new VaultRequest
        {
            Op = op,
            TimeStep = timeStep,
            AuthCode = Convert.ToBase64String(authCode),
        });

        try { await Framing.WriteMessageAsync(ssl, req, ct).ConfigureAwait(false); }
        catch (Exception ex) { throw new MasterServerException("요청 전송 실패.", ex); }

        VaultResponse resp;
        try { resp = await Framing.ReadMessageAsync<VaultResponse>(ssl, ct).ConfigureAwait(false); }
        catch (Exception ex) { throw new MasterServerException("서버 응답 수신 실패.", ex); }

        if (!resp.Ok)
            throw new MasterServerException(resp.Error ?? "서버가 요청을 거부했습니다.");
        return resp;
    }
}
