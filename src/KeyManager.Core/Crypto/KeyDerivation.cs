using System.Security.Cryptography;

namespace KeyManager.Core.Crypto;

/// <summary>
/// 마스터 암호 → 저장 암호화 키 Kd 유도(설계 §5). 키는 디스크에 저장하지 않고
/// salt + 파라미터만 저장해 unlock 시점마다 "수식"으로 재현한다.
///
/// 1단계는 BCL 내장 PBKDF2-SHA256 사용(외부 패키지 불필요).
/// 설계상 의도된 프로덕션 KDF는 Argon2id이며, <see cref="IKeyDerivation"/>로
/// 추상화해 두어 추후 무중단 교체가 가능하다(파라미터에 algorithm 기록).
/// </summary>
public interface IKeyDerivation
{
    string Algorithm { get; }

    /// <summary>새 vault용 기본 파라미터(랜덤 salt 포함) 생성.</summary>
    KdfParameters CreateParameters();

    /// <summary>마스터 암호 + 파라미터 → 32바이트 Kd(AES-256).</summary>
    byte[] DeriveKey(string masterPassword, KdfParameters parameters);
}

/// <summary>vault 헤더에 평문으로 저장되는 KDF 파라미터(비밀 아님).</summary>
public sealed record KdfParameters
{
    public string Algorithm { get; init; } = "";
    public string Salt { get; init; } = "";       // base64
    public int Iterations { get; init; }
}

public sealed class Pbkdf2KeyDerivation : IKeyDerivation
{
    public const int KeyBytes = 32;       // AES-256
    public const int SaltBytes = 16;
    public const int DefaultIterations = 600_000; // OWASP PBKDF2-SHA256 권장 수준

    public string Algorithm => "PBKDF2-SHA256";

    public KdfParameters CreateParameters() => new()
    {
        Algorithm = Algorithm,
        Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltBytes)),
        Iterations = DefaultIterations,
    };

    public byte[] DeriveKey(string masterPassword, KdfParameters parameters)
    {
        if (parameters.Algorithm != Algorithm)
            throw new NotSupportedException($"지원하지 않는 KDF: {parameters.Algorithm}");

        byte[] salt = Convert.FromBase64String(parameters.Salt);
        return Rfc2898DeriveBytes.Pbkdf2(
            masterPassword,
            salt,
            parameters.Iterations,
            HashAlgorithmName.SHA256,
            KeyBytes);
    }
}
