## J365 IntunePSApp 2.7.0

### Português

Atualizações que funcionam em redes corporativas, pacotes mais rápidos e seguros e uma revisão do código inteiro, a partir dos logs de uma implantação real.

- **"Você tem a versão mais recente" com uma versão nova disponível:** a verificação usava a API do GitHub, limitada a 60 consultas por hora por endereço público, e numa rede corporativa todos dividem o mesmo endereço. Agora a versão é lida no `SHA256SUMS.txt` da última release, no github.com e sem esse limite; a API e a página da release ficam como alternativas, e o log registra cada verificação. **Cópias até a 2.6.0 ainda dependem da API:** se continuarem dizendo "versão mais recente", baixe esta versão uma vez manualmente.
- **Apps da Microsoft Store voltam a ser publicados:** o tipo `winGetApp` só existe no Microsoft Graph beta. Uma falha de rede não cria mais apps nem registros duplicados.
- **A mesma versão já instalada não é mais reinstalada:** num Acrobat Reader real, o pacote removia e reinstalava a versão idêntica em 9 minutos, depois de pedir para fechar o app. Agora ele só aplica as ações pós-instalação e grava a marca. A opção fica na etapa Desinstalação.
- **Pacotes em pastas longas (OneDrive):**
  - nomes de pasta curtos e caminho verificado antes do IntuneWinAppUtil;
  - empacotador interno muito mais rápido, porque não recompacta o instalador;
  - aviso quando a pasta de pacotes está no OneDrive.
- **Uma janela por vez:** abrir o aplicativo de novo traz a janela aberta para a frente. Antes, duas janelas sobrescreviam as configurações uma da outra. O `config.json` agora é gravado com segurança.
- **Outras correções:**
  - lista de versões anteriores não pré-seleciona mais um app de outro nome;
  - lote não inicia duas vezes durante o login;
  - duplo clique na barra de rolagem do catálogo não baixa mais um app;
  - edições nas Configurações não se perdem mais.
- **Textos e documentação:** textos em português revisados e documentação atualizada.

### English

Updates that work on company networks, faster and safer packages, and a review of the whole code, driven by the logs of a real deployment.

- **"You have the latest version" while a newer one existed:** the check used the GitHub API, limited to 60 requests an hour per public address, and on a company network everyone shares that address. The version is now read from the `SHA256SUMS.txt` of the latest release, on github.com and without that limit. The API and the release page are fallbacks, and every check is logged. **Copies up to 2.6.0 still depend on the API:** if one keeps saying "up to date", download this release once by hand.
- **Apps of the Microsoft Store can be published again:** the `winGetApp` type exists only in Microsoft Graph beta. A network failure no longer creates duplicate apps or registrations.
- **The same version already installed is no longer reinstalled:** with a real Acrobat Reader, the package removed and reinstalled the identical version in 9 minutes, after asking the user to close the app. It now only applies the post-install actions and records its mark. The option is on the Uninstall step.
- **Packages in long folders (OneDrive):**
  - short folder names, and the path is checked before IntuneWinAppUtil runs;
  - a much faster built-in packager, because it no longer recompresses the installer;
  - a warning when the packages folder is inside OneDrive.
- **One window at a time:** starting the application again brings the open window to the front. Before, two windows overwrote each other's settings. `config.json` is now written safely.
- **Other fixes:**
  - the list of earlier versions no longer preselects an app with another name;
  - a batch no longer starts twice during the sign-in;
  - a double-click on the catalog's scroll bar no longer downloads an app;
  - edits in Settings are no longer lost.
- **Texts and documentation:** Portuguese texts reviewed and documentation brought up to date.

See the [CHANGELOG](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md).

### Download

- `J365-IntunePSApp-2.7.0-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-2.7.0-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

**Já tem o J365 IntunePSApp?**
- Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão.
- Uma cópia até a 2.6.0 que insista em "versão mais recente" precisa ser atualizada uma vez manualmente: baixe o zip acima e execute o `J365.IntunePSApp.exe`. Em Configurações > Geral, use **Atualizar a cópia instalada**.

**Already using it?**
- **Settings > General > Check now** downloads and installs this version.
- A copy up to 2.6.0 that keeps saying "up to date" needs one manual update: download the zip above and run `J365.IntunePSApp.exe`. In Settings > General, use **Update the installed copy**.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
