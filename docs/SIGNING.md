# Code signing (trust on Windows 11)

*Português: [abaixo](#assinatura-de-código-confiança-no-windows-11).*

## Why Windows warns or blocks

- **SmartScreen** shows *"Windows protected your PC"* for a downloaded program it does not know yet. *More info > Run anyway* opens it.
- **Smart App Control** (Windows 11) asks Microsoft's cloud service about every program. When the service does not know the file, it accepts it only with a **valid digital signature** (SHA-256, from a certificate authority of the Microsoft Trusted Root Program). There is no exception per application. Its message reads *"Smart App Control blocked an app that may be unsafe ... we couldn't verify its publisher"*.
- **Every version is a new file**: an unsigned release starts unknown again, so a version that opened yesterday says nothing about the next one. Nothing inside the program can change this; only a signature does.

What the project already does so the download can be checked: every release is built by GitHub Actions from the tagged commit, `SHA256SUMS.txt` lists the hash of every file (the application checks it for its own updates), and a signed build provenance attestation proves where each zip came from (`gh attestation verify <file> --repo sesantanajr/J365-IntunePSApp`).

## Choosing a way to sign

| Way | Cost | Who can use it | Where the signing runs | Publisher Windows shows |
|---|---|---|---|---|
| **SignPath Foundation** | Free | Open-source projects accepted by the foundation (OSI licence, public repository, builds on GitHub Actions, MFA) | GitHub Actions (already in the workflow) | SignPath Foundation |
| **Certum Open Source Code Signing** (cloud, SimplySign) | Paid, per year | Individual open-source developers, in any country (identity validation) | Your computer: `tools/Sign-Release.ps1` | Open Source Developer, *your name* |
| **Azure Artifact Signing** (formerly Trusted Signing) | Monthly plan | Organisations and individuals of the countries Microsoft accepts (check the current list) | GitHub Actions (already in the workflow) | The validated name |
| **Microsoft Store** (MSIX) | Free developer account | Anyone, after Store certification | Microsoft signs the package | Microsoft |

The application accepts an update signed by any publisher of the project (SignPath Foundation, the author or Jornada 365), so the way can change later without breaking *Update now*.

### SignPath Foundation (free)

1. Read the conditions and apply on [signpath.org](https://signpath.org). The project is MIT-licensed, public and built by GitHub Actions; [CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md) is the policy the foundation asks for.
2. After the approval, in SignPath: the project (slug, for example `J365-IntunePSApp`), the predefined trusted build system **GitHub.com** linked to it, a **signing policy** (for example `release-signing`; each request is approved by hand) and two **artifact configurations** (examples below; SignPath's editor validates the exact syntax):

   ```xml
   <!-- exe: the artifact uploaded by the workflow is a zip with J365.IntunePSApp.exe -->
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="J365.IntunePSApp.exe" product-name="J365 IntunePSApp">
         <authenticode-sign/>
       </pe-file>
     </zip-file>
   </artifact-configuration>

   <!-- scripts: a zip with the two companion scripts embedded in the executable -->
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <powershell-file path="Publish-ToIntune.ps1"><authenticode-sign/></powershell-file>
       <powershell-file path="Sandbox-Test.ps1"><authenticode-sign/></powershell-file>
     </zip-file>
   </artifact-configuration>
   ```

3. In GitHub, *Settings > Secrets and variables > Actions*: variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`, `SIGNPATH_EXE_CONFIGURATION` (slug of the first configuration) and, to sign the scripts too, `SIGNPATH_SCRIPTS_CONFIGURATION`; secret `SIGNPATH_API_TOKEN`.
4. Push the next tag. The workflow signs the scripts (when configured) and embeds them, builds, sends the executable, waits up to an hour for the approval, checks that the signature is valid and timestamped (`tools/Assert-Signature.ps1`), and only then zips, hashes and attests. The release notes name the signer and carry the attribution *Free code signing provided by SignPath.io, certificate by SignPath Foundation*.

**What to write in the application** (ready to paste):

| Field | Answer |
|---|---|
| Project | J365 IntunePSApp - https://github.com/sesantanajr/J365-IntunePSApp |
| Licence | MIT (OSI approved), no dual licensing; no proprietary code in the repository or the release |
| Description | Windows desktop application (WPF, .NET 10) that packages applications for Microsoft Intune on top of PSAppDeployToolkit: it writes the deployment package, the detection script and the `.intunewin`, and can publish to the user's own tenant. |
| Releases | Public GitHub releases since 2026, built only by GitHub Actions on GitHub-hosted runners from tagged commits, with SHA256SUMS and build provenance attestations |
| What to sign | `J365.IntunePSApp.exe` (single-file executable) and two PowerShell scripts embedded in it (`Publish-ToIntune.ps1`, `Sandbox-Test.ps1`) |
| Why | Smart App Control blocks the unsigned executable on Windows 11; signed scripts keep working where App Control restricts PowerShell |
| Code signing policy | https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CODE_SIGNING_POLICY.md (linked from the README and from every release) |
| Team | Sérgio Sant'Ana Junior (author, reviewer, approver), MFA on GitHub and SignPath |

### Certum Open Source certificate (your own name)

1. Buy *Open Source Code Signing in the cloud* in the Certum shop, complete the identity validation and activate **SimplySign** (mobile app and SimplySign Desktop). Since 2023 the private key cannot be exported: it stays in Certum's cloud HSM.
2. On the signing computer: the **Windows SDK** signing tools (`signtool.exe`) and the **GitHub CLI** (`gh auth login`).
3. After the workflow has published `vX.Y.Z`, connect SimplySign Desktop and run, in the repository folder:

   ```powershell
   .\tools\Sign-Release.ps1 -Version X.Y.Z                         # lists the code-signing certificates
   .\tools\Sign-Release.ps1 -Version X.Y.Z -Thumbprint <thumbprint> -Upload
   ```

   The script downloads the release, checks it against `SHA256SUMS.txt`, signs `J365.IntunePSApp.exe` (SHA-256, Certum timestamp; with `signtool` when the Windows SDK is installed, otherwise with Windows' own signing API), checks that the signature is valid and timestamped, builds the zip and `SHA256SUMS.txt` again and replaces them in the release. The build provenance attestation keeps describing the zip built by GitHub Actions (unsigned); signing inside the workflow keeps both. The scripts embedded in the executable stay unsigned on this path.

The Certum shop lists the cloud *Open Source Code Signing* certificate at about €49 per year (2026), with up to 5,000 signatures a month; the identity validation documents are listed on Certum's support site.

### A company that allows the application in its App Control policy

Companies that run **App Control for Business** (WDAC) can sign the release with their internal code-signing certificate authority and allow that signer in their policy. `tools/Sign-Release.ps1` signs a local zip with a `.pfx` file (the private key is loaded in memory only), with the timestamp server of the company's choice; `-AllowUntrustedRoot` lets the computer that signs accept a root that only the company's devices trust:

```powershell
.\tools\Sign-Release.ps1 -ZipPath .\J365-IntunePSApp-X.Y.Z-win-x64.zip -PfxPath .\contoso-codesign.pfx -TimestampUrl http://timestamp.digicert.com
```

### Azure Artifact Signing

The step *Sign the executable (Azure Artifact Signing)* of [`build.yml`](../.github/workflows/build.yml) runs when the repository has its variables (and SignPath is not configured):

1. In the Azure portal, create an **Artifact Signing account** (resource provider `Microsoft.CodeSigning`) and note its endpoint (for example `https://eus.codesigning.azure.net`) and name.
2. Complete the **identity validation** (public trust) and create a **certificate profile** of type *Public Trust*.
3. In Entra ID, create an app registration for GitHub and give it the role **Artifact Signing Certificate Profile Signer** (formerly *Trusted Signing Certificate Profile Signer*) on the account.
4. In GitHub: variables `SIGNING_ENDPOINT`, `SIGNING_ACCOUNT`, `SIGNING_PROFILE`; secrets `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`.

### Until the release is signed

- **SmartScreen:** check the hash of the zip (README > *Verify the download*), then *More info > Run anyway*.
- **Smart App Control:** Windows offers no exception for one application. Use the application on a computer where Smart App Control is off (devices managed by a company usually have it off), or turn it off in *Windows Security > App & browser control > Smart App Control settings*. Microsoft's FAQ says recent Windows 11 updates (25H2) let you turn it back on later without reinstalling Windows; on older builds turning it off is permanent. That is a decision about the security of your computer, not something the application asks for.

---

# Assinatura de código (confiança no Windows 11)

## Por que o Windows avisa ou bloqueia

- O **SmartScreen** mostra *"O Windows protegeu o computador"* para um programa baixado que ele ainda não conhece. *Mais informações > Executar assim mesmo* abre o programa.
- O **Controle Inteligente de Aplicativos** (Smart App Control, Windows 11) consulta o serviço de nuvem da Microsoft sobre cada programa. Quando o serviço não conhece o arquivo, ele só o aceita com uma **assinatura digital válida** (SHA-256, de uma autoridade certificadora do Programa de Raiz Confiável da Microsoft). Não existe exceção por aplicativo. A mensagem é *"O controle inteligente de aplicativos bloqueou um aplicativo que pode não ser seguro ... não conseguimos verificar seu fornecedor"*.
- **Cada versão é um arquivo novo**: uma versão sem assinatura começa desconhecida de novo, então uma versão que abriu ontem não garante a próxima. Nada dentro do programa muda isso; só uma assinatura.

O que o projeto já faz para o download ser verificável: cada versão é compilada pelo GitHub Actions a partir do commit marcado, o `SHA256SUMS.txt` lista o hash de cada arquivo (o aplicativo o confere nas próprias atualizações), e um atestado de procedência assinado prova de onde veio cada zip (`gh attestation verify <arquivo> --repo sesantanajr/J365-IntunePSApp`).

## Escolhendo como assinar

| Caminho | Custo | Quem pode usar | Onde a assinatura roda | Editor que o Windows mostra |
|---|---|---|---|---|
| **SignPath Foundation** | Gratuito | Projetos de código aberto aceitos pela fundação (licença OSI, repositório público, builds no GitHub Actions, MFA) | GitHub Actions (já no workflow) | SignPath Foundation |
| **Certum Open Source Code Signing** (nuvem, SimplySign) | Pago, por ano | Desenvolvedores de código aberto pessoa física, em qualquer país (validação de identidade) | No seu computador: `tools/Sign-Release.ps1` | Open Source Developer, *seu nome* |
| **Azure Artifact Signing** (antigo Trusted Signing) | Plano mensal | Organizações e pessoas dos países que a Microsoft aceita (confira a lista atual) | GitHub Actions (já no workflow) | O nome validado |
| **Microsoft Store** (MSIX) | Conta de desenvolvedor gratuita | Qualquer pessoa, após a certificação da Store | A Microsoft assina o pacote | Microsoft |

O aplicativo aceita uma atualização assinada por qualquer editor do projeto (SignPath Foundation, o autor ou a Jornada 365), então o caminho pode mudar depois sem quebrar o *Atualizar agora*.

### SignPath Foundation (gratuito)

1. Leia as condições e inscreva o projeto em [signpath.org](https://signpath.org). O projeto tem licença MIT, é público e é compilado pelo GitHub Actions; o [CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md) é a política que a fundação pede.
2. Depois da aprovação, no SignPath: o projeto (slug, por exemplo `J365-IntunePSApp`), o sistema de build confiável pré-definido **GitHub.com** vinculado a ele, uma **política de assinatura** (por exemplo `release-signing`; cada pedido é aprovado à mão) e duas **configurações de artefato** (os exemplos estão na seção em inglês acima; o editor do SignPath valida a sintaxe exata): uma para o zip com o `J365.IntunePSApp.exe` (arquivo PE) e outra para o zip com os dois scripts (`Publish-ToIntune.ps1` e `Sandbox-Test.ps1`, arquivos PowerShell).
3. No GitHub, *Settings > Secrets and variables > Actions*: variáveis `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`, `SIGNPATH_EXE_CONFIGURATION` (slug da primeira configuração) e, para assinar também os scripts, `SIGNPATH_SCRIPTS_CONFIGURATION`; segredo `SIGNPATH_API_TOKEN`.
4. Envie a próxima tag. O workflow assina os scripts (quando configurado) e os embute, compila, envia o executável, espera até uma hora pela aprovação, confere se a assinatura é válida e tem carimbo de tempo (`tools/Assert-Signature.ps1`) e só então compacta, calcula os hashes e atesta. As notas da versão dizem quem assinou e trazem a atribuição *Free code signing provided by SignPath.io, certificate by SignPath Foundation*.

**O que escrever na inscrição** (pronto para colar, em inglês, como o formulário pede):

| Campo | Resposta |
|---|---|
| Project | J365 IntunePSApp - https://github.com/sesantanajr/J365-IntunePSApp |
| Licence | MIT (OSI approved), no dual licensing; no proprietary code in the repository or the release |
| Description | Windows desktop application (WPF, .NET 10) that packages applications for Microsoft Intune on top of PSAppDeployToolkit: it writes the deployment package, the detection script and the `.intunewin`, and can publish to the user's own tenant. |
| Releases | Public GitHub releases since 2026, built only by GitHub Actions on GitHub-hosted runners from tagged commits, with SHA256SUMS and build provenance attestations |
| What to sign | `J365.IntunePSApp.exe` (single-file executable) and two PowerShell scripts embedded in it (`Publish-ToIntune.ps1`, `Sandbox-Test.ps1`) |
| Why | Smart App Control blocks the unsigned executable on Windows 11; signed scripts keep working where App Control restricts PowerShell |
| Code signing policy | https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CODE_SIGNING_POLICY.md (linked from the README and from every release) |
| Team | Sérgio Sant'Ana Junior (author, reviewer, approver), MFA on GitHub and SignPath |

### Certificado Open Source da Certum (no seu nome)

1. Compre *Open Source Code Signing in the cloud* na loja da Certum, conclua a validação de identidade e ative o **SimplySign** (aplicativo no celular e SimplySign Desktop). Desde 2023 a chave privada não pode ser exportada: ela fica no HSM em nuvem da Certum.
2. No computador que assina: as ferramentas de assinatura do **Windows SDK** (`signtool.exe`) e o **GitHub CLI** (`gh auth login`).
3. Depois que o workflow publicar a `vX.Y.Z`, conecte o SimplySign Desktop e execute, na pasta do repositório:

   ```powershell
   .\tools\Sign-Release.ps1 -Version X.Y.Z                         # lista os certificados de assinatura
   .\tools\Sign-Release.ps1 -Version X.Y.Z -Thumbprint <impressão digital> -Upload
   ```

   O script baixa a versão, confere com o `SHA256SUMS.txt`, assina o `J365.IntunePSApp.exe` (SHA-256, carimbo de tempo da Certum; com o `signtool` quando o Windows SDK está instalado, senão com a própria API de assinatura do Windows), confere se a assinatura é válida e tem carimbo de tempo, gera de novo o zip e o `SHA256SUMS.txt` e os substitui na release. O atestado de procedência continua descrevendo o zip gerado pelo GitHub Actions (sem assinatura); assinar dentro do workflow mantém os dois. Por esse caminho, os scripts embutidos no executável ficam sem assinatura.

A loja da Certum lista o certificado *Open Source Code Signing* em nuvem por cerca de €49 por ano (2026), com até 5.000 assinaturas por mês; os documentos da validação de identidade estão no site de suporte da Certum.

### Uma empresa que libera o aplicativo na sua política de Controle de Aplicativos

Empresas que usam o **Controle de Aplicativos para Empresas** (WDAC) podem assinar a versão com a autoridade certificadora interna delas e liberar esse assinante na política. O `tools/Sign-Release.ps1` assina um zip local com um arquivo `.pfx` (a chave privada é carregada só em memória), com o servidor de carimbo de tempo que a empresa escolher; `-AllowUntrustedRoot` permite que o computador que assina aceite uma raiz que só os dispositivos da empresa confiam:

```powershell
.\tools\Sign-Release.ps1 -ZipPath .\J365-IntunePSApp-X.Y.Z-win-x64.zip -PfxPath .\contoso-codesign.pfx -TimestampUrl http://timestamp.digicert.com
```

### Azure Artifact Signing

A etapa *Sign the executable (Azure Artifact Signing)* do [`build.yml`](../.github/workflows/build.yml) roda quando o repositório tem as variáveis dela (e o SignPath não está configurado):

1. No portal do Azure, crie uma **conta do Artifact Signing** (provedor `Microsoft.CodeSigning`) e anote o endpoint (ex.: `https://eus.codesigning.azure.net`) e o nome.
2. Conclua a **validação de identidade** (confiança pública) e crie um **perfil de certificado** do tipo *Public Trust*.
3. No Entra ID, crie um registro de aplicativo para o GitHub e dê a ele a função **Artifact Signing Certificate Profile Signer** (antiga *Trusted Signing Certificate Profile Signer*) na conta.
4. No GitHub: variáveis `SIGNING_ENDPOINT`, `SIGNING_ACCOUNT`, `SIGNING_PROFILE`; segredos `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`.

### Enquanto a versão não estiver assinada

- **SmartScreen:** confira o hash do zip (README > *Verifique o download*) e use *Mais informações > Executar assim mesmo*.
- **Controle Inteligente de Aplicativos:** o Windows não oferece exceção para um aplicativo. Use o aplicativo num computador em que ele esteja desligado (dispositivos gerenciados por empresas costumam tê-lo desligado), ou desligue-o em *Segurança do Windows > Controle de aplicativos e do navegador > Configurações do Controle Inteligente de Aplicativos*. O FAQ da Microsoft diz que as atualizações recentes do Windows 11 (25H2) permitem religá-lo depois sem reinstalar o Windows; em versões anteriores, desligar é definitivo. Essa é uma decisão sobre a segurança do seu computador, não algo que o aplicativo exija.
