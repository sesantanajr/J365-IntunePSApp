# Notice

**J365 IntunePSApp**
Copyright (c) 2026 Sérgio Sant'Ana Junior - Jornada 365 (https://jornada365.cloud)
Created by Sérgio Sant'Ana Junior - https://www.linkedin.com/in/sergiosantanacloud/
Licensed under the MIT License (see [LICENSE](LICENSE)).

## Based on PSAppDeployToolkit

This project was created on top of **PSAppDeployToolkit** (https://psappdeploytoolkit.com, https://github.com/PSAppDeployToolkit/PSAppDeployToolkit), licensed under the GNU Lesser General Public License v3.0.

J365 IntunePSApp does not contain or modify PSAppDeployToolkit's code. At run time it downloads the official v4 template from the project's GitHub releases, verifies it and copies it, unchanged, into each package it generates, together with a generated `Invoke-AppDeployToolkit.ps1` and customised configuration, text and image files, as the toolkit is designed to be used. The packages therefore include PSAppDeployToolkit under its own licence; its licence files are kept inside every package.

## Other acknowledgements

- Application database derived from **microsoft/winget-pkgs** (MIT).
- **Microsoft Win32 Content Prep Tool** is downloaded from Microsoft at run time and is not redistributed.
- The optional publishing script calls Microsoft Graph directly; it uses no third-party module.

Details in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## If you fork or reuse this project

You are welcome to (see [docs/FORKING.md](docs/FORKING.md)). Please keep this notice and the licence file, and make clear that your edition is not the original J365 IntunePSApp. The names and logos "Jornada 365" and "J365" identify the original project and its author.

---

*Português:* o **J365 IntunePSApp** foi criado por Sérgio Sant'Ana Junior (Jornada 365) e é distribuído sob a licença MIT. O projeto foi criado com base no **PSAppDeployToolkit** (LGPL-3.0): ele não contém nem modifica o código do toolkit; o template oficial é baixado em tempo de execução, verificado e copiado sem alterações para cada pacote gerado. Se você fizer um fork ou reutilizar o projeto, mantenha este aviso e o arquivo de licença e deixe claro que a sua edição não é o J365 IntunePSApp original.
