using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyManager.Protocol;

/// <summary>
/// 전송 계층(②) 암호. 서버·클라가 공유 시드 S로 각자 동일하게 계산한다(설계 §9, §10.1).
///   authCode   = HMAC-SHA256(S, "auth" ‖ nonce ‖ timeStep ‖ op ‖ key)   — 인증/요청 바인딩, 전송됨
///   sessionKey = HMAC-SHA256(S, "enc"  ‖ nonce)                          — 값 암호화, 전송 안 됨
/// S와 sessionKey는 절대 와이어로 흐르지 않는다.
/// </summary>
public static class TransportCrypto
{
    public static long ComputeTimeStep(DateTimeOffset now)
        => now.ToUnixTimeSeconds() / ProtocolConstants.TimeStepSeconds;

    /// <summary>요청을 인증하고 (op, key)까지 바인딩하는 코드.</summary>
    public static byte[] ComputeAuthCode(byte[] seed, byte[] nonce, long timeStep, string op, string? key)
    {
        byte[] message = BuildCanonical(
            ProtocolConstants.AuthLabel,
            nonce,
            EncodeLong(timeStep),
            Encoding.UTF8.GetBytes(op),
            Encoding.UTF8.GetBytes(key ?? ""));
        return HMACSHA256.HashData(seed, message);
    }

    /// <summary>전송 암호화용 세션 키(AES-256). nonce마다 달라진다.</summary>
    public static byte[] DeriveSessionKey(byte[] seed, byte[] nonce)
    {
        byte[] message = BuildCanonical(ProtocolConstants.EncLabel, nonce);
        return HMACSHA256.HashData(seed, message); // 32바이트 = AES-256 키
    }

    /// <summary>sessionKey로 평문을 AES-256-GCM 암호화. (iv, tag, ciphertext) 반환.</summary>
    public static (byte[] Iv, byte[] Tag, byte[] Ciphertext) Seal(byte[] sessionKey, byte[] plaintext)
    {
        byte[] iv = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
        byte[] ciphertext = new byte[plaintext.Length];
        using var gcm = new AesGcm(sessionKey, AesGcm.TagByteSizes.MaxSize);
        gcm.Encrypt(iv, plaintext, ciphertext, tag);
        return (iv, tag, ciphertext);
    }

    /// <summary>sessionKey로 복호화. 인증 실패 시 CryptographicException.</summary>
    public static byte[] Open(byte[] sessionKey, byte[] iv, byte[] tag, byte[] ciphertext)
    {
        byte[] plaintext = new byte[ciphertext.Length];
        using var gcm = new AesGcm(sessionKey, AesGcm.TagByteSizes.MaxSize);
        gcm.Decrypt(iv, ciphertext, tag, plaintext);
        return plaintext;
    }

    public static bool ConstantTimeEquals(byte[] a, byte[] b)
        => CryptographicOperations.FixedTimeEquals(a, b);

    private static byte[] EncodeLong(long value)
    {
        byte[] buf = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buf, value);
        return buf;
    }

    /// <summary>
    /// 각 필드를 [4바이트 길이 ‖ 내용]으로 직렬화해 필드 경계 혼동을 막는다.
    /// 첫 인자(label)는 문자열, 나머지는 바이트 청크.
    /// </summary>
    private static byte[] BuildCanonical(string label, params byte[][] chunks)
    {
        using var ms = new MemoryStream();
        WriteChunk(ms, Encoding.UTF8.GetBytes(label));
        foreach (byte[] c in chunks)
            WriteChunk(ms, c);
        return ms.ToArray();

        static void WriteChunk(Stream s, byte[] data)
        {
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            s.Write(len);
            s.Write(data);
        }
    }
}
