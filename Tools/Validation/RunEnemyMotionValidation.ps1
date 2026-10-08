param([Parameter(Mandatory = $true)][string]$GodotPath)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validationRoot = Join-Path $repoRoot '.tmp/enemy-motion-validation'
New-Item -ItemType Directory -Path "$validationRoot/appdata", "$validationRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = "$validationRoot/appdata"
    $env:LOCALAPPDATA = "$validationRoot/localappdata"
    $arguments = @('--headless', '--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/ValidateEnemyMotion.tscn', '--fixed-fps', '60', '--quit-after', '1800', '--log-file', ('"{0}/integration.log"' -f $validationRoot))
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$validationRoot/stdout.log" -RedirectStandardError "$validationRoot/stderr.log"
    Get-Content -LiteralPath "$validationRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Enemy motion validation failed (exit $($process.ExitCode))." }
    if (-not (Select-String -LiteralPath "$validationRoot/integration.log" -SimpleMatch 'Enemy motion validation passed:')) { throw 'Enemy motion validation did not complete. See integration.log.' }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
