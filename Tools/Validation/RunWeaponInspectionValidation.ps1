param([Parameter(Mandatory = $true)][string]$GodotPath, [switch]$Native)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validationRoot = Join-Path $repoRoot '.tmp/weapon-inspection-validation'
New-Item -ItemType Directory -Path "$validationRoot/appdata", "$validationRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = "$validationRoot/appdata"
    $env:LOCALAPPDATA = "$validationRoot/localappdata"
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/ValidateWeaponInspection.tscn', '--fixed-fps', '60', '--quit-after', '3600', '--log-file', ('"{0}/integration.log"' -f $validationRoot))
    if ($Native) { $arguments += @('--resolution', '1600x1000') }
    else { $arguments += '--headless' }
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$validationRoot/stdout.log" -RedirectStandardError "$validationRoot/stderr.log"
    Get-Content -LiteralPath "$validationRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Weapon inspection validation failed (exit $($process.ExitCode))." }
    if (-not (Select-String -LiteralPath "$validationRoot/integration.log" -SimpleMatch 'Weapon inspection validation passed:')) { throw 'Weapon inspection validation did not complete successfully. See integration.log.' }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
