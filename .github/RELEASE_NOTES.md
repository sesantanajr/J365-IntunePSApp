## J365 IntunePSApp 2.4.2

Package any Windows application for Microsoft Intune in minutes, with no scripting. Built on [PSAppDeployToolkit](https://psappdeploytoolkit.com). Created by [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

### Why 2.4.2

Publishing from the application failed in a real tenant: the search for earlier versions asked Intune for a property that only Windows (Win32) apps have (*"Could not find a property named 'displayVersion'"*), and then Intune refused the account itself (HTTP 401 *"Forbidden"*: an Intune role not active in PIM, or no Intune license). This version fixes the search, explains every refusal and adds a modern connection with no user and no secret.

- **Connect with an app certificate** (recommended): the registration *J365 IntunePSApp* gets `DeviceManagementApps.ReadWrite.All` as an application permission and a certificate created on this computer, its key in the **TPM** and never exportable. Publishing needs no sign-in and no longer depends on the role, PIM or Intune license of an account. The connection with your own account is still there.
- **Publishing comes first**: the card of a generated package shows *Publish to Microsoft Intune* with *Publish now* (or *Connect and publish*), and *Publish to Intune when done* opens the publication right after the build. No longer a preview.
- **Fixed**: the search for earlier versions; a failed search no longer blocks the publication; a network failure no longer signs you out.
- **Refusals of Intune explained**: role and PIM, license or *Allow access to unlicensed admins*, subscription of the tenant, consent, and the certificate as the way around.
- `Publish-ToIntune.ps1 -CertificateThumbprint` publishes as the application by hand too.

How the certificate connection works and what to know: [docs/INTUNE-PUBLISH.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/INTUNE-PUBLISH.md). Full list: [CHANGELOG.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md).

On Windows 11 with **Smart App Control** on, an unsigned release is blocked: see [docs/SIGNING.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md).

### Download

| File | For |
|---|---|
| `J365-IntunePSApp-<version>-win-x64.zip` | **The application.** Extract and run `J365.IntunePSApp.exe`. Windows 10/11 x64, nothing to install. |
| `J365-IntunePSApp-<version>-source.zip` | **Source package** of this exact release, to audit, build or fork. |
| `SHA256SUMS.txt` | Checksums of both files (also used by the update inside the application). |

Verify: `Get-FileHash <file> -Algorithm SHA256` and `gh attestation verify <file> --repo sesantanajr/J365-IntunePSApp`.

---

## J365 IntunePSApp 2.4.2 (Português)

Empacote qualquer aplicativo Windows para o Microsoft Intune em minutos, sem escrever scripts. Baseado no [PSAppDeployToolkit](https://psappdeploytoolkit.com). Criado por [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

**Por que a 2.4.2:** a publicação pelo aplicativo falhou num locatário real: a busca de versões anteriores pedia ao Intune uma propriedade que só os apps do Windows (Win32) têm (*"Could not find a property named 'displayVersion'"*), e depois o Intune recusou a própria conta (HTTP 401 *"Forbidden"*: função do Intune não ativa no PIM, ou sem licença do Intune). Esta versão corrige a busca, explica cada recusa e traz uma conexão moderna, sem usuário e sem segredo.

- **Conectar com certificado do aplicativo** (recomendado): o registro *J365 IntunePSApp* recebe `DeviceManagementApps.ReadWrite.All` como permissão de aplicativo e um certificado criado neste computador, com a chave no **TPM** e nunca exportável. A publicação não pede login e deixa de depender da função, do PIM e da licença do Intune de uma conta. A conexão com a sua própria conta continua disponível.
- **A publicação vem primeiro**: o cartão de um pacote gerado mostra *Publicar no Microsoft Intune* com *Publicar agora* (ou *Conectar e publicar*), e *Publicar no Intune ao terminar* abre a publicação logo depois da geração. Deixou de ser prévia.
- **Corrigido**: a busca de versões anteriores; uma busca que falha não bloqueia mais a publicação; uma falha de rede não desconecta mais.
- **Recusas do Intune explicadas**: função e PIM, licença ou *Permitir acesso a administradores sem licença*, assinatura do tenant, consentimento, e o certificado como alternativa.
- `Publish-ToIntune.ps1 -CertificateThumbprint` também publica como o aplicativo manualmente.

Como funciona a conexão com certificado e o que saber: [docs/INTUNE-PUBLISH.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/INTUNE-PUBLISH.md#publicar-no-intune). Lista completa: [CHANGELOG.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md).

No Windows 11 com o **Controle Inteligente de Aplicativos** ligado, uma versão sem assinatura é bloqueada: veja o [docs/SIGNING.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md).
