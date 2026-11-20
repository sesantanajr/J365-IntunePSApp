# Changelog

All notable changes to J365 IntunePSApp. Versions follow [Semantic Versioning](https://semver.org).

## 2.4.2 - 2026-11-19

### Why this version

Publishing from the application failed in a real tenant. First, the search for earlier versions of the app asked Intune for a property that only Windows (Win32) apps have, and the dialog stopped with *"Could not connect to Microsoft Intune: ... Could not find a property named 'displayVersion' on type 'microsoft.graph.mobileApp'"*. Then Intune refused the account itself (HTTP 401 *"Forbidden"*): Microsoft Graph accepted the sign-in, but the account had no active Intune role (PIM) or no Intune license, and the message pointed at the app registration instead. The connection also depended on the sign-in of a user (delegated permission). This version fixes the search, explains every refusal of Intune and adds a modern connection with no user and no secret: an app certificate.

### Added

- **Connect with an app certificate** (*Connect to Intune > Automatic with an app certificate*, recommended; also *Settings > Microsoft Intune > Connect with a certificate*). The registration *J365 IntunePSApp* gets `DeviceManagementApps.ReadWrite.All` as an **application permission** too, granted by the administrator during the setup (an app role assignment of Microsoft Graph; it takes a Global Administrator or Privileged Role Administrator), and the public part of a **certificate created on this computer**: RSA 2048, one year, its key in the **TPM** when there is one (otherwise in the Windows key store), never exportable, for the current Windows user only. Every token request is a short assertion signed with that key (PS256 with the SHA-256 thumbprint, as Microsoft recommends; the signature is checked before it is sent, and a key that signs PSS another way, such as some TPMs, uses RS256). Publishing then needs no sign-in and does not depend on the role, the PIM activation or the Intune license of an account. The setup keeps every other permission and key of the registration and removes expired keys it added. A new certificate becomes the connection only once it obtained a token: a renewal keeps the certificate in use working until then, *Try again* after a slow activation retries the sign-in only, and a cancelled or abandoned setup removes the new certificate. Then the application proves the connection by reading Intune. The delegated permission stays, so technicians can still connect with their own accounts.
- **`Publish-ToIntune.ps1 -CertificateThumbprint`**: the script signs in as the application with the certificate of the current Windows user (read through .NET, no dependency on the `Cert:` drive), renews the token by itself during long uploads, and the application passes it for publications inside the app, *Copy command* and *Run in a PowerShell window*. Checked in Windows PowerShell 5.1 against a stand-in that verifies the signature, and a full publication (create, upload, commit) with the certificate against the local Intune stand-in.
- **The publish card of a generated package**: *Publish to Microsoft Intune*, highlighted, with the connection it will use and *Publish now* (or *Connect and publish*, which opens the connection first when nothing is set up). *Open folder* and the tests moved below it.
- **Publish to Intune when done** (next to *Generate package*, remembered): the publication opens as soon as the package is generated.
- **Refusals of Intune explained.** When Intune refuses a request that Microsoft Graph accepted (HTTP 401 or 403 *"Forbidden"*), the connection window, *Test connection*, the publishing dialog and the result of a publication list what to check, read from the token in use: the delegated permission, an active Intune role (with a shortcut to PIM), an Intune license or *Allow access to unlicensed admins*, the Intune subscription of the tenant, and the certificate as the way around; for the certificate, the application permission and the tenant. The script prints the same causes.
- *Settings > Microsoft Intune* shows the certificate in use (thumbprint, expiry, TPM or key store), warns a month before it expires, and *Remove the certificate from this PC* deletes it with its key (after a confirmation); a certificate whose key is gone (a cleared TPM) shows as not usable. Switching to a user connection (a code, your account, another registration) never deletes the certificate: *Use the certificate of this PC* goes back to it without an administrator. In the publishing dialog the tenant and the registration come from the certificate. The permission is shown as delegated or application.

### Changed

- *Publish to Intune* is no longer labelled preview (dialog, card, script, summary).
- The connection window offers four ways: *Automatic with an app certificate* (recommended), *Automatic with my account* (delegated), an existing registration, Azure Cloud Shell. The roles asked for before the setup follow the way chosen.
- The error of Intune keeps its sentence and the ids for support but no longer its internal address; errors of the script show the request without its query string and with the error code (`HTTP 401 (Forbidden)`). Toasts of a publication are short; the card keeps the whole text.
- The security notes of *Settings > Microsoft Intune* describe both connections.

### Fixed

- **The search for earlier versions** asked for `displayVersion` on the list of every app, which Intune refuses; it now reads the Win32 apps without a selection. A search that fails for another reason no longer stops the publication: the dialog asks what to do with an app of the same name.
- A network failure while renewing the session signed the user out (since 2.4.1 the network error carries a code that the renewal treated as an expired session).
- The row of *Generate package* wraps on narrow windows instead of cutting the switches off.

## 2.4.1 - 2026-11-19

### Why this version

Smart App Control (Windows 11) blocked 2.4.0 with *"we couldn't verify its publisher"*. It asks Microsoft's cloud service about every program and, when the file is unknown, accepts it only with a valid digital signature; every new version is a new, unknown file. No change inside the program can get past it: the releases have to be signed. This version makes signing possible with the identities an individual open-source author can actually obtain, and makes the rest of the application behave well on computers with such policies.

### Added

- **Release signing with SignPath** (free for open-source projects through SignPath Foundation): the workflow uploads the executable, waits up to an hour for the manual approval, checks the signature and only then zips, hashes and attests. Enabled by the repository variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`, `SIGNPATH_EXE_CONFIGURATION` and the secret `SIGNPATH_API_TOKEN`. Azure Artifact Signing stays available.
- **The companion scripts are signed too** (`Publish-ToIntune.ps1`, `Sandbox-Test.ps1`; SignPath with `SIGNPATH_SCRIPTS_CONFIGURATION`, or Azure Artifact Signing) before the build embeds them, so they keep full PowerShell where App Control restricts unsigned scripts. The application writes them into the packages byte for byte, so the signature stays valid (checked with a throw-away certificate: identical bytes, and a single changed character breaks it); a fork that changes their text drops the signature block instead of leaving an invalid one.
- **`tools/Assert-Signature.ps1`**: a release with signing configured stops unless the executable and the scripts carry a valid, timestamped signature.
- **Release notes say who signed** the executable (or that it is not signed yet, with the link to `docs/SIGNING.md`), link the code signing policy, and carry the SignPath attribution when SignPath signed it. The release zip includes `CODE_SIGNING_POLICY.md`.
- **`tools/Sign-Release.ps1`**: signs a published release (or a local zip) with a certificate of the signer's computer: a Certum "Open Source Developer" certificate in SimplySign, any OV/EV token, or a `.pfx` of a company's internal authority for App Control allow-lists (key loaded in memory only). It checks the downloaded files against `SHA256SUMS.txt`, signs with SHA-256 and an RFC 3161 timestamp (`signtool` when the Windows SDK is installed, otherwise Windows' own signing API), checks the signature, rebuilds the zip and the checksums and, with `-Upload`, replaces them in the release. Run end to end on a local zip with a throw-away certificate and DigiCert's timestamp server.
- `CODE_SIGNING_POLICY.md` (what is signed, roles, privacy; linked from the README and every release, as SignPath Foundation asks), and `docs/SIGNING.md` rewritten: how Smart App Control decides, a comparison of the ways to sign (SignPath Foundation, Certum, Azure Artifact Signing, Microsoft Store), step by step for each, the answers for the SignPath application, example artifact configurations, signing for a company's App Control policy, and what to do until a release is signed.
- Settings > About shows whether the executable carries a valid signature, and whose.
- The local tests, the detection test and publishing inside the application detect a Windows PowerShell restricted by App Control (Constrained Language Mode, where unsigned scripts cannot call .NET) and say so, instead of failing with an obscure error; the restriction is written once in the log.

### Changed

- The update inside the application accepts a new version signed by any publisher of the project (SignPath Foundation, the author, Jornada 365), compared without accents and punctuation, so the signing service can change without breaking *Update now*; a foreign or missing signature is still refused once the running copy is signed.

### Fixed

Found reviewing the 2.4.0 changes:

- **"Keep a newer version" compared with the wrong version** when the minimum version of the detection was lower than the package's (package 2.5.0, minimum 2.0): a copy older than the package (2.3) counted as newer and was kept instead of updated. It now compares with the version the package installs. Checked inside PSAppDeployToolkit 4.1.8: 2.3 installed by hand is updated to 2.5.0.
- **Connecting with a code, IDs or Azure Cloud Shell no longer ends a working connection** before the new one works: the new registration is saved only after its sign-in succeeds, so a wrong code, a cancel or a failure leaves the previous registration and session as they were. The same for *Sign in* in Settings with IDs typed by hand; *Save* in Settings with another registration ends the session of the old one, as the rest of the application does.
- **No package with unsaved changes is replaced without a question** by *Update to the newest version* (Packages) or by an installer of another application (dropped, picked or from the catalog). New defaults saved in Settings, or another output folder, no longer mark the open package as changed.
- **Closing for an update, an installation or an uninstall** asks about unsaved changes and waits for a running build before the step starts, instead of asking while the application was already closing (when the answer no longer mattered); the window size and position are always kept.
- **Connection failures**: a timeout of Microsoft Graph is reported as a failure, with the network as the likely cause, instead of looking like a cancel; the network cause is offered only for network failures (no answer, timeout, HTTP 429 and 5xx), not for refusals such as HTTP 400 or 403; a failure at the very start of the automatic setup marks the right step.
- **A toolkit copy that fails the signature check before a build** (changed or damaged on disk) is removed and installed again from the official release, instead of making every build fail.
- **Environment variables** set by a package (PATH entries too) reach the signed-in user's Explorer and the rest of the deployment right away (`Update-ADTEnvironmentPsProvider`, `Update-ADTDesktop`), not only after the next sign-in. Checked inside PSAppDeployToolkit 4.1.8.
- The icons of the quick actions in the editor follow a change of theme.

## 2.4.0 - 2026-11-19

### Added

- **Connection window.** *Connect to Intune* (Settings, sidebar, publishing dialog) offers three ways: the automatic setup, a registration that already exists (a *connection code* `J365-CONNECT tenant=... client=...` that an administrator copies in Settings, or the two IDs) and **Azure Cloud Shell** (a Bash or PowerShell command that creates the same registration with the Azure CLI, already authorised in every tenant, and prints a connection code). Every step is listed with its state; a failure names the step and lists the likely causes with their action (try again, activate a role in PIM, try another account, use Azure Cloud Shell, open or copy the consent link, copy the details). The administrator account can be typed beforehand (login hint).
- **Themes.** Light, dark or same as Windows (*Settings > Appearance*, or the sidebar button), applied live. The WPF Fluent controls use the brand accent instead of the Windows accent colour; dialog windows draw their title bar in the theme; Windows 11 rounded corners and a dark frame in the dark theme.
- **Redesign.** New palette and typography, sidebar with the Intune connection and a compact database card, catalog search from any page (Ctrl+K), Home with clickable indicators, Settings in sections (General, Appearance, Company, Defaults, Microsoft Intune, Components, About), toasts as cards.
- *Keep a newer version that is already installed* (step 5, on by default): a package that finds a newer version of the application keeps it, writes its mark and returns success, instead of downgrading it.
- The editor asks before *New*, *Open project*, a dropped project or *Reopen* replace a package with changes that were neither saved nor generated, and so does closing the application.
- Publishing: *Cancel* asks the script to stop at its next safe point (`-CancelFile`); it is killed only if it does not stop within 90 seconds.

### Fixed

- **Microsoft's "sign-in cancelled" answer during the setup was a dead end.** `access_denied` with `error_subcode=cancel` is recognised and explained (closed consent screen, account without an active administrator role, tenant that blocks or saturated Microsoft Graph Command Line Tools); a sign-in that may not create applications is reported as such (HTTP 403 of Microsoft Graph); AADSTS7000112 is explained. The page the browser shows after the sign-in says what to do.
- **Downgrade of applications that updated themselves.** The detection accepted only a package mark whose version reached the minimum, so Firefox 134 with the mark of package 132 was "not detected" for package 133, which then removed 134 and installed 133. A mark of any version now counts (the version is checked on the installed application).
- **False "uninstalled".** An `.exe` that registers no silent uninstaller was skipped by the toolkit, the mark was removed and the package returned 0. The uninstallation now checks that nothing matching the name is left (with a short wait) and fails otherwise, keeping the mark.
- **Updating an app in Intune changed it before its new content existed.** The settings and the detection were sent first, then the content; a failure in between left the old content with the new detection. The new content is now uploaded and committed first, and one change switches settings, detection, version and content together.
- **Cancelling a publication** killed the script, which could leave a half-created app; the script now removes it.
- "Set environment variable" with *Path* (or *PSModulePath*, *PATHEXT*, or a value that refers to the variable itself) replaced the whole value with the literal text (for example `%PATH%;C:\Tools`) and could drop System32 from the PATH of the devices. The entries are now appended, and the value keeps its variables (REG_EXPAND_SZ).
- The PSAppDeployToolkit template had to be signed only in theory (a warning was logged). The module and the launcher must now have a valid signature of the toolkit publisher, checked when the template is installed and before every build.
- Typographic double quotes in the company name cut it short in the dialogs (PowerShell takes them as quotes when the toolkit expands its config).
- A failed removal of the previous version left its exit code behind and made the installation report a failure.
- The *Uninstall application* action did not wait for uninstallers that run from a copy of themselves (`-WaitForChildProcesses`).
- Publishing behind a proxy that asks for the Windows sign-in (NTLM or Kerberos) failed with HTTP 407.
- The *Website* button of the catalog opened any scheme; only http and https now.

## 2.3.0 - 2026-11-19

### Added

- **Connection to the tenant, with the app registration created automatically.** *Settings > Microsoft Intune > Set up automatically* (also in the publishing dialog): an administrator signs in in the browser (Microsoft Graph Command Line Tools, `Application.ReadWrite.All` and `DelegatedPermissionGrant.ReadWrite.All`, for this setup only) and the application creates the registration *J365 IntunePSApp*: single tenant, public client with `http://localhost`, no secret, only the delegated `DeviceManagementApps.ReadWrite.All`; then the enterprise application (retrying through the replication delay) and the admin consent for the organisation. A registration from an earlier setup is found and repaired. Without the right to consent, the registration is kept and the admin consent link is shown. Manual registration keeps working.
- **Session remembered.** Sign-in with authorization code and PKCE on a loopback port; the refresh token is stored encrypted with DPAPI for the Windows user (`intune-session.dat`); *Test connection* and *Sign out*. Sign-in errors are explained in plain words (consent, redirect URI, public client, Conditional Access, wrong tenant...).
- **Publishing inside the application.** With the session open, *Publish to Intune* runs the package's script without a window: the session goes in an environment variable of that process only, the steps are shown translated, with the upload progress, and can be cancelled. The script gained `-AppId` and reads the session from `J365_GRAPH_REFRESH_TOKEN`.
- **A new version updates the app already in Intune.** The dialog lists the Win32 apps whose name starts with the name of the application ("7-Zip 25.01" for a "7-Zip 26.06" package): update one of them (its assignments are kept) or create a new app.
- **Display modes:** *Automatic* (dialogs whenever a user is signed in; generated with `NoProcessDetection`), *Discreet* (the toolkit's own Auto), *Interactive*, *Silent*.
- **Live preview of the dialog in Settings**, in the Fluent or the Classic style, and the Classic style in the editor preview. Accent colour swatches in Settings.
- Home: the Intune connection is one of the indicators (opens Settings > Microsoft Intune).
- Release workflow: optional code signing with Azure Trusted Signing before zipping, hashing and attesting ([docs/SIGNING.md](docs/SIGNING.md)); `SECURITY.md` goes into the release zip.

### Fixed

- **The branded PSAppDeployToolkit dialogs did not appear when the package ran from Intune** (only Intune's notification). In the toolkit's Auto mode, a list of applications to close with none of them running turns the deployment silent; *Automatic* now keeps the dialogs whenever a user is signed in, and stays silent with nobody signed in and during OOBE/ESP.
- **Dialog style and accent colour in Settings had no visible effect**: they only applied to new packages. Saving Settings now updates the package open in the editor wherever it still had the previous defaults (what was changed in it is kept), and the preview shows the result.
- Side-by-side products (.NET 8 / 9 / 10 desktop runtime, .NET runtime, ASP.NET Core, Visual C++ 2015+ per architecture, VC++ 2013) have their own uninstall pattern, and the package mark of a database package is keyed by its database id, so one version no longer satisfies the detection of another or gets uninstalled with it.
- Two rows of a database entry with the same file name (x64 and ARM64 of Zoom) no longer overwrite each other in the download folder.
- Updating or importing the database while Settings is open no longer throws away what was typed and not saved.
- The output folder of Settings is used by every package (projects kept the folder they were first built in, or a folder of another computer).
- Per-user packages: shortcuts go to the user's own desktop and Start menu and environment variables to the user scope; actions that need an administrator (services, scheduled tasks, HKLM, all profiles, dependencies, .reg) are flagged.
- "Check the free disk space" works when no welcome dialog is generated.
- "Show a message" that waits for OK no longer ends the deployment when nobody answers (`-NoExitOnTimeout`).
- "Save as the company default" in the editor validates the colour and the logo before saving.
- Opening a project while a package is being generated says so instead of doing nothing.
- MSI read errors are in the language of the interface; the installer language preferred by default follows Windows.
- The application refuses an update not signed by the same publisher once releases are signed.

## 2.2.0 - 2026-11-19

### Added

- **Update from inside the application.** Home and *Settings* offer **Update now** when a new version is out: the release zip is downloaded with progress (it can be cancelled), checked against `SHA256SUMS.txt` of the release, the new executable is checked for its version, put in place of the running one and the application restarts in the new version ("updated to 2.2.x, it was 2.2.0"). A running executable cannot be overwritten but can be renamed: the current one becomes `….exe.old` and is removed at the next start; on failure nothing changes. Works for the portable copy and the installed copy (*Installed apps* shows the new version). README and other files that came with a portable copy are refreshed too. Command line: `--update` and `--update --check` (exit code 10 when a newer version exists). `J365_UPDATE_API` points the check to another server (internal mirror, tests).
- **Package mark.** A generated package writes `HKLM\SOFTWARE\J365\IntunePSApp\Apps\<application>` (HKCU for per-user packages; 64-bit view) when the installation (or repair) finishes, and removes it after the uninstallation. The detection script requires the mark with a version equal to or higher than the minimum. On by default (*Require the mark of this package*, Detection step); the review step warns when it is off and existing versions are removed.
- *Test on this computer* tells "installed here, but not by this package" (Intune would run the package) apart from "not installed".
- Home tells when the installed copy is older than the copy you are running, and updates it with one click.

### Fixed

- **Intune reported the application as installed and the copy already on the device was never removed** (reported with 7-Zip). Intune runs the detection rule before the package; a rule by name and version also matched a copy installed some other way, so the package, and its removal of existing versions, never ran on those devices. The package mark fixes it.
- **Removing an existing version did not wait for uninstallers that hand the work to a copy of themselves** in `%TEMP%` and exit at once (7-Zip, NSIS): the new version could be installed while the old one was still being removed. EXE removals now wait for the child processes.
- **Generic icon for MSI files without an icon of their own** (7-Zip). The program the package installs (the target of its shortcuts, otherwise its largest program) is read from the cabinet, embedded in the MSI or next to it, with `expand.exe`; nothing is installed. When even that has none, the icon of the copy installed on the computer (its *DisplayIcon*) is used before the tile with the initials. Icons are cached, and the editor reads them in the background.
- **Updating the installed copy failed while it was open** ("the installed copy is in use"). It is now replaced by renaming the running file; the open window is asked to close first and keeps the old version until it closes. The installed copy also fixes the version shown in *Installed apps* when it starts.
- The Windows Sandbox test window printed "Detected: Detected: ...".

### Changed

- Home banner: the Jornada 365 logo in its version for dark backgrounds, transparent, without the white box. The README uses the dark version of the logo in GitHub's dark theme.
- Packages generated by 2.2 run once more on devices where the application came from an older package (no mark yet): they reinstall the same version and write the mark. Turn the mark off for an application that is already deployed everywhere if you do not want that.

## 2.1.3 - 2026-11-19

### Added

- **Look-alike applications.** The Uninstall step shows which other applications of the database the program name also matches ("7-Zip*" also matches 7-Zip ZS; "KeePass*" would match KeePassXC). Where one of them is installed it would be uninstalled before the installation and would count for the detection rule.

### Fixed

- The built-in packager keeps its two working copies in the application's temporary folder instead of next to the package (often a synchronised OneDrive folder), stops as soon as the build is cancelled, and writes zip entries with "/" like Microsoft's tool.
- A download that fails for good, or is cancelled, no longer leaves a `.part` file; a server that never answers fails after 60 seconds instead of waiting forever.
- "Remove desktop shortcut" and "Delete file" treat names with brackets (`App [x64].lnk`) literally, unless the name uses `*` or `?` on purpose.

### Changed

- README cover images show the current application.

## 2.1.2 - 2026-11-19

### Publish to Intune, rewritten

- **The script talks to Microsoft Graph directly.** The community module it depended on is gone: nothing is installed on the administrator's computer. That module failed on Windows set to day-first dates (pt-BR, en-GB...), reported a failed sign-in as "connected" and could leave an empty app in the tenant.
- **Truthful outcome.** The script writes `publish-result.json` on every exit path and the result card shows it, with an *Open in Intune* link. Exit codes: 0 published, 1 failed, 2 cancelled.
- **No orphan apps.** If anything fails after the app was created, the incomplete app is deleted again.
- **An app with the same name** is detected before anything is created: ask, stop, update the existing app (assignments kept) or create another one.
- **Optional assignment** when publishing: available or required, for all users, all devices or a group. Off by default; a tenant-wide required assignment asks twice.
- **Validate package** button and a clear explanation for the usual sign-in errors (consent, redirect URI, public client, Conditional Access).
- Only one permission is requested (`DeviceManagementApps.ReadWrite.All`); sign-in is authorization code with PKCE in the browser, or a device code.
- Windows 11 23H2 can be chosen as the minimum operating system.
- Packages generated by older versions get the new script the next time *Publish to Intune* is used.
- Still labelled **preview**: every step was run against a local stand-in for Entra ID, Graph and Azure Storage that verifies what is sent (including MAC, IV and digest of the uploaded content), but not against a real tenant.

### Application icons

- **Every package has an icon**, in `Intune\AppIcon.png`, and nothing has to be downloaded for it: the icon chosen in the editor (new field on the Application step), the icon inside the installer, or a generated tile with the initials of the application.
- **Icons are now read from MSI installers** too (the product icon of the Icon table).
- The icon is shown in the editor header and in the package lists, used by the publishing script and, when there is no company logo, by the PSAppDeployToolkit dialogs.
- Changed: when the installer has no icon, the company logo is no longer used as the application icon in Intune (every app looked the same in the Company Portal); the generated tile is used instead.

### Interface

- Home: the banner shows the Jornada 365 logo. Sidebar: the credits card is back to text only. *Settings > About*: photo of the author, who he is and his LinkedIn.

### Fixed: generated packages

- **Detection never matched names or paths with accents** ("Aplicação" was written as "Aplicacao"). Name patterns now use a one-character wildcard for such letters and file paths are written as exact characters.
- **"Run PowerShell" action:** code is written exactly as typed; here-strings no longer break the script and multi-line strings keep their content. Valid code with braces inside strings is no longer rejected.
- **"Continue on error" really continues:** a failed program in an action no longer makes the whole deployment report a failure to Intune.
- Pre-install actions run before the removal of the versions already installed, so they can help it.
- A company name with `$` or a backtick is shown as typed in the dialogs.
- The syntax check no longer reports "valid" when PowerShell could not run the check at all.
- Validation now reports: a registry number that is not a number (it became 0), success codes written as a range (they were dropped), HKCU registry actions in a System package, a welcome message that would never be shown, and "include the installer folder" with the installer inside the output folder (the installer was left out of the package).
- Updating a package to a new version that changed from `.exe` to `.msi` (or back) no longer keeps the switches of the old installer.
- Temporary files of another running instance are no longer deleted at start.

### Fixed: application database

- **A file listed for several architectures or scopes** (Google Drive, Bitwarden, KeePass and about 80 others) could produce an ARM64-only or per-user package. The row chosen in the catalog now decides, and a lookup by hash prefers the machine-wide x64 row.
- **MSI properties declared by the manifest are kept** (licence acceptance of the Microsoft ODBC and OLE DB drivers, VeraCrypt and others); only the quiet and log switches are dropped.
- **An installer matched by name no longer inherits the pattern of a sibling** (a Python 3.12 installer got "Python 3.13*": never detected, and 3.13 would be removed).
- An older MSI that shares its product code with the newest version keeps its own version; an installer the target cannot run (ARM64 on x64) is never picked; `/ALLUSERS` is not added twice.
- **Search** ranks a whole word and the winget moniker above a piece of a longer word ("edge" finds Microsoft Edge before EdgeEver).
- Database generator: versions with a prefix (`v2.7.6`, `b12532`) are compared by their number (25 packages were stuck on an old version); the English description is used when the default language of a package is another one; one malformed translation no longer drops the package; curated ids that match nothing are reported.
- Curated list: 84 applications (was 63). Fixed patterns for SQL Server Management Studio, Firefox ESR, KeePass and TeamViewer; AnyDesk's id corrected; new entries for OneDrive, WebView2, Remote Desktop, OpenVPN, WireGuard, WinMerge, draw.io, PDF-XChange Editor, Temurin 8/17/25, Python 3.12/3.14 and more runtimes.
- Exit codes a manifest declares as success are applied to the package.

### Not done

- Application icons from the database: no package of the winget repository carries an icon today, so there is nothing to download; the icon comes from the installer.
- Supersedence of the previous version when publishing.

## 2.1.1 - 2026-11-19

### Added

- A reopened package shows the result of its last Windows Sandbox test on the result card.

### Changed

- Documentation: new screenshots made with 2.1 (both languages) and README sections about the Windows Sandbox test, screen sizes and troubleshooting. The README shipped inside the release package is now the current one.
- Capture mode (`--snapshot`): steps taller than the window get a second capture of their last part.

## 2.1.0 - 2026-11-19

### Fixed

- **The Windows Sandbox test looked frozen on "Installing".** Two causes, both measured inside Windows Sandbox with the 7-Zip MSI package:
  - Windows Sandbox starts with *Smart App Control* in evaluation mode, and a 2 MB MSI took about two minutes to install. The test now turns that evaluation off inside the sandbox before installing (`-KeepSmartAppControl` skips it); the same installation takes seconds.
  - One click on the PowerShell window put it in selection mode (QuickEdit), which pauses whatever is writing to it. QuickEdit is now turned off for the test window.
- The test waited for the package **and every process it left behind**; it now waits for the package only, with a time limit (60 minutes, Intune's default) and a clear message when it is reached.
- The local detection test and the script syntax check could wait forever on a process that filled its error stream; both streams are now read while the process runs and the time limit is enforced.

### Added

- **Windows Sandbox not installed? The application says so and installs it.** *Test in Sandbox* now tells apart: feature turned off (offers to install it with DISM, asking for administrator permission, and tells you to restart), edition without the feature (Home, Server), a sandbox already open, and virtualization that looks disabled in the firmware.
- **Live progress and result of the sandbox test.** While the package runs, the test window shows the elapsed time and the last entry of the PSAppDeployToolkit log. The result (exit code, detection, time) and the logs come back to `Sandbox\Results` of the package and are shown on the result card, with an *Open logs* button. New command in the test window: `Show-Log`.
- **"Versions already installed on the device"** on the first step of the editor: the switch *Uninstall any version already installed before installing the new one* is now where it is seen first (it stays on step 5 with its details) and spells out the order on the device: close the application, uninstall what is there, install. The generated script lists what it found, removes MSI and EXE installations, checks again and logs anything that survived. No detection is consulted before uninstalling.
- **Warning when the detection rule would hide an old version**: without the version check, Intune considers an older version "installed" and never runs the package, so nothing is replaced.
- **Jornada 365 logo** (the one of the blog) on the sidebar, the home page and *Settings > About*.
- `--sandbox <package folder>` on the command line, and `--snapshot-size <width>x<height>` for layout checks.

### Changed

- **The window adapts to the screen.** Minimum size reduced from 1120 x 720 to 900 x 580; the first size never exceeds the work area and the window starts maximized where the default does not fit (small notebooks, 125-150 % scaling, remote sessions); size and maximized state are remembered. On narrow windows the sidebar keeps only its icons, the home page and the catalog reflow, the editor steps wrap and the dialog preview gives its room to the form. Dialogs never open larger than the screen.
- Packages generated by 2.0 get the new test script automatically the next time *Test in Sandbox* is used.

## 2.0.0 - 2026-11-19

### Added

- **English and Portuguese interface.** Switch at any time in the sidebar or in Settings; "Automatic" follows Windows. The generated instructions use the same language; generated scripts are commented in English.
- **Automatic first-run setup.** The application downloads and verifies its dependencies by itself (application database, PSAppDeployToolkit template, Microsoft IntuneWinAppUtil) and shows the progress. *Settings > Dependencies* verifies and repairs them later. `--setup` does the same from the command line.
- **Optional per-user installation** (no administrator rights): Start menu shortcut, entry in *Installed apps*, clean uninstall. The portable mode remains the default. `--install` / `--uninstall`.
- **Dependencies.** The database now carries the package dependencies declared by winget manifests (per installer). The catalog shows them and the editor downloads, ships and installs them first with one click.
- **New actions:** *Install a dependency*, *Uninstall another application*, *Remove Start menu shortcut*, *Import .reg file* (20 actions in total).
- **Package update tracking.** *My packages* and the home page show which packages have a newer version in the database; *Update* replaces the installer and keeps every customisation. Dropping a new version of the same application on an open package offers the same.
- **Test in Windows Sandbox.** One click opens a disposable Windows, installs the package and runs the detection rule.
- **Publish to Intune (preview).** Every package includes `Publish-ToIntune.ps1` and `intune-app.json`; the application starts the script with your tenant and app registration. Uses the community module IntuneWin32App and your own sign-in.
- **Notices** on the home page: new application version, new PSAppDeployToolkit version, missing dependencies, outdated packages.
- **Minimum operating system** per package (used by the instructions and by the publishing script).
- **Source package** (`J365-IntunePSApp-<version>-source.zip`), **build provenance attestation** and a smoke test of the published executable in every release.
- **Unit tests** (125) run on every push.
- Command line: `--help`, `--version`, `--lang`, `--setup`, `--update-catalog`, `--install`, `--uninstall`.
- Documentation: building, forking, architecture, publishing to Intune, security policy, changelog.

### Changed

- **The database is only downloaded when it changed** (a 200-byte metadata file is checked first) and is checked daily by default.
- Downloads retry transient failures, fail after 60 seconds without data instead of hanging, and only appear at their destination when complete and verified.
- Package folders and files use English names: `Package`, `Detection_*.ps1`, `Intune-Instructions.html`, `project.json`, `build.log`. Packages and projects created by 1.x are still listed, opened and migrated automatically.
- Action options are stored as language-neutral codes; projects saved by 1.x are converted when opened.
- Application dialogs are drawn by the application (in its language) instead of Windows message boxes.
- A failed or cancelled build removes its incomplete folder; builds can be cancelled.
- A new PSAppDeployToolkit template is validated before it replaces the working one.
- Reopening a project inside a package folder restores its result (commands, tests, publishing).
- Deleting a package sends it to the Recycle Bin.
- Stack traces and generated files no longer carry the local build path.

### Fixed

- Packages that install in the **user context** no longer ask PSAppDeployToolkit to block application execution (it needs administrator rights and failed).
- *Include every file of the installer folder* silently produced a package without the installer when the installer came from the catalog; shared folders (Downloads, Desktop, a drive root...) are now refused with a clear message.
- Two extra files with the same name overwrote each other; actions pointing to a file that is not in the package are reported before building.
- Detection by file with a per-user path in a System-context package now warns that the path will not be the user's.
- The installer technology is no longer shown as "unknown" after a database match.

## 1.0.0 - 2026-11-18

First public release: portable WPF application, application database generated daily from winget-pkgs, package wizard (experience, actions, uninstall, detection, branding), `.intunewin` creation with Microsoft's tool or the built-in packager, Intune instructions.
