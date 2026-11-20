<#
.SYNOPSIS
    Signs the executable of a J365 IntunePSApp release with a code-signing certificate of this computer and, with
    -Upload, replaces the files of the GitHub release.

.DESCRIPTION
    For certificates that cannot be used inside GitHub Actions:
      - an individual "Open Source Developer" certificate from Certum, used through SimplySign Desktop;
      - any OV/EV certificate on a USB token;
      - the internal certificate authority of a company that allows the application in its App Control (WDAC) policy
        (a .pfx file, or a certificate of the user's store).

    Steps:
      1. takes the release zip (downloaded from GitHub with -Version and checked against its SHA256SUMS.txt, or a local
         zip with -ZipPath);
      2. signs J365.IntunePSApp.exe inside it (SHA-256 and an RFC 3161 timestamp) with signtool when the Windows SDK is
         installed, or with Set-AuthenticodeSignature otherwise, and checks the signature;
      3. builds the zip again with the same files and writes a new SHA256SUMS.txt;
      4. with -Upload (GitHub release only), replaces the zip and SHA256SUMS.txt of the release.

    The update inside the application reads SHA256SUMS.txt, so it accepts the signed zip. The build provenance
    attestation of a GitHub release keeps describing the zip built by GitHub Actions (unsigned): signing inside the
    workflow (SignPath or Azure Artifact Signing, docs/SIGNING.md) keeps both.

.PARAMETER Version
    GitHub release to sign, for example 2.4.1 (needs the GitHub CLI, gh).

.PARAMETER ZipPath
    A local release zip to sign instead (nothing is downloaded or uploaded).

.PARAMETER Thumbprint
    Thumbprint (SHA1) of the code-signing certificate in Cert:\CurrentUser\My. With neither -Thumbprint nor -PfxPath
    the script lists the code-signing certificates it can see.

.PARAMETER PfxPath
    A .pfx file with the certificate and its private key (internal company CA, older certificates).

.PARAMETER TimestampUrl
    RFC 3161 timestamp server of the certificate authority: Certum by default; DigiCert http://timestamp.digicert.com,
    Sectigo http://timestamp.sectigo.com.

.PARAMETER AllowUntrustedRoot
    Accepts a signature whose root is not trusted on this computer (an internal CA trusted only on the company's
    devices). The signature must still match the file and carry a timestamp.

.EXAMPLE
    .\tools\Sign-Release.ps1 -Version 2.4.1
    .\tools\Sign-Release.ps1 -Version 2.4.1 -Thumbprint 0123456789ABCDEF0123456789ABCDEF01234567 -Upload
    .\tools\Sign-Release.ps1 -ZipPath .\J365-IntunePSApp-2.4.1-win-x64.zip -PfxPath .\contoso-codesign.pfx -TimestampUrl http://timestamp.digicert.com
#>
[CmdletBinding(DefaultParameterSetName = 'GitHub')]
param
(
    [Parameter(Mandatory = $true, ParameterSetName = 'GitHub')][string]$Version,
    [Parameter(Mandatory = $true, ParameterSetName = 'Local')][string]$ZipPath,
    [string]$Thumbprint,
    [string]$PfxPath,
    [System.Security.SecureString]$PfxPassword,
    [string]$Repository = 'sesantanajr/J365-IntunePSApp',
    [string]$TimestampUrl = 'http://time.certum.pl',
    [string]$WorkFolder = (Join-Path $env:TEMP 'J365-IntunePSApp-signing'),
    [Parameter(ParameterSetName = 'GitHub')][switch]$Upload,
    [switch]$AllowUntrustedRoot
)

$ErrorActionPreference = 'Stop'

function Find-SignTool
{
    $roots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "$env:ProgramFiles\Windows Kits\10\bin")
    $found = foreach ($r in $roots) { if (Test-Path $r) { Get-ChildItem $r -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } } }
    $tool = $found | Sort-Object { [version]($_.Directory.Parent.Name -replace '[^0-9.]', '') } -Descending | Select-Object -First 1
    if ($tool) { return $tool.FullName }
    return $null
}

function Get-SimpleName($certificate)
{
    if (-not $certificate) { return '' }
    return $certificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
}

# ---------------------------------------------------------------- certificate
$certificate = $null
if ($PfxPath)
{
    if (-not (Test-Path -LiteralPath $PfxPath)) { throw "PFX file not found: $PfxPath" }
    if (-not $PfxPassword) { $PfxPassword = Read-Host 'Password of the PFX file' -AsSecureString }
    # Ephemeral key: the private key is not written to this computer's key store.
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2((Resolve-Path -LiteralPath $PfxPath).Path, $PfxPassword, $flags)
    if (-not $certificate.HasPrivateKey) { throw 'The PFX file has no private key.' }
}
else
{
    $certificates = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.NotAfter -gt (Get-Date) })
    if (-not $Thumbprint)
    {
        if ($certificates.Count -eq 0) { throw 'No valid code-signing certificate in Cert:\CurrentUser\My. Connect SimplySign Desktop or the token (or use -PfxPath) and try again.' }
        Write-Host 'Code-signing certificates of this user (pass one with -Thumbprint):'
        $certificates | ForEach-Object { Write-Host ('  {0}  {1}  (valid until {2:yyyy-MM-dd})' -f $_.Thumbprint, $_.Subject, $_.NotAfter) }
        return
    }
    $certificate = $certificates | Where-Object { $_.Thumbprint -eq $Thumbprint.Replace(' ', '').ToUpperInvariant() } | Select-Object -First 1
    if (-not $certificate) { throw "The certificate $Thumbprint is not in Cert:\CurrentUser\My, has expired, or is not a code-signing certificate." }
}
if ($certificate.NotAfter -lt (Get-Date)) { throw "The certificate expired on $($certificate.NotAfter)." }

# ---------------------------------------------------------------- the zip to sign
if (Test-Path $WorkFolder) { Remove-Item $WorkFolder -Recurse -Force }
$download = Join-Path $WorkFolder 'download'
$content = Join-Path $WorkFolder 'content'
$out = Join-Path $WorkFolder 'signed'
New-Item -ItemType Directory -Force $download, $content, $out | Out-Null

$extraZips = @()
if ($PSCmdlet.ParameterSetName -eq 'GitHub')
{
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The GitHub CLI (gh) is needed: https://cli.github.com (then gh auth login).' }
    $tag = 'v' + $Version.TrimStart('v')
    $appZipName = "J365-IntunePSApp-$($Version.TrimStart('v'))-win-x64.zip"
    $srcZipName = "J365-IntunePSApp-$($Version.TrimStart('v'))-source.zip"
    Write-Host "> Downloading $tag of $Repository..."
    gh release download $tag --repo $Repository --dir $download --pattern $appZipName --pattern $srcZipName --pattern 'SHA256SUMS.txt'
    if ($LASTEXITCODE -ne 0) { throw "The release $tag could not be downloaded." }
    $sums = @{}
    foreach ($line in Get-Content (Join-Path $download 'SHA256SUMS.txt')) { if ($line -match '^([0-9A-Fa-f]{64})\s+(\S+)$') { $sums[$Matches[2]] = $Matches[1].ToUpperInvariant() } }
    foreach ($f in $appZipName, $srcZipName)
    {
        $hash = (Get-FileHash (Join-Path $download $f) -Algorithm SHA256).Hash
        if ($sums[$f] -ne $hash) { throw "$f does not match SHA256SUMS.txt of the release." }
    }
    Write-Host '  OK     downloaded files match SHA256SUMS.txt'
    $appZip = Join-Path $download $appZipName
    $extraZips = @(Join-Path $download $srcZipName)
}
else
{
    $appZip = (Resolve-Path -LiteralPath $ZipPath).Path
    $appZipName = Split-Path -Leaf $appZip
}

Expand-Archive -LiteralPath $appZip -DestinationPath $content
$exe = Join-Path $content 'J365.IntunePSApp.exe'
if (-not (Test-Path $exe)) { throw "J365.IntunePSApp.exe is not in $appZipName." }
$before = Get-AuthenticodeSignature -LiteralPath $exe
if ($before.Status -eq 'Valid') { Write-Host "  Note   the executable is already signed by $(Get-SimpleName $before.SignerCertificate); it is signed again." }

# ---------------------------------------------------------------- signing
Write-Host "> Signing with $($certificate.Subject)..."
$signtool = Find-SignTool
if ($signtool -and -not $PfxPath)
{
    & $signtool sign /sha1 $certificate.Thumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 /d 'J365 IntunePSApp' /du "https://github.com/$Repository" $exe
    if ($LASTEXITCODE -ne 0) { throw 'signtool could not sign the executable (is SimplySign Desktop or the token connected?).' }
}
else
{
    # Without the Windows SDK, or with a PFX file: the signing API of Windows through PowerShell.
    $r = Set-AuthenticodeSignature -LiteralPath $exe -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $TimestampUrl
    if ($r.Status -ne 'Valid' -and -not ($AllowUntrustedRoot -and $r.Status -eq 'UnknownError')) { throw "The executable could not be signed: $($r.Status) $($r.StatusMessage)" }
}

$after = Get-AuthenticodeSignature -LiteralPath $exe
$accepted = $after.Status -eq 'Valid' -or ($AllowUntrustedRoot -and $after.Status -eq 'UnknownError' -and $after.SignerCertificate.Thumbprint -eq $certificate.Thumbprint)
if (-not $accepted) { throw "The new signature is not valid: $($after.Status) $($after.StatusMessage)" }
if (-not $after.TimeStamperCertificate) { throw "The signature has no timestamp: check the timestamp server $TimestampUrl." }
Write-Host "  OK     signed by $(Get-SimpleName $after.SignerCertificate) ($($after.Status)), timestamp by $(Get-SimpleName $after.TimeStamperCertificate)"

# ---------------------------------------------------------------- the release files again
Write-Host '> Building the release files again...'
$signedZip = Join-Path $out $appZipName
Compress-Archive -Path (Join-Path $content '*') -DestinationPath $signedZip
foreach ($z in $extraZips) { Copy-Item $z $out }
$newSums = Join-Path $out 'SHA256SUMS.txt'
Get-ChildItem $out -Filter *.zip | Sort-Object Name -Descending | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  $($_.Name)" } | Out-File $newSums -Encoding ascii
Get-Content $newSums | ForEach-Object { Write-Host "  $_" }

if ($Upload)
{
    Write-Host "> Replacing the files of the release $tag..."
    gh release upload $tag $signedZip $newSums --repo $Repository --clobber
    if ($LASTEXITCODE -ne 0) { throw 'The release files could not be replaced.' }
    Write-Host "  OK     https://github.com/$Repository/releases/tag/$tag"
}
else
{
    Write-Host ''
    Write-Host "Signed files in $out."
    if ($PSCmdlet.ParameterSetName -eq 'GitHub') { Write-Host 'Run again with -Upload to replace the files of the release.' }
}
