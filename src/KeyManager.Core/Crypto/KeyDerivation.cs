using System.Security.Cryptography;

namespace KeyManager.Core.Crypto;

/// <summary>
/// 마스터 암호 → 저장 암호화 키 Kd 유도(설계 §5). 키는 디스크에 저장하지 않고
/// salt + 파라미터만 저장해 unlock 시점마다 "수식"으로 재현한다.
///
/// 기본 KDF는 <see cref="Argon2idKeyDerivation"/>(메모리-하드, GPU 크래킹 저항).
/// 구 vault(PBKDF2-SHA256)는 파일의 algorithm으로 <see cref="KeyDerivations.Resolve"/>가
/// 골라 그대로 열 수 있고, 암호 변경 시 Argon2id로 자동 이관된다.
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

    /// <summary>Argon2 메모리 비용(KiB). PBKDF2는 0/무시.</summary>
    public int MemoryKib { get; init; }

    /// <summary>Argon2 병렬도(lane). PBKDF2는 0/무시.</summary>
    public int Parallelism { get; init; }
}

/// <summary>파일에 기록된 알고리즘 이름으로 알맞은 KDF 구현을 고른다(구 vault 호환).</summary>
public static class KeyDerivations
{
    /// <summary>새 vault·암호 변경 시 사용하는 기본 KDF.</summary>
    public static IKeyDerivation Default { get; } = new Argon2idKeyDerivation();

    public static IKeyDerivation Resolve(string algorithm) => algorithm switch
    {
        "Argon2id" => new Argon2idKeyDerivation(),
        "PBKDF2-SHA256" => new Pbkdf2KeyDerivation(),
        _ => throw new NotSupportedException($"지원하지 않는 KDF: {algorithm}"),
    };
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
