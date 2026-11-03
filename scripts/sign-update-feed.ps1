param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Url,
    [Parameter(Mandatory = $true)][string]$PrivateKeyPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$Notes = '',
    [string]$ExpectedFingerprint = '20C98D68C2E761E57EC40C3AD0A173EDE4528279464A5E643E456C5DE3C47583'
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must contain three or four numeric parts.' }
if (-not [IO.File]::Exists($PackagePath)) { throw "Package not found: $PackagePath" }
if (-not [IO.File]::Exists($PrivateKeyPath)) { throw "Private key not found: $PrivateKeyPath" }
if (-not $Url.StartsWith('https://csumcl.mcapcs.cn/update/windows-x86_64/', [StringComparison]::OrdinalIgnoreCase) -or
    -not $Url.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Update URL must be an HTTPS ZIP on the resource host.'
}

$pem = [IO.File]::ReadAllText($PrivateKeyPath)
$base64 = ($pem -replace '-----BEGIN PRIVATE KEY-----', '' -replace '-----END PRIVATE KEY-----', '' -replace "`r", '' -replace "`n", '').Trim()
if ([string]::IsNullOrWhiteSpace($base64)) { throw 'Private key is empty or not PKCS#8 PEM.' }
$keyBlob = [Convert]::FromBase64String($base64)
$key = [Security.Cryptography.CngKey]::Import($keyBlob, [Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
$ecdsa = New-Object Security.Cryptography.ECDsaCng($key)
try {
    # Refuse to sign with a key that the shipped client will not trust.
    $publicBlob = $key.Export([Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    $spkiPrefix = [Convert]::FromBase64String('MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE')
    $spki = New-Object byte[] ($spkiPrefix.Length + 64)
    [Array]::Copy($spkiPrefix, 0, $spki, 0, $spkiPrefix.Length)
    [Array]::Copy($publicBlob, 8, $spki, $spkiPrefix.Length, 64)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $fingerprint = ([BitConverter]::ToString($sha.ComputeHash($spki)) -replace '-', '').ToUpperInvariant() } finally { $sha.Dispose() }
    if ($fingerprint -ne $ExpectedFingerprint) {
        throw "UPDATE_KEY_FINGERPRINT_MISMATCH expected=$ExpectedFingerprint actual=$fingerprint"
    }

    $packageBytes = [IO.File]::ReadAllBytes($PackagePath)
    $size = [long]$packageBytes.LongLength
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $packageHash = ([BitConverter]::ToString($sha.ComputeHash($packageBytes)) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
    $archiveSignature = [Convert]::ToBase64String($ecdsa.SignData($packageBytes, [Security.Cryptography.HashAlgorithmName]::SHA256))

    $payload = [ordered]@{
        formatVersion = 2
        platform = 'windows-x86_64'
        version = $Version
        url = $Url
        size = $size
        sha256 = $packageHash
        archiveSignature = [ordered]@{
            algorithm = 'ECDSA-P256-SHA256'
            format = 'ieee-p1363-base64'
            value = $archiveSignature
        }
        notes = $Notes
    }
    $payloadJson = $payload | ConvertTo-Json -Depth 10 -Compress
    $payloadBytes = [Text.UTF8Encoding]::new($false).GetBytes($payloadJson)
    $payloadSignature = [Convert]::ToBase64String($ecdsa.SignData($payloadBytes, [Security.Cryptography.HashAlgorithmName]::SHA256))
    $envelope = [ordered]@{
        payload = [Convert]::ToBase64String($payloadBytes)
        signature = [ordered]@{
            algorithm = 'ECDSA-P256-SHA256'
            format = 'ieee-p1363-base64'
            value = $payloadSignature
        }
    }
    $envelopeJson = $envelope | ConvertTo-Json -Depth 10 -Compress
    [IO.File]::WriteAllBytes($OutputPath, [Text.UTF8Encoding]::new($false).GetBytes($envelopeJson))

    $info = [ordered]@{
        version = $Version
        fileName = [IO.Path]::GetFileName($PackagePath)
        size = $size
        sha256 = $packageHash
        url = $Url
        fingerprint = $fingerprint
        signedAt = (Get-Date).ToString('o')
    }
    $info | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($OutputPath + '.info.json') -Encoding UTF8
}
finally {
    $ecdsa.Dispose()
    $key.Dispose()
}
