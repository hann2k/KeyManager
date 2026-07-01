namespace KeyManager.Protocol;

/// <summary>
/// TCP 버전(설계 §5) 와이어 메시지. TLS 위에서 기존 <see cref="Framing"/>(4B 길이 + UTF-8 JSON)로 주고받는다.
///   [연결+TLS]  서버 → ServerHello { Protocol, Nonce }
///   [요청]      클라 → VaultRequest { Op, Client?, TimeStep, AuthCode, Push? }
///   [응답]      서버 → VaultResponse { Ok, Error?, ...op별 필드 }
/// ServerHello는 기존(Named Pipe) 타입을 재사용한다.
/// </summary>
public static class VaultOps
{
    /// <summary>소비 앱이 자기 봉투(Iv/Tag/Ct)를 받아온다. 인증 secret = 소비앱 S.</summary>
    public const string FetchEnvelope = "fetchEnvelope";

    /// <summary>마스터 GUI가 서버에서 마스터 금고 JSON을 받아온다. 인증 secret = admin A.</summary>
    public const string PullVault = "pullVault";

    /// <summary>마스터 GUI가 금고+봉투를 서버에 원자적으로 올린다. 인증 secret = admin A.</summary>
    public const string PushVault = "pushVault";
}

/// <summary>
/// TCP 요청. op에 따라 인증 secret이 달라진다(소비앱 S 또는 admin A).
///   authCode = HMAC(secret, "auth" ‖ nonce ‖ timeStep ‖ op ‖ arg)
///   arg = fetchEnvelope면 clientName, pull/push면 ""(빈 문자열).
/// </summary>
public sealed record VaultRequest
{
    /// <summary>"fetchEnvelope" | "pullVault" | "pushVault".</summary>
    public string Op { get; init; } = "";

    /// <summary>fetchEnvelope일 때 봉투를 조회할 소비 앱 이름. pull/push면 null.</summary>
    public string? Client { get; init; }

    /// <summary>클라가 계산한 time step (unixSeconds / TimeStepSeconds).</summary>
    public long TimeStep { get; init; }

    /// <summary>base64(authCode). fetchEnvelope=S 기반, pull/push=A 기반.</summary>
    public string AuthCode { get; init; } = "";

    /// <summary>pushVault일 때만: 올릴 금고 JSON + 봉투 목록.</summary>
    public PushPayload? Push { get; init; }
}

/// <summary>
/// TCP 응답. op별로 채워지는 필드가 다르다.
///   fetchEnvelope 성공: Iv/Tag/Ct
///   pullVault 성공: MasterVaultJson (서버에 금고가 없으면 null이지만 Ok=true)
///   pushVault 성공: (추가 필드 없음)
/// 실패면 Ok=false, Error(평문).
/// </summary>
public sealed record VaultResponse
{
    public bool Ok { get; init; }

    /// <summary>실패 사유(평문). Ok=false일 때만.</summary>
    public string? Error { get; init; }

    /// <summary>base64(봉투 AES-GCM IV). fetchEnvelope 성공 시.</summary>
    public string? Iv { get; init; }

    /// <summary>base64(봉투 AES-GCM 태그). fetchEnvelope 성공 시.</summary>
    public string? Tag { get; init; }

    /// <summary>base64(봉투 암호문). fetchEnvelope 성공 시.</summary>
    public string? Ct { get; init; }

    /// <summary>마스터 금고 VaultFile JSON(Kd 암호). pullVault 성공 시. 서버에 없으면 null.</summary>
    public string? MasterVaultJson { get; init; }

    public static VaultResponse Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>pushVault 페이로드: 마스터 금고 전체 + 소비앱별 봉투 목록.</summary>
public sealed record PushPayload
{
    /// <summary>마스터 금고 VaultFile JSON(Kd 암호 — 서버는 못 엶).</summary>
    public string MasterVaultJson { get; init; } = "";

    /// <summary>소비 앱별 봉투. 서버는 이걸 통째로 저장한다.</summary>
    public EnvelopeRecord[] Envelopes { get; init; } = [];
}

/// <summary>
/// 서버 디스크·와이어에 저장/전달되는 봉투 한 개(설계 §4). Iv/Tag/Ct는 EnvelopeContent를
/// 소비앱 envKey로 봉인한 것. Client는 라우팅용 평문 식별자.
/// </summary>
public sealed record EnvelopeRecord
{
    /// <summary>소비 앱 이름(평문, 서버 라우팅용 식별자).</summary>
    public string Client { get; init; } = "";

    /// <summary>base64(봉투 AES-GCM IV).</summary>
    public string Iv { get; init; } = "";

    /// <summary>base64(봉투 AES-GCM 태그).</summary>
    public string Tag { get; init; } = "";

    /// <summary>base64(봉투 암호문 = envKey로 봉인한 EnvelopeContent JSON).</summary>
    public string Ct { get; init; } = "";

    /// <summary>ISO-8601 최종 생성 시각.</summary>
    public string UpdatedAt { get; init; } = "";
}

/// <summary>
/// 봉인 전 봉투 평문(설계 §4). 서버는 절대 못 본다. 그 앱이 허용받은 키의 {전체이름: 값} 묶음.
/// </summary>
public sealed record EnvelopeContent
{
    public Dictionary<string, string> Entries { get; init; } = new();
}
