param([Parameter(Mandatory = $true)][string]$GodotPath, [switch]$AllShapes)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$captureRoot = Join-Path $repoRoot '.tmp/weapon-inspection-capture'
New-Item -ItemType Directory -Path "$captureRoot/appdata", "$captureRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
$previousCaptureAll = $env:WEAPON_INSPECTION_CAPTURE_ALL
try {
    $env:APPDATA = "$captureRoot/appdata"
    $env:LOCALAPPDATA = "$captureRoot/localappdata"
    $env:WEAPON_INSPECTION_CAPTURE_ALL = if ($AllShapes) { '1' } else { '0' }
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/CaptureWeaponInspection.tscn', '--resolution', '1600x1000', '--fixed-fps', '60', '--quit-after', '1800', '--log-file', ('"{0}/capture.log"' -f $captureRoot))
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$captureRoot/stdout.log" -RedirectStandardError "$captureRoot/stderr.log"
    Get-Content -LiteralPath "$captureRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Weapon inspection capture failed (exit $($process.ExitCode))." }
    if (-not (Select-String -LiteralPath "$captureRoot/capture.log" -SimpleMatch 'Weapon inspection capture passed.')) { throw 'Weapon inspection capture did not complete successfully.' }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
    $env:WEAPON_INSPECTION_CAPTURE_ALL = $previousCaptureAll
}
