"""샘플 소비 앱(파이썬, TCP 버전) — C# KeyManager.SampleClient의 파이썬판.

마스터 GUI에서 클라이언트를 등록하고 받은 base64 시드를 넣어 사용한다.
서버에 TCP/TLS로 접속해 자기 봉투를 받아 시드 S로 복호화한다.

사용법:
  python sample_client.py [--host H] [--port P] <clientName> <base64Seed> <list|get|getGroup> [key]

엔드포인트 해석 순서: --host/--port 플래그 > 환경변수 KM_HOST/KM_PORT > 기본 127.0.0.1:9713

예:
  python sample_client.py myapp AAAA... list
  python sample_client.py myapp AAAA... get openai
  python sample_client.py --host 192.168.0.10 --port 9713 myapp AAAA... getGroup LsOpenApi
"""

import os
import sys

# 설치 없이 바로 실행할 수 있게 상위 SDK 경로를 넣는다(패키지 설치 시엔 불필요).
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from keymanager import DEFAULT_TCP_PORT, KeyManagerClient, KeyManagerError


def print_usage() -> None:
    print("사용법:")
    print("  python sample_client.py [--host H] [--port P] <clientName> <base64Seed> <list|get|getGroup> [key]")
    print()
    print("  명령:")
    print("    list                접근 가능한 키 이름 목록")
    print("    get <key>           키 하나의 평문 값")
    print("    getGroup <prefix>   그룹 prefix 아래 {전체이름:값} 전부")
    print()
    print("  엔드포인트: --host/--port > 환경변수 KM_HOST/KM_PORT > 기본 127.0.0.1:9713")


def main(argv: list) -> int:
    host_flag = None
    port_flag = None
    positional = []

    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--host":
            if i + 1 >= len(argv):
                print("--host 값이 필요합니다.", file=sys.stderr)
                return 1
            i += 1
            host_flag = argv[i]
        elif a == "--port":
            if i + 1 >= len(argv):
                print("--port 값이 필요합니다.", file=sys.stderr)
                return 1
            i += 1
            port_flag = argv[i]
        elif a in ("-h", "--help"):
            print_usage()
            return 0
        else:
            positional.append(a)
        i += 1

    if len(positional) < 3:
        print_usage()
        return 1

    client_name = positional[0]
    base64_seed = positional[1]
    command = positional[2].lower()

    host = host_flag or os.environ.get("KM_HOST") or "127.0.0.1"
    port_text = port_flag or os.environ.get("KM_PORT")
    port = DEFAULT_TCP_PORT
    if port_text:
        try:
            port = int(port_text)
        except ValueError:
            print(f"잘못된 포트: {port_text}", file=sys.stderr)
            return 1

    try:
        km = KeyManagerClient.from_base64_seed(client_name, base64_seed, host, port)

        if command == "list":
            keys = km.list()
            print(f"접근 가능한 키 ({len(keys)}개):")
            for k in keys:
                print(f"  - {k}")
        elif command == "get":
            if len(positional) < 4:
                print("키 이름이 필요합니다.", file=sys.stderr)
                return 1
            print(km.get(positional[3]))  # 평문 값 출력
        elif command == "getgroup":
            if len(positional) < 4:
                print("그룹 prefix가 필요합니다.", file=sys.stderr)
                return 1
            group = km.get_group(positional[3])
            print(f"'{positional[3]}' 그룹 ({len(group)}개):")
            for k, val in group.items():
                print(f"  {k} = {val}")
        else:
            print(f"알 수 없는 명령: {command}", file=sys.stderr)
            print_usage()
            return 1
        return 0
    except KeyManagerError as ex:
        print(f"오류: {ex}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
