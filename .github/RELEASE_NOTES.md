## J365 IntunePSApp 2.4.1

Package any Windows application for Microsoft Intune in minutes, with no scripting. Built on [PSAppDeployToolkit](https://psappdeploytoolkit.com). Created by [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

### Why 2.4.1

On Windows 11 with **Smart App Control** on, 2.4.0 was blocked with *"we couldn't verify its publisher"*. Smart App Control asks Microsoft's cloud service about every program and, when the file is unknown, accepts it only with a valid digital signature; there is no exception for one application, and every new version is a new, unknown file. Code signing is the only fix, and this version makes it ready:

- **Signing in the release workflow** with SignPath (free for open-source projects through SignPath Foundation) or Azure Artifact Signing: the executable **and the PowerShell scripts embedded in it** are signed before the release is zipped, hashed and attested, and a release with signing configured stops unless every signature is valid and timestamped.
- **`tools/Sign-Release.ps1`** signs a published release with a certificate of the author's computer (Certum Open Source in SimplySign, any OV/EV token) or a company's internal authority (`.pfx`, for App Control allow-lists).
- **Code signing policy** published ([CODE_SIGNING_POLICY.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CODE_SIGNING_POLICY.md)) and [docs/SIGNING.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md) rewritten: how Smart App Control decides, the ways to sign, and what to do until a release is signed.
- **Settings > About** shows whether the executable is signed and by whom. The update inside the application accepts any publisher of the project, so the signing service can change.
- The local tests and publishing explain a PowerShell restricted by App Control instead of failing obscurely.
- **Fixes from a review of 2.4.0**: *keep a newer version* compared with the minimum version of the detection instead of the version of the package (a copy older than the package could be kept); connecting with a code, IDs or Cloud Shell no longer ends a working connection before the new one works; a package with unsaved changes is never replaced or closed without a question; a damaged toolkit copy is installed again; environment variables reach the signed-in user right away.

Until a release carries a signature: use the application on a computer where Smart App Control is off (company-managed devices usually are), or see the options in docs/SIGNING.md. SmartScreen alone still offers *More info > Run anyway*.

Full list: [CHANGELOG.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md).

### Download

| File | For |
|---|---|
| `J365-IntunePSApp-<version>-win-x64.zip` | **The application.** Extract and run `J365.IntunePSApp.exe`. Windows 10/11 x64, nothing to install. |
| `J365-IntunePSApp-<version>-source.zip` | **Source package** of this exact release, to audit, build or fork. |
| `SHA256SUMS.txt` | Checksums of both files (also used by the update inside the application). |

Verify: `Get-FileHash <file> -Algorithm SHA256` and `gh attestation verify <file> --repo sesantanajr/J365-IntunePSApp`.

---

## J365 IntunePSApp 2.4.1 (Português)

Empacote qualquer aplicativo Windows para o Microsoft Intune em minutos, sem escrever scripts. Baseado no [PSAppDeployToolkit](https://psappdeploytoolkit.com). Criado por [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

**Por que a 2.4.1:** no Windows 11 com o **Controle Inteligente de Aplicativos** ligado, a 2.4.0 foi bloqueada com *"não conseguimos verificar seu fornecedor"*. Ele consulta o serviço de nuvem da Microsoft sobre cada programa e, quando o arquivo é desconhecido, só o aceita com uma assinatura digital válida; não há exceção para um aplicativo, e cada versão nova é um arquivo novo e desconhecido. A assinatura de código é a única solução, e esta versão a deixa pronta:

- **Assinatura no workflow de release** com o SignPath (gratuito para código aberto pela SignPath Foundation) ou o Azure Artifact Signing: o executável **e os scripts PowerShell embutidos nele** são assinados antes de a versão ser compactada, ter os hashes calculados e ser atestada, e uma versão com assinatura configurada para se alguma assinatura não for válida e com carimbo de tempo.
- **`tools/Sign-Release.ps1`** assina uma versão publicada com um certificado do computador do autor (Certum Open Source no SimplySign, qualquer token OV/EV) ou da autoridade interna de uma empresa (`.pfx`, para listas de permissão do Controle de Aplicativos).
- **Política de assinatura de código** publicada ([CODE_SIGNING_POLICY.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CODE_SIGNING_POLICY.md)) e o [docs/SIGNING.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md) reescrito: como o Controle Inteligente de Aplicativos decide, os caminhos para assinar e o que fazer enquanto a versão não estiver assinada.
- **Configurações > Sobre** mostra se o executável está assinado e por quem. A atualização dentro do aplicativo aceita qualquer editor do projeto, então o serviço de assinatura pode mudar.
- Os testes locais e a publicação explicam um PowerShell restrito pelo Controle de Aplicativos, em vez de falhar sem explicação.
- **Correções de uma revisão da 2.4.0**: *manter versão mais nova* comparava com a versão mínima da detecção em vez da versão do pacote (uma cópia mais antiga que o pacote podia ser mantida); conectar com código, IDs ou Cloud Shell não encerra mais uma conexão que funciona antes de a nova funcionar; um pacote com alterações não salvas nunca é substituído nem fechado sem pergunta; uma cópia danificada do toolkit é instalada de novo; variáveis de ambiente chegam na hora ao usuário conectado.

Enquanto a versão não tiver assinatura: use o aplicativo num computador em que o Controle Inteligente de Aplicativos esteja desligado (dispositivos gerenciados por empresas costumam estar), ou veja as opções no docs/SIGNING.md. Só com o SmartScreen continua valendo *Mais informações > Executar assim mesmo*.
