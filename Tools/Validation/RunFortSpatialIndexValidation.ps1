param([Parameter(Mandatory = $true)][string]$GodotPath)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validationRoot = Join-Path $repoRoot '.tmp/fort-spatial-index-validation'
New-Item -ItemType Directory -Path "$validationRoot/appdata", "$validationRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = "$validationRoot/appdata"
    $env:LOCALAPPDATA = "$validationRoot/localappdata"
    $arguments = @('--headless', '--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/ValidateFortSpatialIndex.tscn',
        '--fixed-fps', '60', '--quit-after', '3600', '--log-file', ('"{0}/integration.log"' -f $validationRoot))
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$validationRoot/stdout.log" -RedirectStandardError "$validationRoot/stderr.log"
    Get-Content -LiteralPath "$validationRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Fort spatial index validation failed (exit $($process.ExitCode)). See $validationRoot/stderr.log" }
    if (-not (Select-String -LiteralPath "$validationRoot/stdout.log" -SimpleMatch 'Fort spatial index validation passed:')) {
        throw 'Fort spatial index validation did not complete; an early engine exit is not success.'
    }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
