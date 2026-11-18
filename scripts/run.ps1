<#
.SYNOPSIS
    개발 빌드를 실행하고 선택적으로 시작 폴더를 전달합니다.
.DESCRIPTION
    실행파일이 없으면 먼저 빌드합니다. 경로는 인수로 전달합니다.
.PARAMETER Folder
    앱을 시작할 이미지 폴더입니다. 생략하면 저장된 경로를 사용합니다.
#>
param([string]$Folder)
$ErrorActionPreference = 'Stop'

Set-Location (Split-Path $PSScriptRoot -Parent)
$exe=Join-Path (Get-Location) 'out/Release/Upscaler.Wpf.exe'

if (-not (Test-Path -LiteralPath $exe)) {
    & ./scripts/build.ps1
}

if ($Folder) {
    & $exe $Folder
} else {
    & $exe
}
