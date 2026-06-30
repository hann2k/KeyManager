using System.Security.Cryptography;
using System.Text;
using KeyManager.Core.Crypto;
using KeyManager.Protocol;

namespace KeyManager.Core.Tests;

public class CryptoTests
{
    [Fact]
    public void Kdf_IsDeterministic_ForSameParams()
    {
        var kdf = new Pbkdf2KeyDerivation();
        var p = kdf.CreateParameters();
        byte[] a = kdf.DeriveKey("hunter2", p);
        byte[] b = kdf.DeriveKey("hunter2", p);
        Assert.Equal(a, b);
        Assert.Equal(32, a.Length);
    }

    [Fact]
    public void Kdf_DiffersByPassword()
    {
        var kdf = new Pbkdf2KeyDerivation();
        var p = kdf.CreateParameters();
        Assert.NotEqual(kdf.DeriveKey("a", p), kdf.DeriveKey("b", p));
    }

    [Fact]
    public void AeadBox_Roundtrips()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var sealed1 = AeadBox.SealString(key, "secret-value-한글");
        Assert.Equal("secret-value-한글", AeadBox.OpenString(key, sealed1));
    }

    [Fact]
    public void AeadBox_WrongKey_Throws()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] wrong = RandomNumberGenerator.GetBytes(32);
        var sealed1 = AeadBox.SealString(key, "x");
        // 인증 실패는 CryptographicException의 파생 타입을 던질 수 있음
        Assert.ThrowsAny<CryptographicException>(() => AeadBox.OpenString(wrong, sealed1));
    }

    [Fact]
    public void TransportCrypto_SessionKey_Roundtrips()
    {
        byte[] seed = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(16);
        byte[] sk1 = TransportCrypto.DeriveSessionKey(seed, nonce);
        byte[] sk2 = TransportCrypto.DeriveSessionKey(seed, nonce);
        Assert.Equal(sk1, sk2); // 양쪽이 동일하게 유도

        byte[] plain = Encoding.UTF8.GetBytes("payload");
        var (iv, tag, ct) = TransportCrypto.Seal(sk1, plain);
        Assert.Equal(plain, TransportCrypto.Open(sk2, iv, tag, ct));
    }

    [Fact]
    public void AuthCode_And_SessionKey_AreDomainSeparated()
    {
        // authCode와 sessionKey는 둘 다 (S, nonce)에서 나오지만 라벨로 분리되어 서로 달라야 한다.
        byte[] seed = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(16);
        byte[] auth = TransportCrypto.ComputeAuthCode(seed, nonce, 100, "get", "k");
        byte[] enc = TransportCrypto.DeriveSessionKey(seed, nonce);
        Assert.NotEqual(auth, enc);
    }

    [Fact]
    public void AuthCode_BindsRequestFields()
    {
        byte[] seed = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(16);
        byte[] baseCode = TransportCrypto.ComputeAuthCode(seed, nonce, 100, "get", "k1");
        Assert.NotEqual(baseCode, TransportCrypto.ComputeAuthCode(seed, nonce, 100, "get", "k2")); // key
        Assert.NotEqual(baseCode, TransportCrypto.ComputeAuthCode(seed, nonce, 100, "list", "k1")); // op
        Assert.NotEqual(baseCode, TransportCrypto.ComputeAuthCode(seed, nonce, 101, "get", "k1")); // step
    }
}
