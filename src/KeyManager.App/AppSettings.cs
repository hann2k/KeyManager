using System.Text.Json;

namespace KeyManager.App;

/// <summary>%APPDATA%\KeyManager\settings.json 에 저장되는 앱 설정(언어 등).</summary>
internal sealed class AppSettings
{
    /// <summary>"en" 또는 "ko". 기본 영어.</summary>
    public string Language { get; set; } = "en";

    private static string Path => System.IO.Path.Combine(AppPaths.Dir, "settings.json");

    /// <summary>설정 파일 존재 여부 = 최초 실행이 아님.</summary>
    public static bool Exists => File.Exists(Path);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(Path)) ?? new AppSettings();
        }
        catch { /* 손상 시 기본값 */ }
        return new AppSettings();
    }

    public void Save()
    {
        AppPaths.EnsureDir();
        File.WriteAllBytes(Path, JsonSerializer.SerializeToUtf8Bytes(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
