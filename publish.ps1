# KeyManager 배포 패키징.
# Release 빌드 후 실행파일과 DLL을 release/ 아래로 모은다.
#   release/KeyManager.Server/    → 2단계(TCP) 상주 서버. KeyManager.Server.exe 실행(금고 보관·봉투 전달, 트레이 상주).
#   release/KeyManager.MasterGui/ → 2단계(TCP) 비상주 관리 GUI. KeyManager.MasterGui.exe 실행(pull→unlock→편집→push).
#   release/KeyManager.App/       → 1단계(Named Pipe) 단일 에이전트. KeyManager.App.exe 실행(로컬 전용, 유물 보존).
#   release/sdk/                  → 소비 앱이 참조할 SDK DLL(Client + Protocol).
#
# 사용: pwsh ./publish.ps1   (또는 powershell ./publish.ps1)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$rel  = Join-Path $root 'release'

# 실행 중인 서버/GUI/에이전트가 있으면 파일 잠금 → 종료
foreach ($proc in 'KeyManager.Server', 'KeyManager.MasterGui', 'KeyManager.App') {
    Get-Process $proc -ErrorAction SilentlyContinue | Stop-Process -Force
}

# release/ 비우고 새로
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
New-Item -ItemType Directory -Path $rel | Out-Null

Write-Host "[1/4] TCP 서버(상주) publish..." -ForegroundColor Cyan
dotnet publish "$root/src/KeyManager.Server" -c Release -o "$rel/KeyManager.Server" --nologo

Write-Host "[2/4] 마스터 GUI(비상주) publish..." -ForegroundColor Cyan
dotnet publish "$root/src/KeyManager.MasterGui" -c Release -o "$rel/KeyManager.MasterGui" --nologo

Write-Host "[3/4] 1단계 에이전트(Named Pipe) publish..." -ForegroundColor Cyan
dotnet publish "$root/src/KeyManager.App" -c Release -o "$rel/KeyManager.App" --nologo

Write-Host "[4/4] SDK DLL 수집..." -ForegroundColor Cyan
$sdk = Join-Path $rel 'sdk'
New-Item -ItemType Directory -Path $sdk | Out-Null
dotnet build "$root/src/KeyManager.Client" -c Release --nologo -v q | Out-Null
Copy-Item "$root/src/KeyManager.Client/bin/Release/net10.0/KeyManager.Client.dll"     $sdk
Copy-Item "$root/src/KeyManager.Protocol/bin/Release/net10.0/KeyManager.Protocol.dll" $sdk

Write-Host ""
Write-Host "완료 → $rel" -ForegroundColor Green
Write-Host "  2단계(TCP) 서버:      release/KeyManager.Server/KeyManager.Server.exe   (상주, 최초 실행 시 admin 토큰 A 출력)"
Write-Host "  2단계(TCP) 관리 GUI:  release/KeyManager.MasterGui/KeyManager.MasterGui.exe   (비상주)"
Write-Host "  1단계(Named Pipe):    release/KeyManager.App/KeyManager.App.exe   (로컬 유물)"
Write-Host "  SDK:                  release/sdk/  (KeyManager.Client.dll + KeyManager.Protocol.dll)"
