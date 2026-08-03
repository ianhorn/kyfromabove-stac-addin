#requires -Version 5.1
<#
  One-time per-machine setup: creates a self-signed Authenticode code-signing
  certificate ("CN=KyFromAbove Dev") and trusts it for the current user so
  ArcGIS Pro / Windows accept add-ins signed with it on THIS machine.

  Usage (run once):
    powershell -ExecutionPolicy Bypass -File tools\setup-code-signing.ps1

  For distribution to OTHER machines, replace this with a real CA-issued
  code-signing certificate (EV or OV) and update tools\sign.ps1 if needed.
#>
$ErrorActionPreference = 'Stop'

$subject = 'CN=KyFromAbove Dev'
$my      = 'Cert:\CurrentUser\My'

# 1. Remove any pre-existing cert with this subject (Root / TrustedPublisher / My)
foreach ($store in 'My','Root','TrustedPublisher') {
    Get-ChildItem "Cert:\CurrentUser\$store" -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $subject } |
        ForEach-Object { Remove-Item "Cert:\CurrentUser\$store\$($_.Thumbprint)" -ErrorAction SilentlyContinue }
}

# 2. Create a self-signed CODE-SIGNING certificate (RSA-2048, SHA-256, 3yr)
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $subject `
    -KeyAlgorithm RSA -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -CertStoreLocation $my `
    -NotAfter (Get-Date).AddYears(3)

Write-Host ("Created cert: {0} (Thumbprint: {1})" -f $cert.Subject, $cert.Thumbprint)

# 3. Export the public cert (.cer) and a PFX backup (gitignored) into the project root
$root    = Split-Path -Parent $PSScriptRoot
$cerPath = Join-Path $root 'KyFromAbove.cer'
$pfxPath = Join-Path $root 'KyFromAbove.pfx'
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
$pw = ConvertTo-SecureString -String 'KyFromAboveDev' -AsPlainText -Force
Export-PfxCertificate  -Cert $cert -FilePath $pfxPath -Password $pw -Force | Out-Null
Write-Host ("Exported: {0} , {1}" -f $cerPath, $pfxPath)

Write-Host 'Done. The build will now Authenticode-sign the assembly.'
Write-Host 'NEXT (one-time, to TRUST the signature on this machine): run  tools\trust-cert.ps1'
