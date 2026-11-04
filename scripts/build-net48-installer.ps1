param(
    [Parameter(Mandatory = $true)][string]$PackageZip,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$BaseName = '',
    [string]$InnoSetupPath = ''
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must contain three or four numeric parts.' }
if (-not [IO.File]::Exists($PackageZip)) { throw "Package ZIP not found: $PackageZip" }
if ([string]::IsNullOrWhiteSpace($BaseName)) { $BaseName = "CMUmcl-$Version-setup" }
if ($BaseName -notmatch '^[A-Za-z0-9._-]+$') { throw 'Installer base name contains invalid characters.' }

$repo = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $repo 'installer\CMUmcl.iss'
if (-not [IO.File]::Exists($iss)) { throw "Inno Setup script not found: $iss" }

$iscc = $InnoSetupPath
if ([string]::IsNullOrWhiteSpace($iscc)) {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($command) { $iscc = $command.Source }
}
if ([string]::IsNullOrWhiteSpace($iscc)) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($iscc) -or -not (Test-Path -LiteralPath $iscc)) {
    throw 'Inno Setup compiler (ISCC.exe) was not found. Install Inno Setup 6 or pass -InnoSetupPath.'
}
if ([IO.Directory]::Exists($iscc)) { $iscc = Join-Path $iscc 'ISCC.exe' }

$output = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
$stage = Join-Path $env:TEMP ('cmumcl-installer-stage-' + [Guid]::NewGuid().ToString('N'))
try {
    [void][IO.Directory]::CreateDirectory($stage)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($PackageZip, $stage)
    if (-not [IO.File]::Exists((Join-Path $stage 'CMUmcl.exe'))) { throw 'Package ZIP does not contain CMUmcl.exe.' }

    $arguments = @(
        "/DCMUMCL_VERSION=$Version",
        "/DCMUMCL_PACKAGE_DIR=$stage",
        "/DCMUMCL_INSTALLER_OUT=$output",
        "/DCMUMCL_INSTALLER_NAME=$BaseName",
        $iss
    )
    # The caller captures stdout as a single installer path, including across
    # powershell.exe. Keep native compiler output in a separate reviewable log.
    $compilerLog = Join-Path $output ($BaseName + '-compiler.log')
    & $iscc @arguments *> $compilerLog
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE. See $compilerLog" }

    $installer = Join-Path $output ($BaseName + '.exe')
    if (-not [IO.File]::Exists($installer)) { throw 'Installer output was not created.' }
    $size = (Get-Item -LiteralPath $installer).Length
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    $info = [ordered]@{
        version = $Version
        fileName = [IO.Path]::GetFileName($installer)
        size = [long]$size
        sha256 = $hash
        sourceZip = [IO.Path]::GetFileName($PackageZip)
        builtAt = (Get-Date).ToString('o')
    }
    $info | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'installer-info.json') -Encoding UTF8
    Write-Output $installer
}
finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if (-not $resolvedStage.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedStage) -notmatch '^cmumcl-installer-stage-[a-f0-9]{32}$') {
        throw 'Refusing to remove an unexpected installer staging directory.'
    }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force -ErrorAction SilentlyContinue }
}
