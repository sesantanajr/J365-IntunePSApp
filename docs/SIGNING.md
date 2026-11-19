# Code signing (trust on Windows 11)

*Português: [abaixo](#assinatura-de-código-confiança-no-windows-11).*

## Why Windows warns today

Windows SmartScreen shows *"Windows protected your PC"* for any program that is **not code-signed** or whose signature has no reputation yet, and **Smart App Control** blocks unsigned programs it does not know, with no exception. Nothing in the program itself can turn these warnings off: only a **code-signing certificate from a trusted authority** does, and the reputation of that certificate grows with downloads.

What the project already does to make the download verifiable:

- every release is built by GitHub Actions from the tagged commit;
- `SHA256SUMS.txt` lists the hash of every file, and the application checks it for its own updates;
- a signed **build provenance attestation** proves where each zip came from: `gh attestation verify <file> --repo sesantanajr/J365-IntunePSApp`.

## The release workflow signs automatically

[`build.yml`](../.github/workflows/build.yml) has a step *Sign the executable (Azure Trusted Signing)*. It runs only when the repository has the variables below; without them the build is the same as before (unsigned). Once configured, every tag signs `J365.IntunePSApp.exe` **before** it is zipped, hashed and attested, and the step fails the release if the signature is not valid. From that release on, the application also refuses an update that is not signed by the same publisher.

### Azure Trusted Signing (recommended)

Microsoft's managed signing service: the certificate lives in Microsoft's HSMs, is renewed automatically, and is trusted by Windows (SmartScreen and Smart App Control). Cost: the Basic plan is a few dollars a month. Identity validation of an individual or an organisation is required, and availability depends on the country of the subscriber: check the current list in the Azure documentation.

1. In the Azure portal, create a **Trusted Signing account** (resource provider `Microsoft.CodeSigning`) and note its **endpoint** (for example `https://eus.codesigning.azure.net`) and **name**.
2. In the account, complete the **identity validation** (public trust) and create a **certificate profile** of type *Public Trust*. Note its name.
3. In Entra ID, create an app registration for GitHub (or use workload identity federation) and give it the role **Trusted Signing Certificate Profile Signer** on the account.
4. In the GitHub repository, *Settings > Secrets and variables > Actions*:
   - **Variables**: `SIGNING_ENDPOINT`, `SIGNING_ACCOUNT`, `SIGNING_PROFILE`;
   - **Secrets**: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`.
5. Push the next tag (`vX.Y.Z`). The log of the step shows *Signed by: CN=...*.

### Other options

- **SignPath Foundation**: free code signing for open-source projects, with a GitHub Actions integration (an application to the program is needed).
- **A certificate from a commercial authority** (OV), on a hardware token or a cloud HSM: replace the step with `signtool sign /fd SHA256 /tr <timestamp> /td SHA256`.
- **Distribution channels**: the Microsoft Store (signed by Microsoft) and `winget` (a manifest in microsoft/winget-pkgs) add discovery and trust, but the binary still needs a signature for SmartScreen when downloaded directly.

---

# Assinatura de código (confiança no Windows 11)

## Por que o Windows avisa hoje

O SmartScreen mostra *"O Windows protegeu o computador"* para qualquer programa **sem assinatura de código** ou cuja assinatura ainda não tem reputação, e o **Controle Inteligente de Aplicativos** (Smart App Control) bloqueia programas sem assinatura que não conhece, sem exceção. Nada no próprio programa desliga esses avisos: só um **certificado de assinatura de código de uma autoridade confiável**, e a reputação desse certificado cresce com os downloads.

O que o projeto já faz para o download ser verificável:

- cada versão é compilada pelo GitHub Actions a partir do commit marcado;
- o `SHA256SUMS.txt` lista o hash de cada arquivo, e o aplicativo o confere nas próprias atualizações;
- um **atestado de procedência** assinado prova de onde veio cada zip: `gh attestation verify <arquivo> --repo sesantanajr/J365-IntunePSApp`.

## O workflow de release assina sozinho

O [`build.yml`](../.github/workflows/build.yml) tem a etapa *Sign the executable (Azure Trusted Signing)*. Ela só roda quando o repositório tem as variáveis abaixo; sem elas o build é igual ao de antes (sem assinatura). Depois de configurada, cada tag assina o `J365.IntunePSApp.exe` **antes** de ele ser compactado, ter o hash calculado e ser atestado, e a etapa falha a release se a assinatura não for válida. A partir dessa versão o aplicativo também recusa uma atualização que não esteja assinada pelo mesmo editor.

### Azure Trusted Signing (recomendado)

Serviço de assinatura gerenciado da Microsoft: o certificado fica nos HSMs da Microsoft, é renovado automaticamente e é confiável para o Windows (SmartScreen e Smart App Control). Custo: o plano Basic custa poucos dólares por mês. É preciso validar a identidade de uma pessoa ou de uma organização, e a disponibilidade depende do país do assinante: confira a lista atual na documentação do Azure.

1. No portal do Azure, crie uma **conta do Trusted Signing** (provedor `Microsoft.CodeSigning`) e anote o **endpoint** (ex.: `https://eus.codesigning.azure.net`) e o **nome**.
2. Na conta, conclua a **validação de identidade** (confiança pública) e crie um **perfil de certificado** do tipo *Public Trust*. Anote o nome.
3. No Entra ID, crie um registro de aplicativo para o GitHub (ou use federação de identidade de carga de trabalho) e dê a ele a função **Trusted Signing Certificate Profile Signer** na conta.
4. No repositório do GitHub, *Settings > Secrets and variables > Actions*:
   - **Variáveis**: `SIGNING_ENDPOINT`, `SIGNING_ACCOUNT`, `SIGNING_PROFILE`;
   - **Segredos**: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`.
5. Envie a próxima tag (`vX.Y.Z`). O log da etapa mostra *Signed by: CN=...*.

### Outras opções

- **SignPath Foundation**: assinatura de código gratuita para projetos de código aberto, com integração ao GitHub Actions (é preciso se inscrever no programa).
- **Certificado de uma autoridade comercial** (OV), em token de hardware ou HSM na nuvem: troque a etapa por `signtool sign /fd SHA256 /tr <timestamp> /td SHA256`.
- **Canais de distribuição**: a Microsoft Store (assinada pela Microsoft) e o `winget` (um manifesto no microsoft/winget-pkgs) trazem descoberta e confiança, mas o binário baixado direto ainda precisa de assinatura para o SmartScreen.
