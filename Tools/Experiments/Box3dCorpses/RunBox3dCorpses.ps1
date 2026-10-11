param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$RunName = 'comparison',
    [string]$Counts = '64,256,2056',
    [switch]$Headless,
    [switch]$Demo,
    [switch]$SkipBuild,
    [switch]$SkipNativeBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if ($Counts -notmatch '^(64|256|1024|2056)(,(64|256|1024|2056))*$') { throw 'Counts must be comma-separated 64, 256, 1024, or 2056.' }
if ($Demo -and $Headless) { throw 'The interactive demo requires a window.' }
$runLabel = [regex]::Replace($RunName, '[^a-zA-Z0-9_-]', '_')
if ([string]::IsNullOrWhiteSpace($runLabel)) { throw 'RunName must contain a label.' }
$mode = if ($Headless) { 'headless' } else { 'native' }
$runLabel = "$runLabel-$($Counts.Replace(',', '_'))-$mode"
if ($Demo) { $runLabel += '-demo' }
$runRoot = Join-Path $repoRoot ".tmp/box3d-corpses/$runLabel"
if (Test-Path -LiteralPath "$runRoot/report.txt") { throw 'Choose a new RunName to preserve the existing report.' }
New-Item -ItemType Directory -Path "$runRoot/appdata", "$runRoot/localappdata" -Force | Out-Null
$savedEnvironment = @{}
foreach ($name in @('APPDATA', 'LOCALAPPDATA', 'SHAPES_BOX3D_LABEL', 'SHAPES_BOX3D_COUNTS', 'SHAPES_BOX3D_DEMO')) {
    $savedEnvironment[$name] = [System.Environment]::GetEnvironmentVariable($name)
}
try {
    if (-not $SkipNativeBuild) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'BuildNative.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Box3D native build failed.' }
    }
    $dllPath = if ($env:BOX3D_CORPSES_DLL) { $env:BOX3D_CORPSES_DLL } else { Join-Path $repoRoot '.tmp/box3d-corpses/build/box3d_corpses.dll' }
    if (-not (Test-Path -LiteralPath $dllPath)) { throw "Native bridge is missing: $dllPath" }
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $repoRoot 'SuperGatchaRebuild.csproj') --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Box3D harness C# build failed.' }
    }
    $env:APPDATA = "$runRoot/appdata"
    $env:LOCALAPPDATA = "$runRoot/localappdata"
    $env:SHAPES_BOX3D_LABEL = $runLabel
    $env:SHAPES_BOX3D_COUNTS = $Counts
    $env:SHAPES_BOX3D_DEMO = [string]$Demo.IsPresent
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Experiments/Box3dCorpses/TestBox3dCorpses.tscn',
        '--log-file', ('"{0}/integration.log"' -f $runRoot))
    if ($Headless) { $arguments += '--headless' }
    else { $arguments += @('--rendering-method', 'forward_plus', '--rendering-driver', 'vulkan', '--resolution', '1600x900') }
    if (-not $Demo) { $arguments += @('--quit-after', '1000000') }
    $windowStyle = if ($Demo) { 'Normal' } else { 'Hidden' }
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle $windowStyle `
        -RedirectStandardOutput "$runRoot/stdout.log" -RedirectStandardError "$runRoot/stderr.log"
    Get-Content -LiteralPath "$runRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Box3D experiment failed (exit $($process.ExitCode)). See $runRoot/stderr.log" }
    if (-not $Demo -and -not (Select-String -LiteralPath "$runRoot/stdout.log" -SimpleMatch 'Box3D corpse validation passed:')) {
        throw 'The experiment exited before completing validation.'
    }
    $errors = @(Get-Content -LiteralPath "$runRoot/stderr.log" | Where-Object {
        ($_ -match '^(ERROR:|SCRIPT ERROR:)') -and ($_ -notmatch '^ERROR: Failed to read the root certificate store\.$')
    })
    if ($errors.Count -gt 0) { throw "Unexpected engine errors: $($errors -join ' | ')" }
    Write-Output "Report: $runRoot/report.txt"
} finally {
    foreach ($name in $savedEnvironment.Keys) { [System.Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
}
