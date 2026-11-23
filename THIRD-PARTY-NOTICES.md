# Third-party notices

J365 IntunePSApp is distributed under the MIT License. It uses the components below, each under its own licence.
*(Português: o J365 IntunePSApp é distribuído sob a licença MIT e usa os componentes abaixo, cada um sob a sua própria licença.)*

## PSAppDeployToolkit

- Project: https://github.com/PSAppDeployToolkit/PSAppDeployToolkit - https://psappdeploytoolkit.com
- Licence: GNU Lesser General Public License v3.0 (LGPL-3.0)
- Use: this project is **based on** PSAppDeployToolkit. The official v4 template is **downloaded at run time** from the project's GitHub releases, its digital signature is checked, and it is copied, with no change to the module, into each generated package. J365 IntunePSApp only generates `Invoke-AppDeployToolkit.ps1` and customises the configuration, text and image files the toolkit provides for that purpose. The PSAppDeployToolkit source code is available at the link above.

## Microsoft Win32 Content Prep Tool (IntuneWinAppUtil.exe)

- Project: https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool
- Licence: Microsoft Software License Terms (see the repository)
- Use: **downloaded at run time** from Microsoft's official repository and used only after its Microsoft digital signature is verified. It is **not redistributed** by this project. When it is unavailable, J365 IntunePSApp uses its own packager, compatible with the `.intunewin` format.

## Windows Package Manager Community Repository (winget-pkgs)

- Project: https://github.com/microsoft/winget-pkgs
- Licence: MIT
- Use: the application database (`catalog.json.gz`) is derived from the public manifests of this repository (name, publisher, version, official URLs, SHA256, silent switches, product codes, *Installed apps* entries and package dependencies).

## Microsoft Store catalog

- Service: the public catalog of the Microsoft Store (`storeedgefd.dsx.mp.microsoft.com`, `displaycatalog.mp.microsoft.com`)
- Use: only when the user searches the Store or publishes a Store app, to read the product id, name, publisher, description and logo. Store apps are **published by reference**: Intune installs and updates them from the Store, and nothing of the Store is downloaded into a package or redistributed.

## YamlDotNet

- Project: https://github.com/aaubry/YamlDotNet
- Licence: MIT
- Use: reading the YAML manifests in the database generator (`tools/CatalogBuilder`). Not part of the application executable.

## .NET and test libraries

- .NET runtime and WPF (https://github.com/dotnet) - MIT - included in the self-contained executable.
- xUnit (https://github.com/xunit/xunit) - Apache-2.0 - used by the tests only.

## Trademarks

Microsoft, Windows, Intune and Microsoft 365 are trademarks of Microsoft Corporation. The names and logos of the applications listed in the catalog belong to their respective owners. This project is not affiliated with Microsoft or with the PSAppDeployToolkit project.

The Jornada 365 name and logo belong to Jornada 365 (https://jornada365.cloud).
