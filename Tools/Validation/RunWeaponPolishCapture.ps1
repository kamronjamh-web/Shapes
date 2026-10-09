param([Parameter(Mandatory = $true)][string]$GodotPath)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$captureRoot = Join-Path $repoRoot '.tmp/weapon-polish-capture'
New-Item -ItemType Directory -Path "$captureRoot/appdata", "$captureRoot/localappdata" -Force | Out-Null
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
$manifestPath = Join-Path $captureRoot 'completed.json'
try {
    $env:APPDATA = "$captureRoot/appdata"
    $env:LOCALAPPDATA = "$captureRoot/localappdata"
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath }
    $captureStarted = [DateTime]::UtcNow
    $arguments = @('--path', ('"{0}"' -f $repoRoot), '--scene', 'res://Tools/Validation/CaptureWeaponPolish.tscn', '--resolution', '1600x1000', '--quit-after', '6000', '--log-file', ('"{0}/capture.log"' -f $captureRoot))
    $process = Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$captureRoot/stdout.log" -RedirectStandardError "$captureRoot/stderr.log"
    Get-Content -LiteralPath "$captureRoot/stdout.log"
    if ($process.ExitCode -ne 0) { throw "Weapon polish capture failed (exit $($process.ExitCode))." }
    if (-not (Select-String -LiteralPath "$captureRoot/capture.log" -SimpleMatch 'Weapon polish capture passed.')) { throw 'Weapon polish capture did not complete.' }
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Capture completion manifest was not written.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $expected = @('Crowd-2000-zero', 'Crowd-2000-normal')
    $shapes = @('square', 'circle', 'triangle', 'hexagon', 'sphere', 'cube', 'pyramid', 'cone', 'tesseract', 'hyperphere', 'pentachoron', 'hypercone', 'impossiblecube', 'mobius', 'klein', 'boys')
    if ($manifest.shapes.Count -ne 16 -or @($shapes | Where-Object { $manifest.shapes -notcontains $_ }).Count -ne 0) { throw 'Capture manifest does not cover all sixteen shapes.' }
    foreach ($shape in $shapes) { $expected += "Idle-$shape", "Fire-$shape", "World-$shape" }
    foreach ($shape in @('hexagon', 'pyramid', 'hyperphere', 'mobius', 'boys')) { $expected += "Resolve-$shape" }
    foreach ($name in $expected) {
        if ($manifest.captures -notcontains $name) { throw "Capture is missing $name." }
        $file = Get-Item -LiteralPath (Join-Path $captureRoot "$name.png")
        if ($file.Length -eq 0 -or $file.LastWriteTimeUtc -lt $captureStarted) { throw "Capture $name is empty or stale." }
    }
    if ($manifest.timings.Count -ne 2 -or $manifest.timings[0].budget -ne 'zero' -or $manifest.timings[1].budget -ne 'normal') { throw 'Missing paired cosmetic-budget timing samples.' }
    foreach ($timing in $manifest.timings) {
        if ($timing.PSObject.Properties.Name -notcontains 'peak_solid' -or $timing.PSObject.Properties.Name -notcontains 'peak_burst') { throw 'Timing manifest is missing solid/burst pool peaks.' }
    }
    $manifest.timings | Format-Table
} finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
