param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunName = 'latest',
    [ValidatePattern('^[0-9]+(,[0-9]+)*$')][string]$Counts = '500,1000,2000',
    [ValidateRange(2000,100000)][int]$Capacity = 2000,
    [switch]$FlatControl
)

$ErrorActionPreference = 'Stop'
foreach ($requestedCount in $Counts.Split(',')) {
    if ([int]$requestedCount -lt 1 -or [int]$requestedCount -gt $Capacity) { throw 'Every count must fit Capacity.' }
}
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$probeRoot = Join-Path $repoRoot ".tmp/gpu-crowd-investigation/$RunName"
New-Item -ItemType Directory -Path "$probeRoot/appdata", "$probeRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
$previousTiered = $env:DOTNET_TieredCompilation
$previousLabel = $env:SHAPES_GPU_PROBE_LABEL
$previousCounts = $env:SHAPES_GPU_PROBE_COUNTS
$previousCapacity = $env:SHAPES_GPU_PROBE_CAPACITY
$previousFlat = $env:SHAPES_GPU_PROBE_FLAT
try {
    # The Godot editor binary loads bin/Debug. Optimize that assembly explicitly,
    # and avoid tiered-JIT promotion halfway through the original full-scan CPU,
    # indexed CPU and GPU contacts/world-check comparison. Experimental maps
    # explicitly disable the live GPU backend while profiling advancing CPU work.
    & dotnet build (Join-Path $repoRoot 'SuperGatchaRebuild.csproj') --no-restore -t:Rebuild -p:Optimize=true
    if ($LASTEXITCODE -ne 0) { throw 'Optimized investigation build failed.' }
    $env:APPDATA = "$probeRoot/appdata"
    $env:LOCALAPPDATA = "$probeRoot/localappdata"
    $env:DOTNET_TieredCompilation = '0'
    $env:SHAPES_GPU_PROBE_LABEL = $RunName
    $env:SHAPES_GPU_PROBE_COUNTS = $Counts
    $env:SHAPES_GPU_PROBE_CAPACITY = [string]$Capacity
    $env:SHAPES_GPU_PROBE_FLAT = if ($FlatControl) { '1' } else { '0' }
    # Compute shaders are imported RDShaderFile resources, including their GLSL
    # dependencies. Refresh them on a clean checkout before the measured scene.
    $importArguments = @('--path', ('"{0}"' -f $repoRoot), '--editor', '--import', '--quit',
        '--rendering-method', 'forward_plus', '--rendering-driver', 'vulkan', '--position', '-32000,-32000')
    $importProcess = Start-Process -FilePath $GodotPath -ArgumentList $importArguments -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$probeRoot/import-stdout.log" -RedirectStandardError "$probeRoot/import-stderr.log"
    if ($importProcess.ExitCode -ne 0) { throw 'GPU shader import failed. See import-stderr.log.' }
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Experiments/GpuCrowd/InvestigateGpuCrowd.tscn',
        '--rendering-method', 'forward_plus', '--rendering-driver', 'vulkan', '--disable-render-loop',
        '--resolution', '960x540', '--position', '-32000,-32000', '--quit-after', '5000',
        '--log-file', ('"{0}/integration.log"' -f $probeRoot))
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$probeRoot/stdout.log" -RedirectStandardError "$probeRoot/stderr.log"
    Get-Content -LiteralPath "$probeRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "GPU crowd investigation failed (exit $($process.ExitCode)). See $probeRoot/stderr.log" }
    if (-not (Select-String -LiteralPath "$probeRoot/stdout.log" -SimpleMatch 'GPU crowd investigation complete.')) {
        throw 'Investigation did not complete; an early engine exit is not a successful benchmark.'
    }
    if (-not (Select-String -LiteralPath "$probeRoot/summary.csv" -SimpleMatch 'cpu_full_scan_contact_median_ms')) {
        throw 'Investigation output is missing the original full-scan CPU comparison.'
    }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
    $env:DOTNET_TieredCompilation = $previousTiered
    $env:SHAPES_GPU_PROBE_LABEL = $previousLabel
    $env:SHAPES_GPU_PROBE_COUNTS = $previousCounts
    $env:SHAPES_GPU_PROBE_CAPACITY = $previousCapacity
    $env:SHAPES_GPU_PROBE_FLAT = $previousFlat
}
