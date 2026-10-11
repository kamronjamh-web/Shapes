param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$RunName = 'gpu-render',
    [ValidateSet(10000, 25000, 50000, 100000)][int]$Count = 10000,
    [switch]$CpuRendering,
    [switch]$MeasureGpuTime,
    [switch]$CpuMovement,
    [switch]$ComponentsOnly,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runLabel = [regex]::Replace($RunName, '[^a-zA-Z0-9_-]', '_')
if ([string]::IsNullOrWhiteSpace($runLabel)) { $runLabel = 'gpu-render' }
$renderMode = if ($CpuRendering) { 'cpu' } else { 'gpu' }
$runLabel = "$runLabel-$Count-$renderMode"
if ($MeasureGpuTime) { $runLabel = "$runLabel-timed" }
if ($CpuMovement) { $runLabel = "$runLabel-cpu-movement" }
if ($ComponentsOnly) { $runLabel = "$runLabel-components" }
$profileRoot = Join-Path $repoRoot ".tmp/horde-frame-profile/$runLabel"
if (Test-Path -LiteralPath "$profileRoot/report.txt") { throw 'Run label already contains a report. Choose a new RunName to preserve existing measurements.' }
New-Item -ItemType Directory -Path "$profileRoot/appdata", "$profileRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
$previousProfileLabel = $env:SHAPES_FRAME_PROFILE_LABEL
$previousProfileCount = $env:SHAPES_FRAME_PROFILE_COUNT
$previousProfileRender = $env:SHAPES_FRAME_PROFILE_RENDER
$previousProfileGpuTiming = $env:SHAPES_FRAME_PROFILE_GPU_TIMING
$previousProfileCpuMovement = $env:SHAPES_FRAME_PROFILE_CPU_MOVEMENT
$previousProfileComponentsOnly = $env:SHAPES_FRAME_PROFILE_COMPONENTS_ONLY
try {
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $repoRoot 'SuperGatchaRebuild.csproj') --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Horde frame profile build failed.' }
    }
    $env:APPDATA = "$profileRoot/appdata"
    $env:LOCALAPPDATA = "$profileRoot/localappdata"
    $env:SHAPES_FRAME_PROFILE_LABEL = $runLabel
    $env:SHAPES_FRAME_PROFILE_COUNT = [string]$Count
    $env:SHAPES_FRAME_PROFILE_RENDER = $renderMode
    $env:SHAPES_FRAME_PROFILE_GPU_TIMING = [string]$MeasureGpuTime.IsPresent
    $env:SHAPES_FRAME_PROFILE_CPU_MOVEMENT = [string]$CpuMovement.IsPresent
    $env:SHAPES_FRAME_PROFILE_COMPONENTS_ONLY = [string]$ComponentsOnly.IsPresent
    $importArguments = @('--path', ('"{0}"' -f $repoRoot), '--editor', '--import', '--quit',
        '--rendering-method', 'forward_plus', '--rendering-driver', 'vulkan', '--position', '-32000,-32000')
    $importProcess = Start-Process -FilePath $GodotPath -ArgumentList $importArguments -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$profileRoot/import-stdout.log" -RedirectStandardError "$profileRoot/import-stderr.log"
    if ($importProcess.ExitCode -ne 0) { throw 'Frame profile shader import failed. See import-stderr.log.' }
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Experiments/GpuCrowd/ProfileHordeFrameCost.tscn',
        '--quit-after', '1000000', '--log-file', ('"{0}/integration.log"' -f $profileRoot),
        '--rendering-method', 'forward_plus', '--rendering-driver', 'vulkan', '--resolution', '1920x1080', '--position', '-32000,-32000')
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$profileRoot/stdout.log" -RedirectStandardError "$profileRoot/stderr.log"
    Get-Content -LiteralPath "$profileRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Horde frame profile failed (exit $($process.ExitCode)). See $profileRoot/stderr.log" }
    if (-not (Select-String -LiteralPath "$profileRoot/stdout.log" -SimpleMatch 'Horde frame profile complete.')) {
        throw 'Horde frame profile did not complete; early engine exit is not success.'
    }
    $unexpectedErrors = @(Get-Content -LiteralPath "$profileRoot/stderr.log" | Where-Object {
        ($_ -match '^(ERROR:|SCRIPT ERROR:)') -and ($_ -notmatch '^ERROR: Failed to read the root certificate store\.$')
    })
    if ($unexpectedErrors.Count -gt 0) { throw "Frame profile logged unexpected errors: $($unexpectedErrors -join ' | ')" }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
    $env:SHAPES_FRAME_PROFILE_LABEL = $previousProfileLabel
    $env:SHAPES_FRAME_PROFILE_COUNT = $previousProfileCount
    $env:SHAPES_FRAME_PROFILE_RENDER = $previousProfileRender
    $env:SHAPES_FRAME_PROFILE_GPU_TIMING = $previousProfileGpuTiming
    $env:SHAPES_FRAME_PROFILE_CPU_MOVEMENT = $previousProfileCpuMovement
    $env:SHAPES_FRAME_PROFILE_COMPONENTS_ONLY = $previousProfileComponentsOnly
}
