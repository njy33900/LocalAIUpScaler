param([string]$Folder)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$exe=Join-Path (Get-Location) 'src/Upscaler.Wpf/bin/Release/net10.0-windows/Upscaler.Wpf.exe'
if (-not (Test-Path -LiteralPath $exe)) { & ./scripts/build.ps1 }
if ($Folder) { & $exe $Folder } else { & $exe }
