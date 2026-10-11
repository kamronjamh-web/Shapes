param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$RunName = 'validation',
    [ValidateSet('full', 'flat', 'world')][string]$Mode = 'full',
    [switch]$Native,
    [switch]$MissingNative,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runLabel = [regex]::Replace($RunName, '[^a-zA-Z0-9_-]', '_')
$renderMode = if ($Native) { 'native' } else { 'headless' }
$runLabel = "$runLabel-$Mode-$renderMode"
if ($MissingNative) { $runLabel += '-missing' }
$runRoot = Join-Path $repoRoot ".tmp/corpse-worker-validation/$runLabel"
if (Test-Path -LiteralPath "$runRoot/stdout.log") { throw 'Choose a new RunName to preserve the previous validation log.' }
New-Item -ItemType Directory -Path "$runRoot/appdata", "$runRoot/localappdata" -Force | Out-Null
$savedEnvironment = @{}
foreach ($name in @('APPDATA', 'LOCALAPPDATA', 'BOX3D_CORPSES_DLL', 'SHAPES_CORPSE_WORKER_MISSING', 'SHAPES_CORPSE_WORKER_MODE')) {
    $savedEnvironment[$name] = [System.Environment]::GetEnvironmentVariable($name)
}
try {
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $repoRoot 'SuperGatchaRebuild.csproj') --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Corpse worker validation build failed.' }
    }
    $env:APPDATA = "$runRoot/appdata"
    $env:LOCALAPPDATA = "$runRoot/localappdata"
    $env:SHAPES_CORPSE_WORKER_MISSING = [string]$MissingNative.IsPresent
    $env:SHAPES_CORPSE_WORKER_MODE = $Mode
    if ($MissingNative) { $env:BOX3D_CORPSES_DLL = "$runRoot/intentionally-absent.dll" }
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/ValidateCorpseWorker.tscn',
        '--quit-after', '1000000', '--log-file', ('"{0}/integration.log"' -f $runRoot))
    if ($Native) { $arguments += @('--rendering-method', 'forward_plus', '--rendering-driver', 'vulkan', '--resolution', '1280x720', '--position', '-32000,-32000') }
    else { $arguments += '--headless' }
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$runRoot/stdout.log" -RedirectStandardError "$runRoot/stderr.log"
    Get-Content -LiteralPath "$runRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Corpse worker validation failed (exit $($process.ExitCode)). See $runRoot/stderr.log" }
    if (-not (Select-String -LiteralPath "$runRoot/stdout.log" -SimpleMatch 'Corpse worker validation passed:')) { throw 'Validation did not complete.' }
    $errors = @(Get-Content -LiteralPath "$runRoot/stderr.log" | Where-Object {
        ($_ -match '^(ERROR:|SCRIPT ERROR:)') -and ($_ -notmatch '^ERROR: Failed to read the root certificate store\.$')
    })
    if ($errors.Count -gt 0) { throw "Unexpected engine errors: $($errors -join ' | ')" }
} finally {
    foreach ($name in $savedEnvironment.Keys) { [System.Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
}
