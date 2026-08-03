#requires -Version 5.1
<#
  One-time, INTERACTIVE: trusts the self-signed "KyFromAbove Dev" code-signing
  certificate on the CURRENT machine so ArcGIS Pro / Windows accept add-ins
  signed with it WITHOUT an "unknown publisher" warning.

  This installs the cert into CurrentUser\Root and CurrentUser\TrustedPublisher.
  Windows shows a confirmation dialog for the Root install — click "Yes".

  Usage (run once, interactively):
    powershell -ExecutionPolicy Bypass -File tools\trust-cert.ps1
#>
$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot
$cerPath = Join-Path $root 'KyFromAbove.cer'
if (-not (Test-Path -LiteralPath $cerPath)) {
    throw "KyFromAbove.cer not found. Run tools\setup-code-signing.ps1 first."
}

# Use certutil (console) — it does not show a blocking dialog for per-user stores.
Write-Host 'Installing KyFromAbove Dev cert into Trusted Root (CurrentUser)...'
certutil -user -addstore Root $cerPath | Out-Null

Write-Host 'Installing into Trusted Publishers (CurrentUser)...'
certutil -user -addstore TrustedPublisher $cerPath | Out-Null

Write-Host 'Done. Add-ins signed with this cert are now trusted on this machine.'
