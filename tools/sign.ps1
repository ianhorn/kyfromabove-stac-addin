#requires -Version 5.1
<#
  Authenticode-signs a file (called by the build, KyFromAboveAddin.csproj,
  via the "AuthenticodeSignAddin" MSBuild target — before the Esri targets
  zip the assembly into the .esriAddinX).

  Default: uses the self-signed "CN=KyFromAbove Dev" cert from
           CurrentUser\My (created by setup-code-signing.ps1).

  To use a REAL certificate instead, set environment variables before building:
    $env:KYFROMABOVE_SIGN_PFX = 'C:\path\to\real-code-signing.pfx'
    $env:KYFROMABOVE_SIGN_PWD = 'the-pfx-password'
#>
param([Parameter(Mandatory = $true, Position = 0)][string]$File)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $File)) { throw "File not found: $File" }

$cert = $null

# 1) Prefer an explicit PFX (real certificate) if provided
$pfx = $env:KYFROMABOVE_SIGN_PFX
$pwd = $env:KYFROMABOVE_SIGN_PWD
if ($pfx -and (Test-Path -LiteralPath $pfx) -and $pwd) {
    $secure = ConvertTo-SecureString -String $pwd -AsPlainText -Force
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
        $pfx, $secure,
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet -bor
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
    Write-Host "[sign] Using PFX: $pfx"
}

# 2) Otherwise fall back to the self-signed dev cert in the current-user store
if (-not $cert) {
    $cert = Get-ChildItem 'Cert:\CurrentUser\My' -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -match 'KyFromAbove' } |
        Select-Object -First 1
    if ($cert) { Write-Host "[sign] Using store cert: $($cert.Subject)" }
}

if (-not $cert) {
    throw 'No code-signing certificate found. Run tools\setup-code-signing.ps1, or set KYFROMABOVE_SIGN_PFX / KYFROMABOVE_SIGN_PWD.'
}

# 3) Sign (SHA-256; add a timestamp so signatures survive cert expiry when using a real CA cert)
$hasRealPfx = [bool]$pfx
$params = @{ FilePath = $File; Certificate = $cert; HashAlgorithm = 'SHA256' }
if ($hasRealPfx) { $params['TimestampServer'] = 'http://timestamp.digicert.com' }

$result = Set-AuthenticodeSignature @params
if ($result.Status -ne 'Valid') {
    throw ("Authenticode signing failed for $File. Status: $($result.Status) - $($result.StatusMessage)")
}
Write-Host "[sign] $($cert.Subject) -> $File"
