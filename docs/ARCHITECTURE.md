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
| `Native.cs`, `ImageTools.cs` | P/Invoke (MSI, WinVerifyTrust, icons) and image work: the icon of an installer, of the program an `.msi` installs (read from its cabinet with `expand.exe`) or of the copy installed on the computer. The generic icon of setup programs is recognised by a 16x16 thumbnail and never used. |
| `BatchPackager.cs` | Batch packaging of several applications of the database with shared settings. |

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

The detection script first applies the rule (name/version, product code or file) and then requires the mark. Since 2.4 a mark of any version counts: the version is checked on the installed application, which may have updated itself since an older package wrote the mark (requiring the version of the mark made Intune downgrade such applications). A copy without the mark is reported as not installed (the script writes `Not detected: ...` and exits 1), so Intune runs the package, which removes that copy and installs its own version. The mark is written at the end of *Post-Install* (a failed step leaves none) and of *Repair*, and removed after the uninstallation. It can be turned off per package (*Require the mark of this package*).

Two guards around it (2.4): at the start of *Pre-Install*, a version newer than the package already installed is kept (the package writes its mark and returns, *Keep a newer version that is already installed*); after the uninstallation, anything still registered with the name makes the deployment fail and keeps the mark, so Intune never shows "uninstalled" for a program that is still there.

## Display modes

PSAppDeployToolkit 4.1 resolves `-DeployMode Auto` at the start of the session: silent with nobody signed in, during OOBE/ESP, and also when `AppProcessesToClose` lists applications and none of them is running. That last rule made the branded dialogs disappear for most deployments. The *Automatic* mode of the application writes `NoProcessDetection = $true` in `$adtSession` (passed to `Open-ADTSession`), so the dialogs are shown whenever a user is signed in; *Discreet* keeps the toolkit's rule; *Interactive* and *Silent* go on the command line. A silent package also leaves out what it never shows (deferral, countdown, messages, restart prompt) and, by default, hides the toasts of Intune.

## Batch packaging (2.6)

`Core/BatchPackager.cs` packages several applications of the database with the same `BatchOptions` (company, owner, display mode, Intune notifications, deferral). `Plan` picks the installer the editor would choose (`CatalogService.PickInstaller`); `RunAsync` handles one application after the other: download and SHA256 check, `InstallerAnalyzer.Analyze`, `CatalogService.Apply`, company defaults (`AppConfig.NewPackage`) plus the batch options, `PackageBuilder.Validate`, then `PackageBuilder.BuildAsync` (the same build gate as the editor). `Blockers` skips what needs a person: validation errors, an `.exe` without silent switches, and a program name that also matches other applications of the database. `Views/BatchWindow` shows each `BatchItem`; the catalog keeps the ticked applications (`CatalogApp.IsQueued`). `--batch` runs the same from the command line.

## Intune notifications and restarts

`PackageBuilder.IntuneNotifications` gives the toasts of the assignment (`showAll`, `showReboot`, `hideAll`): the choice of the package, or `hideAll` for a silent package and `showAll` otherwise. `PackageBuilder.RestartBehavior` is `suppress` for a silent package and `basedOnReturnCode` otherwise. Both go to `intune-app.json` (`notifications`, `restartBehavior`), which `Publish-ToIntune.ps1` reads when it creates the app and its assignment.

## Updating the application

`AppUpdater` reads `https://api.github.com/repos/<AppInfo.GitHubRepo>/releases/latest` (or `J365_UPDATE_API`), takes the asset `J365-IntunePSApp-<version>-win-x64.zip` and `SHA256SUMS.txt`, downloads the zip with progress and checks its SHA256, extracts `J365.IntunePSApp.exe` next to the running one (`….exe.new`) and checks its version. A running executable cannot be overwritten but can be renamed: the current file becomes `….exe.old`, the new one takes its name, the application restarts with `--updated <previous version>` and the new process removes the `.old` file. On failure the original stays in place. The same rename lets the portable copy update an installed copy that is open. `--update [--check]` does the same from the command line.

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

## Application database

`tools/CatalogBuilder` streams the `microsoft/winget-pkgs` tarball (nothing is cloned), keeps the newest version of each package and writes a compact JSON (short keys, see the classes at the end of `Program.cs`) merged with `catalog/curated.json`. The result is published to the `catalog` release. The application first reads `catalog-meta.json` and downloads the database only when `generated` is newer than its local copy.

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

## Project consistency in 2.5.1

`Core/PackageRevision.cs` fingerprints normalized package settings, excluding the output location, and persists `build-revision.json` for new builds. The editor distinguishes saved state from built state; changing settings blocks publish/test actions until rebuilding. Corrupt or mismatched revision records prevent reopening an old result. Legacy packages without a record remain readable. `PackageDirectory` reserves output paths across processes; a process-wide semaphore protects the shared build log and tool state. See [quality review](QUALITY-25.md).
