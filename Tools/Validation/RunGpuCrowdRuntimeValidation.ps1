param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [switch]$Headless,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$modeLabel = if ($Headless) { 'headless' } else { 'native' }
$validationRoot = Join-Path $repoRoot ".tmp/gpu-crowd-runtime-validation/$modeLabel"
New-Item -ItemType Directory -Path "$validationRoot/appdata", "$validationRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $repoRoot 'SuperGatchaRebuild.csproj') --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Runtime validation build failed.' }
    }
    $env:APPDATA = "$validationRoot/appdata"
    $env:LOCALAPPDATA = "$validationRoot/localappdata"
    if (-not $Headless) {
        $importArguments = @('--path', ('"{0}"' -f $repoRoot), '--editor', '--import', '--quit',
            '--rendering-method', 'forward_plus', '--rendering-driver', 'vulkan', '--position', '-32000,-32000')
        $importProcess = Start-Process -FilePath $GodotPath -ArgumentList $importArguments -Wait -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput "$validationRoot/import-stdout.log" -RedirectStandardError "$validationRoot/import-stderr.log"
        if ($importProcess.ExitCode -ne 0) { throw 'GPU shader import failed. See import-stderr.log.' }
    }
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/ValidateGpuCrowdRuntime.tscn',
        '--quit-after', '1000000', '--log-file', ('"{0}/integration.log"' -f $validationRoot))
    if ($Headless) { $arguments += '--headless' }
    else {
        $arguments += @('--rendering-method', 'forward_plus', '--rendering-driver', 'vulkan', '--disable-render-loop',
            '--resolution', '960x540', '--position', '-32000,-32000')
    }
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$validationRoot/stdout.log" -RedirectStandardError "$validationRoot/stderr.log"
    Get-Content -LiteralPath "$validationRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "GPU crowd runtime validation failed (exit $($process.ExitCode)). See $validationRoot/stderr.log" }
    if (-not (Select-String -LiteralPath "$validationRoot/stdout.log" -SimpleMatch 'GPU crowd runtime validation passed:')) {
        throw 'Runtime validation did not complete; early engine exit is not success.'
    }
    # Native shutdown diagnostics can appear on either stream after the sentinel.
    $unexpectedErrors = @(Get-Content -LiteralPath "$validationRoot/stdout.log", "$validationRoot/stderr.log" | Where-Object {
        ($_ -match '^(ERROR:|SCRIPT ERROR:)') -and ($_ -notmatch '^ERROR: Failed to read the root certificate store\.$')
    })
    if ($unexpectedErrors.Count -gt 0) { throw "Runtime validation logged unexpected errors: $($unexpectedErrors -join ' | ')" }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
