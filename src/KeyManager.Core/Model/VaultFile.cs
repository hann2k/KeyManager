using KeyManager.Core.Crypto;

namespace KeyManager.Core.Model;

/// <summary>
/// 디스크에 저장되는 vault 파일 전체(설계 §6). 값·이름·시드·권한목록은 모두 암호문.
/// 평문으로 남는 것은 KDF 파라미터(비밀 아님)와 구조적 메타(생성/수정 시각, id)뿐.
/// </summary>
public sealed class VaultFile
{
    public int Version { get; set; } = 1;

    /// <summary>Kd 유도 파라미터(평문, 비밀 아님).</summary>
    public KdfParameters Kdf { get; set; } = new();

    /// <summary>마스터 암호 검증용. 고정 매직을 Kd로 봉인해 둔다.</summary>
    public SealedData Verifier { get; set; } = new();

    public List<VaultEntryRecord> Entries { get; set; } = [];

    public List<ClientRecord> Clients { get; set; } = [];
}

/// <summary>시크릿 한 항목. name/value 모두 암호문.</summary>
public sealed class VaultEntryRecord
{
    public string Id { get; set; } = "";
    public SealedData Name { get; set; } = new();
    public SealedData Value { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>소비 앱 등록 레코드. seed/allowedKeys 모두 암호문.</summary>
public sealed class ClientRecord
{
    public string Id { get; set; } = "";

    /// <summary>소비 앱 이름. 이것도 암호문(메타데이터 보호).</summary>
    public SealedData Name { get; set; } = new();

    /// <summary>공유 시드 S(암호문).</summary>
    public SealedData Seed { get; set; } = new();

    /// <summary>접근 허용 키 이름 목록을 JSON 직렬화해 봉인한 것.</summary>
    public SealedData AllowedKeys { get; set; } = new();

    /// <summary>(선택) 실행파일 바인딩 경로. null이면 미사용.</summary>
    public string? BoundProcessPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
