<p align="center">
  <img src="docs/assets/cover-en.png" alt="J365 IntunePSApp - package any application for Microsoft Intune" width="100%">
</p>

<h1 align="center">J365 IntunePSApp</h1>

<p align="center">
  <b>Package any Windows application for Microsoft Intune in minutes. No scripting.</b><br>
  Built on <a href="https://psappdeploytoolkit.com">PSAppDeployToolkit</a> · Portable · Open source (MIT)
</p>

<p align="center">
  <a href="https://github.com/sesantanajr/J365-IntunePSApp/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/sesantanajr/J365-IntunePSApp?label=release&color=0A5CA8"></a>
  <a href="https://github.com/sesantanajr/J365-IntunePSApp/actions/workflows/build.yml"><img alt="Build" src="https://github.com/sesantanajr/J365-IntunePSApp/actions/workflows/build.yml/badge.svg"></a>
  <a href="https://github.com/sesantanajr/J365-IntunePSApp/actions/workflows/catalog.yml"><img alt="Application database" src="https://github.com/sesantanajr/J365-IntunePSApp/actions/workflows/catalog.yml/badge.svg"></a>
  <a href="LICENSE"><img alt="MIT licence" src="https://img.shields.io/badge/licence-MIT-0A5CA8"></a>
</p>

<p align="center">
  <b>English</b> · <a href="#-português">Português</a>
</p>

> **Created by [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/)** · [Jornada 365](https://jornada365.cloud)
>
> This project is **based on [PSAppDeployToolkit](https://psappdeploytoolkit.com)**, the open-source deployment framework: every package it generates is a standard PSAppDeployToolkit v4 package. J365 IntunePSApp is the friendly front end that writes the package for you.

<p align="center"><a href="https://jornada365.cloud"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/assets/j365-blog-logo-dark.png"><img src="docs/assets/j365-blog-logo.png" alt="Jornada 365" width="240"></picture></a></p>

---

## What it does

You pick an application (from a database of 16,000+ apps, or your own `.msi` / `.exe`), answer a few questions, and get everything Intune needs:

- the **`.intunewin`** file, ready to upload;
- the **install and uninstall command lines**;
- a **detection script**;
- an **instructions page** with every field of the Intune wizard ready to copy;
- the complete **PSAppDeployToolkit package**, so you can keep editing it by hand if you want.

Anyone can use it: there is nothing to install, no script to write and no PowerShell knowledge required.

## What is new in 2.3

- **Connect to your tenant once, publish with one click.** *Settings > Microsoft Intune > Set up automatically*: you sign in as an administrator and the application creates the app registration **J365 IntunePSApp** in your Entra ID (single tenant, no secret, only `DeviceManagementApps.ReadWrite.All`, admin consent included). Nothing to do by hand in the portal. Your session is remembered, encrypted for your Windows user, and *Publish to Intune* runs **inside the application**, with the steps and the upload progress on screen, no PowerShell window. A new version can **update the app already in Intune** (its assignments are kept) instead of creating a second one. See [docs/INTUNE-PUBLISH.md](docs/INTUNE-PUBLISH.md).
- **Fixed: the branded PSAppDeployToolkit dialogs did not appear** when the package ran from Intune; users only saw Intune's notification. The toolkit's *Auto* mode turns the deployment silent when none of the applications to close is open. The *Automatic* mode of the application now shows the welcome, progress and completion dialogs whenever a user is signed in (silent with nobody signed in and during Autopilot). The previous behaviour is the new *Discreet* mode. Proven in the PSAppDeployToolkit log: *"running in [Interactive] mode"* instead of *"changing deployment mode to [Silent]"*.
- **Fixed: "Dialog style" and "Accent colour" in Settings seemed to do nothing.** They were defaults for new packages only. Settings now shows a live **preview of the dialog** (Fluent or Classic, with your logo, name and colour), and saving applies the new defaults to the package open in the editor wherever it still had the old ones. The editor preview now shows the Classic style too.
- **Found by a review of every feature and fixed:** side-by-side products (.NET 8 and 9) no longer share the uninstall pattern and the package mark; installers of different architectures no longer overwrite each other in the download folder; a database update no longer reverts unsaved Settings; the output folder of Settings is used by every package; shortcuts and environment variables of per-user packages go to the user's own folders, and actions that need an administrator are flagged; "Check free disk space" works without a welcome dialog; a message that waits for OK can no longer end the deployment.
- **Trust on Windows 11:** the release workflow signs the executable with Azure Trusted Signing as soon as the signing identity is configured ([docs/SIGNING.md](docs/SIGNING.md)); the application then refuses updates not signed by the same publisher.
- **Executive look:** Home shows the Intune connection next to the other indicators; refined cards and depth.

<p align="center"><img src="docs/screenshots/en/intune.png" alt="Settings > Microsoft Intune: connect the tenant automatically" width="88%"></p>

Earlier in 2.2: update from inside the application, the package mark that fixed "Intune shows installed but the old copy stayed", the real application icon from MSI files, the transparent Jornada 365 logo. The [changelog](CHANGELOG.md) lists everything.

## Download

1. Download **`J365-IntunePSApp-<version>-win-x64.zip`** from the [latest release](https://github.com/sesantanajr/J365-IntunePSApp/releases/latest).
2. Extract it anywhere (a USB stick works) and run **`J365.IntunePSApp.exe`**.
3. On the first run the application **prepares itself**: it downloads and verifies the application database, the PSAppDeployToolkit template and Microsoft's IntuneWinAppUtil. Nothing is installed on Windows.

<p align="center"><img src="docs/screenshots/en/setup.png" alt="Automatic first-run setup" width="88%"></p>

**Requirements:** Windows 10 or 11 (x64). No .NET to install (it is inside the executable). Internet access on the first run.

> **Windows protection prompts.** The executable is not code-signed (this is a free community project). Windows SmartScreen may show *"Windows protected your PC"*: check the file first (see [Verify the download](#verify-the-download)), then choose *More info > Run anyway*. On computers with **Smart App Control** turned on, Windows can block unsigned applications it does not know yet and offers no exception; in that case use another computer or a virtual machine. The release workflow is ready to sign the executable ([docs/SIGNING.md](docs/SIGNING.md)).

## Features

| | |
|---|---|
| **Application database** | 16,000+ applications with silent switches, official download URLs, SHA256 hashes and uninstall data. Rebuilt **every day** from the public [winget-pkgs](https://github.com/microsoft/winget-pkgs) repository, plus curated knowledge (processes to close, detection tips). Only a tiny metadata file is downloaded when nothing changed. |
| **Any installer** | Drop an `.msi` or `.exe`. The technology is identified (MSI, Inno Setup, NSIS, WiX Burn, InstallShield, Advanced Installer, Squirrel) and the silent switches are suggested. The file is matched against the database by hash, product code or name. |
| **User experience** | Close running applications (with countdown), let the user defer, block the app during the installation, custom welcome / progress / completion messages, restart prompt. Display mode **Automatic** (branded dialogs whenever a user is signed in, silent during Autopilot), Discreet, Interactive or Silent. Live preview of the dialog, Fluent or Classic. |
| **Company branding** | Your logo, company name and accent colour in every dialog. Save once, reuse in every package. |
| **Ready-made actions** | 20 actions, no scripting: install a **dependency**, uninstall another app, shortcuts, files and folders, copy to every user profile, registry values, import `.reg`, services, scheduled tasks, environment variables, run a program, show a message... and a PowerShell box for everything else. |
| **Dependencies** | When the winget manifest declares dependencies (Visual C++ Redistributable, .NET Desktop Runtime...), one click downloads the right installer, ships it inside the package and installs it first. |
| **Versions already installed** | Uninstalls any version already on the device (MSI and EXE) and only then installs the new one, so two versions never stay side by side. No detection is consulted before uninstalling; uninstallers that hand the work to a copy of themselves are waited for; what was found and removed goes to the log. |
| **Detection** | Generated detection script: by program name and minimum version, by product code, or by file, plus the **mark of the package**, so a copy installed some other way does not stop Intune from running the package. Test it on your computer with one click. |
| **Keeping packages current** | *My packages* shows which packages have a **newer version in the database**. *Update* downloads the new installer and keeps every customisation. |
| **Test before publishing** | **Windows Sandbox**: one click installs the package in a disposable Windows and runs the detection rule, with live progress; the result and the logs come back to the application. If the Windows feature is not installed, the application says so and installs it for you. Or test on the local computer. |
| **Application icon** | Every package gets `Intune\AppIcon.png` for Intune and the Company Portal: the icon of the installer, of the program an `.msi` installs (read from its cabinet), of the copy installed on your computer, a file you choose, or a generated tile with the initials. Nothing to download separately. |
| **Publish to Intune (preview)** | Connect once (the app registration is created for you), then publish with one click inside the application, with progress: create or update the app (a new version updates the previous one), optional assignment, link to the admin center. The same `Publish-ToIntune.ps1` also runs by hand. See [docs/INTUNE-PUBLISH.md](docs/INTUNE-PUBLISH.md). |
| **English and Portuguese** | Switch the language at any time (sidebar or Settings). The generated instructions follow the same language. |
| **Portable or installed** | Runs from any folder. Optionally installs for your user (Start menu, *Installed apps*) without administrator rights, and uninstalls cleanly. **Updates itself** with the window open. |
| **Any screen size** | The window fits small notebooks, 125-150 % scaling and remote sessions: icon-only sidebar and pages that reflow. |
| **Command line** | `--build`, `--sandbox`, `--setup`, `--update-catalog`, `--install`, `--update`... for automation. |

## How it works

**1. Choose the application** - search the catalog or drop your installer.

<p align="center"><img src="docs/screenshots/en/catalog.png" alt="App catalog" width="88%"></p>

The installer is identified (technology, silent switches, database match) and you decide right there what happens to **versions already installed on the device**: uninstall whatever is found, then install the new one.

<p align="center"><img src="docs/screenshots/en/installer.png" alt="Installer identified and the option to uninstall versions already installed" width="88%"></p>

**2. Customise** - experience, actions, dependencies, uninstall, detection and branding. Everything is filled in with sensible defaults.

<p align="center"><img src="docs/screenshots/en/application.png" alt="Application step with the application icon for Intune and the Company Portal" width="88%"></p>
<p align="center"><img src="docs/screenshots/en/experience.png" alt="User experience with live preview" width="88%"></p>
<p align="center"><img src="docs/screenshots/en/actions.png" alt="Dependencies and ready-made actions" width="88%"></p>

**3. Generate** - the package is validated (script syntax, `.intunewin` integrity) and you get the commands, the detection script and the instructions.

**4. Test in Windows Sandbox, then send to Intune** - one click installs the package in a disposable Windows and runs the detection rule. The result comes back to the same card.

<p align="center"><img src="docs/screenshots/en/generated.png" alt="Package ready for Intune, with the result of the Windows Sandbox test" width="88%"></p>

**Later** - the application tells you when a packaged app has a new version.

<p align="center"><img src="docs/screenshots/en/packages.png" alt="My packages with update notice" width="88%"></p>

## Testing in Windows Sandbox

*Test in Sandbox* opens a disposable Windows, installs the package with the same command line Intune uses and runs the detection rule. Nothing changes on your computer.

| | |
|---|---|
| **Requirements** | Windows 10/11 Pro, Enterprise or Education with the *Windows Sandbox* feature and hardware virtualisation enabled. When the feature is turned off, the application offers to install it (administrator permission, then a restart). Windows Home and Windows Server do not have it. |
| **What you see in the sandbox** | A PowerShell window with the elapsed time and the last entry of the PSAppDeployToolkit log while the package runs, then `RESULT`. It stays open with four commands: `Test-Install`, `Test-Uninstall`, `Test-Detection` and `Show-Log`. |
| **What comes back** | Exit code, detection and time on the result card; `result.json` and the logs in `Sandbox\Results` of the package. Reopening the package shows its last result again (2.1.1). |
| **What the test changes inside the sandbox** | It turns off the Smart App Control evaluation (it makes installers take minutes there) and QuickEdit in the test window (a click would pause it). Both only exist in that disposable Windows. |
| **Time limit** | 60 minutes per run, the same default as Intune. A package that asks a question nobody answers is stopped and reported as failed. |
| **Differences from Intune** | Intune installs as SYSTEM; the sandbox installs as its local administrator. Always finish with a pilot group. |
| **Command line** | `J365.IntunePSApp.exe --sandbox "<package folder>"` |

## Any screen size

The window never opens larger than the screen and remembers its size. On small notebooks, 125-150 % scaling and remote sessions the sidebar keeps only its icons and every page reflows.

<p align="center"><img src="docs/screenshots/en/compact.png" alt="The same result card in a 900 x 580 window" width="62%"></p>

## What is generated

```
<Output>\<App>_<Version>\
├── Package\                     PSAppDeployToolkit package (Invoke-AppDeployToolkit.exe/.ps1, Files, SupportFiles, Config, Assets...)
├── Intune\
│   ├── <App>_<Version>.intunewin    upload this file to Intune
│   ├── Detection_<App>_<Version>.ps1
│   ├── Intune-Instructions.html     every field of the Intune wizard, ready to copy (+ .txt)
│   ├── intune-app.json              the same information, machine-readable
│   ├── Publish-ToIntune.ps1         optional publishing script (preview)
│   └── AppIcon.png
├── Sandbox\
│   ├── Sandbox-Test.ps1             test script used by "Test in Sandbox"
│   └── Results\                     result.json and logs written by the last sandbox test
├── project.json                 reopen, change and generate again
└── build.log
```

In Intune (*Apps > Windows > Add > Windows app (Win32)*):

| Field | Value |
|---|---|
| Install command | `Invoke-AppDeployToolkit.exe -DeploymentType Install` |
| Uninstall command | `Invoke-AppDeployToolkit.exe -DeploymentType Uninstall` |
| Install behavior | System (or User, when you choose so) |
| Detection rules | Use a custom detection script: `Detection_<App>_<Version>.ps1` |

## Command line

```text
J365.IntunePSApp.exe [file]                                   open a project (project.json) or analyse an installer
J365.IntunePSApp.exe --build <project.json|installer> [--out <folder>] [--native]
J365.IntunePSApp.exe --sandbox <package folder>               test a generated package in Windows Sandbox
J365.IntunePSApp.exe --setup                                  download and verify the dependencies
J365.IntunePSApp.exe --update-catalog [--force]               update the application database
J365.IntunePSApp.exe --install [--desktop-shortcut] [--quiet] install for the current user
J365.IntunePSApp.exe --uninstall [--purge] [--quiet]          remove the installation
J365.IntunePSApp.exe --update [--check]                       update this executable to the latest release
    --lang <en|pt-BR>    --version    --help
```

## Verify the download

Every release is built by GitHub Actions from the tagged commit and carries checksums and a signed build provenance attestation.

```powershell
# 1. Checksum: compare with SHA256SUMS.txt of the release
Get-FileHash .\J365-IntunePSApp-2.3.0-win-x64.zip -Algorithm SHA256

# 2. Provenance: proves the file was built by this repository's workflow (GitHub CLI)
gh attestation verify .\J365-IntunePSApp-2.3.0-win-x64.zip --repo sesantanajr/J365-IntunePSApp
```

What the application itself verifies: the database installers (SHA256 from the winget manifest), the PSAppDeployToolkit module (Authenticode signature), IntuneWinAppUtil (Microsoft signature, otherwise discarded), every `.intunewin` it creates (HMAC and SHA256) and its own updates (SHA256 of the release zip and the version of the new executable). See [SECURITY.md](SECURITY.md).

## Source package, building and forking

The project is open source so you can **audit it, build it yourself, fork it or base another project on it**.

- **Source package:** every release includes `J365-IntunePSApp-<version>-source.zip`, the exact source of that release.
- **Build:** `dotnet publish src/IntunePSApp -c Release -r win-x64` with the .NET 10 SDK. Details in [docs/BUILDING.md](docs/BUILDING.md).
- **Fork / rebrand / translate:** [docs/FORKING.md](docs/FORKING.md) lists the few places to change (name, links, logo, database URL) and how to add a language.
- **How it is organised:** [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
- **Contributing:** [CONTRIBUTING.md](CONTRIBUTING.md). The quickest way to help is improving [`catalog/curated.json`](catalog/curated.json).

## Troubleshooting

| Symptom | What to do |
|---|---|
| The sandbox test stays on *Installing* for minutes (version 2.0) | Update to 2.1 and click *Test in Sandbox* again: packages generated by 2.0 get the new test script automatically. If you had clicked on the test window, press Enter in it. |
| *Windows Sandbox is not enabled on this computer* | Choose *Install the feature now*, accept the Windows permission prompt and restart. By hand: *Turn Windows features on or off > Windows Sandbox*, or as administrator `dism /Online /Enable-Feature /FeatureName:Containers-DisposableClientVM /All`. Virtualisation (Intel VT-x / AMD-V) must be enabled in the firmware. |
| *Windows Sandbox is already open* | Windows allows one sandbox at a time: close its window and test again. |
| The sandbox test failed | *Open logs* on the result card, or `Show-Log` in the test window. Exit codes: `0`, `1707`, `3010`, `1641` are success; `1618` means another installation was running; `1602` means the user deferred or cancelled. |
| Intune says *Installed* but the copy that was already on the device is still there | The detection rule matched that copy, so Intune never ran the package. Generate the package again with 2.2: *Require the mark of this package* (Detection step) makes a copy installed some other way count as not installed. Also keep *Require a version equal to or higher than* on (the review step warns about both). On the device, `HKLM\SOFTWARE\J365\IntunePSApp\Apps` lists what the packages installed. |
| *Test on this computer* says the application is installed, but not by this package | Expected where the package has not run: Intune will run it there (remove the copy found, install this version, write the mark). |
| The update inside the application does not start | It needs to reach `api.github.com` and `github.com` (proxy and firewall). A copy in a read-only folder cannot replace itself: the application offers the download page instead. The installed copy is in `%LOCALAPPDATA%\Programs\J365 IntunePSApp`. |
| *Windows protected your PC* when starting the application | See [Download](#download): the executable is not code-signed yet ([docs/SIGNING.md](docs/SIGNING.md)). |
| The package ran from Intune but the user only saw Intune's notification, not the branded dialogs | Packages of 2.2 or earlier in *Automatic* mode turned silent when none of the applications to close was open. Generate the package again with 2.3 (*Automatic* now shows the dialogs whenever a user is signed in). |
| *Set up automatically* created the registration but says the consent is missing | The administrator could create apps but not grant consent for Microsoft Graph. A Global Administrator opens the consent link the application shows, or grants it in Entra ID > App registrations > J365 IntunePSApp > API permissions. |

Application logs: `IntunePSApp-Data\logs` next to the executable (portable) or `%LOCALAPPDATA%\J365\IntunePSApp\logs` (installed). On the devices, PSAppDeployToolkit writes by default to `C:\ProgramData\Microsoft\IntuneManagementExtension\Logs`.

## What was tested (and what was not)

- **Automated:** 195 unit tests run on every push (generated scripts are parsed by Windows PowerShell 5.1, `.intunewin` round trip, both languages, project migration, validations, the detection script run against a real registry mark, the replacement of a running executable, the automatic tenant setup and the sign-in against a local Entra ID stand-in).
- **End to end, by the author:** packages built from the interface and from the command line in both languages; a generated package executed for real with a test installer (install, repair, uninstall, 22 actions including dependency and `.reg` import); first-run setup on an empty folder; per-user install and uninstall; database generation.
- **In Windows Sandbox (2.1):** the 7-Zip MSI package was installed by the test script in a real sandbox (exit code 0, detected, 31 s; the same installation took about 150 s before the Smart App Control fix), and run as SYSTEM three times in a row: install on a clean Windows, install again over the existing version (the log shows it being listed and uninstalled first) and uninstall (no longer detected). Not exercised: enabling the Windows feature from the application (it needs a computer without it) and the test of an `.exe` installer.
- **For 2.1.2:** a package built with the new code was run as SYSTEM in Windows Sandbox (install, install over the existing version, uninstall; an action failing on purpose was logged and the deployment still returned 0; user code with a here-string ran). Icon extraction was checked with two real MSI files (one carrying an `.ico`, one an `.exe`). The publishing script was run end to end, in Windows PowerShell 5.1 and PowerShell 7, against a local stand-in for Entra ID, Microsoft Graph and Azure Storage that verified the uploaded content (MAC, IV, digest), the retries, the update of an existing app, the assignment and the removal of an incomplete app.
- **For 2.2:** the reported case was reproduced as SYSTEM in Windows Sandbox: "7-Zip 26.06 (x64)" registered as installed by its `.exe` installer, with an uninstaller that hands the work to a copy of itself in `%TEMP%`. The detection of the old package reported it as installed (so Intune would never run the package); the new one did not. The package waited for that copy to finish, installed the MSI, wrote the mark and was detected; the uninstallation removed the MSI and the mark. The update inside the application was run end to end against a local release server, from the command line and through the window (notice, confirmation, download with progress, replacement of the running executable, restart in the new version, cleanup). Icon extraction was checked with three real MSI files.
- **For 2.3:** a harmless per-user package was run on Windows in both display modes: the PSAppDeployToolkit log shows *"running in [Interactive] mode"* and the progress dialog created in *Automatic*, against *"changing deployment mode to [Silent]"* in *Discreet* (the old behaviour). The automatic setup was run against a local stand-in for Entra ID and Microsoft Graph that checks the registration (single tenant, public client, no secret, the one permission), the enterprise application created after a replication delay, the admin consent, the PKCE sign-in and the encrypted session; publishing inside the application was run against the Graph and Azure Storage stand-in with the session of the application (no browser), and the uploaded content was verified (MAC, IV, digest).
- **Not verified yet:** *Publish to Intune* and the automatic setup against a real tenant; that is why it is still labelled **preview**. The database generator changes were compiled but the database was not regenerated locally. Feedback is welcome in the [issues](https://github.com/sesantanajr/J365-IntunePSApp/issues).

As with any packaging tool: **always test a package with a pilot group before a broad deployment.**

## Roadmap

Dark theme · code signing (the workflow is ready) · more languages (the texts are two JSON files) · ARM64 build · code signing · Intune assignments and supersedence in the publishing script.

## Credits

- **[PSAppDeployToolkit](https://psappdeploytoolkit.com)** (LGPL-3.0) - the deployment framework this project is based on. Thank you to the PSAppDeployToolkit team and community.
- **[winget-pkgs](https://github.com/microsoft/winget-pkgs)** (MIT) - source of the application database.
- **[Microsoft Win32 Content Prep Tool](https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool)** - downloaded from Microsoft at run time, never redistributed.

Not affiliated with Microsoft or with the PSAppDeployToolkit team. See [NOTICE.md](NOTICE.md) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Licence

[MIT](LICENSE) © 2026 Sérgio Sant'Ana Junior - Jornada 365.

<br>

---

<p align="center">
  <img src="docs/assets/cover-pt.png" alt="J365 IntunePSApp - empacote qualquer aplicativo para o Microsoft Intune" width="100%">
</p>

# 🇧🇷 Português

<p align="center">
  <b>Empacote qualquer aplicativo Windows para o Microsoft Intune em minutos. Sem escrever scripts.</b><br>
  Baseado no <a href="https://psappdeploytoolkit.com">PSAppDeployToolkit</a> · Portátil · Código aberto (MIT)
</p>

<p align="center">
  <a href="#j365-intunepsapp">English</a> · <b>Português</b>
</p>

> **Criado por [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/)** · [Jornada 365](https://jornada365.cloud)
>
> Este projeto foi **criado com base no [PSAppDeployToolkit](https://psappdeploytoolkit.com)**, o framework de implantação de código aberto: todo pacote gerado é um pacote padrão do PSAppDeployToolkit v4. O J365 IntunePSApp é a interface amigável que escreve o pacote para você.

<p align="center"><a href="https://jornada365.cloud"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/assets/j365-blog-logo-dark.png"><img src="docs/assets/j365-blog-logo.png" alt="Jornada 365" width="240"></picture></a></p>

## O que ele faz

Você escolhe um aplicativo (em um banco de dados com mais de 16.000 apps, ou o seu próprio `.msi` / `.exe`), responde a algumas perguntas e recebe tudo o que o Intune precisa:

- o arquivo **`.intunewin`**, pronto para enviar;
- os **comandos de instalação e desinstalação**;
- um **script de detecção**;
- uma **página de instruções** com todos os campos do assistente do Intune prontos para copiar;
- o **pacote PSAppDeployToolkit** completo, para continuar editando manualmente se quiser.

Qualquer pessoa consegue usar: não há nada para instalar, nenhum script para escrever e não é preciso conhecer PowerShell.

## Novidades da 2.3

- **Conecte ao seu tenant uma vez e publique com um clique.** *Configurações > Microsoft Intune > Configurar automaticamente*: você entra como administrador e o aplicativo cria o registro **J365 IntunePSApp** no seu Entra ID (um único tenant, sem segredo, só `DeviceManagementApps.ReadWrite.All`, com o consentimento do administrador). Nada a fazer à mão no portal. A sessão fica lembrada, criptografada para o seu usuário do Windows, e *Publicar no Intune* roda **dentro do aplicativo**, com as etapas e o progresso do envio na tela, sem janela do PowerShell. Uma versão nova pode **atualizar o app que já está no Intune** (as atribuições são mantidas) em vez de criar um segundo. Veja [docs/INTUNE-PUBLISH.md](docs/INTUNE-PUBLISH.md#publicar-no-intune-prévia).
- **Corrigido: as janelas personalizadas do PSAppDeployToolkit não apareciam** quando o pacote rodava pelo Intune; o usuário via só a notificação do Intune. O modo *Auto* do toolkit deixa a implantação silenciosa quando nenhum dos aplicativos a fechar está aberto. O modo *Automático* do aplicativo agora mostra as janelas de boas-vindas, progresso e conclusão sempre que houver um usuário conectado (silencioso sem ninguém conectado e durante o Autopilot). O comportamento antigo virou o modo *Discreto*. Comprovado no log do PSAppDeployToolkit: *"running in [Interactive] mode"* no lugar de *"changing deployment mode to [Silent]"*.
- **Corrigido: "Estilo das janelas" e "Cor de destaque" nas Configurações pareciam não fazer nada.** Eram padrões só para pacotes novos. As Configurações agora mostram uma **prévia da janela** ao vivo (Fluent ou Clássico, com o seu logo, nome e cor), e salvar aplica os novos padrões ao pacote aberto no editor onde ele ainda tinha os antigos. A prévia do editor também mostra o estilo Clássico.
- **Encontrado numa revisão de todos os recursos e corrigido:** produtos lado a lado (.NET 8 e 9) não compartilham mais o padrão de desinstalação nem a marca do pacote; instaladores de arquiteturas diferentes não se sobrescrevem mais na pasta de downloads; uma atualização do banco não desfaz mais o que foi digitado nas Configurações; a pasta de saída das Configurações vale para todo pacote; atalhos e variáveis de ambiente de pacotes por usuário vão para as pastas do próprio usuário, e ações que exigem administrador são sinalizadas; "Verificar espaço em disco" funciona sem a janela de boas-vindas; uma mensagem que espera o OK não encerra mais a implantação.
- **Confiança no Windows 11:** o workflow de release assina o executável com o Azure Trusted Signing assim que a identidade de assinatura for configurada ([docs/SIGNING.md](docs/SIGNING.md)); a partir daí o aplicativo recusa atualizações não assinadas pelo mesmo editor.
- **Visual executivo:** a tela inicial mostra a conexão com o Intune ao lado dos outros indicadores; cartões e profundidade refinados.

<p align="center"><img src="docs/screenshots/pt-BR/intune.png" alt="Configurações > Microsoft Intune: conectar o tenant automaticamente" width="88%"></p>

Antes, na 2.2: atualização dentro do aplicativo, a marca do pacote que corrigiu "o Intune mostra instalado mas a cópia antiga ficou", o ícone real do aplicativo em MSI, o logo transparente do Jornada 365. O [changelog](CHANGELOG.md) lista tudo.

## Download

1. Baixe **`J365-IntunePSApp-<versão>-win-x64.zip`** na [última versão](https://github.com/sesantanajr/J365-IntunePSApp/releases/latest).
2. Extraia em qualquer pasta (funciona em pendrive) e execute **`J365.IntunePSApp.exe`**.
3. Na primeira execução o aplicativo **se prepara sozinho**: baixa e verifica o banco de dados de aplicativos, o template do PSAppDeployToolkit e o IntuneWinAppUtil da Microsoft. Nada é instalado no Windows.

<p align="center"><img src="docs/screenshots/pt-BR/setup.png" alt="Preparação automática na primeira execução" width="88%"></p>

**Requisitos:** Windows 10 ou 11 (x64). Não precisa instalar o .NET (ele está dentro do executável). Acesso à internet na primeira execução.

> **Avisos de proteção do Windows.** O executável não tem assinatura digital (é um projeto gratuito da comunidade). O Windows SmartScreen pode mostrar *"O Windows protegeu o computador"*: confira o arquivo antes (veja [Verifique o download](#verifique-o-download)) e escolha *Mais informações > Executar assim mesmo*. Em computadores com o **Controle Inteligente de Aplicativos** (Smart App Control) ativado, o Windows pode bloquear aplicativos sem assinatura que ainda não conhece e não oferece exceção; nesse caso use outro computador ou uma máquina virtual. O workflow de release já está pronto para assinar o executável ([docs/SIGNING.md](docs/SIGNING.md)).

## Recursos

| | |
|---|---|
| **Banco de dados de aplicativos** | Mais de 16.000 aplicativos com parâmetros silenciosos, links oficiais de download, hashes SHA256 e dados de desinstalação. Recriado **todos os dias** a partir do repositório público [winget-pkgs](https://github.com/microsoft/winget-pkgs), com curadoria (processos a fechar, dicas de detecção). Quando nada mudou, só um pequeno arquivo de metadados é baixado. |
| **Qualquer instalador** | Arraste um `.msi` ou `.exe`. A tecnologia é identificada (MSI, Inno Setup, NSIS, WiX Burn, InstallShield, Advanced Installer, Squirrel) e os parâmetros silenciosos são sugeridos. O arquivo é comparado com o banco de dados por hash, código do produto ou nome. |
| **Experiência do usuário** | Fechar aplicativos abertos (com contagem), permitir adiar, impedir abrir o app durante a instalação, mensagens personalizadas de boas-vindas / progresso / conclusão, pedido de reinicialização. Modo de exibição **Automático** (janelas personalizadas sempre que houver um usuário conectado, silencioso no Autopilot), Discreto, Interativo ou Silencioso. Prévia da janela em tempo real, Fluent ou Clássico. |
| **Identidade da empresa** | Seu logo, nome da empresa e cor de destaque em todas as janelas. Salve uma vez e reutilize em todos os pacotes. |
| **Ações prontas** | 20 ações, sem script: instalar uma **dependência**, desinstalar outro app, atalhos, arquivos e pastas, copiar para o perfil de cada usuário, valores de registro, importar `.reg`, serviços, tarefas agendadas, variáveis de ambiente, executar um programa, mostrar mensagem... e uma caixa de PowerShell para todo o resto. |
| **Dependências** | Quando o manifesto do winget declara dependências (Visual C++ Redistributable, .NET Desktop Runtime...), um clique baixa o instalador certo, envia dentro do pacote e instala antes. |
| **Versões já instaladas** | Desinstala qualquer versão que já esteja no dispositivo (MSI e EXE) e só então instala a nova, para nunca ficarem duas versões lado a lado. Nenhuma detecção é consultada antes de desinstalar; desinstaladores que passam o trabalho para uma cópia de si mesmos são aguardados; o que foi encontrado e removido vai para o log. |
| **Detecção** | Script de detecção gerado: pelo nome do programa e versão mínima, pelo código do produto ou por arquivo, mais a **marca do pacote**, para que uma cópia instalada de outra forma não impeça o Intune de executar o pacote. Teste no seu computador com um clique. |
| **Pacotes sempre atuais** | *Meus pacotes* mostra quais pacotes têm **versão mais nova no banco de dados**. *Atualizar* baixa o novo instalador e mantém todas as personalizações. |
| **Teste antes de publicar** | **Windows Sandbox**: um clique instala o pacote em um Windows descartável e executa a regra de detecção, com progresso ao vivo; o resultado e os logs voltam para o aplicativo. Se o recurso do Windows não estiver instalado, o aplicativo avisa e instala para você. Ou teste no computador local. |
| **Ícone do aplicativo** | Todo pacote recebe `Intune\AppIcon.png` para o Intune e o Portal da Empresa: o ícone do instalador, do programa que um `.msi` instala (lido do gabinete dele), da cópia instalada no seu computador, um arquivo que você escolher, ou um ícone gerado com as iniciais. Nada para baixar separado. |
| **Publicar no Intune (prévia)** | Conecte uma vez (o registro de aplicativo é criado para você) e publique com um clique dentro do aplicativo, com progresso: cria ou atualiza o app (uma versão nova atualiza a anterior), atribuição opcional, link para o centro de administração. O mesmo `Publish-ToIntune.ps1` também roda manualmente. Veja [docs/INTUNE-PUBLISH.md](docs/INTUNE-PUBLISH.md#publicar-no-intune-prévia). |
| **Inglês e português** | Troque o idioma a qualquer momento (barra lateral ou Configurações). As instruções geradas seguem o mesmo idioma. |
| **Portátil ou instalado** | Roda de qualquer pasta. Se quiser, instala para o seu usuário (Menu Iniciar, *Aplicativos instalados*) sem precisar de administrador, e desinstala sem deixar resíduos. **Atualiza a si mesmo** com a janela aberta. |
| **Qualquer tamanho de tela** | A janela cabe em notebooks pequenos, escala de 125-150 % e sessões remotas: barra lateral só com ícones e páginas que se reorganizam. |
| **Linha de comando** | `--build`, `--sandbox`, `--setup`, `--update-catalog`, `--install`, `--update`... para automação. |

## Como funciona

**1. Escolha o aplicativo** - busque no catálogo ou arraste o seu instalador.

<p align="center"><img src="docs/screenshots/pt-BR/catalog.png" alt="Catálogo de aplicativos" width="88%"></p>

O instalador é identificado (tecnologia, parâmetros silenciosos, correspondência no banco de dados) e ali mesmo você decide o que acontece com as **versões já instaladas no dispositivo**: desinstalar o que for encontrado e depois instalar a nova.

<p align="center"><img src="docs/screenshots/pt-BR/installer.png" alt="Instalador identificado e a opção de desinstalar versões já instaladas" width="88%"></p>

**2. Personalize** - experiência, ações, dependências, desinstalação, detecção e visual. Tudo já vem preenchido com padrões sensatos.

<p align="center"><img src="docs/screenshots/pt-BR/application.png" alt="Passo Aplicativo com o ícone do aplicativo para o Intune e o Portal da Empresa" width="88%"></p>
<p align="center"><img src="docs/screenshots/pt-BR/experience.png" alt="Experiência do usuário com prévia" width="88%"></p>
<p align="center"><img src="docs/screenshots/pt-BR/actions.png" alt="Dependências e ações prontas" width="88%"></p>

**3. Gere** - o pacote é validado (sintaxe dos scripts, integridade do `.intunewin`) e você recebe os comandos, o script de detecção e as instruções.

**4. Teste no Windows Sandbox e envie ao Intune** - um clique instala o pacote em um Windows descartável e executa a regra de detecção. O resultado volta para o mesmo cartão.

<p align="center"><img src="docs/screenshots/pt-BR/generated.png" alt="Pacote pronto para o Intune, com o resultado do teste no Windows Sandbox" width="88%"></p>

**Depois** - o aplicativo avisa quando um app empacotado tem versão nova.

<p align="center"><img src="docs/screenshots/pt-BR/packages.png" alt="Meus pacotes com aviso de atualização" width="88%"></p>

## Teste no Windows Sandbox

*Testar no Sandbox* abre um Windows descartável, instala o pacote com a mesma linha de comando usada pelo Intune e executa a regra de detecção. Nada muda no seu computador.

| | |
|---|---|
| **Requisitos** | Windows 10/11 Pro, Enterprise ou Education com o recurso *Área Restrita do Windows* (Windows Sandbox) e virtualização de hardware habilitada. Quando o recurso está desligado, o aplicativo oferece instalar (permissão de administrador e, depois, reiniciar). O Windows Home e o Windows Server não têm o recurso. |
| **O que aparece no sandbox** | Uma janela do PowerShell com o tempo decorrido e a última linha do log do PSAppDeployToolkit enquanto o pacote roda, e depois `RESULT`. Ela fica aberta com quatro comandos: `Test-Install`, `Test-Uninstall`, `Test-Detection` e `Show-Log`. |
| **O que volta** | Código de saída, detecção e tempo no cartão de resultado; `result.json` e os logs em `Sandbox\Results` do pacote. Ao reabrir o pacote, o último resultado aparece de novo (2.1.1). |
| **O que o teste altera dentro do sandbox** | Desliga a avaliação do Smart App Control (ela faz os instaladores levarem minutos ali) e o QuickEdit da janela do teste (um clique a pausaria). As duas alterações só existem naquele Windows descartável. |
| **Limite de tempo** | 60 minutos por execução, o mesmo padrão do Intune. Um pacote que faz uma pergunta sem resposta é interrompido e reportado como falha. |
| **Diferenças em relação ao Intune** | O Intune instala como SYSTEM; o sandbox instala como o administrador local dele. Sempre finalize com um grupo piloto. |
| **Linha de comando** | `J365.IntunePSApp.exe --sandbox "<pasta do pacote>"` |

## Qualquer tamanho de tela

A janela nunca abre maior que a tela e lembra o tamanho. Em notebooks pequenos, escala de 125-150 % e sessões remotas a barra lateral fica só com os ícones e todas as páginas se reorganizam.

<p align="center"><img src="docs/screenshots/pt-BR/compact.png" alt="O mesmo cartão de resultado em uma janela de 900 x 580" width="62%"></p>

## O que é gerado

```
<Saída>\<App>_<Versão>\
├── Package\                     pacote PSAppDeployToolkit (Invoke-AppDeployToolkit.exe/.ps1, Files, SupportFiles, Config, Assets...)
├── Intune\
│   ├── <App>_<Versão>.intunewin     envie este arquivo ao Intune
│   ├── Detection_<App>_<Versão>.ps1
│   ├── Intune-Instructions.html     todos os campos do assistente do Intune, prontos para copiar (+ .txt)
│   ├── intune-app.json              as mesmas informações, para automação
│   ├── Publish-ToIntune.ps1         script opcional de publicação (prévia)
│   └── AppIcon.png
├── Sandbox\
│   ├── Sandbox-Test.ps1             script de teste usado por "Testar no Sandbox"
│   └── Results\                     result.json e logs gravados pelo último teste no sandbox
├── project.json                 reabra, altere e gere novamente
└── build.log
```

No Intune (*Aplicativos > Windows > Adicionar > Aplicativo do Windows (Win32)*):

| Campo | Valor |
|---|---|
| Comando de instalação | `Invoke-AppDeployToolkit.exe -DeploymentType Install` |
| Comando de desinstalação | `Invoke-AppDeployToolkit.exe -DeploymentType Uninstall` |
| Comportamento de instalação | Sistema (ou Usuário, se você escolher) |
| Regras de detecção | Usar um script de detecção personalizado: `Detection_<App>_<Versão>.ps1` |

## Linha de comando

```text
J365.IntunePSApp.exe [arquivo]                                abre um projeto (project.json) ou analisa um instalador
J365.IntunePSApp.exe --build <project.json|instalador> [--out <pasta>] [--native]
J365.IntunePSApp.exe --sandbox <pasta do pacote>              testa um pacote gerado no Windows Sandbox
J365.IntunePSApp.exe --setup                                  baixa e verifica as dependências
J365.IntunePSApp.exe --update-catalog [--force]               atualiza o banco de dados de aplicativos
J365.IntunePSApp.exe --install [--desktop-shortcut] [--quiet] instala para o usuário atual
J365.IntunePSApp.exe --uninstall [--purge] [--quiet]          remove a instalação
J365.IntunePSApp.exe --update [--check]                       atualiza este executável para a última versão
    --lang <en|pt-BR>    --version    --help
```

## Verifique o download

Cada versão é compilada pelo GitHub Actions a partir do commit marcado e traz os checksums e um atestado assinado de procedência da compilação.

```powershell
# 1. Checksum: compare com o SHA256SUMS.txt da versão
Get-FileHash .\J365-IntunePSApp-2.3.0-win-x64.zip -Algorithm SHA256

# 2. Procedência: prova que o arquivo foi compilado pelo workflow deste repositório (GitHub CLI)
gh attestation verify .\J365-IntunePSApp-2.3.0-win-x64.zip --repo sesantanajr/J365-IntunePSApp
```

O que o próprio aplicativo verifica: os instaladores do banco de dados (SHA256 do manifesto do winget), o módulo do PSAppDeployToolkit (assinatura Authenticode), o IntuneWinAppUtil (assinatura da Microsoft, senão é descartado), cada `.intunewin` criado (HMAC e SHA256) e as próprias atualizações (SHA256 do zip da release e a versão do novo executável). Veja [SECURITY.md](SECURITY.md).

## Pacote de código-fonte, compilação e fork

O projeto é de código aberto para que você possa **auditar, compilar, fazer um fork ou criar outro projeto com base nele**.

- **Pacote de código-fonte:** cada versão inclui `J365-IntunePSApp-<versão>-source.zip`, o código exato daquela versão.
- **Compilar:** `dotnet publish src/IntunePSApp -c Release -r win-x64` com o SDK do .NET 10. Detalhes em [docs/BUILDING.md](docs/BUILDING.md).
- **Fork / mudar a marca / traduzir:** [docs/FORKING.md](docs/FORKING.md) lista os poucos lugares a alterar (nome, links, logo, endereço do banco de dados) e como adicionar um idioma.
- **Como está organizado:** [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
- **Contribuir:** [CONTRIBUTING.md](CONTRIBUTING.md). A forma mais rápida de ajudar é melhorar o [`catalog/curated.json`](catalog/curated.json).

## Solução de problemas

| Sintoma | O que fazer |
|---|---|
| O teste no sandbox fica em *Installing* por minutos (versão 2.0) | Atualize para a 2.1 e clique em *Testar no Sandbox* novamente: os pacotes gerados pela 2.0 recebem o novo script de teste automaticamente. Se você tinha clicado na janela do teste, pressione Enter nela. |
| *O Windows Sandbox não está ativo neste computador* | Escolha *Instalar o recurso agora*, aceite o pedido de permissão do Windows e reinicie. Manualmente: *Ativar ou desativar recursos do Windows > Área Restrita do Windows*, ou, como administrador, `dism /Online /Enable-Feature /FeatureName:Containers-DisposableClientVM /All`. A virtualização (Intel VT-x / AMD-V) precisa estar habilitada no firmware. |
| *O Windows Sandbox já está aberto* | O Windows permite um sandbox por vez: feche a janela dele e teste de novo. |
| O teste no sandbox falhou | *Abrir logs* no cartão de resultado, ou `Show-Log` na janela do teste. Códigos de saída: `0`, `1707`, `3010` e `1641` são sucesso; `1618` indica outra instalação em andamento; `1602` indica que o usuário adiou ou cancelou. |
| O Intune mostra *Instalado*, mas a cópia que já estava no dispositivo continua lá | A regra de detecção encontrou essa cópia, então o Intune nunca executou o pacote. Gere o pacote de novo com a 2.2: *Exigir a marca deste pacote* (passo Detecção) faz uma cópia instalada de outra forma contar como não instalada. Mantenha também *Exigir versão igual ou superior a* (o passo de revisão avisa sobre os dois). No dispositivo, `HKLM\SOFTWARE\J365\IntunePSApp\Apps` lista o que os pacotes instalaram. |
| *Testar neste computador* diz que o aplicativo está instalado, mas não por este pacote | Esperado onde o pacote ainda não rodou: o Intune vai executá-lo ali (remover a cópia encontrada, instalar esta versão e gravar a marca). |
| A atualização dentro do aplicativo não começa | Ela precisa acessar `api.github.com` e `github.com` (proxy e firewall). Uma cópia numa pasta somente leitura não consegue se substituir: o aplicativo oferece a página de download. A cópia instalada fica em `%LOCALAPPDATA%\Programs\J365 IntunePSApp`. |
| *O Windows protegeu o computador* ao abrir o aplicativo | Veja [Download](#download-1): o executável ainda não tem assinatura de código ([docs/SIGNING.md](docs/SIGNING.md)). |
| O pacote rodou pelo Intune, mas o usuário viu só a notificação do Intune, sem as janelas personalizadas | Pacotes da 2.2 ou anteriores no modo *Automático* ficavam silenciosos quando nenhum dos aplicativos a fechar estava aberto. Gere o pacote de novo com a 2.3 (o *Automático* agora mostra as janelas sempre que houver um usuário conectado). |
| *Configurar automaticamente* criou o registro, mas diz que falta o consentimento | O administrador podia criar aplicativos, mas não conceder consentimento para o Microsoft Graph. Um Administrador Global abre o link de consentimento que o aplicativo mostra, ou concede em Entra ID > Registros de aplicativo > J365 IntunePSApp > Permissões de API. |

Logs do aplicativo: `IntunePSApp-Data\logs` ao lado do executável (portátil) ou `%LOCALAPPDATA%\J365\IntunePSApp\logs` (instalado). Nos dispositivos, o PSAppDeployToolkit grava por padrão em `C:\ProgramData\Microsoft\IntuneManagementExtension\Logs`.

## O que foi testado (e o que não foi)

- **Automatizado:** 195 testes unitários rodam a cada push (os scripts gerados são analisados pelo Windows PowerShell 5.1, ida e volta do `.intunewin`, os dois idiomas, migração de projetos, validações, o script de detecção executado contra uma marca real no registro, a substituição de um executável em execução, a configuração automática do tenant e o login contra um substituto local do Entra ID).
- **De ponta a ponta, pelo autor:** pacotes gerados pela interface e pela linha de comando nos dois idiomas; um pacote gerado executado de verdade com um instalador de teste (instalação, reparo, desinstalação, 22 ações incluindo dependência e importação de `.reg`); preparação da primeira execução em uma pasta vazia; instalação e desinstalação por usuário; geração do banco de dados.
- **No Windows Sandbox (2.1):** o pacote MSI do 7-Zip foi instalado pelo script de teste em um sandbox real (código de saída 0, detectado, 31 s; a mesma instalação levava cerca de 150 s antes da correção do Smart App Control) e executado como SYSTEM três vezes seguidas: instalação em um Windows limpo, instalação por cima da versão existente (o log mostra ela sendo listada e desinstalada antes) e desinstalação (não é mais detectado). Não exercitado: a ativação do recurso do Windows pelo aplicativo (exige um computador sem ele) e o teste de um instalador `.exe`.
- **Para a 2.1.2:** um pacote gerado com o código novo foi executado como SYSTEM no Windows Sandbox (instalação, instalação por cima da versão existente, desinstalação; uma ação que falha de propósito foi registrada e a implantação ainda retornou 0; código do usuário com here-string rodou). A extração de ícone foi conferida com dois arquivos MSI reais (um com `.ico`, outro com `.exe`). O script de publicação foi executado de ponta a ponta, no Windows PowerShell 5.1 e no PowerShell 7, contra um substituto local do Entra ID, do Microsoft Graph e do Azure Storage que conferiu o conteúdo enviado (MAC, IV, hash), as repetições, a atualização de um app existente, a atribuição e a remoção de um app incompleto.
- **Para a 2.2:** o caso relatado foi reproduzido como SYSTEM no Windows Sandbox: "7-Zip 26.06 (x64)" registrado como instalado pelo instalador `.exe`, com um desinstalador que passa o trabalho para uma cópia de si mesmo no `%TEMP%`. A detecção do pacote antigo o dava como instalado (o Intune nunca executaria o pacote); a nova, não. O pacote esperou essa cópia terminar, instalou o MSI, gravou a marca e foi detectado; a desinstalação removeu o MSI e a marca. A atualização dentro do aplicativo foi executada de ponta a ponta contra um servidor de releases local, pela linha de comando e pela janela (aviso, confirmação, download com progresso, troca do executável em uso, reinício na versão nova, limpeza). A extração de ícone foi conferida com três arquivos MSI reais.
- **Para a 2.3:** um pacote inofensivo por usuário foi executado no Windows nos dois modos de exibição: o log do PSAppDeployToolkit mostra *"running in [Interactive] mode"* e a janela de progresso criada no *Automático*, contra *"changing deployment mode to [Silent]"* no *Discreto* (o comportamento antigo). A configuração automática foi executada contra um substituto local do Entra ID e do Microsoft Graph que confere o registro (um único tenant, cliente público, sem segredo, a única permissão), o aplicativo empresarial criado após um atraso de replicação, o consentimento do administrador, o login PKCE e a sessão criptografada; a publicação dentro do aplicativo foi executada contra o substituto do Graph e do Azure Storage com a sessão do aplicativo (sem navegador), e o conteúdo enviado foi conferido (MAC, IV, hash).
- **Ainda não verificado:** *Publicar no Intune* e a configuração automática contra um locatário real; por isso continua marcado como **prévia**. As alterações do gerador do banco de dados foram compiladas, mas o banco não foi regenerado localmente. Relatos são bem-vindos nas [issues](https://github.com/sesantanajr/J365-IntunePSApp/issues).

Como em qualquer ferramenta de empacotamento: **sempre teste o pacote em um grupo piloto antes de uma implantação ampla.**

## Próximos passos

Tema escuro · assinatura de código (o workflow está pronto) · mais idiomas (os textos são dois arquivos JSON) · versão ARM64 · assinatura de código · atribuições e substituição (supersedence) no script de publicação.

## Créditos

- **[PSAppDeployToolkit](https://psappdeploytoolkit.com)** (LGPL-3.0) - o framework de implantação em que este projeto é baseado. Obrigado à equipe e à comunidade do PSAppDeployToolkit.
- **[winget-pkgs](https://github.com/microsoft/winget-pkgs)** (MIT) - origem do banco de dados de aplicativos.
- **[Microsoft Win32 Content Prep Tool](https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool)** - baixado da Microsoft em tempo de execução, nunca redistribuído.

Sem vínculo com a Microsoft ou com a equipe do PSAppDeployToolkit. Veja [NOTICE.md](NOTICE.md) e [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Licença

[MIT](LICENSE) © 2026 Sérgio Sant'Ana Junior - Jornada 365.
