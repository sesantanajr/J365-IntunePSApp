# Screenshots — J365 IntunePSApp 2.5.1

These are unmodified PNG captures rendered by the application on the Windows CI runner, in English and Portuguese, light and dark themes. They use a generic demonstration project and isolated data; no tenant connection, app installation or publication was performed.

Source: [interface-captures artifact, workflow run 44848884088](https://github.com/sesantanajr/J365-IntunePSApp/actions/runs/44848884088), generated on 2026-11-20 (America/Sao_Paulo). The application version is 2.5.1. The CI commit only extends capture language coverage; application code is the released 2.5.1 code.

| Documentation file | Capture | Theme |
| --- | --- | --- |
| application-dark.png | 03-editor-2.png | Dark |
| application.png | 03-editor-2.png | Light / compact |
| branding.png | 03-editor-7.png | Light / compact |
| packages.png | 05-packages.png | Dark |
| no-results.png | 05-packages-no-results.png | Light / compact |
| needs-rebuild.png | 04-editor-needs-rebuild.png | Dark |
| connect-failed.png | 08-connect-failed.png | Light |
| connect-shell.png | 08-connect-shell.png | Light |

`pt-BR/` uses the `dark` / `light` artifact folders; `en/` uses `en-dark` / `en-light`. The demonstration project's name is user data and stays in Portuguese when the interface language changes. The sample package on the stale-result screen is a UI test fixture, not an installable package. Connection screens are staged UI states.

Obsolete, unreferenced screenshots were removed; historical versions remain in Git history. To regenerate, use the build workflow and its `interface-captures` artifact. See [BUILDING.md](../BUILDING.md).
