param([Parameter(Mandatory = $true)][string]$GodotPath, [switch]$AllShapes, [string[]]$Shapes, [switch]$Modulators, [switch]$QualityWorkshop, [switch]$Workbench, [ValidateSet('1280x720', '1600x1000')][string]$Resolution = '1600x1000')

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$captureRoot = Join-Path $repoRoot '.tmp/weapon-inspection-capture'
New-Item -ItemType Directory -Path "$captureRoot/appdata", "$captureRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
$previousCaptureAll = $env:WEAPON_INSPECTION_CAPTURE_ALL
$previousCaptureShapes = $env:WEAPON_INSPECTION_CAPTURE_SHAPES
$previousCaptureModulators = $env:WEAPON_INSPECTION_CAPTURE_MODULATORS
$previousCaptureWorkshop = $env:WEAPON_INSPECTION_CAPTURE_WORKSHOP
$previousCaptureWorkbench = $env:WEAPON_INSPECTION_CAPTURE_WORKBENCH
$manifestPath = Join-Path $captureRoot 'completed.json'
try {
    $env:APPDATA = "$captureRoot/appdata"
    $env:LOCALAPPDATA = "$captureRoot/localappdata"
    $env:WEAPON_INSPECTION_CAPTURE_ALL = if ($AllShapes) { '1' } else { '0' }
    $env:WEAPON_INSPECTION_CAPTURE_MODULATORS = if ($Modulators) { '1' } else { '0' }
    $env:WEAPON_INSPECTION_CAPTURE_WORKSHOP = if ($QualityWorkshop) { '1' } else { '0' }
    $env:WEAPON_INSPECTION_CAPTURE_WORKBENCH = if ($Workbench) { '1' } else { '0' }
    if ($Shapes) { $env:WEAPON_INSPECTION_CAPTURE_SHAPES = $Shapes -join ',' }
    elseif ($AllShapes) { $env:WEAPON_INSPECTION_CAPTURE_SHAPES = '' }
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath }
    $captureStarted = [DateTime]::UtcNow
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/CaptureWeaponInspection.tscn', '--resolution', $Resolution, '--fixed-fps', '60', '--quit-after', '12000', '--log-file', ('"{0}/capture.log"' -f $captureRoot))
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$captureRoot/stdout.log" -RedirectStandardError "$captureRoot/stderr.log"
    Get-Content -LiteralPath "$captureRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Weapon inspection capture failed (exit $($process.ExitCode))." }
    if (-not (Select-String -LiteralPath "$captureRoot/capture.log" -SimpleMatch 'Weapon inspection capture passed.')) { throw 'Weapon inspection capture did not complete successfully.' }
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Capture completion manifest was not written.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($env:WEAPON_INSPECTION_CAPTURE_SHAPES) {
        $requestedKeys = @($env:WEAPON_INSPECTION_CAPTURE_SHAPES -split '[,; ]+' | Where-Object { $_ } | ForEach-Object {
            $key = $_.Trim().ToLowerInvariant()
            if ($key -eq 'hypersphere') { 'hyperphere' } else { $key }
        } | Select-Object -Unique)
        if ($manifest.shapes.Count -ne $requestedKeys.Count -or @($requestedKeys | Where-Object { $manifest.shapes -notcontains $_ }).Count -gt 0) { throw 'Capture manifest does not cover the requested shape filter.' }
    } elseif ($manifest.shapes.Count -ne 16) { throw 'Unfiltered capture did not cover all sixteen shapes.' }
    foreach ($key in $manifest.shapes) {
        $expected = @("FPS-$key", "Aim-$key", "Fire-$key", "Inspector-$key")
        if ($key -ne 'square') { $expected += "Mechanism-$key" }
        if ($Modulators) { $expected += "ModulatorDetails-$key" }
        foreach ($name in $expected) {
            if ($manifest.captures -notcontains $name) { throw "Capture is missing $name." }
            $file = Get-Item -LiteralPath (Join-Path $captureRoot "$name.png")
            if ($file.Length -eq 0 -or $file.LastWriteTimeUtc -lt $captureStarted) { throw "Capture $name is empty or stale." }
        }
    }
    if ($QualityWorkshop) {
        foreach ($name in @('QualityShop', 'QualityFusionBefore', 'QualityFusionAfter', 'QualityVictory', 'QualityVictoryShop')) {
            if ($manifest.captures -notcontains $name) { throw "Capture is missing $name." }
            $file = Get-Item -LiteralPath (Join-Path $captureRoot "$name.png")
            if ($file.Length -eq 0 -or $file.LastWriteTimeUtc -lt $captureStarted) { throw "Capture $name is empty or stale." }
        }
    }
    if ($Workbench) {
        if ("$($manifest.window.width)x$($manifest.window.height)" -ne $Resolution) { throw 'Native workbench capture used a different window resolution.' }
        foreach ($stage in @('Grid', 'ModulatorGrid', 'ShapeDrag', 'Disassemble', 'Assemble', 'SocketSeat', 'SocketInstalled', 'SocketRemoved')) {
            $name = "Workbench$stage-$Resolution"
            if ($manifest.captures -notcontains $name) { throw "Capture is missing $name." }
            $file = Get-Item -LiteralPath (Join-Path $captureRoot "$name.png")
            if ($file.Length -eq 0 -or $file.LastWriteTimeUtc -lt $captureStarted) { throw "Capture $name is empty or stale." }
        }
    }
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
    $env:WEAPON_INSPECTION_CAPTURE_ALL = $previousCaptureAll
    $env:WEAPON_INSPECTION_CAPTURE_SHAPES = $previousCaptureShapes
    $env:WEAPON_INSPECTION_CAPTURE_MODULATORS = $previousCaptureModulators
    $env:WEAPON_INSPECTION_CAPTURE_WORKSHOP = $previousCaptureWorkshop
    $env:WEAPON_INSPECTION_CAPTURE_WORKBENCH = $previousCaptureWorkbench
}
