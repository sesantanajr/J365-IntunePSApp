# Architecture

A short tour for people who want to read, change or reuse the code. (Em português: o código e os comentários estão em inglês; este documento resume como as peças se encaixam. Dúvidas podem ser abertas em português nas issues.)

## The idea

J365 IntunePSApp does not deploy anything by itself. It **writes a standard [PSAppDeployToolkit](https://psappdeploytoolkit.com) v4 package** from the answers given in the interface, wraps it in an `.intunewin` file and explains how to register it in Intune:

```
installer (.msi/.exe)  ─┐
application database   ─┼─►  PackageSettings  ─►  PackageBuilder  ─►  Package\  (PSADT template + generated Invoke-AppDeployToolkit.ps1)
answers in the wizard  ─┘        (project.json)                      Intune\   (.intunewin, detection script, instructions, intune-app.json)
```

Everything a package needs is in `PackageSettings`, saved as `project.json` next to the package. A project can be reopened, changed, built again from the interface or with `--build`.

## Projects

| Project | Role |
|---|---|
| `src/IntunePSApp` | The WPF application (.NET 10, single-file, self-contained). |
| `tests/IntunePSApp.Tests` | xUnit tests of the core. |
| `tools/CatalogBuilder` | Console tool run by GitHub Actions to build the application database. |

## `src/IntunePSApp/Core` (no UI code)

| File | Responsibility |
|---|---|
| `Models.cs` | `AppInfo` (product identity), `PackageSettings`, `PackageAction`, `AppConfig`, catalog model. |
| `Infrastructure.cs` | `AppPaths` (portable vs. installed data folder), `Log`, `ConfigStore`, `Http` (retries, stall timeout, hash check), small helpers (`Names`, `VersionUtil`). |
| `CatalogService.cs` | Loads, updates and searches the database; picks the best installer; maps winget metadata to silent switches. |
| `InstallerAnalyzer.cs` | Reads an installer: MSI properties, EXE technology (Inno, NSIS, Burn...), signature, database match. |
| `ActionCatalog.cs` | The ready-made actions: definition (fields), validation and the PSADT/PowerShell code each one generates. |
| `ScriptGenerator.cs` | Generates `Invoke-AppDeployToolkit.ps1` from the toolkit template, patches `config.psd1` / `strings.psd1`, writes the detection script. |
| `PackageBuilder.cs` | Orchestrates a build, validates settings, knows the package layout, loads/migrates projects. |
| `IntuneWinBuilder.cs` | Creates and validates `.intunewin` (Microsoft's tool, or the built-in implementation of the same format). |
| `SummaryWriter.cs` | The Intune instructions (HTML and text). |
| `ToolsService.cs` | Downloads and validates PSAppDeployToolkit and IntuneWinAppUtil; `PowerShellValidator` parses generated scripts with Windows PowerShell 5.1. |
| `SetupService.cs` | First-run dependency setup and the update checks. |
| `IntuneConnection.cs` | Entra ID sign-in (authorization code + PKCE on a loopback port), the session kept with DPAPI, the automatic app registration (application, enterprise application, admin consent) with its steps, the connection code and the Graph calls of publishing (test, earlier versions of an app). |
| `ConnectScripts.cs` | The Azure CLI commands (Bash and PowerShell) that create the same registration from Azure Cloud Shell. |
| `AppUpdater.cs` | Updates the application while it is open: reads the latest GitHub release, downloads the zip, checks it against `SHA256SUMS.txt`, puts the new executable in place of the running one and restarts. |
| `InstallService.cs` | Optional per-user installation (also over an installed copy that is open), shortcuts, uninstall. |
| `Integrations.cs` | Windows Sandbox: state of the Windows feature (ready, open, turned off, not in this edition), enabling it with DISM, the `.wsb` configuration and the result of the test. Launcher of the publishing script. |
| `Native.cs`, `ImageTools.cs` | P/Invoke (MSI, WinVerifyTrust, icons) and image work: the icon of an installer, of the program an `.msi` installs (read from its cabinet with `expand.exe`) or of the copy installed on the computer. Generic setup icons (Inno Setup, WiX Burn, NSIS stock icons; `Resources/GenericSetupIcons.txt`) are recognised by a 16x16 thumbnail and never used. |
| `BatchPackager.cs` | Batch packaging of several applications of the database with shared settings. |
| `IntuneAppStatus.cs` | The install status of a published app (2.8): the app, its assignments and supersedence, and the device install report, read page by page from the report functions of Intune, or exported (`DeviceInstallStatusByApp`) when no function answers. |

## How a package is generated

1. `PackageBuilder.Validate` checks the settings (the same result is shown on the review step).
2. The official PSADT template is copied to `Package\`; the installer goes to `Files\`, extra files to `SupportFiles\`.
3. `ScriptGenerator.DeployScript` keeps the header and the tail of the template's `Invoke-AppDeployToolkit.ps1` and replaces the middle (`$adtSession` and the Install / Uninstall / Repair functions) with generated code. Every user value is written as a single-quoted PowerShell literal (`ScriptGenerator.Lit`).
4. Actions are rendered by `ActionCatalog.Generate` in their phase (before/after install or uninstall).
5. The script is parsed by Windows PowerShell 5.1. A syntax error stops the build.
6. Branding is applied (`config.psd1`, `strings.psd1`, `Assets`), the detection script is written (pure ASCII) and parsed.
7. `IntuneWinBuilder` creates the `.intunewin` and immediately validates it (HMAC, decryption, SHA256).
8. Instructions, `intune-app.json`, `Publish-ToIntune.ps1` and `Sandbox-Test.ps1` are written; `project.json` is saved last, so an incomplete folder is never listed as a package. A failed build removes its folder.

## Detection and the package mark

Intune runs the detection rule **before** a Win32 app: when it already says "installed", the package never runs. A rule that only looks at the program name and version also matches a copy installed some other way (by hand, by another tool), so on those devices nothing was removed or installed. Since 2.2 the generated package writes a mark when it finishes:

```
HKLM\SOFTWARE\J365\IntunePSApp\Apps\<application>     (HKCU for packages installed per user; 64-bit view)
    Version, Name, Vendor, PackageVersion, InstalledUtc, Generator
```

The detection script first applies the rule (name/version, product code or file) and then requires the mark. Since 2.4 a mark of any version counts: the version is checked on the installed application, which may have updated itself since an older package wrote the mark (requiring the version of the mark made Intune downgrade such applications). A copy without the mark is reported as not installed (the script writes `Not detected: ...` and exits 1), so Intune runs the package, which removes an older copy and installs its own version (a newer or, since 2.7, the same version of the same product is kept and only marked, see below). The mark is written at the end of *Post-Install* (a failed step leaves none) and of *Repair*, and removed after the uninstallation. Every package of an application writes the same key (`Apps\<database id or name>`), so a device where an earlier package installed the application counts as installed and a new package never runs there. Since 3.0.3 *Reinstall also where the app is already installed* (`PackageSettings.ForceReinstall`) writes the round it started (`ReinstallRound`, UTC) in the mark and the detection asks for it (`$RequiredRound`): Intune runs the package once on every device, which removes the installed versions as chosen and installs again; packages generated again with the same round do not reinstall again. The batch uses one round for all its packages (`--reinstall` on the command line). It can be turned off per package (*Require the mark of this package*).

Two guards around it (2.4): at the start of *Pre-Install*, a version newer than the package already installed is kept (the package writes its mark and returns; `PackageSettings.KeepNewerVersion`, the choice *Remove older versions and the same one; keep a newer version*); after the uninstallation, anything still registered with the name makes the deployment fail and keeps the mark, so Intune never shows "uninstalled" for a program that is still there.

Since 3.0 a newer version is the only copy a package keeps. Since 3.0.1 one choice, *Versions already installed on the device* (`ExistingVersions` in `Core/Models.cs`, for the package, the company defaults, the batch and `--existing` of the command line), sets `RemovePreviousVersions` and `KeepNewerVersion` together, so projects keep their format: *Remove any installed version* (`RemoveAll`, not even a newer copy is kept: the script has no `Test-J365NewerVersion`), *Remove older versions and the same one; keep a newer version* (`KeepNewer`, the default) and *Do not remove* (`InstallOver`). Shared components (Edge, WebView2, the Visual C++ and .NET runtimes, `neverRemove` in `catalog/curated.json`) are never removed; an application the database advises not to remove (`removePrevious: false`) is removed only with `RemoveAll`, and the analysis says so. 2.7 also kept the same version installed some other way (`KeepSameVersion`); that option is gone, so every device ends with the copy the package installs. The removal is written once, as functions at the top of the generated script (`ScriptGenerator.RemovalHelpers`), and used by the installation and by the uninstallation: `Get-J365InstalledCopies` (the programs with the name, MSI and EXE, plus the MSI with the product code of the package), `Remove-J365InstalledCopy` (an MSI by its product code; an .exe with the switches of the package, or the silent command it registered, or the switches of its setup technology: Inno Setup by its key `…_is1`, NSIS by the manifest of the uninstaller; an .exe nobody knows how to silence is not started, because as SYSTEM its window would wait forever) and `Wait-J365CopiesRemoved` (what is still installed, after up to 30 seconds for uninstallers that finish in the background). Before the installation, a copy that is still there stops the deployment with an error that names it (`PackageSettings.InstallIfRemovalFails` false, the default) or is logged and installed over (true). `Release30Tests` runs these functions in Windows PowerShell 5.1 against stand-ins of the toolkit.

## Silent or with notification

A package is installed one of two ways (`DeployModes`, 3.0): *With notification* (`Notify`) or *Silent*. PSAppDeployToolkit 4.1 resolves `-DeployMode Auto` at the start of the session: silent with nobody signed in, during OOBE/ESP, and also when `AppProcessesToClose` lists applications and none of them is running. That last rule made the branded dialogs disappear for most deployments, so a package that notifies writes `NoProcessDetection = $true` in `$adtSession` (passed to `Open-ADTSession`) and needs no switch on the command line: its dialogs are shown whenever a user is signed in. *Silent* goes on the command line (`-DeployMode Silent`), leaves out what it never shows (countdown, messages, restart prompt) and hides the toasts of Intune. Nothing is deferred in either: the welcome dialog only warns that an open application is about to be closed. The four modes of 2.x (`Auto`, `WhenNeeded`, `Interactive`, `Silent`) are read through `DeployModes.Normalize`: everything that is not `Silent` notifies.

## Batch packaging (2.6)

`Core/BatchPackager.cs` packages several applications of the database with the same `BatchOptions` (company, owner, silent or with notification). `Plan` picks the installer the editor would choose (`CatalogService.PickInstaller`); `RunAsync` handles one application after the other: download and SHA256 check, `InstallerAnalyzer.Analyze`, `CatalogService.Apply`, company defaults (`AppConfig.NewPackage`) plus the batch options, `PackageBuilder.Validate`, then `PackageBuilder.BuildAsync` (the same build gate as the editor). `Blockers` skips what needs a person: validation errors, an `.exe` without silent switches, and a program name that also matches other applications of the database. With `BatchOptions.Publish`, each package is published right after its build the way the editor publishes inside the application (`IntunePublisher.LaunchInApp`, the session or the certificate of this computer, `-Force` with the chosen `IfExists` and assignment); a failed publication is retried alone. `PublishTexts` (Core) turns the lines and the result of the script into texts for the editor and the batch. `Views/BatchWindow` shows each `BatchItem`; the catalog keeps the ticked applications (`CatalogApp.IsQueued`) and opens a skipped one in the editor. `--batch` (with `--publish`) runs the same from the command line, and `--snapshot` renders the window with sample items so the interface checks of the build cover it.

## Applications of the Microsoft Store (2.6)

The database is built from winget-pkgs, which does not contain the msstore source of winget. `Core/StoreApps.cs` searches the Store live with the same public service winget uses (`storeedgefd.dsx.mp.microsoft.com/v9.0`: `manifestSearch` by keyword, or exactly by product id; `packageManifests/{id}` for scopes and links) and the catalog of the Store (`displaycatalog.mp.microsoft.com`) for the localized description and the official logo. Results are `CatalogApp` objects with `IsStore` (one object per product id). They are not packaged: `StorePublisher` creates a `winGetApp` ("Microsoft Store app (new)") with Graph, using the session or the certificate of the connection (`packageIdentifier`, `installExperience.runAsAccount`: system for UWP/MSIX apps, user for a Win32 app of the Store that only installs per user; the logo as `largeIcon`), keeps or refreshes an app of the same product id, waits until Intune has read it from the Store and assigns it with `winGetAppAssignmentSettings` (the toasts of the batch). The catalog appends Store results after the database; the batch publishes them (`--batch <product id> --publish` on the command line).

## Intune notifications and restarts

`PackageBuilder.IntuneNotifications` gives the toasts of the assignment: `hideAll` for a silent package and `showAll` for one that notifies (since 3.0 the type of installation decides; there is no separate choice). `PackageBuilder.RestartBehavior` is `suppress` for a silent package and `basedOnReturnCode` otherwise. Both go to `intune-app.json` (`notifications`, `restartBehavior`), which `Publish-ToIntune.ps1` reads when it creates the app and its assignment.

## Supersedence and the install status (2.8)

`Publish-ToIntune.ps1 -IfExists Supersede` creates the new version as a new app and, once its content is active and before its assignment, posts its relationships (`updateRelationships` with one `mobileAppSupersedence`: the earlier app, `update` or `replace`), as the Intune admin center does. The earlier app is the one given with `-AppId` (the application passes the version chosen in the publishing dialog) or the newest app of the same name by version. A refused link does not undo the publication: `publish-result.json` records it (`supersedence`), with the superseded app and the type; the application reads it into `PublishResult`. `IntuneAppStatus` reads the status of a published app for the status window (`Views/AppStatusWindow.cs`), with the same permission; the report functions of Intune are not documented and were renamed once, so it tries them newest first and falls back to the documented export job.

## Updating the application

`AppUpdater.GetLatestAsync` finds the latest release in three places, in turn: the `SHA256SUMS.txt` of the latest release (`github.com/<AppInfo.GitHubRepo>/releases/latest/download/SHA256SUMS.txt`, which names `J365-IntunePSApp-<version>-win-x64.zip`), the API (`api.github.com/repos/<repo>/releases/latest`) and the page of the latest release (it redirects to `…/releases/tag/v<version>`). The first one is not the API, so it is not subject to the 60 unauthenticated requests an hour per public address that a company network shares (copies up to 2.6.0 asked only the API and reported "up to date" whenever that limit was reached). `J365_UPDATE_API` points to a mirror that answers alone. Every check is logged with its source; a check that read nothing is shown as a failure, never as "up to date". The update takes `J365-IntunePSApp-<version>-win-x64.zip` and `SHA256SUMS.txt` of that release, downloads the zip with progress and checks its SHA256, extracts `J365.IntunePSApp.exe` next to the running one (`….exe.new`) and checks its version. A running executable cannot be overwritten but can be renamed: the current file becomes `….exe.old`, the new one takes its name, the application restarts with `--updated <previous version>` and the new process removes the `.old` file. On failure the original stays in place. The same rename lets the portable copy update an installed copy that is open. `--update [--check]` does the same from the command line.

## The Windows Sandbox test

`SandboxService.Prepare` writes the current `Sandbox-Test.ps1` and a `.wsb` file with two mapped folders: the package, read-only, at `C:\J365Package`, and `Sandbox\Results`, writable, at `C:\J365Results`. At logon the script:

1. turns off QuickEdit in its console (a click would otherwise pause it) and the Smart App Control evaluation of the sandbox (it makes an MSI take minutes instead of seconds; measured with a 2 MB MSI: about 120 s against 1 s);
2. copies the package to `C:\J365Test`, runs the detection script, then the install command line of `intune-app.json`, waiting for that process only, with a time limit, while showing the elapsed time and the last entry of the PSAppDeployToolkit log;
3. runs the detection script again and writes `result.json` and the logs to `C:\J365Results`.

The editor polls `Sandbox\Results\result.json` and shows each step (install, uninstall) on the result card. `SandboxService.State` decides what *Test in Sandbox* does before that: start, ask to close the open sandbox, offer to enable the Windows feature (`dism /Online /Enable-Feature /FeatureName:Containers-DisposableClientVM /All`, elevated), or explain that the edition has no sandbox.

## Themes and look

`App.xaml` merges, in this order, the WPF Fluent control styles (`Fluent.Light.xaml` or `Fluent.Dark.xaml`), the colors of the theme (`Themes/Light.xaml` or `Themes/Dark.xaml`, the same keys in both) and `Styles.xaml`. Every color is referenced with `DynamicResource`, and colors set in code use `SetResourceReference`, so `Views/ThemeManager.cs` swaps the first two dictionaries and every open page follows at once. The theme dictionaries also give the Fluent controls the brand accent (`SystemAccentColor*`, `AccentFillColor*`) instead of the Windows accent color. *System* follows `AppsUseLightTheme` and listens to Windows changes. Dialog windows (`DialogWindow`) draw their title bar in the theme (`DialogWindowStyle`); `ThemeManager.ApplyWindowFrame` asks Windows 11 for rounded corners and a dark frame. `--theme light|dark` forces a theme for the screenshots.

## The connection window

`Views/ConnectWindow.xaml` drives the three ways to connect (automatic setup, connection code or IDs, Azure Cloud Shell). `IntuneConnection.CreateRegistrationAsync` reports each `SetupStep`; the window adds the activation wait, the user sign-in and a read of one Intune app, and maps the failure (`AuthException.Code`, for example `cancel` for `access_denied` + `error_subcode=cancel`, or `insufficient_privileges` for HTTP 403 of Graph) to its likely causes and actions. `Core/ConnectScripts.cs` holds the Azure CLI commands of the Cloud Shell way; both they and *Copy connection code* produce `J365-CONNECT tenant=... client=...`, read by `IntuneConnection.TryParseConnectionCode`. `--snapshot` also draws the pages of this window with sample content.

## Responsive layout

The window never opens larger than the work area and remembers its size. `MainWindow.ApplyResponsiveLayout` switches the sidebar to icons only under 1180 px of width and hides its database card under 760 px of height; `HomeView` and `EditorView` have their own thresholds (indicator columns, stacked cards, dialog preview). `--snapshot <folder> --snapshot-size 900x580` renders every page at a given window size to check them.

## The `.intunewin` format

```
IntuneWinPackage/Contents/IntunePackage.intunewin   [HMAC-SHA256 (32 bytes)][IV (16 bytes)][AES-256-CBC(zip of the package folder)]
IntuneWinPackage/Metadata/Detection.xml             encryption key, MAC key, IV, MAC, SHA256 of the unencrypted zip, setup file
```

The outer file is a zip stored without compression. The HMAC covers IV + ciphertext.

### Progress

A build is one bar (`BuildProgress`): each step weighs the seconds it should take and says how far it is. The `.intunewin` step follows the real work of the tool that makes it. IntuneWinAppUtil.exe 1.8.7 draws its own bar only in a console window; with the output redirected it prints one line as each of its six passes over the package begins: the content compressed into a zip (Fastest), the zip encrypted into a second file, the HMAC of that file, the SHA256 of the zip (both managed code of .NET Framework), the encrypted file copied over the zip, and the package folder stored in the `.intunewin` through System.IO.Packaging, which writes the content to a buffer before the file. `IwauProgress` (in `IntuneWinBuilder.cs`) turns those lines into passes, measures each pass by the bytes the process has read since its line (`GetProcessIoCounters`: the two hashes only read, so the files alone would leave the bar still for about 40% of the time) and gives each pass its share of the time; without the counters it measures the files the tool writes, and a version whose lines it does not recognise is followed by what it writes (about three times the content). The built-in packager weighs its three passes (zip, encryption, outer file) by their cost: a byte to compress (Optimal) about ten times a byte stored as it is, so an installer stored as it is makes each pass about a third (`NativeShares`). In the batch window each item shows the percentage of the step it is in (download, build, upload) and the time left at the pace so far (`BatchItem.Estimate`).

## Application database

`tools/CatalogBuilder` streams the `microsoft/winget-pkgs` tarball (nothing is cloned), keeps the four newest versions of each package and writes the newest one in a compact JSON (short keys, see the classes at the end of `Program.cs`) merged with `catalog/curated.json`. The result is published to the `catalog` release. The application first reads `catalog-meta.json` and downloads the database only when `generated` is newer than its local copy.

Since 3.0.5 the builder also asks every installer link the application can package whether it still gives the file (`LinkChecker.cs`: HEAD, then a GET of one byte; a few requests per host). Only 404, 410 or a host name that no longer resolves counts against a link; a refusal (403, 429), a server error or a timeout says nothing about the file. A gone link is written on its installer (`"g": "404"`); when every link of the newest version is gone, the newest of the three earlier versions whose link works takes its place (`"fb"`: the version it replaces). With more than 15% of the links gone the run is suspect and nothing is marked. `catalog-meta.json` reports the counts (`links`). The application does not offer an installer whose link is gone: *Supported only* hides the application, the batch skips it with the reason, and a link that dies between two checks fails at once (404 and 410 are not retried) with the vendor's page offered. In November 2026, 334 of 16,723 links were gone: 16 packages fell back to an earlier version and 275 had no working link (Autodesk DWG TrueView 2027 among them).

## Localization

- Texts live in `Resources/Strings.en.json` and `Strings.pt-BR.json` (flat key/value, embedded in the executable).
- XAML uses `{l:T Key}`: a binding to `Loc.Instance[Key]`, so the interface changes language without restarting.
- Code uses `T("Key", args)`. Texts built in code are refreshed through `ILocalized.OnLanguageChanged`.
- Values saved in projects are language-neutral codes; labels are resolved when shown.
- Dynamic keys follow conventions: `Act_<Type>`, `ActD_<Type>`, `ActF_<Type>_<Field>`, `ActO_<Type>_<Field>_<Option>`, `Phase_<Phase>`, `Setup_Item_<key>`.
- Tests fail when a key is missing in a language, when placeholders differ, or when the code uses an undefined key.

## Data folder

| Mode | Data folder |
|---|---|
| Portable (default) | `IntunePSApp-Data` next to the executable |
| Installed per user, or read-only folder | `%LOCALAPPDATA%\J365\IntunePSApp` |
| `J365_INTUNEPSAPP_DATA` set | That folder (used by tests and automation) |

It holds `config.json`, `catalog\`, `tools\` (PSADT templates, IntuneWinAppUtil), `downloads\`, `branding\`, `logs\` and, in portable mode, `Packages\`.

## Project consistency (since 2.5.1)

`Core/PackageRevision.cs` fingerprints normalized package settings, excluding the output location, and persists `build-revision.json` for new builds. The editor distinguishes saved state from built state; changing settings blocks publish/test actions until rebuilding. Corrupt or mismatched revision records prevent reopening an old result. Legacy packages without a record remain readable. `PackageDirectory` reserves output paths across processes; a process-wide semaphore protects the shared build log and tool state. See the [quality review](QUALITY.md).
