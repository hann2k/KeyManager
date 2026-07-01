using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace KeyManager.Protocol;

/// <summary>
/// TCP 버전(설계 §7 "TLS 신뢰 TOFU")용 TLS 헬퍼. 두 축:
///   (a) 서버측: 자체서명 인증서 생성/로드(PFX 보관).
///   (b) 클라측: 최초 접속 시 thumbprint를 pin 파일에 고정(TOFU), 이후 불일치면 거부.
/// 클라측(SslStream)은 non-Windows에서도 동작해야 하므로 BCL만 사용한다.
/// </summary>
public static class TlsSupport
{
    /// <summary>자체서명 인증서 CN(서버 라우팅과 무관, 신뢰는 thumbprint pin으로 함).</summary>
    public const string DefaultSubject = "CN=KeyManager Server";

    // ---- (a) 서버 인증서 ------------------------------------------------

    /// <summary>
    /// 자체서명 서버 인증서(ECDSA P-256, 개인키 포함)를 새로 만든다. 유효기간 기본 10년.
    /// TLS 서버 인증 EKU를 넣는다.
    /// </summary>
    public static X509Certificate2 CreateSelfSignedServerCert(
        string subject = DefaultSubject, int validYears = 10)
    {
        using ECDsa ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest(subject, ec, HashAlgorithmName.SHA256);

        // 서버 인증(TLS Web Server Authentication) EKU.
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 created = req.CreateSelfSigned(now.AddDays(-1), now.AddYears(validYears));

        // PFX로 export 후 재로드해야 개인키가 SslStream.AuthenticateAsServer에서 안정적으로 쓰인다.
        // 주의: Windows SChannel의 서버 인증은 ephemeral 키를 지원하지 않으므로
        // EphemeralKeySet을 쓰면 "platform does not support ephemeral keys"로 실패한다 →
        // ServerKeyStorageFlags(Exportable, ephemeral 아님)로 로드한다.
        byte[] pfx = created.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, ServerKeyStorageFlags);
    }

    /// <summary>
    /// 서버 인증서 로드 플래그. EphemeralKeySet은 Windows 서버 인증에서 미지원이라 제외.
    /// Exportable만 지정(개인키를 다시 export할 수 있게).
    /// </summary>
    private const X509KeyStorageFlags ServerKeyStorageFlags = X509KeyStorageFlags.Exportable;

    /// <summary>
    /// PFX 파일이 있으면 로드하고, 없으면 자체서명 인증서를 만들어 저장한 뒤 반환한다.
    /// 개인키를 파일에 안전하게 보관해야 하는 서버측 프로비저닝 진입점.
    /// </summary>
    public static X509Certificate2 LoadOrCreateServerCert(string pfxPath, string? password = null)
    {
        if (File.Exists(pfxPath))
            return LoadServerCert(pfxPath, password);

        using X509Certificate2 fresh = CreateSelfSignedServerCert();
        byte[] pfx = fresh.Export(X509ContentType.Pfx, password);
        string? dir = Path.GetDirectoryName(pfxPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(pfxPath, pfx);
        return LoadServerCert(pfxPath, password);
    }

    /// <summary>PFX에서 개인키 포함 인증서를 로드. AuthenticateAsServer가 개인키를 요구하므로
    /// Exportable로 로드한다(EphemeralKeySet은 Windows 서버 인증 미지원이라 제외).</summary>
    public static X509Certificate2 LoadServerCert(string pfxPath, string? password = null)
        => X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password, ServerKeyStorageFlags);

    // ---- (b) 클라 TOFU thumbprint pin -----------------------------------

    /// <summary>인증서의 SHA-256 thumbprint를 소문자 hex로.</summary>
    public static string ComputeThumbprint(X509Certificate2 cert)
        => Convert.ToHexStringLower(SHA256.HashData(cert.RawData));

    /// <summary>
    /// TOFU 검증 콜백을 만든다. pin 파일이 없으면 최초 thumbprint를 기록(신뢰)하고,
    /// 있으면 원격 인증서 thumbprint와 상수시간 비교해 일치할 때만 신뢰한다.
    /// self-signed라 체인 검증은 무시하고 thumbprint pin만 신뢰 근거로 삼는다.
    /// <paramref name="allowUnpinned"/>=true면 pin이 없을 때 각인만 하고 통과(1단계 기본).
    /// </summary>
    public static RemoteCertificateValidationCallback CreateTofuValidator(string pinFilePath)
    {
        return (_, cert, _, _) =>
        {
            if (cert is null) return false;
            // X509Certificate → X509Certificate2로 안전하게 thumbprint 계산(RawData 사용).
            string remote = Convert.ToHexStringLower(SHA256.HashData(cert.GetRawCertData()));

            string? pinned = ReadPin(pinFilePath);
            if (pinned is null)
            {
                // TOFU: 최초 접속 → 각인.
                WritePin(pinFilePath, remote);
                return true;
            }

            // 상수시간 비교(hex 문자열 → 바이트 아님이지만 FixedTimeEquals로 타이밍 노출 최소화).
            return FixedTimeStringEquals(pinned, remote);
        };
    }

    /// <summary>기본 pin 파일 경로: <paramref name="baseDir"/>\pinned-&lt;host&gt;.txt (설계 §7).</summary>
    public static string DefaultPinPath(string baseDir, string host)
    {
        string safeHost = string.Concat(host.Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return Path.Combine(baseDir, $"pinned-{safeHost}.txt");
    }

    private static string? ReadPin(string pinFilePath)
    {
        try
        {
            if (!File.Exists(pinFilePath)) return null;
            string s = File.ReadAllText(pinFilePath).Trim();
            return s.Length == 0 ? null : s;
        }
        catch { return null; }
    }

    private static void WritePin(string pinFilePath, string thumbprint)
    {
        try
        {
            string? dir = Path.GetDirectoryName(pinFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(pinFilePath, thumbprint);
        }
        catch { /* pin 저장 실패는 치명적이지 않음(다음 접속 때 재각인 시도) */ }
    }

    private static bool FixedTimeStringEquals(string a, string b)
    {
        byte[] ab = System.Text.Encoding.ASCII.GetBytes(a);
        byte[] bb = System.Text.Encoding.ASCII.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
