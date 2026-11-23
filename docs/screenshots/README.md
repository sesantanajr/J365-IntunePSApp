# Screenshots — J365 IntunePSApp 2.6

Unmodified PNG captures rendered by the application on the Windows CI runner, in English and Portuguese, light and dark themes, with a generic demonstration project and isolated data: no tenant connection, installation or publication.

Source: `interface-captures` artifact of [workflow run 45189845492](https://github.com/sesantanajr/J365-IntunePSApp/actions/runs/45189845492) (2.6.1, 2026-11-23). The pages shown did not change in 2.6.2.

| Documentation file | Capture | Theme / size |
| --- | --- | --- |
| application-dark.png | 03-editor-2.png | Dark |
| application.png | 03-editor-2.png | Light / compact |
| metadata.png | 03-editor-2-end.png | Dark |
| experience.png | 03-editor-3.png | Dark |
| actions.png | 03-editor-4.png | Dark |
| uninstall.png | 03-editor-5.png | Dark |
| detection.png | 03-editor-6.png | Dark |
| branding.png | 03-editor-7.png | Light / compact |
| needs-rebuild.png | 04-editor-needs-rebuild.png | Dark |
| packages.png | 05-packages.png | Dark |
| no-results.png | 05-packages-no-results.png | Light / compact |
| appearance.png | 06-settings-appearance.png | Dark |
| defaults.png | 06-settings-defaults.png | Dark |
| intune.png | 06-settings-intune.png | Dark |
| connection.png | 08-connect-choose.png | Dark |
| connect-failed.png | 08-connect-failed.png | Light |
| connect-shell.png | 08-connect-shell.png | Light |

`pt-BR/` uses the `dark` / `light` artifact folders; `en/` uses `en-dark` / `en-light`. The demonstration project's name is user data and stays in Portuguese when the interface language changes. Connection screens are staged UI states. The batch window is rendered as `02-batch.png` with sample applications in every state. The catalog page needs the application database, which the CI runner does not download, so it is not used here.

To regenerate, run the build workflow and take its `interface-captures` artifact; see [BUILDING.md](../BUILDING.md). Earlier screenshots remain in the Git history.
