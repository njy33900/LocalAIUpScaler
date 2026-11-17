[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$releases = @{
    'RealESRGAN_x4plus' = 'v0.1.0'
    'RealESRGAN_x4plus_anime_6B' = 'v0.2.2.4'
    'realesr-general-x4v3' = 'v0.2.5.0'
    'realesr-general-wdn-x4v3' = 'v0.2.5.0'
}
foreach ($line in Get-Content models/SHA256SUMS) {
    $parts = $line.Trim() -split '\s+'
    if ($parts.Count -ne 2) { continue }
    $name = [IO.Path]::GetFileNameWithoutExtension($parts[1])
    $destination = Join-Path $PWD ('models/' + $parts[1])
    if (Test-Path -LiteralPath $destination) {
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() -eq $parts[0]) { continue }
        throw "모델 체크섬 불일치: $name"
    }
    $partial = $destination + '.download'
    try {
        Invoke-WebRequest "https://github.com/xinntao/Real-ESRGAN/releases/download/$($releases[$name])/$name.pth" -OutFile $partial
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $parts[0]) { throw "모델 체크섬 불일치: $name" }
        Move-Item -LiteralPath $partial -Destination $destination
    } finally {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial }
    }
}
