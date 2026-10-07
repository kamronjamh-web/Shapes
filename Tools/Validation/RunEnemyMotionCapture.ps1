param([Parameter(Mandatory = $true)][string]$GodotPath)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$captureRoot = Join-Path $repoRoot '.tmp/enemy-motion-capture'
New-Item -ItemType Directory -Path "$captureRoot/appdata", "$captureRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = "$captureRoot/appdata"
    $env:LOCALAPPDATA = "$captureRoot/localappdata"
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/CaptureEnemyMotion.tscn', '--resolution', '1600x900', '--quit-after', '1800', '--log-file', ('"{0}/integration.log"' -f $captureRoot))
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$captureRoot/stdout.log" -RedirectStandardError "$captureRoot/stderr.log"
    Get-Content -LiteralPath "$captureRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Enemy motion capture failed (exit $($process.ExitCode))." }
    if (-not (Select-String -LiteralPath "$captureRoot/integration.log" -SimpleMatch 'Enemy motion captures complete.')) { throw 'Enemy motion capture did not complete. See integration.log.' }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
