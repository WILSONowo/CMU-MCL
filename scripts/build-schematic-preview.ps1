param(
    [string]$OutputDirectory,
    [switch]$DebugBuild
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../components/schematic-preview'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $project 'artifacts/0.2.3' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$cargo = (Get-Command cargo -ErrorAction SilentlyContinue).Source
if (!$cargo) { $cargo = Join-Path $env:USERPROFILE '.cargo/bin/cargo.exe' }
$arguments = @('build', '--locked', '--manifest-path', (Join-Path $project 'Cargo.toml'))
$profile = 'debug'
if (!$DebugBuild) { $arguments += '--release'; $profile = 'release' }
& $cargo @arguments
if ($LASTEXITCODE -ne 0) { throw 'Preview worker build failed' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$targetRoot=if($env:CARGO_TARGET_DIR){[IO.Path]::GetFullPath($env:CARGO_TARGET_DIR)}else{Join-Path $project 'target'}
Copy-Item -LiteralPath (Join-Path $targetRoot "$profile/SchematicPreviewWorker.exe") -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $project 'upstream/LICENSE') -Destination $OutputDirectory
$upstreamNotice = Get-Content -LiteralPath (Join-Path $project 'upstream/NOTICE') -Raw
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'NOTICE'), $upstreamNotice + "`nCMUmcl optional preview adapter: upstream native Direct3D12 pipeline rendered directly to a dedicated launcher child viewport. Includes the unchanged upstream vanilla resource base; its separate Minecraft asset notice above remains applicable. Custom user resource packs are not included. See SOURCE.txt and source archive for build instructions and dependency license inventory.`n")
$hash = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'SchematicPreviewWorker.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = @{ component = 'schematic-preview'; apiVersion = 2; version = '0.2.3'; entrySha256 = $hash; upstream = 'https://github.com/Arcadi4/LitematicaPreview'; upstreamTag = 'v0.5.2'; localBuild = $true }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'component.json'), ($manifest | ConvertTo-Json))
Copy-Item -LiteralPath (Join-Path $project 'SOURCE.txt') -Destination $OutputDirectory
Write-Output $OutputDirectory
