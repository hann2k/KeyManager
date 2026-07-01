using System.Text.Json;

namespace KeyManager.MasterGui;

/// <summary>
/// MasterGui 전용 설정(설계 §8). %APPDATA%\KeyManager\master-gui.json.
/// 서버 접속 정보(호스트/포트/admin 토큰 A base64)와 언어를 보관한다.
/// 서버 파일(server-store.json 등)과는 별개 — 재사용 금지.
/// </summary>
internal sealed class MasterSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = KeyManager.Protocol.ProtocolConstants.DefaultTcpPort;

    /// <summary>base64(admin 토큰 A, 32B).</summary>
    public string AdminTokenBase64 { get; set; } = "";

    /// <summary>"en" 또는 "ko". 기본 영어.</summary>
    public string Language { get; set; } = "en";

    // ---- 경로 ----

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyManager");

    private static string FilePath => Path.Combine(Dir, "master-gui.json");

    /// <summary>서버 TLS 인증서 TOFU pin 파일(호스트별).</summary>
    public string PinFilePath => Path.Combine(Dir, $"pinned-{Sanitize(Host)}.txt");

    /// <summary>설정 파일 존재 여부(= 최초 실행 아님).</summary>
    public static bool Exists => File.Exists(FilePath);

    /// <summary>접속에 필요한 최소 정보가 채워졌는지.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && Port is > 0 and <= 65535 && !string.IsNullOrWhiteSpace(AdminTokenBase64);

    public static MasterSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<MasterSettings>(File.ReadAllBytes(FilePath)) ?? new MasterSettings();
        }
        catch { /* 손상 시 기본값 */ }
        return new MasterSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(FilePath, JsonSerializer.SerializeToUtf8Bytes(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Sanitize(string host)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) host = host.Replace(c, '_');
        return host;
    }
}
