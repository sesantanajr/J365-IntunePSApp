<#
.SYNOPSIS
    Renders the screenshots of the documentation (README and user guide) in English and Portuguese.

.DESCRIPTION
    1. Builds an isolated demonstration data folder (by default C:\Users\Public\J365-Docs: a neutral path, because the
       output folder appears on some pages) with the application database and the PSAppDeployToolkit, copied from a data
       folder that has them (-CopyFrom) or downloaded by the application itself (--setup).
    2. Writes settings for a fictitious company (Contoso) and a sample Microsoft Intune connection. Nothing is ever sent:
       sign-in and Microsoft Graph point to a closed local port, and the session file is a placeholder protected with
       DPAPI for the current Windows user.
    3. Runs the capture mode of the application (--snapshot) for each language and theme: the installer given with
       -Installer is analysed in the editor, a package is really generated from it, and the publishing dialog is drawn.
    4. Copies the captures listed in $Map into docs/screenshots/<language> (unless -NoCopy) and removes the demonstration
       folder (unless -Keep).

.PARAMETER Installer
    An .msi or .exe to analyse and package (for example the 7-Zip MSI). It is copied; the original is not changed.

.PARAMETER CopyFrom
    A data folder of the application that already has catalog\catalog.json.gz and tools\ (for example
    %LOCALAPPDATA%\J365\IntunePSApp). Without it the application downloads them (--setup).

.NOTES
    Needs PowerShell 7 (pwsh): Windows PowerShell 5.1 writes dates in JSON as "\/Date(...)\/", which the application
    does not read, so the demonstration settings would be ignored.

.EXAMPLE
    dotnet build src/IntunePSApp -c Release
    pwsh .\tools\Render-Screenshots.ps1 -Installer C:\Installers\7z2606-x64.msi -CopyFrom "$env:LOCALAPPDATA\J365\IntunePSApp"
#>
#Requires -Version 7.0
[CmdletBinding()]
param
(
    [Parameter(Mandatory = $true)][string]$Installer,
    # Installers packaged before the captures (with --build), so Home and My packages show some packages.
    [string[]]$Extra = @(),
    [string]$CopyFrom,
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\IntunePSApp\bin\Release\net10.0-windows\J365.IntunePSApp.exe'),
    [string]$DataRoot = 'C:\Users\Public\J365-Docs',
    [string]$Out,
    [string[]]$Languages = @('en', 'pt-BR'),
    [string[]]$Themes = @('light', 'dark'),
    [string]$Size = '1440x900',
    [switch]$NoCopy,
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Exe = (Resolve-Path $Exe).Path
if (-not (Test-Path -LiteralPath $Installer -PathType Leaf)) { throw "Installer not found: $Installer" }
if (-not $Out) { $Out = Join-Path $DataRoot 'captures' }

# Captures used by the documentation: documentation file <- capture of the application (light theme unless noted).
$Map = [ordered]@{
    'setup.png'               = '00-setup-done.png'
    'home.png'                = '01-home.png'
    'home-dark.png'           = 'dark:01-home.png'
    'catalog.png'             = '02-catalog.png'
    'batch.png'               = '02-batch.png'
    'installer.png'           = '03-editor-1.png'
    'application.png'         = '03-editor-2.png'
    'application-dark.png'    = 'dark:03-editor-2.png'
    'metadata.png'            = '03-editor-2-end.png'
    'experience.png'          = '03-editor-3.png'
    'experience-end.png'      = '03-editor-3-end.png'
    'actions.png'             = '03-editor-4.png'
    'uninstall.png'           = '03-editor-5.png'
    'detection.png'           = '03-editor-6.png'
    'branding.png'            = '03-editor-7.png'
    'review.png'              = '03-editor-8.png'
    'progress.png'            = '04-progress.png'
    'generated.png'           = '04-generated.png'
    'publish.png'             = '09-publish.png'
    'needs-rebuild.png'       = 'checks:04-editor-needs-rebuild.png'
    'packages.png'            = '05-packages.png'
    'no-results.png'          = 'checks:05-packages-no-results.png'
    'settings.png'            = '06-settings.png'
    'appearance.png'          = '06-settings-appearance.png'
    'company.png'             = '06-settings-company.png'
    'defaults.png'            = '06-settings-defaults.png'
    'intune.png'              = '06-settings-intune.png'
    'components.png'          = '06-settings-tools.png'
    'about.png'               = '06-settings-about.png'
    'connection.png'          = '08-connect-choose.png'
    'connect-shell.png'       = '08-connect-shell.png'
    'connect-progress.png'    = '08-connect-progress.png'
    'connect-done.png'        = '08-connect-done.png'
    'connect-failed.png'      = '08-connect-failed.png'
}

# ---------------------------------------------------------------- demonstration data
if (Test-Path $DataRoot) { Remove-Item $DataRoot -Recurse -Force }
$data = Join-Path $DataRoot 'data'
$packages = Join-Path $DataRoot 'Packages'
$installers = Join-Path $DataRoot 'Installers'
New-Item -ItemType Directory -Force $data, $packages, $installers | Out-Null
$inst = Join-Path $installers (Split-Path $Installer -Leaf)
Copy-Item -LiteralPath $Installer $inst

$env:J365_INTUNEPSAPP_DATA = $data
# Nothing reaches Microsoft: sign-in and Graph go to a closed local port.
$env:J365_LOGIN_URL = 'http://127.0.0.1:9'
$env:J365_GRAPH_URL = 'http://127.0.0.1:9'

if ($CopyFrom) {
    New-Item -ItemType Directory -Force (Join-Path $data 'catalog'), (Join-Path $data 'tools') | Out-Null
    Copy-Item (Join-Path $CopyFrom 'catalog\catalog.json.gz') (Join-Path $data 'catalog') -Force
    Copy-Item (Join-Path $CopyFrom 'tools\*') (Join-Path $data 'tools') -Recurse -Force
}
else {
    Write-Host 'Downloading the database and the tools (--setup)...'
    $p = Start-Process $Exe -ArgumentList '--setup' -PassThru -WindowStyle Hidden -Wait
    if ($p.ExitCode -ne 0) { throw "--setup failed: $($p.ExitCode)" }
}

$tenantId = '8d4f2c61-3b7e-4a90-9f15-2c6e7d1b0a42'      # sample values: no such tenant or registration
$clientId = '0e7a9b35-61c4-4d28-b3f0-5a91c8e27d16'
$now = [DateTime]::UtcNow
$config = [ordered]@{
    SetupCompleted = $true; CheckForUpdates = $true; LastUpdateCheck = [DateTime]::Now; LastCatalogCheck = [DateTime]::Now
    CompanyName = 'Contoso'; Author = 'Contoso IT'; AccentColor = '#2563EB'; DialogStyle = 'Fluent'
    OutputFolder = $packages; IntuneTenantId = $tenantId; IntuneClientId = $clientId; IntuneTenantName = 'contoso.onmicrosoft.com'
    IntuneAuthMode = 'delegated'; IntunePlan = 'active'; IntunePlanTenant = $tenantId; IntunePlanUtc = $now
}
$config | ConvertTo-Json | Set-Content (Join-Path $data 'config.json') -Encoding utf8

# A placeholder session of a signed-in user, protected as the application protects it (DPAPI, current user).
$session = @{ TenantId = $tenantId; ClientId = $clientId; Account = 'admin@contoso.onmicrosoft.com'; Name = 'Contoso Admin'; Token = 'sample-for-screenshots'; SignedInUtc = $now } | ConvertTo-Json
Add-Type -AssemblyName System.Security
$protected = [System.Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($session),
    [Text.Encoding]::UTF8.GetBytes('J365.IntunePSApp.IntuneSession'), [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
[IO.File]::WriteAllBytes((Join-Path $data 'intune-session.dat'), $protected)

# Packages already made before the captures (the editor then generates one more in every run).
foreach ($i in @($Installer) + $Extra) {
    $copy = Join-Path $installers (Split-Path $i -Leaf)
    if (-not (Test-Path $copy)) { Copy-Item -LiteralPath $i $copy }
    $p = Start-Process $Exe -ArgumentList @('--build', "`"$copy`"", '--out', "`"$packages`"") -PassThru -WindowStyle Hidden -Wait
    if ($p.ExitCode -ne 0) { Write-Warning "Packaging $(Split-Path $i -Leaf) before the captures failed: $($p.ExitCode)" }
}
$prebuilt = @(Get-ChildItem $packages -Directory | Select-Object -ExpandProperty FullName)
Write-Host "Packages made before the captures: $($prebuilt.Count)"

# ---------------------------------------------------------------- captures
function Invoke-Capture([string]$Folder, [string[]]$Arguments)
{
    if (Test-Path $Folder) { Remove-Item $Folder -Recurse -Force }
    Get-ChildItem $packages -Force | Where-Object { $prebuilt -notcontains $_.FullName } | Remove-Item -Recurse -Force
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process $Exe -ArgumentList (@('--snapshot', "`"$Folder`"") + $Arguments) -PassThru -WindowStyle Hidden
    if (-not $p.WaitForExit(600000)) { $p.Kill(); throw "Rendering $Folder timed out." }
    if (Test-Path (Join-Path $Folder 'error.txt')) { throw (Get-Content (Join-Path $Folder 'error.txt') -Raw) }
    if ($p.ExitCode -ne 0) { throw "Rendering $Folder failed: $($p.ExitCode)" }
    Write-Host ('{0}: {1} captures in {2:N0} s' -f (Split-Path $Folder -Leaf), (Get-ChildItem $Folder -Filter *.png).Count, $sw.Elapsed.TotalSeconds)
}

foreach ($lang in $Languages) {
    # Every page, with the installer analysed, a package really generated and the publishing dialog.
    foreach ($theme in $Themes) {
        Invoke-Capture (Join-Path $Out "$lang-$theme") @('--lang', $lang, '--theme', $theme, '--snapshot-size', $Size,
            '--snapshot-installer', "`"$inst`"", '--snapshot-build', '--snapshot-out', "`"$packages`"", '--snapshot-publish',
            '--snapshot-progress', '--snapshot-setup', '--snapshot-search', '7-Zip', '--no-update')
    }
    # The checks of the editor and of the library (an outdated package, an empty search). They edit the open project,
    # so they run apart from the pass above.
    Invoke-Capture (Join-Path $Out "$lang-checks") @('--lang', $lang, '--theme', 'light', '--snapshot-size', $Size,
        '--snapshot-installer', "`"$inst`"", '--snapshot-state-checks', '--snapshot-search', '7-Zip', '--no-update')
}

# ---------------------------------------------------------------- copy into the documentation
if (-not $NoCopy) {
    foreach ($lang in $Languages) {
        $dest = Join-Path $repo "docs\screenshots\$lang"
        New-Item -ItemType Directory -Force $dest | Out-Null
        foreach ($doc in $Map.Keys) {
            $capture = $Map[$doc]
            $theme = 'light'
            if ($capture -match '^(dark|checks):(.+)$') { $theme = $Matches[1]; $capture = $Matches[2] }
            $src = Join-Path $Out "$lang-$theme\$capture"
            if (Test-Path $src) { Copy-Item $src (Join-Path $dest $doc) -Force }
            else { Write-Warning "Missing capture for ${lang}: $theme\$capture" }
        }
    }
    Write-Host "Copied into docs\screenshots."
}
if (-not $Keep) { Remove-Item $DataRoot -Recurse -Force -ErrorAction SilentlyContinue }
