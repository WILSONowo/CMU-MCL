param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$MainOutput,
    [Parameter(Mandatory = $true)][string]$UpdaterOutput,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [switch]$RequireCurseForgeKey
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw 'Version must contain three or four numeric parts.'
}

$main = [IO.Path]::GetFullPath($MainOutput).TrimEnd('\', '/')
$updater = [IO.Path]::GetFullPath($UpdaterOutput).TrimEnd('\', '/')
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not [IO.Directory]::Exists($main)) { throw "Main output not found: $main" }
if (-not [IO.Directory]::Exists($updater)) { throw "Updater output not found: $updater" }
[void][IO.Directory]::CreateDirectory($output)

function Copy-Payload([string]$from, [string]$to) {
    $root = [IO.Path]::GetFullPath($from).TrimEnd('\', '/')
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        if ($file.Extension -eq '.pdb') { continue }
        $relative = $file.FullName.Substring($root.Length).TrimStart('\', '/')
        if ($relative.StartsWith('..')) { throw 'PAYLOAD_PATH_INVALID' }
        $target = Join-Path $to $relative
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        [IO.File]::Copy($file.FullName, $target, $true)
    }
}

$stage = Join-Path $output ('.stage-' + [Guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory($stage)
    Copy-Payload $main $stage
    Copy-Payload $updater (Join-Path $stage 'updater')

    foreach ($name in @('CMUmcl.exe', 'CMUmcl.exe.config', 'Assets\authlib-injector.jar',
            'System.Text.Json.dll', 'CmlLib.Core.dll',
            'updater\CMUmcl.Updater.exe', 'updater\CMUmcl.Updater.exe.config')) {
        if (-not [IO.File]::Exists((Join-Path $stage $name))) { throw "PAYLOAD_INCOMPLETE $name" }
    }
    if (Test-Path -LiteralPath (Join-Path $stage 'test-data')) { throw 'PAYLOAD_HAS_TEST_DATA' }
    if (Test-Path -LiteralPath (Join-Path $stage 'sandbox-data')) { throw 'PAYLOAD_HAS_SANDBOX_DATA' }

    $sourceExe = Join-Path $main 'CMUmcl.exe'
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($sourceExe).Version.ToString()
    if ($assemblyVersion -ne $Version) { throw "VERSION_MISMATCH expected=$Version actual=$assemblyVersion" }
    $assembly = [Reflection.Assembly]::LoadFile($sourceExe)
    try {
        $hasKey = $assembly.GetManifestResourceNames() -contains 'CMUmcl.CurseForge.ProjectKey'
    }
    finally {
        $assembly = $null
    }
    if ($RequireCurseForgeKey -and -not $hasKey) { throw 'CURSEFORGE_KEY_RESOURCE_MISSING' }
    if (-not $RequireCurseForgeKey -and $hasKey) { throw 'CURSEFORGE_KEY_RESOURCE_PRESENT_IN_PUBLIC_BUILD' }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $tempZip = Join-Path $output ('.package-' + [Guid]::NewGuid().ToString('N') + '.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $tempZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $hash = (Get-FileHash -LiteralPath $tempZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $fileName = "CMUmcl-$Version-$($hash.Substring(0, 16)).zip"
    $final = Join-Path $output $fileName
    Move-Item -LiteralPath $tempZip -Destination $final
    $size = (Get-Item -LiteralPath $final).Length
    if ($size -le 0) { throw 'ZIP_EMPTY' }

    $info = [ordered]@{
        version = $Version
        fileName = $fileName
        size = [long]$size
        sha256 = $hash
        hasCurseForgeKey = [bool]$hasKey
        builtAt = (Get-Date).ToString('o')
    }
    $info | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'release-info.json') -Encoding UTF8
    Write-Output $final
}
finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
