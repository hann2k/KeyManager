using KeyManager.Protocol;

namespace KeyManager.Server;

/// <summary>
/// 서버 트레이(winform)가 서버를 구성·기동하기 위한 팩토리(설계 §9). UI는 여기 없다.
///   - ServerStore 로드(없으면 admin 토큰 A 생성).
///   - TLS 인증서 로드/생성(PFX).
///   - TcpVaultServer 구성·Start.
/// 트레이는 <see cref="Store"/>로 읽기전용 메타데이터(GetMetadata)를 얻어 목록 창을 채운다.
/// </summary>
public sealed class ServerHost : IAsyncDisposable
{
    /// <summary>기동한 서버 저장소(트레이가 GetMetadata/GetAdminTokenBase64에 사용).</summary>
    public ServerStore Store { get; }

    /// <summary>기동한 TCP/TLS 서버.</summary>
    public TcpVaultServer Server { get; }

    private ServerHost(ServerStore store, TcpVaultServer server)
    {
        Store = store;
        Server = server;
    }

    /// <summary>실제 리슨 포트.</summary>
    public int Port => Server.Port;

    /// <summary>
    /// 기본 경로(%APPDATA%\KeyManager)로 서버를 구성해 즉시 기동한다.
    /// 인증서(PFX)와 저장소가 없으면 자동 생성. 트레이 진입점.
    /// </summary>
    public static ServerHost StartDefault(int port = ProtocolConstants.DefaultTcpPort)
        => Start(ServerStore.DefaultPath, ServerStore.DefaultCertPath, port);

    /// <summary>경로를 지정해 서버를 구성·기동(테스트/커스텀 배치용).</summary>
    public static ServerHost Start(string storePath, string certPfxPath, int port, string? certPassword = null)
    {
        ServerStore store = ServerStore.LoadOrCreate(storePath);
        var cert = TlsSupport.LoadOrCreateServerCert(certPfxPath, certPassword);
        var server = new TcpVaultServer(store, cert, port);
        server.Start();
        return new ServerHost(store, server);
    }

    public async ValueTask DisposeAsync() => await Server.DisposeAsync();
}
