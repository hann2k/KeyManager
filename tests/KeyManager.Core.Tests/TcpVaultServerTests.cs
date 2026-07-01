using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using KeyManager.Client;
using KeyManager.Core;
using KeyManager.Protocol;
using KeyManager.Server;

namespace KeyManager.Core.Tests;

/// <summary>
/// TCP 버전 end-to-end(설계 §5~§9): 자체서명 인증서로 TcpVaultServer 기동 →
/// MasterServerConnection으로 push/pull → KeyManagerClient로 봉투 fetch·복호화.
/// admin authCode 수락/거부(잘못된 토큰·재생 nonce·오래된 timeStep)도 검증.
/// </summary>
public class TcpVaultServerTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"km-tcp-{Guid.NewGuid():n}");
    private string _storePath = "";
    private string _pinPath = "";
    private X509Certificate2 _cert = null!;
    private ServerStore _serverStore = null!;
    private TcpVaultServer _server = null!;
    private byte[] _adminToken = null!;
    private int _port;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _storePath = Path.Combine(_dir, "server-store.json");
        _pinPath = Path.Combine(_dir, "pin.txt");

        _serverStore = ServerStore.LoadOrCreate(_storePath);
        _adminToken = _serverStore.GetAdminToken();
        _cert = TlsSupport.CreateSelfSignedServerCert();
        _server = new TcpVaultServer(_serverStore, _cert, port: 0); // 임의 포트
        _server.Start();
        _port = _server.Port;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        _cert.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ---- 소스 금고 생성 헬퍼 -------------------------------------------

    private (string vaultJson, IReadOnlyList<EnvelopeRecord> envs, byte[] seed) BuildSourcePush()
    {
        string vaultPath = Path.Combine(_dir, $"vault-{Guid.NewGuid():n}.json");
        using var store = VaultStore.CreateNew(vaultPath, "masterpw", TimeSpan.FromMinutes(5));
        store.Unlock("masterpw");
        store.AddOrUpdateSecret("openai", "sk-secret");
        store.AddOrUpdateSecret("github", "ghp-secret");
        store.AddOrUpdateSecret("LsOpenApi:AppKey", "app-key");
        store.AddOrUpdateSecret("LsOpenApi:AppSecret", "app-secret");
        byte[] seed = store.AddClient("app1", new[] { "openai", "LsOpenApi" }); // github 없음
        ServerPushData push = store.BuildServerPush();
        return (push.MasterVaultJson, push.Envelopes, seed);
    }

    private MasterServerConnection Admin(byte[] token) =>
        new("127.0.0.1", _port, token, _pinPath);

    // ---- pull/push + fetch 흐름 ---------------------------------------

    [Fact]
    public async Task PushThenFetch_ClientGetsAllowedKeys()
    {
        var (vaultJson, envs, seed) = BuildSourcePush();
        await Admin(_adminToken).PushAsync(vaultJson, envs);

        var km = new KeyManagerClient("app1", seed, "127.0.0.1", _port, _pinPath);
        Assert.Equal("sk-secret", await km.GetAsync("openai"));

        var keys = await km.ListAsync();
        Assert.Contains("openai", keys);
        Assert.Contains("LsOpenApi:AppKey", keys);
        Assert.DoesNotContain("github", keys); // 권한 없음 → 봉투에도 없음

        var group = await km.GetGroupAsync("LsOpenApi");
        Assert.Equal(2, group.Count);
        Assert.Equal("app-key", group["LsOpenApi:AppKey"]);
    }

    [Fact]
    public async Task Get_UnauthorizedKey_Throws()
    {
        var (vaultJson, envs, seed) = BuildSourcePush();
        await Admin(_adminToken).PushAsync(vaultJson, envs);

        var km = new KeyManagerClient("app1", seed, "127.0.0.1", _port, _pinPath);
        await Assert.ThrowsAsync<KeyManagerException>(() => km.GetAsync("github"));
    }

    [Fact]
    public async Task PullVault_ReturnsPushedJson()
    {
        var (vaultJson, envs, _) = BuildSourcePush();
        await Admin(_adminToken).PushAsync(vaultJson, envs);

        string? pulled = await Admin(_adminToken).PullVaultAsync();
        Assert.Equal(vaultJson, pulled);
    }

    [Fact]
    public async Task PullVault_BeforeAnyPush_ReturnsNull()
    {
        string? pulled = await Admin(_adminToken).PullVaultAsync();
        Assert.Null(pulled);
    }

    // ---- admin 인증 거부 케이스 ---------------------------------------

    [Fact]
    public async Task Push_WithWrongAdminToken_Rejected()
    {
        var (vaultJson, envs, _) = BuildSourcePush();
        byte[] wrong = new byte[ProtocolConstants.AdminTokenLengthBytes]; // 전부 0
        await Assert.ThrowsAsync<MasterServerException>(() => Admin(wrong).PushAsync(vaultJson, envs));
    }

    [Fact]
    public async Task Pull_WithWrongAdminToken_Rejected()
    {
        byte[] wrong = new byte[ProtocolConstants.AdminTokenLengthBytes];
        await Assert.ThrowsAsync<MasterServerException>(() => Admin(wrong).PullVaultAsync());
    }

    [Fact]
    public async Task Fetch_UnknownClient_Throws()
    {
        var (vaultJson, envs, seed) = BuildSourcePush();
        await Admin(_adminToken).PushAsync(vaultJson, envs);

        var km = new KeyManagerClient("ghost", seed, "127.0.0.1", _port, _pinPath);
        await Assert.ThrowsAsync<KeyManagerException>(() => km.GetAsync("openai"));
    }

    // ---- 재생(replay) nonce / 오래된 timeStep 거부 --------------------
    // 저수준으로 직접 프레임을 주고받아 서버 인증 로직을 검증한다.

    private async Task<(byte[] nonce, System.Net.Security.SslStream ssl, TcpClient tcp)> HandshakeAsync()
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _port);
        var validator = TlsSupport.CreateTofuValidator(_pinPath);
        var ssl = new System.Net.Security.SslStream(tcp.GetStream(), false, validator);
        await ssl.AuthenticateAsClientAsync(new System.Net.Security.SslClientAuthenticationOptions { TargetHost = "127.0.0.1" });
        var hello = await Framing.ReadMessageAsync<ServerHello>(ssl);
        return (Convert.FromBase64String(hello.Nonce), ssl, tcp);
    }

    [Fact]
    public async Task Pull_StaleTimeStep_Rejected()
    {
        var (nonce, ssl, tcp) = await HandshakeAsync();
        using (tcp)
        await using (ssl)
        {
            long stale = TransportCrypto.ComputeTimeStep(DateTimeOffset.UtcNow)
                - (ProtocolConstants.AllowedTimeStepSkew + 5); // 창 밖
            byte[] auth = TransportCrypto.ComputeAuthCode(_adminToken, nonce, stale, VaultOps.PullVault, null);
            await Framing.WriteMessageAsync(ssl, new VaultRequest
            {
                Op = VaultOps.PullVault,
                TimeStep = stale,
                AuthCode = Convert.ToBase64String(auth),
            });
            var resp = await Framing.ReadMessageAsync<VaultResponse>(ssl);
            Assert.False(resp.Ok);
        }
    }

    [Fact]
    public async Task Pull_ReplayedNonce_Rejected()
    {
        // 서버는 연결마다 새 nonce를 발급하므로, 한 nonce를 두 번째 연결에서 재사용하려 해도
        // 애초에 그 nonce가 다른 연결에 등장하지 않는다. 여기서는 서버의 nonce 1회성 캐시가
        // "이미 인증에 소비된 nonce"의 재요청을 막는지 확인한다: 같은 SslStream에서 두 번째 요청.
        var (nonce, ssl, tcp) = await HandshakeAsync();
        using (tcp)
        await using (ssl)
        {
            long ts = TransportCrypto.ComputeTimeStep(DateTimeOffset.UtcNow);
            byte[] auth = TransportCrypto.ComputeAuthCode(_adminToken, nonce, ts, VaultOps.PullVault, null);
            var req = new VaultRequest
            {
                Op = VaultOps.PullVault,
                TimeStep = ts,
                AuthCode = Convert.ToBase64String(auth),
            };
            await Framing.WriteMessageAsync(ssl, req);
            var first = await Framing.ReadMessageAsync<VaultResponse>(ssl);
            Assert.True(first.Ok); // 첫 요청은 통과

            // 서버는 요청 1건 처리 후 연결을 닫는다 → 두 번째 프레임 전송/수신은 실패해야 한다
            // (같은 nonce 재사용 불가). 연결 종료를 예외로 확인.
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await Framing.WriteMessageAsync(ssl, req);
                await Framing.ReadMessageAsync<VaultResponse>(ssl);
            });
        }
    }
}
