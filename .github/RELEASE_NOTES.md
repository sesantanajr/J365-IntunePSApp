## J365 IntunePSApp 2.4.0

Package any Windows application for Microsoft Intune in minutes, with no scripting. Built on [PSAppDeployToolkit](https://psappdeploytoolkit.com). Created by [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

### New in 2.4

- **A connection to Intune that explains itself.** *Connect to Intune* opens a guided window with three ways in: the **automatic setup** (an administrator signs in; the registration *J365 IntunePSApp* is created and approved), a **registration that already exists** (paste the *connection code* an administrator shares) or **Azure Cloud Shell** (one command creates the same registration with the Azure CLI, with no consent screen). Every step is shown; a failure comes with its likely causes and the action that fixes each one, including the "sign-in cancelled / need admin approval" case (PIM activation, another account, or Cloud Shell when the tenant blocks Microsoft Graph Command Line Tools).
- **Light and dark themes and a new look.** Every page redesigned: new palette, sidebar with the Intune connection, catalog search from anywhere (Ctrl+K), Settings in sections, dialogs in the theme.
- **No more silent downgrades:** an application that updated itself is no longer replaced by the older version of the package; a newer version already installed is kept.
- **Uninstall tells the truth:** when the program is still there after the uninstallation, the deployment fails instead of reporting "uninstalled".
- **Safer publishing:** an app updated in Intune only changes after its new content is confirmed; cancelling cleans up.
- **Also fixed:** *Path* environment variables are appended (no longer replaced), the PSAppDeployToolkit template must carry its publisher's signature, typographic quotes in the company name, exit code after a failed removal of the previous version, proxies that ask for the Windows sign-in, unsaved changes in the editor.

Verified: the new install and uninstall behaviour inside the real PSAppDeployToolkit 4.1.8; publishing (update, failed commit, cancel) against local stand-ins for Microsoft Graph and Azure Storage; every page in both themes and both languages. 214 automated tests. The connection window and Cloud Shell command were not yet run against a real tenant: please report results in the issues.

Full list: [CHANGELOG.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md). Update from 2.2 or 2.3 with **Update now** inside the application. Packages are generated again to get the new install and uninstall behaviour.

### Download

| File | For |
|---|---|
| `J365-IntunePSApp-<version>-win-x64.zip` | **The application.** Extract and run `J365.IntunePSApp.exe`. Windows 10/11 x64, nothing to install. |
| `J365-IntunePSApp-<version>-source.zip` | **Source package** of this exact release, to audit, build or fork. |
| `SHA256SUMS.txt` | Checksums of both files (also used by the update inside the application). |

Verify: `Get-FileHash <file> -Algorithm SHA256` and `gh attestation verify <file> --repo sesantanajr/J365-IntunePSApp`.

The executable is not code-signed yet: Windows SmartScreen may ask for confirmation (*More info > Run anyway*), and computers with Smart App Control turned on may block it.

---

## J365 IntunePSApp 2.4.0 (Português)

Empacote qualquer aplicativo Windows para o Microsoft Intune em minutos, sem escrever scripts. Baseado no [PSAppDeployToolkit](https://psappdeploytoolkit.com). Criado por [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

**Novo na 2.4:**

- **Uma conexão com o Intune que explica o que acontece.** *Conectar ao Intune* abre uma janela guiada com três caminhos: a **configuração automática** (um administrador entra; o registro *J365 IntunePSApp* é criado e aprovado), um **registro que já existe** (cole o *código de conexão* que um administrador compartilhou) ou o **Azure Cloud Shell** (um comando cria o mesmo registro com o Azure CLI, sem tela de permissão). Cada etapa aparece na tela; uma falha vem com as causas prováveis e a ação que resolve cada uma, inclusive o caso "login cancelado / aprovação do administrador necessária" (ativar a função no PIM, outra conta, ou o Cloud Shell quando o tenant bloqueia o Microsoft Graph Command Line Tools).
- **Temas claro e escuro e um visual novo.** Todas as telas redesenhadas: paleta nova, barra lateral com a conexão do Intune, busca no catálogo de qualquer lugar (Ctrl+K), Configurações em seções, janelas no tema.
- **Sem rebaixar versões em silêncio:** um aplicativo que se atualizou sozinho não é mais trocado pela versão mais antiga do pacote; uma versão mais nova já instalada é mantida.
- **Desinstalação que diz a verdade:** quando o programa continua lá depois da desinstalação, a implantação falha em vez de informar "desinstalado".
- **Publicação mais segura:** um app atualizado no Intune só muda depois que o conteúdo novo é confirmado; cancelar limpa o que foi criado.
- **Também corrigido:** variáveis *Path* são acrescentadas (não mais substituídas), o template do PSAppDeployToolkit precisa ter a assinatura do publicador, aspas tipográficas no nome da empresa, código de saída depois de uma falha ao remover a versão anterior, proxies que pedem o login do Windows, alterações não salvas no editor.

Verificado: o novo comportamento de instalação e desinstalação dentro do PSAppDeployToolkit 4.1.8 real; a publicação (atualização, confirmação com falha, cancelamento) contra substitutos locais do Microsoft Graph e do Azure Storage; todas as telas nos dois temas e nos dois idiomas. 214 testes automatizados. A janela de conexão e o comando do Cloud Shell ainda não foram executados contra um tenant real: relatos são bem-vindos nas issues.

Atualize a partir da 2.2 ou da 2.3 com **Atualizar agora** dentro do aplicativo. Gere os pacotes de novo para ter o novo comportamento de instalação e desinstalação. O executável ainda não tem assinatura de código: o SmartScreen pode pedir confirmação (*Mais informações > Executar assim mesmo*).
