# Changelog

All notable changes to J365 IntunePSApp. Versions follow [Semantic Versioning](https://semver.org).

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
