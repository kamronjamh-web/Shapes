param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [string]$RunName = 'baseline',
    [switch]$Worker,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$label = [regex]::Replace($RunName, '[^a-zA-Z0-9_-]', '_')
if ([string]::IsNullOrWhiteSpace($label)) { $label = 'baseline' }
if ($Worker) { $label = "$label-worker" }
$profileRoot = Join-Path $repoRoot ".tmp/corpse-performance/$label"
if (Test-Path -LiteralPath "$profileRoot/report.txt") { throw 'Choose a new RunName to preserve prior results.' }
New-Item -ItemType Directory -Path "$profileRoot/appdata", "$profileRoot/localappdata" -Force | Out-Null
$savedAppData = $env:APPDATA
$savedLocalAppData = $env:LOCALAPPDATA
$savedLabel = $env:SHAPES_CORPSE_PROFILE_LABEL
$savedWorker = $env:SHAPES_CORPSE_PROFILE_WORKER
try {
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $repoRoot 'SuperGatchaRebuild.csproj') --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Corpse profile build failed.' }
    }
    $env:APPDATA = "$profileRoot/appdata"
    $env:LOCALAPPDATA = "$profileRoot/localappdata"
    $env:SHAPES_CORPSE_PROFILE_LABEL = $label
    $env:SHAPES_CORPSE_PROFILE_WORKER = [string]$Worker.IsPresent
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Experiments/CorpsePerformance/ProfileCorpsePerformance.tscn',
        '--quit-after', '100000', '--log-file', ('"{0}/integration.log"' -f $profileRoot),
        '--rendering-method', 'forward_plus', '--rendering-driver', 'vulkan', '--resolution', '1920x1080', '--position', '-32000,-32000')
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$profileRoot/stdout.log" -RedirectStandardError "$profileRoot/stderr.log"
    Get-Content -LiteralPath "$profileRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Corpse profile failed exit=$($process.ExitCode); see $profileRoot/stderr.log" }
    if (-not (Select-String -LiteralPath "$profileRoot/stdout.log" -SimpleMatch 'Corpse performance profile complete.')) { throw 'Corpse profile exited before completion.' }
    $errors = @(Get-Content -LiteralPath "$profileRoot/stderr.log" | Where-Object {
        ($_ -match '^(ERROR:|SCRIPT ERROR:)') -and ($_ -notmatch '^ERROR: Failed to read the root certificate store\.$')
    })
    if ($errors.Count -gt 0) { throw "Unexpected corpse profile errors: $($errors -join ' | ')" }
} finally {
    $env:APPDATA = $savedAppData
    $env:LOCALAPPDATA = $savedLocalAppData
    $env:SHAPES_CORPSE_PROFILE_LABEL = $savedLabel
    $env:SHAPES_CORPSE_PROFILE_WORKER = $savedWorker
}
