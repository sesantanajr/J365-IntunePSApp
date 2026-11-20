## J365 IntunePSApp 2.4.4

Package any Windows application for Microsoft Intune in minutes, with no scripting. Built on [PSAppDeployToolkit](https://psappdeploytoolkit.com). Created by [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

### Why 2.4.4

A package of Adobe Acrobat Reader (an .exe) warned that it had no uninstall switches, although the catalog knows the MSI it installs. And publishing with the app certificate failed with *HTTP 401 (UnknownError)*, shown as raw JSON under *"Could not connect"*: Intune answers that to every request of a tenant without an active Intune subscription.

- **Does the tenant have Intune?** The setup reads the subscriptions of the tenant (read only) and says at once when Microsoft Intune is missing, expired or limited to *Basic Mobility and Security*. For a connection set up before, **Check the subscription** asks an administrator to sign in and reads it. The sidebar, the home page, Settings and the card of a package show it, with *Activate Intune in Microsoft 365*.
- **Clear refusals:** the publishing dialog says *Intune refused this connection* with its cause and the Activity ID for Microsoft support; the error of Intune is opened to its sentence, even when it comes nested twice or cut short.
- **An .exe that installs an MSI is removed by its product code** (msiexec /x), in the uninstallation and before installing over an existing version: no uninstall switches, no warning, no question. A package of Adobe Acrobat Reader (64-bit) no longer reaches Acrobat Pro or Acrobat 2020.

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

## J365 IntunePSApp 2.4.4 (Português)

Empacote qualquer aplicativo Windows para o Microsoft Intune em minutos, sem escrever scripts. Baseado no [PSAppDeployToolkit](https://psappdeploytoolkit.com). Criado por [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

**Por que a 2.4.4:** um pacote do Adobe Acrobat Reader (um .exe) avisava que não tinha parâmetros de desinstalação, embora o catálogo conheça o MSI que ele instala. E a publicação com o certificado do aplicativo falhava com *HTTP 401 (UnknownError)*, mostrado como JSON cru sob *"Não foi possível conectar"*: o Intune responde isso a qualquer pedido de um tenant sem assinatura ativa do Intune.

- **O tenant tem Intune?** A configuração lê as assinaturas do tenant (só leitura) e avisa na hora quando o Microsoft Intune não existe, expirou ou se limita à *Mobilidade e Segurança Básicas*. Para uma conexão configurada antes, **Verificar a assinatura** pede o login de um administrador e faz a leitura. A barra lateral, o início, as Configurações e o cartão do pacote mostram isso, com *Ativar o Intune no Microsoft 365*.
- **Recusas claras:** a janela de publicação diz *O Intune recusou esta conexão* com a causa e o Activity ID para o suporte da Microsoft; o erro do Intune é aberto até a frase, mesmo quando vem aninhado duas vezes ou cortado.
- **Um .exe que instala um MSI é removido pelo código do produto** (msiexec /x), na desinstalação e antes de instalar sobre uma versão existente: sem parâmetros de desinstalação, sem aviso, sem pergunta. Um pacote do Adobe Acrobat Reader (64 bits) não alcança mais o Acrobat Pro nem o Acrobat 2020.

Lista completa: [CHANGELOG.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md). Como conectar: [docs/INTUNE-PUBLISH.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/INTUNE-PUBLISH.md#publicar-no-intune).

No Windows 11 com o **Controle Inteligente de Aplicativos** ligado, uma versão sem assinatura é bloqueada: veja o [docs/SIGNING.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md).
