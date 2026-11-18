<#
.SYNOPSIS
    Release 구성으로 WPF와 테스트 프로젝트를 빌드합니다.
.DESCRIPTION
    솔루션 빌드의 실패 종료 코드를 호출자에게 전달합니다.
#>
$ErrorActionPreference = 'Stop'

Set-Location (Split-Path $PSScriptRoot -Parent)

dotnet build LocalAIUpscaler.sln -c Release

if ($LASTEXITCODE -ne 0) {
    throw '빌드 실패'
}
