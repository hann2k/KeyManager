using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace KeyManager.Core.Crypto;

/// <summary>
/// Argon2id 마스터 키 유도(메모리-하드). 파라미터(메모리·반복·병렬도·salt)는 vault 헤더에 평문 저장.
/// 기본값은 OWASP Argon2id 권장 최소 수준(m=19 MiB, t=2, p=1)으로 unlock 체감과 저항을 절충.
/// </summary>
public sealed class Argon2idKeyDerivation : IKeyDerivation
{
    public const int KeyBytes = 32;             // AES-256
    public const int SaltBytes = 16;
    public const int DefaultMemoryKib = 19_456; // 19 MiB (OWASP 권장 최소)
    public const int DefaultIterations = 2;
    public const int DefaultParallelism = 1;

    public string Algorithm => "Argon2id";

    public KdfParameters CreateParameters() => new()
    {
        Algorithm = Algorithm,
        Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltBytes)),
        Iterations = DefaultIterations,
        MemoryKib = DefaultMemoryKib,
        Parallelism = DefaultParallelism,
    };

    public byte[] DeriveKey(string masterPassword, KdfParameters parameters)
    {
        if (parameters.Algorithm != Algorithm)
            throw new NotSupportedException($"지원하지 않는 KDF: {parameters.Algorithm}");

        byte[] salt = Convert.FromBase64String(parameters.Salt);
        byte[] pw = Encoding.UTF8.GetBytes(masterPassword);
        try
        {
            using var argon2 = new Argon2id(pw)
            {
                Salt = salt,
                DegreeOfParallelism = parameters.Parallelism > 0 ? parameters.Parallelism : DefaultParallelism,
                MemorySize = parameters.MemoryKib > 0 ? parameters.MemoryKib : DefaultMemoryKib,
                Iterations = parameters.Iterations > 0 ? parameters.Iterations : DefaultIterations,
            };
            return argon2.GetBytes(KeyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pw);
        }
    }
}
