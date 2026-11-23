# Screenshots — J365 IntunePSApp 2.7

Unmodified PNG captures rendered by the application itself (its `--snapshot` mode), in English (`en/`) and Portuguese (`pt-BR/`), used by the [README](../../README.md) and the user guide ([English](../USER-GUIDE.md), [Português](../USER-GUIDE.pt-BR.md)).

They show a fictitious company, *Contoso*, with an isolated data folder (`C:\Users\Public\J365-Docs`): the 7-Zip MSI analysed and really packaged, two packages made before (7-Zip and the Visual C++ Redistributable), and a **sample** Microsoft Intune connection. Nothing was published: sign-in and Microsoft Graph pointed to a closed local port, and the tenant and application IDs shown do not exist.

## Regenerate

```powershell
dotnet build src/IntunePSApp -c Release
pwsh .\tools\Render-Screenshots.ps1 -Installer <7z…-x64.msi> -Extra <VC_redist.x64.exe> -CopyFrom "$env:LOCALAPPDATA\J365\IntunePSApp"
```

[`tools/Render-Screenshots.ps1`](../../tools/Render-Screenshots.ps1) (PowerShell 7) builds the demonstration data (database and PSAppDeployToolkit copied from `-CopyFrom`, or downloaded with `--setup`), renders every page in both languages and both themes at 1440×900, renders the editor checks (an outdated package, an empty search) apart, copies the captures below into this folder and removes the demonstration folder. The build workflow renders the same pages for its interface checks (`interface-captures` artifact) with a generic project.

## Files

| File | Capture | Theme |
| --- | --- | --- |
| setup.png | 00-setup-done | Light |
| home.png / home-dark.png | 01-home | Light / Dark |
| catalog.png | 02-catalog (search "7-Zip") | Light |
| batch.png | 02-batch (sample states) | Light |
| installer.png | 03-editor-1 | Light |
| application.png / application-dark.png | 03-editor-2 | Light / Dark |
| metadata.png | 03-editor-2-end | Light |
| experience.png / experience-end.png | 03-editor-3 / 03-editor-3-end | Light |
| actions.png | 03-editor-4 | Light |
| uninstall.png | 03-editor-5 | Light |
| detection.png | 03-editor-6 | Light |
| branding.png | 03-editor-7 | Light |
| review.png | 03-editor-8 | Light |
| progress.png | 04-progress | Light |
| generated.png | 04-generated | Light |
| needs-rebuild.png | 04-editor-needs-rebuild (checks pass) | Light |
| publish.png | 09-publish | Light |
| packages.png | 05-packages | Light |
| no-results.png | 05-packages-no-results (checks pass) | Light |
| settings.png, appearance.png, company.png, defaults.png, intune.png, components.png, about.png | 06-settings, 06-settings-* | Light |
| connection.png, connect-shell.png, connect-progress.png, connect-done.png, connect-failed.png | 08-connect-* | Light |

Earlier screenshots remain in the Git history.
