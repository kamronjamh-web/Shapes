param([Parameter(Mandatory = $true)][string]$GodotPath)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$stressRoot = Join-Path $repoRoot '.tmp/horde-stress'
New-Item -ItemType Directory -Path "$stressRoot/appdata", "$stressRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = "$stressRoot/appdata"
    $env:LOCALAPPDATA = "$stressRoot/localappdata"
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/CaptureHordeStress.tscn', '--resolution', '1600x900', '--quit-after', '6000', '--log-file', ('"{0}/integration.log"' -f $stressRoot))
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$stressRoot/stdout.log" -RedirectStandardError "$stressRoot/stderr.log"
    Get-Content -LiteralPath "$stressRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Horde stress failed (exit $($process.ExitCode))." }
    if (-not (Select-String -LiteralPath "$stressRoot/integration.log" -SimpleMatch 'Horde stress captures complete.')) { throw 'Horde stress did not complete. See integration.log.' }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
