## J365 IntunePSApp 3.0.3

### Português

Reinstalar também onde o app já está instalado.

- **Nova opção *Reinstalar também onde o app já está instalado*** (desligada por padrão), no editor (etapa 5), na janela de lote e com `--reinstall` na linha de comando. Todo pacote de um app grava a mesma marca; por isso, onde um pacote anterior já tinha instalado o app na versão exigida, o Intune o considerava instalado e não executava o pacote novo — e *Remover qualquer versão instalada* nem chegava a ser aplicada. Ligada, a detecção passa a exigir a marca desta rodada: o Intune executa o pacote uma vez em cada dispositivo da atribuição, que remove as versões instaladas conforme a escolha e instala de novo, no modo silencioso ou com notificação. Gerar o pacote de novo mantém a rodada; desligar e ligar a opção começa outra.
- O teste de detecção no editor e o resumo da etapa 8 mostram a rodada; a explicação da marca do pacote (etapa 6) foi corrigida.

### English

Reinstall also where the app is already installed.

- **New option *Reinstall also where the app is already installed*** (off by default), in the editor (step 5), the batch window and `--reinstall` on the command line. Every package of an application writes the same mark, so where an earlier package had installed the application in the required version Intune reported it as installed and never ran the new package — *Remove any installed version* was never applied. On, the detection asks for the mark of this round: Intune runs the package once on every device of the assignment, which removes the installed versions as chosen and installs again, silently or with notification. Generating the package again keeps the round; turning the option off and on starts another.
- The detection test of the editor and the review of step 8 show the round; the explanation of the package mark (step 6) was corrected.

### Download

- `J365-IntunePSApp-3.0.3-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-3.0.3-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Já tem o J365 IntunePSApp? Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão. / Already using it? **Settings > General > Check now** downloads and installs this version.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
