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
| `InstallService.cs` | Optional per-user installation, shortcuts, uninstall. |
| `Integrations.cs` | Windows Sandbox configuration and the launcher of the publishing script. |
| `Native.cs`, `ImageTools.cs` | P/Invoke (MSI, WinVerifyTrust, icons) and image work. |

## How a package is generated

1. `PackageBuilder.Validate` checks the settings (the same result is shown on the review step).
2. The official PSADT template is copied to `Package\`; the installer goes to `Files\`, extra files to `SupportFiles\`.
3. `ScriptGenerator.DeployScript` keeps the header and the tail of the template's `Invoke-AppDeployToolkit.ps1` and replaces the middle (`$adtSession` and the Install / Uninstall / Repair functions) with generated code. Every user value is written as a single-quoted PowerShell literal (`ScriptGenerator.Lit`).
4. Actions are rendered by `ActionCatalog.Generate` in their phase (before/after install or uninstall).
5. The script is parsed by Windows PowerShell 5.1. A syntax error stops the build.
6. Branding is applied (`config.psd1`, `strings.psd1`, `Assets`), the detection script is written (pure ASCII) and parsed.
7. `IntuneWinBuilder` creates the `.intunewin` and immediately validates it (HMAC, decryption, SHA256).
8. Instructions, `intune-app.json`, `Publish-ToIntune.ps1` and `Sandbox-Test.ps1` are written; `project.json` is saved last, so an incomplete folder is never listed as a package. A failed build removes its folder.

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
