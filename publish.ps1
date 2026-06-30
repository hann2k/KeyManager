# KeyManager 배포 패키징.
# Release 빌드 후 실행파일과 DLL을 release/ 아래로 모은다.
#   release/KeyManager.App/  → 에이전트(트레이 앱). KeyManager.App.exe 실행.
#   release/sdk/             → 소비 앱이 참조할 SDK DLL(Client + Protocol).
#
# 사용: pwsh ./publish.ps1   (또는 powershell ./publish.ps1)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$rel  = Join-Path $root 'release'

# 실행 중인 에이전트가 있으면 파일 잠금 → 종료
Get-Process KeyManager.App -ErrorAction SilentlyContinue | Stop-Process -Force

# release/ 비우고 새로
if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
New-Item -ItemType Directory -Path $rel | Out-Null

Write-Host "[1/2] 에이전트 publish..." -ForegroundColor Cyan
dotnet publish "$root/src/KeyManager.App" -c Release -o "$rel/KeyManager.App" --nologo

Write-Host "[2/2] SDK DLL 수집..." -ForegroundColor Cyan
$sdk = Join-Path $rel 'sdk'
New-Item -ItemType Directory -Path $sdk | Out-Null
dotnet build "$root/src/KeyManager.Client" -c Release --nologo -v q | Out-Null
Copy-Item "$root/src/KeyManager.Client/bin/Release/net10.0/KeyManager.Client.dll"     $sdk
Copy-Item "$root/src/KeyManager.Protocol/bin/Release/net10.0/KeyManager.Protocol.dll" $sdk

Write-Host ""
Write-Host "완료 → $rel" -ForegroundColor Green
Write-Host "  실행: release/KeyManager.App/KeyManager.App.exe"
