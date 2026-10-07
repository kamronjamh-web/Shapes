param([Parameter(Mandatory = $true)][string]$GodotPath, [switch]$ProfileOnly)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$captureRoot = Join-Path $repoRoot '.tmp/horde-piling-capture'
New-Item -ItemType Directory -Path "$captureRoot/appdata", "$captureRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = "$captureRoot/appdata"
    $env:LOCALAPPDATA = "$captureRoot/localappdata"
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/CaptureHordePiling.tscn', '--resolution', '1600x900', '--quit-after', '6000', '--log-file', ('"{0}/integration.log"' -f $captureRoot))
    if ($ProfileOnly) { $arguments = @('--headless', '--fixed-fps', '60') + $arguments + @('--', '--profiling-only') }
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$captureRoot/stdout.log" -RedirectStandardError "$captureRoot/stderr.log"
    Get-Content -LiteralPath "$captureRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Horde piling capture failed (exit $($process.ExitCode))." }
    $successMarker = if ($ProfileOnly) { 'Horde piling profiling complete.' } else { 'Horde piling captures complete.' }
    if (-not (Select-String -LiteralPath "$captureRoot/integration.log" -SimpleMatch $successMarker)) { throw 'Horde piling run did not complete. See integration.log.' }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
