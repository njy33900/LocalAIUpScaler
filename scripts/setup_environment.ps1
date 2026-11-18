<#
.SYNOPSIS
    고정된 Python 환경과 모델을 준비하거나 기존 설치를 검증합니다.
.DESCRIPTION
    VerifyOnly에서는 설치·다운로드를 생략하고 의존성과 모델 무결성만 확인합니다.
.PARAMETER VerifyOnly
    환경과 모델을 변경하지 않고 검사만 수행합니다.
#>
[CmdletBinding()]
param([switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'

Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if (-not $VerifyOnly) {
    if (-not (Test-Path -LiteralPath '.venv/Scripts/python.exe')) {
        py -3.11 -m venv .venv

        if ($LASTEXITCODE -ne 0) {
            throw 'Python 3.11 가상환경 생성 실패'
        }
    }
    & ./.venv/Scripts/python.exe -m pip install --no-deps torch==2.5.1+cu124 torchvision==0.20.1+cu124 --index-url https://download.pytorch.org/whl/cu124

    if ($LASTEXITCODE -ne 0) {
        throw 'PyTorch 설치 실패'
    }
    & ./.venv/Scripts/python.exe -m pip install --no-deps -r python/requirements-deps-lock.txt --index-url https://pypi.org/simple

    if ($LASTEXITCODE -ne 0) {
        throw 'Python 의존성 설치 실패'
    }

    $model = Join-Path (Get-Location) 'models/RealESRGAN_x4plus.pth'

    if (-not (Test-Path -LiteralPath $model)) {
        $partial = $model + '.download'
        try {
            Invoke-WebRequest -Uri 'https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth' -OutFile $partial
            $expected = ((Get-Content models/SHA256SUMS -Raw).Trim() -split '\s+')[0]

            if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
                throw '모델 체크섬 불일치'
            }
            Move-Item -LiteralPath $partial -Destination $model
        } finally {
            if (Test-Path -LiteralPath $partial) {
                Remove-Item -LiteralPath $partial
            }
        }
    }
}

if (-not $VerifyOnly) {
    & ./scripts/install_models.ps1
}
& ./.venv/Scripts/python.exe -m pip check

if ($LASTEXITCODE -ne 0) {
    throw '패키지 호환성 검사 실패'
}
& ./.venv/Scripts/python.exe scripts/verify_environment.py

if ($LASTEXITCODE -ne 0) {
    throw '환경 검사 실패'
}
