<#
.SYNOPSIS
    Fails unless a file carries a valid, timestamped Authenticode signature, and prints who signed it.

.DESCRIPTION
    Used by the release workflow after each signing step, and by tools/Sign-Release.ps1. A valid signature is what
    Smart App Control and SmartScreen look at; the timestamp keeps it valid after the certificate expires.

.EXAMPLE
    .\tools\Assert-Signature.ps1 -Path .\publish\J365.IntunePSApp.exe
#>
[CmdletBinding()]
param
(
    [Parameter(Mandatory = $true)][string]$Path
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "File not found: $Path" }
$sig = Get-AuthenticodeSignature -LiteralPath $Path
$name = Split-Path -Leaf $Path
if ($sig.Status -ne 'Valid') { throw "$name : the signature is not valid ($($sig.Status)): $($sig.StatusMessage)" }
if (-not $sig.TimeStamperCertificate) { throw "$name : the signature has no timestamp (it would stop being valid when the certificate expires)." }
$signer = $sig.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
$stamper = $sig.TimeStamperCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
Write-Host ('{0}: signed by {1} (certificate valid until {2:yyyy-MM-dd}), timestamp by {3}' -f $name, $signer, $sig.SignerCertificate.NotAfter, $stamper)
