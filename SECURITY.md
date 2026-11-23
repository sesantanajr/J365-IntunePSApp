# Security policy

*Português: [abaixo](#política-de-segurança).*

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Report them privately: use *Security > Report a vulnerability* on this repository when that option is shown, or send a private message to the author on [LinkedIn](https://www.linkedin.com/in/sergiosantanacloud/). You will get an answer as soon as possible; fixes are released as a new version and credited to the reporter (unless you prefer otherwise).

Supported version: the latest release (2.7.x). Settings > *Check now* updates a copy in place. A copy up to 2.6.0 may keep saying "you have the latest version" while the GitHub API limit of its network is reached (60 requests an hour per public address, shared by everyone behind a company proxy): download the latest release once by hand; from 2.6.1 on the check no longer depends on that API.

## What the application does to protect you

| Area | Behaviour |
|---|---|
| Downloads from the database | Installers come from the vendor URL recorded in the public winget manifest and are checked against the manifest's SHA256 before use. A mismatch discards the file. |
| PSAppDeployToolkit | Downloaded from the project's official GitHub releases. The module and the launcher must carry a valid Authenticode signature of the toolkit publisher, or the template is refused; the signature is checked again before every build, so a copy changed on disk never reaches a package. A new template is also structurally validated before it replaces the working one. |
| IntuneWinAppUtil.exe | Downloaded from Microsoft's official repository and used only when it carries a valid Microsoft signature. Otherwise it is deleted and the built-in packager is used. |
| Generated scripts | Every user-provided value is written as a single-quoted PowerShell literal. The generated scripts are parsed by Windows PowerShell before the package is created. |
| `.intunewin` | Each file is validated after creation (HMAC, decryption, SHA256). |
| Connecting to Intune | Sign-in in your browser (authorization code with PKCE): the application never sees a password. The connection window explains every step and every failure; the Azure Cloud Shell alternative runs the Azure CLI of your own portal session and only prints the tenant and application IDs, which are not secrets (the *connection code*). The automatic setup creates a single-tenant public client **without secret**, with only the delegated permission `DeviceManagementApps.ReadWrite.All`, and grants the admin consent; the setup sign-in (Microsoft Graph Command Line Tools, `Application.ReadWrite.All` and `DelegatedPermissionGrant.ReadWrite.All`) is not kept. The session of the user is a refresh token encrypted with Windows DPAPI for the current user (`intune-session.dat` in the data folder); *Sign out* deletes it. With the **certificate of this computer** option the same registration also gets the application permission `DeviceManagementApps.ReadWrite.All`, and the application creates a self-signed certificate (6, 12 or 24 months) whose RSA key is generated in the TPM when the computer has one, otherwise in the Windows key store as **non-exportable**, for the current Windows user only; Entra ID receives only the public key. *Disconnect* deletes the certificate and its key from this computer (the public key stays in the registration until it expires or an administrator removes it). |
| Publishing to Intune | Acts as the signed-in user, with that user's Intune rights. Inside the application the publishing script gets the session in an environment variable of its own process (never on the command line) and clears it at once. Apps are created without assignments unless you choose one; an incomplete app is deleted, also when publishing is cancelled. An existing app only changes after its new content is committed. |
| Updates of the application | The latest version is read from the `SHA256SUMS.txt` of the latest release on github.com (then the GitHub API, then the release page): a check that reads nothing says so, it never reports "up to date". The update is downloaded from the GitHub release, checked against that `SHA256SUMS.txt` and the version of the new executable. Once releases are code-signed, an update must carry a valid signature of the same publisher. The running file is renamed, never overwritten; on failure nothing changes. |
| Package mark | Generated packages write `HKLM\SOFTWARE\J365\IntunePSApp\Apps\<app>` (version and date only) so detection can tell their installation from a copy installed some other way. A newer version already installed is kept (never downgraded), and so is the same version of the same product (same MSI product code or architecture) unless the package turns that off; an uninstallation that leaves the program in place fails instead of reporting success. |
| Telemetry | None. The application only contacts GitHub (database, PSAppDeployToolkit, IntuneWinAppUtil, update check), the vendor URL of an installer you choose to download, the public catalog of the Microsoft Store (only when you search the Store or publish a Store app) and Microsoft Entra ID / Microsoft Graph (only when you connect to Intune or publish). |
| Privileges | Runs as a standard user. Administrator rights are requested only by the optional "Test install / uninstall" buttons, which run the package on your computer. |
| Releases | Built by GitHub Actions from the commit of the release (a new version on `main`, or a `vX.Y.Z` tag) only after the unit tests and the interface checks pass, with SHA256 checksums and a signed build provenance attestation (`gh attestation verify`). |

## Things to know

- **The executable is not code-signed yet.** Windows SmartScreen and Smart App Control warn about any unsigned program. The release workflow is ready to sign with SignPath Foundation (free for open-source projects) or Azure Artifact Signing as soon as a signing identity is configured ([docs/SIGNING.md](docs/SIGNING.md)). Until then, verify the checksum or the attestation of a download (see the README).
- **Packages run as SYSTEM on your devices.** Review actions and PowerShell snippets you add, prefer installers recognised by hash, and always test with a pilot group.
- **PowerShell actions are code you write.** They are copied to the package as they are.
- **The application database is community data** (winget-pkgs). Silent switches and URLs are suggestions to review, not guarantees.

---

# Política de segurança

## Como relatar uma vulnerabilidade

Por favor, **não abra uma issue pública** para problemas de segurança. Relate de forma privada: use *Security > Report a vulnerability* neste repositório quando essa opção aparecer, ou envie uma mensagem privada ao autor no [LinkedIn](https://www.linkedin.com/in/sergiosantanacloud/). Você receberá resposta o quanto antes; as correções saem em uma nova versão, com crédito a quem relatou (se desejar).

Versão com suporte: a mais recente (2.7.x). Configurações > *Verificar agora* atualiza a cópia sem sair do aplicativo. Uma cópia até a 2.6.0 pode continuar dizendo "você tem a versão mais recente" enquanto o limite da API do GitHub da sua rede estiver esgotado (60 consultas por hora por endereço público, divididas por todos que saem pelo mesmo proxy da empresa): baixe a versão mais recente uma vez manualmente; a partir da 2.6.1 a verificação não depende mais dessa API.

## O que o aplicativo faz para proteger você

- **Downloads do banco de dados:** os instaladores vêm do endereço do fabricante registrado no manifesto público do winget e são conferidos pelo SHA256 do manifesto antes do uso.
- **PSAppDeployToolkit:** baixado das versões oficiais do projeto no GitHub; o módulo e o inicializador precisam ter assinatura Authenticode válida do publicador do toolkit, senão o template é recusado; a assinatura é conferida de novo antes de cada geração, então uma cópia alterada no disco nunca chega a um pacote. Um template novo também é validado antes de substituir o atual.
- **IntuneWinAppUtil.exe:** baixado do repositório oficial da Microsoft e usado somente com assinatura válida da Microsoft; caso contrário é apagado e o empacotador interno é usado.
- **Scripts gerados:** todo valor informado pelo usuário é gravado como literal entre aspas simples; os scripts são analisados pelo Windows PowerShell antes de o pacote ser criado.
- **Conexão com o Intune:** login no seu navegador (código de autorização com PKCE): o aplicativo nunca vê senha. A configuração automática cria um cliente público de um único tenant **sem segredo**, só com a permissão delegada `DeviceManagementApps.ReadWrite.All`, e concede o consentimento do administrador; o login da configuração (Microsoft Graph Command Line Tools) não é guardado. A sessão do usuário é um refresh token criptografado com o DPAPI do Windows para o usuário atual (`intune-session.dat` na pasta de dados); *Sair* o apaga. Com a opção **certificado deste computador**, o mesmo registro recebe também a permissão de aplicativo `DeviceManagementApps.ReadWrite.All` e o aplicativo cria um certificado autoassinado (6, 12 ou 24 meses) cuja chave RSA é gerada no TPM quando o computador tem um, senão no repositório de chaves do Windows como **não exportável**, só para o usuário atual do Windows; o Entra ID recebe apenas a chave pública. *Desconectar* apaga o certificado e a chave deste computador (a chave pública fica no registro até expirar ou um administrador removê-la).
- **Publicação no Intune:** age como o usuário conectado, com os direitos de Intune dele. Dentro do aplicativo, o script recebe a sessão numa variável de ambiente do próprio processo (nunca na linha de comando) e a apaga em seguida. Um app incompleto é apagado, também quando a publicação é cancelada; um app existente só muda depois que o conteúdo novo é confirmado.
- **Atualizações do aplicativo:** a versão mais recente é lida no `SHA256SUMS.txt` da última release no github.com (depois na API do GitHub e, por último, na página da release); uma verificação que não leu nada diz isso e nunca informa "versão mais recente". A atualização é baixada da release no GitHub, conferida por esse `SHA256SUMS.txt` e pela versão do novo executável; quando as versões forem assinadas, a atualização precisa da assinatura válida do mesmo editor.
- **Marca do pacote:** os pacotes gravam `HKLM\SOFTWARE\J365\IntunePSApp\Apps\<app>` (só versão e data) para a detecção distinguir a sua instalação de uma cópia instalada de outra forma. Uma versão mais nova já instalada é mantida (nunca rebaixada), assim como a mesma versão do mesmo produto (mesmo código de produto MSI ou mesma arquitetura), a menos que o pacote desligue isso; uma desinstalação que deixa o programa no lugar falha em vez de informar sucesso.
- **Telemetria:** nenhuma. O aplicativo só acessa o GitHub (banco de dados, PSAppDeployToolkit, IntuneWinAppUtil, verificação de versão), o endereço do fabricante de um instalador que você escolher baixar, o catálogo público da Microsoft Store (só quando você pesquisa na Store ou publica um app da Store) e o Microsoft Entra ID / Microsoft Graph (só quando você conecta ao Intune ou publica).
- **Versões:** compiladas pelo GitHub Actions a partir do commit da versão (uma versão nova na `main` ou uma tag `vX.Y.Z`), só depois de passarem os testes unitários e as verificações de interface, com checksums SHA256 e atestado assinado de procedência.

## Bom saber

- **O executável ainda não tem assinatura de código.** O SmartScreen e o Controle Inteligente de Aplicativos avisam sobre qualquer programa sem assinatura. O workflow de release já está pronto para assinar com o SignPath Foundation (gratuito para projetos de código aberto) ou com o Azure Artifact Signing assim que uma identidade de assinatura for configurada ([docs/SIGNING.md](docs/SIGNING.md)). Até lá, confira o checksum ou o atestado do download (veja o README).
- **Os pacotes rodam como SYSTEM nos seus dispositivos.** Revise as ações e os trechos de PowerShell que adicionar e sempre teste em um grupo piloto.
- **O banco de dados é mantido pela comunidade** (winget-pkgs). Parâmetros silenciosos e endereços são sugestões a revisar, não garantias.
