using System.Text.Json;
using KeyManager.Protocol;

namespace KeyManager.Server;

/// <summary>
/// 서버 전용 설정. <b>실행파일과 같은 폴더</b>의 server-settings.json.
/// 리슨 포트만 보관(비밀 아님). 포트를 바꾸려면 이 파일의 Port를 고치거나
/// 환경변수 KM_SERVER_PORT를 설정한 뒤 서버를 재시작한다(환경변수가 파일값보다 우선).
/// 포트를 바꾸면 소비 앱·마스터 GUI의 접속 포트도 같은 값으로 맞춰야 한다.
/// </summary>
public sealed class ServerSettings
{
    public int Port { get; set; } = ProtocolConstants.DefaultTcpPort;

    // 설정 파일은 실행파일과 같은 폴더에 둔다(휴대성·찾기 쉬움). 금고/인증서는 %APPDATA% 그대로.
    private static string Dir => AppContext.BaseDirectory;

    private static string FilePath => Path.Combine(Dir, "server-settings.json");

    /// <summary>
    /// 설정 로드. 파일이 없으면 기본값(9713)으로 생성해 사용자가 편집할 수 있게 한다.
    /// 환경변수 KM_SERVER_PORT가 유효하면 그 값을 우선 적용(파일엔 기록하지 않음 — 전이적).
    /// </summary>
    public static ServerSettings Load()
    {
        bool existed = File.Exists(FilePath);
        ServerSettings s;
        try
        {
            s = existed
                ? JsonSerializer.Deserialize<ServerSettings>(File.ReadAllBytes(FilePath)) ?? new ServerSettings()
                : new ServerSettings();
        }
        catch { s = new ServerSettings(); }

        if (s.Port is <= 0 or > 65535) s.Port = ProtocolConstants.DefaultTcpPort;

        if (!existed) s.Save(); // 기본값 파일 시드(사용자가 열어 고칠 수 있게)

        string? env = Environment.GetEnvironmentVariable("KM_SERVER_PORT");
        if (int.TryParse(env, out int envPort) && envPort is > 0 and <= 65535)
            s.Port = envPort;

        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(FilePath, JsonSerializer.SerializeToUtf8Bytes(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 설정 저장 실패는 치명적이지 않음 */ }
    }
}
