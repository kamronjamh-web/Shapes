param([Parameter(Mandatory = $true)][string]$GodotPath, [switch]$NativeCapture)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validationRoot = Join-Path $repoRoot '.tmp/stepped-stronghold-validation'
if ($NativeCapture) { $validationRoot = Join-Path $validationRoot 'native' }
New-Item -ItemType Directory -Path "$validationRoot/appdata", "$validationRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = "$validationRoot/appdata"
    $env:LOCALAPPDATA = "$validationRoot/localappdata"
    if ($NativeCapture) {
        $importArguments = @('--path', ('"{0}"' -f $repoRoot), '--editor', '--import', '--quit', '--position', '-32000,-32000', '--rendering-method', 'forward_plus', '--rendering-driver', 'vulkan')
        $importProcess = Start-Process -FilePath $GodotPath -ArgumentList $importArguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$validationRoot/import-stdout.log" -RedirectStandardError "$validationRoot/import-stderr.log"
        if ($importProcess.ExitCode -ne 0) { throw 'Stronghold asset/shader import failed. See import-stderr.log.' }
    }
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/ValidateSteppedStronghold.tscn', '--log-file', ('"{0}/integration.log"' -f $validationRoot))
    if ($NativeCapture) { $arguments += @('--fixed-fps', '60', '--resolution', '1280x720', '--position', '-32000,-32000', '--', '--capture') }
    else { $arguments += @('--headless', '--fixed-fps', '60', '--quit-after', '5000') }
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$validationRoot/stdout.log" -RedirectStandardError "$validationRoot/stderr.log"
    Get-Content -LiteralPath "$validationRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Stepped stronghold validation failed (exit $($process.ExitCode))." }
    if (-not (Select-String -LiteralPath "$validationRoot/integration.log" -SimpleMatch 'Stepped stronghold validation passed:')) { throw 'Stepped stronghold validation did not complete. See integration.log.' }
    $unexpectedErrors = @(Get-Content -LiteralPath "$validationRoot/stdout.log", "$validationRoot/stderr.log" | Where-Object {
        ($_ -match '^(ERROR:|SCRIPT ERROR:)') -and ($_ -notmatch '^ERROR: Failed to read the root certificate store\.$')
    })
    if ($unexpectedErrors.Count -gt 0) { throw "Stepped stronghold validation logged unexpected errors: $($unexpectedErrors -join ' | ')" }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}

