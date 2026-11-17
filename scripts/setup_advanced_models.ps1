[CmdletBinding()]
param(
    [ValidateSet('all', 'diffbir', 'supir')] [string] $Engine = 'all',
    [switch] $DownloadModels
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$python = Join-Path $root '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw '먼저 기존 setup_environment.ps1을 실행하세요.' }
function Run-Checked([string] $exe, [string[]] $arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "실행 실패 ($LASTEXITCODE): $exe" }
}
$revisions = @{
    diffbir = '5c2d6c177c3f2965e6d33a32931248d242ef7b3d'
    supir = 'bda91af2000042f8bedfec8897d92917e67c1d88'
}
$repos = @{ diffbir = 'https://github.com/XPixelGroup/DiffBIR.git'; supir = 'https://github.com/Fanghua-Yu/SUPIR.git' }
$targets = if ($Engine -eq 'all') { @('diffbir','supir') } else { @($Engine) }
foreach ($target in $targets) {
    $upstream = Join-Path $root "artifacts\advanced-upstream\$target"
    if (-not (Test-Path -LiteralPath (Join-Path $upstream '.git'))) {
        Run-Checked git @('clone', $repos[$target], $upstream)
        Run-Checked git @('-C', $upstream, 'checkout', '--detach', $revisions[$target])
    }
    $revision = & git -C $upstream rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $revision -ne $revisions[$target]) { throw "업스트림 revision 불일치: $target" }
    if (& git -C $upstream status --porcelain --untracked-files=no) { throw "수정된 업스트림을 덮어쓰지 않습니다: $target" }
    $envPath = Join-Path $root ".advanced\env-$target"
    $envPython = Join-Path $envPath 'Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $envPython)) { Run-Checked $python @('-m','venv',$envPath) }
    $lock = Join-Path $root "python\requirements-$target.lock.txt"
    if (Test-Path -LiteralPath $lock) {
        Run-Checked $envPython @('-m','pip','install','-r',$lock,'--extra-index-url','https://download.pytorch.org/whl/cu121')
    } else {
        Run-Checked $envPython @('-m','pip','install','torch==2.2.2','torchvision==0.17.2','--index-url','https://download.pytorch.org/whl/cu121')
        Run-Checked $envPython @('-m','pip','install','-r',"python/requirements-$target.txt",'xformers==0.0.25.post1')
    }
    Run-Checked $envPython @('-m','pip','check')
    $ids = if ($target -eq 'diffbir') { @('diffbir_v21') } else { @('supir_v0q','supir_v0f') }
    if ($target -eq 'supir' -and $DownloadModels) {
        Run-Checked $envPython @('python/advanced/download_supir.py')
    }
    foreach ($id in $ids) {
        $arguments = @('python/advanced/install_assets.py',$id)
        if ($DownloadModels) { $arguments += '--download' }
        Run-Checked $python $arguments
    }
}
Write-Host '환경/파일 설치를 마쳤습니다. verify_advanced_models.ps1 -Smoke로 실제 추론을 검증해야 UI가 활성화됩니다.'
