param([Parameter(Mandatory = $true)][string]$GodotPath, [switch]$NativeCapture)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validationRoot = Join-Path $repoRoot '.tmp/ground-loot-validation'
if ($NativeCapture) { $validationRoot = Join-Path $validationRoot 'native' }
New-Item -ItemType Directory -Path "$validationRoot/appdata", "$validationRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = "$validationRoot/appdata"
    $env:LOCALAPPDATA = "$validationRoot/localappdata"
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/ValidateGroundLoot.tscn', '--log-file', ('"{0}/integration.log"' -f $validationRoot))
    if ($NativeCapture) { $arguments += @('--max-fps', '60', '--resolution', '1280x720', '--position', '-32000,-32000', '--', '--capture') }
    else { $arguments += @('--headless', '--fixed-fps', '60', '--quit-after', '2400') }
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$validationRoot/stdout.log" -RedirectStandardError "$validationRoot/stderr.log"
    Get-Content -LiteralPath "$validationRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Ground loot validation failed (exit $($process.ExitCode))." }
    if (-not (Select-String -LiteralPath "$validationRoot/integration.log" -SimpleMatch 'Ground loot validation passed:')) { throw 'Ground loot validation did not complete. See integration.log.' }
    $unexpectedErrors = @(Get-Content -LiteralPath "$validationRoot/stdout.log", "$validationRoot/stderr.log" | Where-Object {
        ($_ -match '^(ERROR:|SCRIPT ERROR:)') -and ($_ -notmatch '^ERROR: Failed to read the root certificate store\.$')
    })
    if ($unexpectedErrors.Count -gt 0) { throw "Ground loot validation logged unexpected errors: $($unexpectedErrors -join ' | ')" }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
