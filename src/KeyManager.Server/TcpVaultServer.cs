using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KeyManager.Protocol;

namespace KeyManager.Server;

/// <summary>
/// TCP + TLS 위에서 금고/봉투를 서빙하는 상주 서버(설계 §5, §9). Kd 없음 — blob 보관·전달만.
///   fetchEnvelope: Client로 봉투 조회 후 Iv/Tag/Ct 반환. 인증 없음(설계 §5 결정 (1) —
///                  서버는 S를 모르므로 authCode 검증 불가. 기밀성은 envKey가 보장).
///   pullVault/pushVault: admin 토큰 A로 authCode 재계산·대조(±AllowedTimeStepSkew, nonce 1회성).
/// 재전송 방어의 본질은 "연결마다 서버가 발급하는 랜덤 nonce"(BrokerService와 동일 패턴).
/// </summary>
public sealed class TcpVaultServer : IAsyncDisposable
{
    private readonly ServerStore _store;
    private readonly X509Certificate2 _cert;
    private readonly int _port;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedNonces = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public TcpVaultServer(ServerStore store, X509Certificate2 cert, int port = ProtocolConstants.DefaultTcpPort)
    {
        _store = store;
        _cert = cert;
        _port = port;
    }

    /// <summary>실제로 바인딩된 포트(port=0으로 시작한 경우 확인용, 테스트에서 유용).</summary>
    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? _port;

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        try { _listener?.Stop(); } catch { /* 무시 */ }
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); } catch { /* 무시 */ }
        }
        _cts.Dispose();
        _cts = null;
        _loop = null;
        _listener = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }

            _ = HandleConnectionAsync(client, ct);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            await using (var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _cert,
                    ClientCertificateRequired = false,
                }, ct).ConfigureAwait(false);

                // ServerHello(fresh nonce) → VaultRequest → VaultResponse
                byte[] nonce = RandomNumberGenerator.GetBytes(ProtocolConstants.NonceLengthBytes);
                await Framing.WriteMessageAsync(ssl, new ServerHello { Nonce = Convert.ToBase64String(nonce) }, ct)
                    .ConfigureAwait(false);

                var req = await Framing.ReadMessageAsync<VaultRequest>(ssl, ct).ConfigureAwait(false);
                VaultResponse resp = Dispatch(nonce, req);
                await Framing.WriteMessageAsync(ssl, resp, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            // 연결 단위 오류는 삼킨다(서버 계속 운영).
        }
    }

    /// <summary>op별 처리. 인증이 필요한 op는 admin A로 authCode를 검증한다.</summary>
    private VaultResponse Dispatch(byte[] issuedNonce, VaultRequest req)
    {
        return req.Op switch
        {
            VaultOps.FetchEnvelope => HandleFetchEnvelope(req),
            VaultOps.PullVault => HandleAdmin(issuedNonce, req, HandlePullVault),
            VaultOps.PushVault => HandleAdmin(issuedNonce, req, HandlePushVault),
            _ => VaultResponse.Fail("지원하지 않는 요청입니다."),
        };
    }

    // ---- fetchEnvelope (인증 없음, 봉투 기밀성은 envKey가 보장) -----------

    private VaultResponse HandleFetchEnvelope(VaultRequest req)
    {
        if (string.IsNullOrEmpty(req.Client))
            return VaultResponse.Fail("클라이언트 이름이 필요합니다.");

        EnvelopeRecord? env = _store.FindEnvelope(req.Client);
        if (env is null)
            return VaultResponse.Fail("봉투를 찾을 수 없습니다.");

        return new VaultResponse { Ok = true, Iv = env.Iv, Tag = env.Tag, Ct = env.Ct };
    }

    // ---- admin 인증 공통(±skew, nonce 1회성) — BrokerService 패턴 -------

    private VaultResponse HandleAdmin(byte[] issuedNonce, VaultRequest req, Func<VaultRequest, VaultResponse> handler)
    {
        // nonce 1회성(보조). 서버 발급이라 정상 흐름에선 항상 통과.
        string nonceKey = Convert.ToBase64String(issuedNonce);
        EvictOldNonces();
        if (!_usedNonces.TryAdd(nonceKey, DateTimeOffset.UtcNow))
            return VaultResponse.Fail("거부되었습니다.");

        // timeStep 신선도(±AllowedTimeStepSkew — 원격 시계 오차 허용).
        long serverStep = TransportCrypto.ComputeTimeStep(DateTimeOffset.UtcNow);
        if (Math.Abs(serverStep - req.TimeStep) > ProtocolConstants.AllowedTimeStepSkew)
            return VaultResponse.Fail("거부되었습니다.");

        // authCode = HMAC(A, "auth" ‖ nonce ‖ timeStep ‖ op ‖ ""). pull/push의 arg는 빈 문자열.
        byte[] adminToken = _store.GetAdminToken();
        try
        {
            byte[] expected = TransportCrypto.ComputeAuthCode(adminToken, issuedNonce, req.TimeStep, req.Op, null);
            byte[] provided;
            try { provided = Convert.FromBase64String(req.AuthCode); }
            catch { return VaultResponse.Fail("거부되었습니다."); }
            if (!TransportCrypto.ConstantTimeEquals(expected, provided))
                return VaultResponse.Fail("거부되었습니다.");
        }
        finally { CryptographicOperations.ZeroMemory(adminToken); }

        return handler(req);
    }

    private VaultResponse HandlePullVault(VaultRequest req)
        => new() { Ok = true, MasterVaultJson = _store.GetMasterVaultJson() };

    private VaultResponse HandlePushVault(VaultRequest req)
    {
        if (req.Push is null)
            return VaultResponse.Fail("push 페이로드가 없습니다.");
        _store.ReplaceVaultAndEnvelopes(req.Push.MasterVaultJson, req.Push.Envelopes);
        return new VaultResponse { Ok = true };
    }

    private void EvictOldNonces()
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
        foreach (var kv in _usedNonces)
            if (kv.Value < cutoff)
                _usedNonces.TryRemove(kv.Key, out _);
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
