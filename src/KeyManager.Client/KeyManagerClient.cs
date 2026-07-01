using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using KeyManager.Protocol;

namespace KeyManager.Client;

/// <summary>서버가 요청을 거부했거나 통신에 실패했을 때.</summary>
public sealed class KeyManagerException : Exception
{
    public KeyManagerException(string message) : base(message) { }
    public KeyManagerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// 소비 앱용 클라이언트 SDK — TCP 버전(설계 §7). 최초 호출 시 서버에 TCP+TLS로 붙어
/// 자기 봉투(envelope)를 fetch·복호화해 인스턴스에 캐시한다. 이후 조회는 네트워크 없이 로컬에서.
/// 마스터 키(Kd)는 절대 다루지 않고, 봉투는 시드 S에서 유도한 envKey로만 연다.
/// API 시그니처(Get/List/GetGroup)는 Named Pipe 시절과 동일해 소비 앱 코드 불변.
/// </summary>
public sealed class KeyManagerClient
{
    private readonly string _clientName;
    private readonly byte[] _seed;
    private readonly string _host;
    private readonly int _port;
    private readonly string? _pinFilePath;
    private readonly int _connectTimeoutMs;

    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private Dictionary<string, string>? _entries; // 캐시된 봉투 내용

    /// <param name="clientName">등록된 소비 앱 이름.</param>
    /// <param name="seed">등록 시 받은 공유 시드 S.</param>
    /// <param name="host">서버 호스트(예: 127.0.0.1).</param>
    /// <param name="port">TCP 포트(기본 <see cref="ProtocolConstants.DefaultTcpPort"/>).</param>
    /// <param name="pinFilePath">TOFU thumbprint pin 파일. null이면 %APPDATA%\KeyManager\pinned-&lt;host&gt;.txt.</param>
    public KeyManagerClient(string clientName, byte[] seed, string host,
        int port = ProtocolConstants.DefaultTcpPort, string? pinFilePath = null, int connectTimeoutMs = 5000)
    {
        if (string.IsNullOrEmpty(clientName)) throw new ArgumentException("clientName 필요", nameof(clientName));
        if (seed is null || seed.Length == 0) throw new ArgumentException("seed 필요", nameof(seed));
        if (string.IsNullOrEmpty(host)) throw new ArgumentException("host 필요", nameof(host));
        _clientName = clientName;
        _seed = seed;
        _host = host;
        _port = port;
        _pinFilePath = pinFilePath ?? DefaultPinPath(host);
        _connectTimeoutMs = connectTimeoutMs;
    }

    /// <summary>base64로 보관한 시드로 생성하는 편의 생성자.</summary>
    public static KeyManagerClient FromBase64Seed(string clientName, string base64Seed, string host,
        int port = ProtocolConstants.DefaultTcpPort, string? pinFilePath = null)
        => new(clientName, Convert.FromBase64String(base64Seed), host, port, pinFilePath);

    /// <summary>키 평문 값을 가져온다. 권한 없으면(봉투에 없으면) 예외.</summary>
    public async Task<string> GetAsync(string key, CancellationToken ct = default)
    {
        var entries = await EnsureFetchedAsync(ct).ConfigureAwait(false);
        if (entries.TryGetValue(key, out string? value))
            return value;
        throw new KeyManagerException($"키를 찾을 수 없거나 권한이 없습니다: {key}");
    }

    /// <summary>이 클라이언트가 접근 가능한 키 이름 목록(정렬).</summary>
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        var entries = await EnsureFetchedAsync(ct).ConfigureAwait(false);
        return entries.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 콜론(:) 그룹 prefix 아래의 모든 키-값을 반환(봉투에 이미 권한 필터링됨).
    /// 반환 키는 전체 콜론 이름이라 .NET IConfiguration의 AddInMemoryCollection에 그대로 넣을 수 있다.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetGroupAsync(string groupName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(groupName)) throw new ArgumentException("groupName 필요", nameof(groupName));
        var entries = await EnsureFetchedAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in entries)
            if (InGroup(groupName, name))
                result[name] = value;
        return result;
    }

    /// <summary>캐시를 버리고 다음 조회 때 봉투를 강제로 재fetch한다.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _fetchGate.WaitAsync(ct).ConfigureAwait(false);
        try { _entries = null; }
        finally { _fetchGate.Release(); }
    }

    // ---- 봉투 fetch(최초 1회, 캐시) -------------------------------------

    private async Task<Dictionary<string, string>> EnsureFetchedAsync(CancellationToken ct)
    {
        if (_entries is not null) return _entries;
        await _fetchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_entries is not null) return _entries; // 다른 대기자가 먼저 채웠을 수 있음
            _entries = await FetchEnvelopeAsync(ct).ConfigureAwait(false);
            return _entries;
        }
        finally { _fetchGate.Release(); }
    }

    private async Task<Dictionary<string, string>> FetchEnvelopeAsync(CancellationToken ct)
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
            throw new KeyManagerException($"서버에 연결할 수 없습니다({_host}:{_port}).", ex);
        }

        var validator = TlsSupport.CreateTofuValidator(_pinFilePath!);
        await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, validator);
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = _host }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new KeyManagerException("TLS 핸드셰이크 실패(인증서 pin 불일치 가능).", ex);
        }

        // ServerHello → nonce
        ServerHello hello;
        try { hello = await Framing.ReadMessageAsync<ServerHello>(ssl, ct).ConfigureAwait(false); }
        catch (Exception ex) { throw new KeyManagerException("서버 핸드셰이크 수신 실패.", ex); }
        if (hello.Protocol != ProtocolConstants.Version)
            throw new KeyManagerException($"프로토콜 버전 불일치: {hello.Protocol}");
        byte[] nonce = Convert.FromBase64String(hello.Nonce);

        // fetchEnvelope 요청(authCode arg = clientName).
        long timeStep = TransportCrypto.ComputeTimeStep(DateTimeOffset.UtcNow);
        byte[] authCode = TransportCrypto.ComputeAuthCode(_seed, nonce, timeStep, VaultOps.FetchEnvelope, _clientName);
        var req = new VaultRequest
        {
            Op = VaultOps.FetchEnvelope,
            Client = _clientName,
            TimeStep = timeStep,
            AuthCode = Convert.ToBase64String(authCode),
        };
        try { await Framing.WriteMessageAsync(ssl, req, ct).ConfigureAwait(false); }
        catch (Exception ex) { throw new KeyManagerException("요청 전송 실패.", ex); }

        VaultResponse resp;
        try { resp = await Framing.ReadMessageAsync<VaultResponse>(ssl, ct).ConfigureAwait(false); }
        catch (Exception ex) { throw new KeyManagerException("서버 응답 수신 실패.", ex); }
        if (!resp.Ok)
            throw new KeyManagerException(resp.Error ?? "거부되었습니다.");
        if (resp.Iv is null || resp.Tag is null || resp.Ct is null)
            throw new KeyManagerException("봉투 응답이 비어 있습니다.");

        // envKey = HMAC(S, "env")로 봉투 복호화 → EnvelopeContent.
        byte[] envKey = TransportCrypto.DeriveEnvelopeKey(_seed);
        byte[] plaintext;
        try
        {
            plaintext = TransportCrypto.Open(envKey,
                Convert.FromBase64String(resp.Iv),
                Convert.FromBase64String(resp.Tag),
                Convert.FromBase64String(resp.Ct));
        }
        catch (Exception ex)
        {
            throw new KeyManagerException("봉투 복호화 실패(시드 불일치?).", ex);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(envKey); }

        var content = JsonSerializer.Deserialize<EnvelopeContent>(plaintext)
            ?? throw new KeyManagerException("봉투 파싱 실패.");
        return new Dictionary<string, string>(content.Entries, StringComparer.Ordinal);
    }

    // ---- KeyAccess.InGroup 인라인(Client는 Protocol만 참조) ----------------

    private const char Separator = ':';

    /// <summary>이름이 prefix 그룹(자신 또는 prefix: 하위)에 속하는가(세그먼트 경계, 와일드카드 없음).</summary>
    private static bool InGroup(string prefix, string name)
        => string.Equals(name, prefix, StringComparison.Ordinal)
           || name.StartsWith(prefix + Separator, StringComparison.Ordinal);

    private static string DefaultPinPath(string host)
    {
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyManager");
        return TlsSupport.DefaultPinPath(baseDir, host);
    }
}
