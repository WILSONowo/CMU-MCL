param(
    [Parameter(Mandatory=$true)][string]$ClientDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Version='0.3.48.12',
    [string]$InnoSetupPath
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ClientDirectory).TrimEnd('\','/')
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$assembly=[Reflection.Assembly]::LoadFile((Join-Path $root 'CMUmcl.exe'))
if($assembly.GetName().Version.ToString() -ne $Version){throw 'Client version mismatch'}
if($assembly.GetManifestResourceNames() -contains 'CMUmcl.CurseForge.ProjectKey'){throw 'Public update must not contain a CurseForge key'}
$payload=Join-Path $output ('payload-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $payload | Out-Null
foreach($file in Get-ChildItem -LiteralPath $root -File -Recurse){
    if($file.Extension -eq '.pdb'){continue}
    if($file.Name -in @('auth.dat','settings.json','installed.json','SchematicPreviewWorker.exe') -or $file.Extension -in @('.key','.pfx','.p12')){throw 'Unexpected private data or bundled optional worker'}
    if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Payload must not contain links'}
    $relative=$file.FullName.Substring($root.Length).TrimStart('\','/')
    $destination=Join-Path $payload $relative
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
foreach($name in @('CMUmcl.exe','CMUmcl.exe.config','updater/CMUmcl.Updater.exe','Assets/authlib-injector.jar')){if(!(Test-Path -LiteralPath (Join-Path $payload $name))){throw "Incomplete payload: $name"}}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporary=Join-Path $output ('package-'+[Guid]::NewGuid().ToString('N')+'.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($payload,$temporary,[IO.Compression.CompressionLevel]::Optimal,$false)
$hash=(Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
$zip=Join-Path $output "CMUmcl-$Version-$($hash.Substring(0,16)).zip"
Move-Item -LiteralPath $temporary -Destination $zip
$info=[ordered]@{version=$Version;fileName=[IO.Path]::GetFileName($zip);sha256=$hash;size=(Get-Item -LiteralPath $zip).Length;hasCurseForgeKey=$false;optionalWorkerBundled=$false;payload=$payload}
if($InnoSetupPath){
    $repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $installerName="CMUmcl-$Version-setup"
    & $InnoSetupPath "/DCMUMCL_VERSION=$Version" "/DCMUMCL_PACKAGE_DIR=$payload" "/DCMUMCL_INSTALLER_OUT=$output" "/DCMUMCL_INSTALLER_NAME=$installerName" (Join-Path $repo 'installer/CMUmcl.iss')
    if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
    $installer=Join-Path $output ($installerName+'.exe')
    $info.installer=[IO.Path]::GetFileName($installer);$info.installerSha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
}
[IO.File]::WriteAllText((Join-Path $output 'release-info.json'),($info|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
Write-Output $zip
