## J365 IntunePSApp 2.6.1

### Português

Leva a todos o conteúdo completo da 2.6: quem instalou a primeira 2.6.0 recebe por **Configurações > Geral > Verificar agora**.

- **Apps da Microsoft Store:** o catálogo pesquisa a Microsoft Store ao vivo, pelo nome ou pelo ID (`9PM860492SZD` encontra o Microsoft PC Manager), e publica esses apps no Intune como **Microsoft Store app (new)**: o Intune instala pela Store e mantém atualizado, sem pacote, com a descrição em português e o logo oficial no Portal da Empresa. Sozinho, em lote ou por linha de comando (`--batch 9PM860492SZD --publish`).
- **Nada parece faltar:** a busca informa quantos apps o filtro "Somente compatíveis" ocultou (MSIX, ZIP, portáteis) e oferece procurar o app na Microsoft Store.
- **Atualização confiável:** "Verificar agora" só diz que está atualizado quando consultou o GitHub, mostra a versão e usa a página de versões se a API do GitHub estiver bloqueada.

Inclui toda a 2.6.0: deploy em lote no Intune, modo silencioso sem notificar o usuário, ícones e parâmetros corretos. Veja o [CHANGELOG](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md).

### English

Brings the complete 2.6 to everyone: a copy of the first 2.6.0 receives it through **Settings > General > Check now**.

- **Apps of the Microsoft Store:** the catalog searches the Microsoft Store live, by name or product id (`9PM860492SZD` finds Microsoft PC Manager), and publishes them to Intune as **Microsoft Store app (new)**: Intune installs them from the Store and keeps them current, with no package, the description of the Store and its official logo in the Company Portal. Alone, in a batch or from the command line (`--batch 9PM860492SZD --publish`).
- **Nothing seems missing:** a search says how many apps "Only supported" hid (MSIX, ZIP, portable) and offers to search the Microsoft Store.
- **Reliable updates:** "Check now" only says up to date when it reached GitHub, names the version, and uses the releases page when the GitHub API is blocked.

Includes all of 2.6.0: batch deploy to Intune, silent mode without notifying the user, right icons and switches. See the [CHANGELOG](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md).

### Download

- `J365-IntunePSApp-2.6.1-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-2.6.1-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Já tem o J365 IntunePSApp? Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão. / Already using it? **Settings > General > Check now** downloads and installs this version.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
