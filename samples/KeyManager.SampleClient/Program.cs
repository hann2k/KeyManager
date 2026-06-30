using KeyManager.Client;

// 샘플 소비 앱. 에이전트(트레이 앱)에서 클라이언트를 등록하고 받은 base64 시드를 넣어 사용.
//
// 사용법:
//   SampleClient <clientName> <base64Seed> list
//   SampleClient <clientName> <base64Seed> get <keyName>

if (args.Length < 3)
{
    Console.WriteLine("사용법:");
    Console.WriteLine("  SampleClient <clientName> <base64Seed> list");
    Console.WriteLine("  SampleClient <clientName> <base64Seed> get <keyName>");
    return 1;
}

string clientName = args[0];
string base64Seed = args[1];
string command = args[2].ToLowerInvariant();

try
{
    var km = KeyManagerClient.FromBase64Seed(clientName, base64Seed);

    switch (command)
    {
        case "list":
            var keys = await km.ListAsync();
            Console.WriteLine($"접근 가능한 키 ({keys.Count}개):");
            foreach (var k in keys) Console.WriteLine($"  - {k}");
            break;

        case "get":
            if (args.Length < 4) { Console.Error.WriteLine("키 이름이 필요합니다."); return 1; }
            string value = await km.GetAsync(args[3]);
            Console.WriteLine(value); // 평문 값 출력
            break;

        default:
            Console.Error.WriteLine($"알 수 없는 명령: {command}");
            return 1;
    }
    return 0;
}
catch (KeyManagerException ex)
{
    Console.Error.WriteLine($"오류: {ex.Message}");
    return 2;
}
