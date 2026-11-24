## J365 IntunePSApp 3.0.2

### Português

Porcentagem real enquanto o arquivo .intunewin é criado.

- **A barra segue o trabalho de verdade.** Com o IntuneWinAppUtil.exe da Microsoft, a barra acompanhava só os arquivos gravados pela ferramenta: parava enquanto ela calculava os dois hashes (cerca de 40% do tempo) e enquanto gravava o arquivo final num buffer, e depois pulava. Agora o app acompanha as seis passagens que a ferramenta faz sobre o pacote — pela linha que ela imprime no início de cada uma e pelos bytes que o processo lê —, cada passagem com o seu peso no tempo. No empacotador interno, a compactação ficava com 70% da barra mesmo levando cerca de um terço do tempo; agora cada passagem pesa o tempo que leva.
- **Janela de lote**: cada aplicativo mostra a porcentagem da etapa em que está (download, geração, envio ao Intune) ao lado da barra e o tempo restante. Análise, entrada na conta e outras etapas sem porcentagem própria não mostram mais 0%.
- A barra de status dizia "Gerando 1 pacotes em lote".

### English

A real percentage while the .intunewin file is created.

- **The bar follows the actual work.** With Microsoft's IntuneWinAppUtil.exe the bar followed only the files the tool writes: it stood still while the tool computed its two hashes (about 40% of its time) and while it wrote the outer file to a buffer, then jumped. The application now follows the six passes the tool makes over the package — from the line it prints as each one begins and the bytes the process reads — each pass with its share of the time. The built-in packager gave the zip 70% of the bar although it takes about a third of the time; each pass now weighs the time it takes.
- **Batch window**: each application shows the percentage of the step it is in (download, build, upload to Intune) beside the bar, and the time left. Analysing, signing in and other steps without a percentage of their own no longer show 0%.
- The status bar said "Generating 1 packages" in Portuguese for one application.

### Download

- `J365-IntunePSApp-3.0.2-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-3.0.2-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Já tem o J365 IntunePSApp? Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão. / Already using it? **Settings > General > Check now** downloads and installs this version.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
