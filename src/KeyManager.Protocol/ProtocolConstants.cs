namespace KeyManager.Protocol;

/// <summary>
/// 서버(에이전트)와 소비 앱 SDK가 공유하는 프로토콜 상수.
/// 설계 문서 §9 / §10 참고.
/// </summary>
public static class ProtocolConstants
{
    /// <summary>현재 와이어 프로토콜 버전.</summary>
    public const int Version = 1;

    /// <summary>기본 Named Pipe 이름. (로컬 전용)</summary>
    public const string DefaultPipeName = "KeyManager.broker.v1";

    /// <summary>시간 기반 코드의 step 길이(초). loopback이라 시계 오차는 없지만 ±1 step 허용.</summary>
    public const int TimeStepSeconds = 30;

    /// <summary>authCode/ sessionKey 도메인 분리 라벨.</summary>
    public const string AuthLabel = "auth";
    public const string EncLabel = "enc";

    /// <summary>공유 시드 S의 바이트 길이.</summary>
    public const int SeedLengthBytes = 32;

    /// <summary>서버가 발급하는 challenge nonce 길이.</summary>
    public const int NonceLengthBytes = 16;

    /// <summary>프레임 최대 크기(바이트). 비정상 입력 방어.</summary>
    public const int MaxFrameBytes = 1 << 20; // 1 MiB
}
