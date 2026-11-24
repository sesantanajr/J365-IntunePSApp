## J365 IntunePSApp 3.0.4

### Português

Porcentagem real no envio ao Intune.

- **O envio ao Intune mostra a porcentagem.** O script de publicação só informava o andamento a cada 10%: num pacote grande (770 MB), a barra ficava parada por até um minuto, sem número. Agora ele informa cada ponto, no máximo uma vez por segundo, e a janela de publicação mostra a porcentagem ao lado da barra e o tempo restante. A mesma janela mostra os dois no download de um instalador e de uma atualização do app; os itens do lote também acompanham cada ponto.
- Durante uma publicação feita dentro do app, o editor não pede mais para acompanhar uma janela do PowerShell que não existe.

### English

A real percentage while uploading to Intune.

- **The upload to Intune shows its percentage.** The publishing script reported the upload every 10%: on a large package (770 MB) the bar stood still for up to a minute with no number. It now reports every point, at most once a second, and the publishing window shows the percentage beside the bar and the time left. The same window shows both for the download of an installer and of an application update; the items of the batch follow every point too.
- While a publication runs inside the application, the editor no longer says to follow a PowerShell window that is not there.

### Download

- `J365-IntunePSApp-3.0.4-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-3.0.4-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Já tem o J365 IntunePSApp? Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão. / Already using it? **Settings > General > Check now** downloads and installs this version.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
