## J365 IntunePSApp 2.6.1

### Português

Correção da verificação de novas versões.

- **"Você tem a versão mais recente" só quando é verdade:** se o GitHub não respondesse (proxy, limite de 60 consultas por hora da API, falta de conexão), a verificação dizia que o aplicativo estava atualizado. Agora ela diz o motivo, tenta de novo na próxima abertura e oferece a página de versões; quando está atualizado, mostra a versão encontrada.
- **Atualização mesmo com a API do GitHub bloqueada:** se `api.github.com` não responder, a versão mais recente é lida na página de versões do GitHub e os arquivos são baixados pelos nomes de sempre, com a mesma conferência de SHA256 e de versão antes da troca do executável.

Inclui tudo da 2.6.0: deploy em lote no Intune, modo silencioso sem notificar o usuário, ícones e parâmetros corretos. Veja o [CHANGELOG](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md).

### English

Fix of the check for new versions.

- **"You have the latest version" only when it is true:** when GitHub did not answer (proxy, the limit of 60 API requests an hour, no connection), the check said the application was up to date. It now says why, tries again at the next start and offers the releases page; when up to date, it names the version found.
- **Updates even when the GitHub API is blocked:** when `api.github.com` does not answer, the latest version is read from the releases page of GitHub and the files are downloaded by their usual names, with the same SHA256 and version checks before the executable is replaced.

Includes everything of 2.6.0: batch deploy to Intune, silent mode without notifying the user, right icons and switches. See the [CHANGELOG](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md).

### Download

- `J365-IntunePSApp-2.6.1-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-2.6.1-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Já tem o J365 IntunePSApp? Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão. / Already using it? **Settings > General > Check now** downloads and installs this version.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
