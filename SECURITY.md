# Security policy

*Português: [abaixo](#política-de-segurança).*

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Report them privately: use *Security > Report a vulnerability* on this repository when that option is shown, or send a private message to the author on [LinkedIn](https://www.linkedin.com/in/sergiosantanacloud/). You will get an answer as soon as possible; fixes are released as a new version and credited to the reporter (unless you prefer otherwise).

Supported version: the latest release.

## What the application does to protect you

| Area | Behaviour |
|---|---|
| Downloads from the database | Installers come from the vendor URL recorded in the public winget manifest and are checked against the manifest's SHA256 before use. A mismatch discards the file. |
| PSAppDeployToolkit | Downloaded from the project's official GitHub releases. The Authenticode signature of the module is validated and the result is logged; a new template is structurally validated before it replaces the working one. |
| IntuneWinAppUtil.exe | Downloaded from Microsoft's official repository and used only when it carries a valid Microsoft signature. Otherwise it is deleted and the built-in packager is used. |
| Generated scripts | Every user-provided value is written as a single-quoted PowerShell literal. The generated scripts are parsed by Windows PowerShell before the package is created. |
| `.intunewin` | Each file is validated after creation (HMAC, decryption, SHA256). |
| Publishing to Intune | Runs with your own delegated sign-in in a visible PowerShell window. The application never sees, stores or transmits credentials or tokens; only the tenant and client IDs are remembered. Apps are created without assignments. |
| Telemetry | None. The application only contacts GitHub (database, PSAppDeployToolkit, IntuneWinAppUtil, update check) and the vendor URL of an installer you choose to download. |
| Privileges | Runs as a standard user. Administrator rights are requested only by the optional "Test install / uninstall" buttons, which run the package on your computer. |
| Releases | Built by GitHub Actions from the tagged commit, with SHA256 checksums and a signed build provenance attestation (`gh attestation verify`). |

## Things to know

- **The executable is not code-signed.** Verify the checksum or the attestation of a download (see the README).
- **Packages run as SYSTEM on your devices.** Review actions and PowerShell snippets you add, prefer installers recognised by hash, and always test with a pilot group.
- **PowerShell actions are code you write.** They are copied to the package as they are.
- **The application database is community data** (winget-pkgs). Silent switches and URLs are suggestions to review, not guarantees.

---

# Política de segurança

## Como relatar uma vulnerabilidade

Por favor, **não abra uma issue pública** para problemas de segurança. Relate de forma privada: use *Security > Report a vulnerability* neste repositório quando essa opção aparecer, ou envie uma mensagem privada ao autor no [LinkedIn](https://www.linkedin.com/in/sergiosantanacloud/). Você receberá resposta o quanto antes; as correções saem em uma nova versão, com crédito a quem relatou (se desejar).

Versão com suporte: a mais recente.

## O que o aplicativo faz para proteger você

- **Downloads do banco de dados:** os instaladores vêm do endereço do fabricante registrado no manifesto público do winget e são conferidos pelo SHA256 do manifesto antes do uso.
- **PSAppDeployToolkit:** baixado das versões oficiais do projeto no GitHub; a assinatura Authenticode do módulo é validada e um template novo é validado antes de substituir o atual.
- **IntuneWinAppUtil.exe:** baixado do repositório oficial da Microsoft e usado somente com assinatura válida da Microsoft; caso contrário é apagado e o empacotador interno é usado.
- **Scripts gerados:** todo valor informado pelo usuário é gravado como literal entre aspas simples; os scripts são analisados pelo Windows PowerShell antes de o pacote ser criado.
- **Publicação no Intune:** usa o seu próprio login delegado em uma janela visível do PowerShell. O aplicativo nunca vê, guarda ou transmite credenciais ou tokens.
- **Telemetria:** nenhuma. O aplicativo só acessa o GitHub (banco de dados, PSAppDeployToolkit, IntuneWinAppUtil, verificação de versão) e o endereço do fabricante de um instalador que você escolher baixar.
- **Versões:** compiladas pelo GitHub Actions a partir do commit marcado, com checksums SHA256 e atestado assinado de procedência.

## Bom saber

- **O executável não tem assinatura de código.** Confira o checksum ou o atestado do download (veja o README).
- **Os pacotes rodam como SYSTEM nos seus dispositivos.** Revise as ações e os trechos de PowerShell que adicionar e sempre teste em um grupo piloto.
- **O banco de dados é mantido pela comunidade** (winget-pkgs). Parâmetros silenciosos e endereços são sugestões a revisar, não garantias.
