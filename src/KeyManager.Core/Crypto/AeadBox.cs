using System.Security.Cryptography;
using System.Text;

namespace KeyManager.Core.Crypto;

/// <summary>디스크에 저장되는 암호문 한 조각. 모두 base64.</summary>
public sealed record SealedData
{
    public string Iv { get; init; } = "";
    public string Tag { get; init; } = "";
    public string Ct { get; init; } = "";
}

/// <summary>
/// 저장(①) 암호. Kd로 항목별 AES-256-GCM 봉인/해제(설계 §6.1).
/// 항목마다 새 IV를 쓴다.
/// </summary>
public static class AeadBox
{
    public static SealedData Seal(byte[] key, byte[] plaintext)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
        byte[] ct = new byte[plaintext.Length];
        using var gcm = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        gcm.Encrypt(iv, plaintext, ct, tag);
        return new SealedData
        {
            Iv = Convert.ToBase64String(iv),
            Tag = Convert.ToBase64String(tag),
            Ct = Convert.ToBase64String(ct),
        };
    }

    public static SealedData SealString(byte[] key, string plaintext)
        => Seal(key, Encoding.UTF8.GetBytes(plaintext));

    /// <summary>해제. 인증 실패(잘못된 키/변조) 시 CryptographicException.</summary>
    public static byte[] Open(byte[] key, SealedData data)
    {
        byte[] iv = Convert.FromBase64String(data.Iv);
        byte[] tag = Convert.FromBase64String(data.Tag);
        byte[] ct = Convert.FromBase64String(data.Ct);
        byte[] plaintext = new byte[ct.Length];
        using var gcm = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        gcm.Decrypt(iv, ct, tag, plaintext);
        return plaintext;
    }

    public static string OpenString(byte[] key, SealedData data)
        => Encoding.UTF8.GetString(Open(key, data));
}
