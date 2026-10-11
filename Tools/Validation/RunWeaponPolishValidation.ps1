param([Parameter(Mandatory = $true)][string]$GodotPath, [switch]$NativeAudio)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validationRoot = Join-Path $repoRoot '.tmp/weapon-polish-validation'
if ($NativeAudio) { $validationRoot = Join-Path $validationRoot 'native' }
New-Item -ItemType Directory -Path "$validationRoot/appdata", "$validationRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = "$validationRoot/appdata"
    $env:LOCALAPPDATA = "$validationRoot/localappdata"
    if ($NativeAudio) {
        $importArguments = @('--path', ('"{0}"' -f $repoRoot), '--editor', '--import', '--quit', '--position', '-32000,-32000')
        $importProcess = Start-Process -FilePath $GodotPath -ArgumentList $importArguments -Wait -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput "$validationRoot/import-stdout.log" -RedirectStandardError "$validationRoot/import-stderr.log"
        if ($importProcess.ExitCode -ne 0) { throw 'Weapon audio asset import failed.' }
    }
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/ValidateWeaponPolish.tscn', '--log-file', ('"{0}/integration.log"' -f $validationRoot))
    if ($NativeAudio) { $arguments += @('--quit-after', '100000', '--max-fps', '120', '--resolution', '960x540', '--position', '-32000,-32000') }
    else { $arguments += @('--headless', '--fixed-fps', '60', '--quit-after', '1800') }
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$validationRoot/stdout.log" -RedirectStandardError "$validationRoot/stderr.log"
    Get-Content -LiteralPath "$validationRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Weapon polish validation failed (exit $($process.ExitCode))." }
    if (-not (Select-String -LiteralPath "$validationRoot/integration.log" -SimpleMatch 'Weapon polish validation passed:')) { throw 'Weapon polish validation did not complete. See integration.log.' }
    $unexpectedErrors = @(Get-Content -LiteralPath "$validationRoot/stdout.log", "$validationRoot/stderr.log" | Where-Object {
        ($_ -match '^(ERROR:|SCRIPT ERROR:)') -and ($_ -notmatch '^ERROR: Failed to read the root certificate store\.$')
    })
    if ($unexpectedErrors.Count -gt 0) { throw "Weapon validation logged unexpected errors: $($unexpectedErrors -join ' | ')" }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
