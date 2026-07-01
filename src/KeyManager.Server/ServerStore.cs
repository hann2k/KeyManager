using System.Security.Cryptography;
using System.Text.Json;
using KeyManager.Protocol;

namespace KeyManager.Server;

/// <summary>
/// 서버 디스크 저장 포맷(설계 §4). %APPDATA%\KeyManager\server-store.json.
/// 서버는 Kd를 갖지 않으므로 금고·봉투를 불투명 blob으로만 보관한다.
/// admin 토큰 A는 push/pull 인증에 쓰이며 최초 실행 시 랜덤 생성한다.
/// 모든 접근은 스레드 안전(단일 락 + 원자적 저장).
/// </summary>
public sealed class ServerStore
{
    private static readonly JsonSerializerOptions FileJson = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string _path;
    private StoreFile _file;

    private ServerStore(string path, StoreFile file)
    {
        _path = path;
        _file = file;
    }

    /// <summary>기본 저장 경로: %APPDATA%\KeyManager\server-store.json.</summary>
    public static string DefaultPath => Path.Combine(DefaultDir, "server-store.json");

    /// <summary>기본 데이터 디렉터리(pin/pfx도 여기).</summary>
    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyManager");

    /// <summary>기본 TLS 인증서 경로: %APPDATA%\KeyManager\server-cert.pfx.</summary>
    public static string DefaultCertPath => Path.Combine(DefaultDir, "server-cert.pfx");

    /// <summary>
    /// 저장 파일을 로드하거나(없으면) admin 토큰 A를 새로 생성해 만든다.
    /// </summary>
    public static ServerStore LoadOrCreate(string? path = null)
    {
        path ??= DefaultPath;
        if (File.Exists(path))
        {
            byte[] bytes = File.ReadAllBytes(path);
            StoreFile file = JsonSerializer.Deserialize<StoreFile>(bytes, FileJson)
                ?? throw new InvalidDataException("server-store.json을 읽을 수 없습니다.");
            var store = new ServerStore(path, file);
            // 방어: 예전 파일에 토큰이 비어 있으면 생성.
            if (string.IsNullOrEmpty(file.AdminAuthKey))
            {
                store.GenerateAdminToken();
            }
            return store;
        }

        var fresh = new StoreFile
        {
            AdminAuthKey = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(ProtocolConstants.AdminTokenLengthBytes)),
        };
        var created = new ServerStore(path, fresh);
        created.Save();
        return created;
    }

    // ---- admin 토큰 -----------------------------------------------------

    /// <summary>admin 토큰 A(32B)를 반환. push/pull authCode 검증에 사용.</summary>
    public byte[] GetAdminToken()
    {
        lock (_gate) return Convert.FromBase64String(_file.AdminAuthKey);
    }

    /// <summary>admin 토큰 A base64(마스터 GUI 최초 설정 시 사용자에게 보여주기 위함).</summary>
    public string GetAdminTokenBase64()
    {
        lock (_gate) return _file.AdminAuthKey;
    }

    private void GenerateAdminToken()
    {
        lock (_gate)
        {
            _file.AdminAuthKey = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(ProtocolConstants.AdminTokenLengthBytes));
            Save();
        }
    }

    // ---- 마스터 금고 ----------------------------------------------------

    /// <summary>보관 중인 마스터 금고 JSON(없으면 null).</summary>
    public string? GetMasterVaultJson()
    {
        lock (_gate) return _file.MasterVaultJson;
    }

    public void SetMasterVaultJson(string? json)
    {
        lock (_gate)
        {
            _file.MasterVaultJson = json;
            Save();
        }
    }

    // ---- 봉투 -----------------------------------------------------------

    /// <summary>이름으로 봉투 조회(없으면 null).</summary>
    public EnvelopeRecord? FindEnvelope(string client)
    {
        lock (_gate)
            return _file.Envelopes.FirstOrDefault(e =>
                string.Equals(e.Client, client, StringComparison.Ordinal));
    }

    /// <summary>봉투 목록 전체를 반환(복사).</summary>
    public IReadOnlyList<EnvelopeRecord> GetEnvelopes()
    {
        lock (_gate) return _file.Envelopes.ToList();
    }

    /// <summary>금고 + 봉투를 원자적으로 교체(push 처리). 한 번의 저장으로 커밋.</summary>
    public void ReplaceVaultAndEnvelopes(string masterVaultJson, IReadOnlyList<EnvelopeRecord> envelopes)
    {
        lock (_gate)
        {
            _file.MasterVaultJson = masterVaultJson;
            _file.Envelopes = envelopes.ToList();
            Save();
        }
    }

    // ---- 트레이 읽기전용 메타데이터 -------------------------------------

    /// <summary>
    /// 서버 트레이 "Key 목록 보기"용 메타데이터(설계 §9). 서버는 Kd가 없어 키 이름을 못 본다 →
    /// 봉투(소비앱) 이름·UpdatedAt, 금고 존재 여부, 봉투 수만 노출(zero-knowledge 유지).
    /// (봉투당 키 개수는 Ct가 불투명해 알 수 없다.)
    /// </summary>
    public ServerMetadata GetMetadata()
    {
        lock (_gate)
        {
            var items = _file.Envelopes
                .Select(e => new EnvelopeMeta(e.Client, e.UpdatedAt))
                .OrderBy(m => m.Client, StringComparer.Ordinal)
                .ToList();
            return new ServerMetadata(
                MasterVaultPresent: !string.IsNullOrEmpty(_file.MasterVaultJson),
                EnvelopeCount: _file.Envelopes.Count,
                Envelopes: items);
        }
    }

    // ---- 저장 -----------------------------------------------------------

    private void Save()
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(_file, FileJson);
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = _path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        if (File.Exists(_path)) File.Replace(tmp, _path, null);
        else File.Move(tmp, _path);
    }

    /// <summary>디스크 저장 스키마(설계 §4).</summary>
    private sealed class StoreFile
    {
        public int Version { get; set; } = 1;

        /// <summary>base64(admin 토큰 A, 32B).</summary>
        public string AdminAuthKey { get; set; } = "";

        /// <summary>마스터 금고 VaultFile JSON(Kd 암호). null이면 아직 push 안 됨.</summary>
        public string? MasterVaultJson { get; set; }

        public List<EnvelopeRecord> Envelopes { get; set; } = [];
    }
}

/// <summary>트레이 목록 창용 읽기전용 메타데이터.</summary>
public sealed record ServerMetadata(
    bool MasterVaultPresent,
    int EnvelopeCount,
    IReadOnlyList<EnvelopeMeta> Envelopes);

/// <summary>봉투 한 개의 표시용 메타(키 이름·개수는 zero-knowledge라 노출 안 함).</summary>
public sealed record EnvelopeMeta(string Client, string UpdatedAt);
