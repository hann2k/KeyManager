using KeyManager.Client;
using KeyManager.Protocol;

// 샘플 소비 앱(TCP 버전). 마스터 GUI에서 클라이언트를 등록하고 받은 base64 시드를 넣어 사용.
// 서버에 TCP/TLS로 접속해 자기 봉투를 받아 시드 S로 복호화한다.
//
// 사용법:
//   SampleClient [--host H] [--port P] <clientName> <base64Seed> <list|get|getGroup> [key]
//
// 엔드포인트 해석 순서: --host/--port 플래그 > 환경변수 KM_HOST/KM_PORT > 기본 127.0.0.1:9713.
//
// 예:
//   SampleClient myapp AAAA... list
//   SampleClient myapp AAAA... get openai
//   SampleClient --host 192.168.0.10 --port 9713 myapp AAAA... getGroup LsOpenApi

static void PrintUsage()
{
    Console.WriteLine("사용법:");
    Console.WriteLine("  SampleClient [--host H] [--port P] <clientName> <base64Seed> <list|get|getGroup> [key]");
    Console.WriteLine();
    Console.WriteLine("  명령:");
    Console.WriteLine("    list                접근 가능한 키 이름 목록");
    Console.WriteLine("    get <key>           키 하나의 평문 값");
    Console.WriteLine("    getGroup <prefix>   그룹 prefix 아래 {전체이름:값} 전부");
    Console.WriteLine();
    Console.WriteLine("  엔드포인트: --host/--port > 환경변수 KM_HOST/KM_PORT > 기본 127.0.0.1:9713");
}

// --host / --port 플래그를 먼저 뽑아내고 나머지는 위치 인자로 남긴다.
string? hostFlag = null;
string? portFlag = null;
var positional = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--host 값이 필요합니다."); return 1; }
            hostFlag = args[++i];
            break;
        case "--port":
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--port 값이 필요합니다."); return 1; }
            portFlag = args[++i];
            break;
        case "-h":
        case "--help":
            PrintUsage();
            return 0;
        default:
            positional.Add(args[i]);
            break;
    }
}

if (positional.Count < 3)
{
    PrintUsage();
    return 1;
}

string clientName = positional[0];
string base64Seed = positional[1];
string command = positional[2].ToLowerInvariant();

// 엔드포인트: 플래그 > 환경변수 > 기본값.
string host = hostFlag
    ?? Environment.GetEnvironmentVariable("KM_HOST")
    ?? "127.0.0.1";

string? portText = portFlag ?? Environment.GetEnvironmentVariable("KM_PORT");
int port = ProtocolConstants.DefaultTcpPort;
if (!string.IsNullOrEmpty(portText))
{
    if (!int.TryParse(portText, out port))
    {
        Console.Error.WriteLine($"잘못된 포트: {portText}");
        return 1;
    }
}

try
{
    var km = KeyManagerClient.FromBase64Seed(clientName, base64Seed, host, port);

    switch (command)
    {
        case "list":
            var keys = await km.ListAsync();
            Console.WriteLine($"접근 가능한 키 ({keys.Count}개):");
            foreach (var k in keys) Console.WriteLine($"  - {k}");
            break;

        case "get":
            if (positional.Count < 4) { Console.Error.WriteLine("키 이름이 필요합니다."); return 1; }
            string value = await km.GetAsync(positional[3]);
            Console.WriteLine(value); // 평문 값 출력
            break;

        case "getgroup":
            if (positional.Count < 4) { Console.Error.WriteLine("그룹 prefix가 필요합니다."); return 1; }
            var group = await km.GetGroupAsync(positional[3]);
            Console.WriteLine($"'{positional[3]}' 그룹 ({group.Count}개):");
            foreach (var kv in group) Console.WriteLine($"  {kv.Key} = {kv.Value}");
            break;

        default:
            Console.Error.WriteLine($"알 수 없는 명령: {command}");
            PrintUsage();
            return 1;
    }
    return 0;
}
catch (KeyManagerException ex)
{
    Console.Error.WriteLine($"오류: {ex.Message}");
    return 2;
}
