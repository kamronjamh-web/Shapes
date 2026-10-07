param([string]$BlenderPath)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$model = Join-Path $repoRoot 'Art/WeaponConcepts/SquareHandgun/SquareHandgun.blend'
if (-not (Test-Path -LiteralPath $model)) { throw "Model not found: $model" }
if (-not $BlenderPath) {
    $portable = Join-Path $repoRoot '.tmp/tools/blender-4.5.14-windows-x64/blender.exe'
    if (Test-Path -LiteralPath $portable) { $BlenderPath = $portable }
    else {
        $installed = Get-Command blender -ErrorAction SilentlyContinue
        if ($installed) { $BlenderPath = $installed.Source }
    }
}
if (-not $BlenderPath -or -not (Test-Path -LiteralPath $BlenderPath)) {
    throw 'Pass -BlenderPath with the path to your Blender executable.'
}
# The user explicitly requested a visible interactive Blender review window.
$process = Start-Process -FilePath $BlenderPath -ArgumentList @('--factory-startup', ('"{0}"' -f $model)) -WindowStyle Normal -PassThru
"Opened Square handgun in Blender (process $($process.Id))."
