# Code signing policy

*Português: [abaixo](#política-de-assinatura-de-código).*

This policy applies to the executable of **J365 IntunePSApp** (`J365.IntunePSApp.exe`) published in the [releases](https://github.com/sesantanajr/J365-IntunePSApp/releases) of this repository. How the signing is configured: [docs/SIGNING.md](docs/SIGNING.md).

## What is signed

- Only release builds: the executable built by the GitHub Actions workflow [`build.yml`](.github/workflows/build.yml) from the commit of a release of this public repository (a new version pushed to `main`, or a `vX.Y.Z` tag), after the unit tests and the interface checks pass. When the signing service is configured for scripts, the two scripts embedded in the executable and written into every package (`Publish-ToIntune.ps1`, `Sandbox-Test.ps1`) are signed first. Nothing built on a personal computer is signed by the workflow.
- Every release lists the SHA-256 of its files (`SHA256SUMS.txt`) and carries a signed build provenance attestation.
- When the signature comes from SignPath: *Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org)*.

## Team roles

| Role | Who |
|---|---|
| Committer and reviewer | [Sérgio Sant'Ana Junior](https://github.com/sesantanajr) |
| Approver of releases | [Sérgio Sant'Ana Junior](https://github.com/sesantanajr) |

Every account with write access to the repository or to the signing service uses multi-factor authentication. Contributions from other people arrive as pull requests and are reviewed before they are merged.

## Privacy

J365 IntunePSApp has no telemetry. It only contacts:

- **GitHub**, for the application database, the PSAppDeployToolkit template, Microsoft's IntuneWinAppUtil and the check for a new version of the application (that check can be turned off in Settings);
- **the download address of an installer**, when the user chooses to download it from the database;
- **the public catalog of the Microsoft Store**, only when the user searches the Store or publishes a Store app;
- **Microsoft Entra ID and Microsoft Graph**, only when the user connects the application to a Microsoft Intune tenant or publishes a package.

Nothing about the user, the computer or the packages is sent anywhere else.

---

# Política de assinatura de código

Esta política vale para o executável do **J365 IntunePSApp** (`J365.IntunePSApp.exe`) publicado nas [versões](https://github.com/sesantanajr/J365-IntunePSApp/releases) deste repositório. Como a assinatura é configurada: [docs/SIGNING.md](docs/SIGNING.md).

## O que é assinado

- Somente builds de versão: o executável compilado pelo workflow [`build.yml`](.github/workflows/build.yml) do GitHub Actions a partir do commit de uma versão deste repositório público (uma versão nova enviada à `main` ou uma tag `vX.Y.Z`), depois de passarem os testes unitários e as verificações de interface. Quando o serviço de assinatura está configurado para scripts, os dois scripts embutidos no executável e gravados em cada pacote (`Publish-ToIntune.ps1`, `Sandbox-Test.ps1`) são assinados antes. Nada compilado num computador pessoal é assinado pelo workflow.
- Cada versão lista o SHA-256 dos seus arquivos (`SHA256SUMS.txt`) e tem um atestado de procedência assinado.
- Quando a assinatura vem do SignPath: *Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org)*.

## Papéis

| Papel | Quem |
|---|---|
| Commits e revisão | [Sérgio Sant'Ana Junior](https://github.com/sesantanajr) |
| Aprovação de versões | [Sérgio Sant'Ana Junior](https://github.com/sesantanajr) |

Toda conta com acesso de escrita ao repositório ou ao serviço de assinatura usa autenticação multifator. Contribuições de outras pessoas chegam como pull requests e são revisadas antes de entrar.

## Privacidade

O J365 IntunePSApp não tem telemetria. Ele só acessa:

- o **GitHub**, para o banco de dados de aplicativos, o template do PSAppDeployToolkit, o IntuneWinAppUtil da Microsoft e a verificação de versão nova do aplicativo (essa verificação pode ser desligada nas Configurações);
- **o endereço de download de um instalador**, quando o usuário escolhe baixá-lo pelo banco de dados;
- o **catálogo público da Microsoft Store**, só quando o usuário pesquisa na Store ou publica um app da Store;
- o **Microsoft Entra ID e o Microsoft Graph**, só quando o usuário conecta o aplicativo a um tenant do Microsoft Intune ou publica um pacote.

Nada sobre o usuário, o computador ou os pacotes é enviado para outro lugar.
