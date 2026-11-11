$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet build LocalAIUpscaler.sln -c Release
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }
