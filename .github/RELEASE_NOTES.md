## J365 IntunePSApp 2.4.3

Package any Windows application for Microsoft Intune in minutes, with no scripting. Built on [PSAppDeployToolkit](https://psappdeploytoolkit.com). Created by [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

### Why 2.4.3

After the app registration was deleted in Entra ID, the application could neither sign in nor be disconnected: the browser showed Entra's error and never came back, the application waited with every button disabled, and the saved registration could not be cleared. This version makes the connection one thing, with one way in and one way out.

- **Disconnect, always there:** *Settings > Microsoft Intune* is one card with the state of the connection and only the actions that fit it. *Disconnect* clears the session, the certificate and the registration saved on this computer, so you can connect again from the beginning. A sign-in waiting for the browser can be cancelled.
- **A deleted registration is found at once**, at start-up and before any browser opens: Settings says the connection needs attention, with *Connect again* (the automatic setup creates a new registration) and *Disconnect*.
- **Nothing changes until it works:** a new connection takes over only after its sign-in (or the first token of the certificate) succeeded; a cancel or a failure leaves the connection in use as it was.
- **Less to choose from:** the card to type the registration, the extra certificate buttons and the ID fields of the publishing dialog are gone; the connection window keeps three ways (certificate, my account, an existing registration) and Azure Cloud Shell as a link.

Full list: [CHANGELOG.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md). How to connect: [docs/INTUNE-PUBLISH.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/INTUNE-PUBLISH.md).

On Windows 11 with **Smart App Control** on, an unsigned release is blocked: see [docs/SIGNING.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md).

### Download

| File | For |
|---|---|
| `J365-IntunePSApp-<version>-win-x64.zip` | **The application.** Extract and run `J365.IntunePSApp.exe`. Windows 10/11 x64, nothing to install. |
| `J365-IntunePSApp-<version>-source.zip` | **Source package** of this exact release, to audit, build or fork. |
| `SHA256SUMS.txt` | Checksums of both files (also used by the update inside the application). |

Verify: `Get-FileHash <file> -Algorithm SHA256` and `gh attestation verify <file> --repo sesantanajr/J365-IntunePSApp`.

---

## J365 IntunePSApp 2.4.3 (Português)

Empacote qualquer aplicativo Windows para o Microsoft Intune em minutos, sem escrever scripts. Baseado no [PSAppDeployToolkit](https://psappdeploytoolkit.com). Criado por [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

**Por que a 2.4.3:** depois que o registro do aplicativo foi excluído no Entra ID, o aplicativo não conseguia entrar nem ser desconectado: o navegador mostrava o erro do Entra e nunca voltava, o aplicativo esperava com todos os botões desativados, e o registro guardado não podia ser limpo. Esta versão transforma a conexão numa coisa só, com uma entrada e uma saída.

- **Desconectar, sempre disponível:** *Configurações > Microsoft Intune* é um cartão só, com o estado da conexão e apenas as ações que cabem nele. *Desconectar* limpa a sessão, o certificado e o registro guardados neste computador, para você conectar de novo do zero. Um login esperando o navegador pode ser cancelado.
- **Um registro excluído é percebido na hora**, ao abrir e antes de qualquer navegador: as Configurações dizem que a conexão precisa de atenção, com *Conectar de novo* (a configuração automática cria um registro novo) e *Desconectar*.
- **Nada muda até funcionar:** uma conexão nova só assume depois que o login (ou o primeiro token do certificado) deu certo; um cancelamento ou uma falha deixam a conexão em uso como estava.
- **Menos para escolher:** saíram o cartão para digitar o registro, os botões extras do certificado e os campos de IDs da janela de publicação; a janela de conexão mantém três caminhos (certificado, minha conta, um registro que já existe) e o Azure Cloud Shell como link.

Lista completa: [CHANGELOG.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md). Como conectar: [docs/INTUNE-PUBLISH.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/INTUNE-PUBLISH.md#publicar-no-intune).

No Windows 11 com o **Controle Inteligente de Aplicativos** ligado, uma versão sem assinatura é bloqueada: veja o [docs/SIGNING.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md).
