# KeyManager 배포 패키징.
# Release 빌드 후 실행파일과 DLL을 release/ 아래로 모은다.
#   release/KeyManager.Server/    → 2단계(TCP) 서버 세트. 아래 두 실행파일이 같은 폴더에 공존:
#                                     - KeyManager.Server.exe → 헤드리스 콘솔/서비스 호스트(금고 보관·봉투 전달, TCP/TLS 리슨).
#                                     - KeyManager.Tray.exe    → 트레이 동반 앱. 옆의 KeyManager.Server.exe를 띄우고 상태 표시·종료.
#                                   (사용자는 KeyManager.Tray.exe를 실행 → 같은 폴더의 서버를 자동 기동.)
#   release/KeyManager.MasterGui/ → 2단계(TCP) 비상주 관리 GUI. KeyManager.MasterGui.exe 실행(pull→unlock→편집→push).
#   release/KeyManager.App/       → 1단계(Named Pipe) 단일 에이전트. KeyManager.App.exe 실행(로컬 전용, 유물 보존).
#   release/sdk/                  → 소비 앱이 참조할 .NET SDK DLL(Client + Protocol).
#   release/sdk-python/           → 파이썬 소비 앱용 SDK(순수 파이썬, cryptography만 필요).
#
# 참고: KeyManager.ServerCore는 라이브러리 → 자체 publish 단계 불필요. Server/Tray의 의존성으로 함께 딸려온다.
# Server와 Tray는 둘 다 AppContext.BaseDirectory의 server-settings.json을 읽고,
# 트레이가 자기 폴더의 서버를 기동하므로 **반드시 같은 출력 폴더**로 publish해야 한다.
#
# 사용: pwsh ./publish.ps1   (또는 powershell ./publish.ps1)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$rel  = Join-Path $root 'release'

# 실행 중인 서버/트레이/GUI/에이전트가 있으면 파일 잠금 → 종료
foreach ($proc in 'KeyManager.Server', 'KeyManager.Tray', 'KeyManager.MasterGui', 'KeyManager.App') {
    Get-Process $proc -ErrorAction SilentlyContinue | Stop-Process -Force
}

# release/ 비우고 새로
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
New-Item -ItemType Directory -Path $rel | Out-Null

Write-Host "[1/6] TCP 서버(헤드리스 콘솔 호스트) publish..." -ForegroundColor Cyan
dotnet publish "$root/src/KeyManager.Server" -c Release -o "$rel/KeyManager.Server" --nologo

Write-Host "[2/6] 트레이 동반 앱 publish (서버와 같은 폴더로)..." -ForegroundColor Cyan
dotnet publish "$root/src/KeyManager.Tray" -c Release -o "$rel/KeyManager.Server" --nologo

Write-Host "[3/6] 마스터 GUI(비상주) publish..." -ForegroundColor Cyan
dotnet publish "$root/src/KeyManager.MasterGui" -c Release -o "$rel/KeyManager.MasterGui" --nologo

Write-Host "[4/6] 1단계 에이전트(Named Pipe) publish..." -ForegroundColor Cyan
dotnet publish "$root/src/KeyManager.App" -c Release -o "$rel/KeyManager.App" --nologo

Write-Host "[5/6] .NET SDK DLL 수집..." -ForegroundColor Cyan
$sdk = Join-Path $rel 'sdk'
New-Item -ItemType Directory -Path $sdk | Out-Null
dotnet build "$root/src/KeyManager.Client" -c Release --nologo -v q | Out-Null
Copy-Item "$root/src/KeyManager.Client/bin/Release/net10.0/KeyManager.Client.dll"     $sdk
Copy-Item "$root/src/KeyManager.Protocol/bin/Release/net10.0/KeyManager.Protocol.dll" $sdk

Write-Host "[6/6] 파이썬 SDK 수집..." -ForegroundColor Cyan
$pysdk = Join-Path $rel 'sdk-python'
New-Item -ItemType Directory -Path $pysdk | Out-Null
Copy-Item "$root/sdk/python/keymanager" $pysdk -Recurse
Copy-Item "$root/sdk/python/examples"   $pysdk -Recurse
Copy-Item "$root/sdk/python/pyproject.toml" $pysdk
Copy-Item "$root/sdk/python/README.md"      $pysdk

Write-Host ""
Write-Host "완료 → $rel" -ForegroundColor Green
Write-Host "  2단계(TCP) 트레이:    release/KeyManager.Server/KeyManager.Tray.exe   (실행 진입점, 옆의 서버를 자동 기동)"
Write-Host "  2단계(TCP) 서버:      release/KeyManager.Server/KeyManager.Server.exe   (헤드리스 콘솔 호스트, 최초 실행 시 admin 토큰 A 출력)"
Write-Host "  2단계(TCP) 관리 GUI:  release/KeyManager.MasterGui/KeyManager.MasterGui.exe   (비상주)"
Write-Host "  1단계(Named Pipe):    release/KeyManager.App/KeyManager.App.exe   (로컬 유물)"
Write-Host "  .NET SDK:             release/sdk/  (KeyManager.Client.dll + KeyManager.Protocol.dll)"
Write-Host "  Python SDK:           release/sdk-python/  (keymanager 패키지 + 예제, pip install cryptography 필요)"
