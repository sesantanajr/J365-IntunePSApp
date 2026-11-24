## J365 IntunePSApp 3.0.5

### Português

Todo aplicativo que o catálogo oferece baixa.

- **Links conferidos todos os dias.** Cerca de 2% dos links de instaladores do winget davam 404: o fabricante tira o arquivo do ar e links temporários expiram antes de o manifesto mudar (o Autodesk DWG TrueView 2027 falhava com "404 (Not Found)"). O banco de dados agora confere cada link que o app empacota. Quando a versão mais nova do winget está com o link quebrado, a versão anterior que funciona entra no lugar; quando nenhuma funciona, o aplicativo fica marcado como **Link indisponível**.
- **Nada de download que vai falhar.** *Somente compatíveis* esconde esses aplicativos, os detalhes explicam o motivo e o lote os pula com a razão. Um instalador cujo link funciona vem antes de um preferido que está fora do ar.
- **Mensagens claras.** Um 404 não é mais tentado três vezes, e o app oferece abrir o site do fabricante para baixar o instalador e arrastá-lo em Novo pacote. Um arquivo cujo SHA256 não confere com o winget continua nunca sendo usado, agora com a explicação.

O banco de dados com os links conferidos é gerado logo depois desta versão e, a partir daí, todos os dias. Para buscá-lo na hora: Configurações > Componentes > Baixar novamente.

### English

Every application the catalog offers downloads.

- **Links checked every day.** About 2% of the installer links of winget answered 404: vendors take files off their servers and time-limited links expire before the manifest changes (Autodesk DWG TrueView 2027 failed with "404 (Not Found)"). The database now checks every link the application packages. When the newest version in winget has a broken link, the earlier version that works takes its place; when none works, the application is marked **Link unavailable**.
- **No download that will fail.** *Supported only* hides those applications, the details say why and the batch skips them with the reason. An installer whose link works comes before a preferred one that is gone.
- **Clear messages.** A 404 is no longer retried three times, and the application offers the vendor's site to download the installer and drop it on New package. A file whose SHA256 differs from winget's is still never used, now with an explanation.

The database with the checked links is built right after this release and every day from then on. To get it at once: Settings > Components > Download again.

### Download

- `J365-IntunePSApp-3.0.5-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-3.0.5-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Já tem o J365 IntunePSApp? Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão. / Already using it? **Settings > General > Check now** downloads and installs this version.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
