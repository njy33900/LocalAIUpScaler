[CmdletBinding()]
param(
    [ValidateSet('all','diffbir_v21','supir_v0q','supir_v0f')] [string] $Engine = 'all',
    [switch] $Smoke
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path $PSScriptRoot -Parent)
$ids = if ($Engine -eq 'all') { @('diffbir_v21','supir_v0q','supir_v0f') } else { @($Engine) }
foreach ($id in $ids) {
    & ./.venv/Scripts/python.exe python/advanced/install_assets.py $id --verify
    if ($LASTEXITCODE -ne 0) { throw "모델 검사 실패: $id" }
    if ($Smoke) {
        & ./.venv/Scripts/python.exe python/advanced/smoke.py $id
        if ($LASTEXITCODE -ne 0) { throw "실제 추론 실패: $id" }
    }
}
