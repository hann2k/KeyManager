namespace KeyManager.Protocol;

/// <summary>
/// 와이어 메시지 정의. 프로토콜은 고정 순서이므로 타입 판별자 없이
/// 단계별로 정해진 타입을 주고받는다:
///   서버 → ServerHello → 클라
///   클라 → ClientRequest → 서버
///   서버 → ServerResponse → 클라
/// </summary>

/// <summary>서버가 연결 직후 발급하는 challenge.</summary>
public sealed record ServerHello
{
    public int Protocol { get; init; } = ProtocolConstants.Version;

    /// <summary>base64(서버가 생성한 랜덤 nonce). 재전송 차단용.</summary>
    public string Nonce { get; init; } = "";
}

/// <summary>소비 앱의 요청.</summary>
public sealed record ClientRequest
{
    /// <summary>등록된 소비 앱 이름.</summary>
    public string Client { get; init; } = "";

    /// <summary>"get" 또는 "list".</summary>
    public string Op { get; init; } = "";

    /// <summary>op=="get"일 때 요청 키 이름. list면 무시.</summary>
    public string? Key { get; init; }

    /// <summary>클라가 계산한 time step (unixSeconds / TimeStepSeconds).</summary>
    public long TimeStep { get; init; }

    /// <summary>base64(HMAC(S, "auth" ‖ nonce ‖ timeStep ‖ op ‖ key)).</summary>
    public string AuthCode { get; init; } = "";
}

/// <summary>
/// 서버 응답. 성공이면 payload는 sessionKey로 AES-GCM 암호화된 결과 JSON.
/// 실패면 Ok=false, Error에 사유(평문).
/// </summary>
public sealed record ServerResponse
{
    public bool Ok { get; init; }

    /// <summary>실패 사유(평문). Ok=false일 때만.</summary>
    public string? Error { get; init; }

    /// <summary>base64(AES-GCM IV, 12바이트). Ok=true일 때만.</summary>
    public string? Iv { get; init; }

    /// <summary>base64(AES-GCM 인증 태그, 16바이트). Ok=true일 때만.</summary>
    public string? Tag { get; init; }

    /// <summary>base64(암호문). 복호화하면 결과 JSON(GetResult/ListResult).</summary>
    public string? Payload { get; init; }

    public static ServerResponse Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>get 응답의 복호화된 평문 JSON.</summary>
public sealed record GetResult
{
    public string Value { get; init; } = "";
}

/// <summary>list 응답의 복호화된 평문 JSON.</summary>
public sealed record ListResult
{
    public string[] Keys { get; init; } = [];
}

/// <summary>getGroup 응답의 복호화된 평문 JSON. 전체 콜론 이름 → 값.</summary>
public sealed record GroupResult
{
    public Dictionary<string, string> Entries { get; init; } = new();
}
