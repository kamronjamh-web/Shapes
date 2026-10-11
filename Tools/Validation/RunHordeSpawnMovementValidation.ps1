param([Parameter(Mandatory=$true)][string]$GodotPath,[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$repoRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validationRoot=Join-Path $repoRoot '.tmp/horde-spawn-movement/native'
New-Item -ItemType Directory -Path "$validationRoot/appdata","$validationRoot/localappdata" -Force | Out-Null
$previousAppData=$env:APPDATA
$previousLocalAppData=$env:LOCALAPPDATA
try {
 if(-not $SkipBuild) { & dotnet build (Join-Path $repoRoot 'SuperGatchaRebuild.csproj') --no-restore; if($LASTEXITCODE -ne 0) { throw 'Build failed' } }
 $env:APPDATA="$validationRoot/appdata"; $env:LOCALAPPDATA="$validationRoot/localappdata"
 $arguments=@('--path',('"{0}"' -f $repoRoot),'--scene','res://Tools/Validation/ValidateHordeSpawnMovement.tscn','--quit-after','20000','--rendering-method','forward_plus','--rendering-driver','vulkan','--resolution','960x540','--position','-32000,-32000')
 $process=Start-Process -FilePath $GodotPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$validationRoot/stdout.log" -RedirectStandardError "$validationRoot/stderr.log"
 Get-Content -LiteralPath "$validationRoot/stdout.log"
 if($process.ExitCode -ne 0) { throw "Diagnostic failed exit=$($process.ExitCode)" }
 if(-not(Select-String -LiteralPath "$validationRoot/stdout.log" -SimpleMatch 'Horde spawn movement diagnostic complete.')) { throw 'Diagnostic did not complete' }
 $errors=@(Get-Content -LiteralPath "$validationRoot/stdout.log","$validationRoot/stderr.log" | Where-Object { ($_ -match '^(ERROR:|SCRIPT ERROR:|Fatal error)') -and ($_ -notmatch '^ERROR: Failed to read the root certificate store\.$') })
 if($errors.Count -ne 0) { throw "Unexpected diagnostic errors: $($errors -join ' | ')" }
} finally { $env:APPDATA=$previousAppData; $env:LOCALAPPDATA=$previousLocalAppData }
