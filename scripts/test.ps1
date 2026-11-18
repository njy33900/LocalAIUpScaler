<#
.SYNOPSIS
    Python·C# 회귀 검사와 선택적 WPF 검사를 실행합니다.
.DESCRIPTION
    각 검사 실패 시 다음 단계로 진행하지 않습니다. UI 렌더와 통합 검사는 별도 스위치로 선택합니다.
.PARAMETER RenderUI
    두 창 크기와 여러 DPI의 실제 WPF 렌더를 검사합니다.
.PARAMETER UIIntegration
    모달·취소·프리뷰 등의 WPF 통합 검사를 실행합니다.
#>
[CmdletBinding()]
param([switch]$RenderUI, [switch]$UIIntegration)
$ErrorActionPreference = 'Stop'

Set-Location (Split-Path $PSScriptRoot -Parent)
& ./.venv/Scripts/python.exe -m unittest discover -s tests -v

if ($LASTEXITCODE -ne 0) {
    throw 'Python 테스트 실패'
}

dotnet run --project tests/Upscaler.Tests -c Release

if ($LASTEXITCODE -ne 0) {
    throw 'C# 테스트 실패'
}

if ($RenderUI) {
    dotnet run --project tests/Upscaler.Tests -c Release -- --render-ui

    if ($LASTEXITCODE -ne 0) {
        throw 'UI 렌더링 검사 실패'
    }
}

if ($UIIntegration) {
    dotnet run --project tests/Upscaler.Tests -c Release -- --ui-integration

    if ($LASTEXITCODE -ne 0) {
        throw "UI 통합 테스트 실패"
    }
}
