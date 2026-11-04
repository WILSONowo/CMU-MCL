param(
    [Parameter(Mandatory=$true)][string]$ComponentDirectory,
    [Parameter(Mandatory=$true)][string]$UpstreamDirectory,
    [Parameter(Mandatory=$true)][string]$PrivateKeyPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$MinLauncherVersion='0.3.48.12',
    [string]$Notes='可选原理图原生 GPU 预览扩展；材质需从本地选择。'
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workerSource=Join-Path $repo 'components/schematic-preview'
$component=[IO.Path]::GetFullPath($ComponentDirectory)
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$manifest=Get-Content -LiteralPath (Join-Path $component 'component.json') -Raw | ConvertFrom-Json
if($manifest.component -ne 'schematic-preview' -or $manifest.apiVersion -ne 2 -or $manifest.version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$'){throw 'Invalid component manifest'}
$entryHash=(Get-FileHash -LiteralPath (Join-Path $component 'SchematicPreviewWorker.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
if($entryHash -ne $manifest.entrySha256){throw 'Entry hash mismatch'}
$key=[Security.Cryptography.CngKey]::Import([Convert]::FromBase64String(([IO.File]::ReadAllText($PrivateKeyPath) -replace '-----BEGIN PRIVATE KEY-----|-----END PRIVATE KEY-----|\s','')),[Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
$signer=[Security.Cryptography.ECDsaCng]::new($key)
try {
    $raw=$key.Export([Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    $spkiPrefix=[Convert]::FromBase64String('MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE')
    $spki=[byte[]]::new($spkiPrefix.Length+64);[Array]::Copy($spkiPrefix,0,$spki,0,$spkiPrefix.Length);[Array]::Copy($raw,8,$spki,$spkiPrefix.Length,64)
    $publicPem=[IO.File]::ReadAllText((Join-Path $repo 'CMUmcl/Content/SchematicPreviewPublicKey.pem'))
    $codePem=[regex]::Match([IO.File]::ReadAllText((Join-Path $repo 'CMUmcl/Content/SchematicPreviewUpdates.cs')),'(?s)-----BEGIN PUBLIC KEY-----.*?-----END PUBLIC KEY-----').Value
    $expected=$publicPem -replace '-----BEGIN PUBLIC KEY-----|-----END PUBLIC KEY-----|\s',''
    if($expected -ne ($codePem -replace '-----BEGIN PUBLIC KEY-----|-----END PUBLIC KEY-----|\s','') -or $expected -ne [Convert]::ToBase64String($spki)){throw 'Independent signing key does not match pinned client public key'}
    $stage=Join-Path $output ('stage-'+[Guid]::NewGuid().ToString('N'));$source=Join-Path $output ('source-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $stage,$source | Out-Null
    foreach($name in @('component.json','SchematicPreviewWorker.exe','LICENSE','NOTICE','SOURCE.txt')){Copy-Item -LiteralPath (Join-Path $component $name) -Destination $stage}
    $sourceNote="Prepared signed optional component $($manifest.version), API 2. Archive authentication is supplied by the independent latest.json envelope; this does not imply the channel has been published.`n`n"+[IO.File]::ReadAllText((Join-Path $workerSource 'SOURCE.txt'))
    [IO.File]::WriteAllText((Join-Path $stage 'SOURCE.txt'),$sourceNote,[Text.UTF8Encoding]::new($false))
    $licenses=Join-Path $stage 'Licenses';New-Item -ItemType Directory -Force -Path $licenses | Out-Null
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $UpstreamDirectory 'ThirdParty') -File){if($file.Extension -ne '.txt'){throw 'Unexpected license file'};Copy-Item -LiteralPath $file.FullName -Destination $licenses}
    $workerSource=Join-Path $repo 'components/schematic-preview'
    $sourceWorker=Join-Path $source 'components/schematic-preview';$sourceScripts=Join-Path $source 'scripts';New-Item -ItemType Directory -Force -Path $sourceWorker,$sourceScripts | Out-Null
    foreach($name in @('Cargo.toml','Cargo.lock','SOURCE.txt')){Copy-Item -LiteralPath (Join-Path $workerSource $name) -Destination $sourceWorker}
    foreach($name in @('src','upstream')){Copy-Item -LiteralPath (Join-Path $workerSource $name) -Destination $sourceWorker -Recurse}
    Copy-Item -LiteralPath (Join-Path $repo 'scripts/build-schematic-preview.ps1') -Destination (Join-Path $sourceScripts 'build-schematic-preview.ps1')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($source,(Join-Path $stage 'Source.zip'))
    $temporary=Join-Path $output ('package-'+[Guid]::NewGuid().ToString('N')+'.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$temporary,[IO.Compression.CompressionLevel]::Optimal,$false)
    $hash=(Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
    $filename="SchematicPreview-$($manifest.version)-$($hash.Substring(0,16)).zip"
    $zipPath=Join-Path $output $filename;Move-Item -LiteralPath $temporary -Destination $zipPath
    function Signature([byte[]]$bytes) {return [ordered]@{algorithm='ECDSA-P256-SHA256';format='ieee-p1363-base64';value=[Convert]::ToBase64String($signer.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256))}}
    $payload=[ordered]@{formatVersion=1;component='schematic-preview';apiVersion=2;version=$manifest.version;url="https://csumcl.mcapcs.cn/components/schematic-preview/$filename";size=(Get-Item -LiteralPath $zipPath).Length;sha256=$hash;entrySha256=$entryHash;minLauncherVersion=$MinLauncherVersion;notes=$Notes;archiveSignature=(Signature ([IO.File]::ReadAllBytes($zipPath)))}
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($payload|ConvertTo-Json -Depth 8 -Compress))
    $envelope=[ordered]@{payload=[Convert]::ToBase64String($bytes);signature=(Signature $bytes)}
    [IO.File]::WriteAllText((Join-Path $output 'latest.json'),($envelope|ConvertTo-Json -Depth 8 -Compress),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $output 'release-info.json'),($payload|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    Write-Output $zipPath
} finally {$signer.Dispose();$key.Dispose()}
