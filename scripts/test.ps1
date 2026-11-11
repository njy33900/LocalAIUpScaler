[CmdletBinding()]
param([switch]$RenderUI, [switch]$UIIntegration)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
& ./.venv/Scripts/python.exe -m unittest discover -s tests -v
if ($LASTEXITCODE -ne 0) { throw 'Python 테스트 실패' }
dotnet run --project tests/Upscaler.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'C# 테스트 실패' }
if ($RenderUI) {
    dotnet run --project tests/Upscaler.Tests -c Release -- --render-ui
    if ($LASTEXITCODE -ne 0) { throw 'UI 렌더링 검사 실패' }
}

if ($UIIntegration) {
    dotnet run --project tests/Upscaler.Tests -c Release -- --ui-integration
    if ($LASTEXITCODE -ne 0) { throw "UI 통합 테스트 실패" }
}
