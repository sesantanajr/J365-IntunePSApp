## J365 IntunePSApp 3.0.6

### Português

Instalações que colidem são tentadas de novo, e dois apps do mesmo produto não disputam mais o dispositivo.

- **Windows Installer ocupado vira "tentar de novo".** O instalador `.exe` agora espera o Windows Installer (`-WaitForMsiExec`), e um instalador que falha enquanto outra instalação roda termina com **1618**, que o Intune repete em 5 minutos, em vez de mostrar falha. Foi o que aconteceu com o Adobe Acrobat Reader: 0x80070002 (o instalador terminou com 2) enquanto outro app do mesmo produto instalava.
- **Remoção que falhou tem código próprio.** A instalação que para porque uma versão anterior continuou instalada termina com **69001** (antes, o genérico 60001 = 0x8007EA61), e a desinstalação que deixa o aplicativo, com **69002**. O *Status no Intune* explica 60001, 69001, 69002 e 0x80070002.
- **Dois apps do mesmo produto.** A marca do pacote vai nas observações do app no Intune. A publicação mostra os apps do mesmo produto com outro nome e pede confirmação antes de criar um segundo; no lote e na linha de comando, o app do mesmo produto é atualizado ou substituído conforme a escolha.
- Capturas da documentação atualizadas.

Pacotes gerados antes da 3.0.6 não mudam: gere e publique de novo para receber as correções.

### English

Installations that collide are tried again, and two apps of the same product no longer compete on a device.

- **A busy Windows Installer becomes a retry.** An `.exe` setup now waits for Windows Installer (`-WaitForMsiExec`), and a setup that fails while another installation runs ends with **1618**, which Intune retries in 5 minutes, instead of a failure. This is what happened with Adobe Acrobat Reader: 0x80070002 (the setup ended with 2) while another app of the same product was installing.
- **A failed removal has its own code.** The installation that stops because an earlier version is still installed ends with **69001** (it was the generic 60001 = 0x8007EA61), and an uninstallation that leaves the application with **69002**. *Status in Intune* explains 60001, 69001, 69002 and 0x80070002.
- **Two apps of the same product.** The package mark goes into the notes of the app in Intune. The publication lists the apps of the same product under another name and asks before creating a second one; in a batch and on the command line, the app of the same product is updated or superseded as chosen.
- Documentation screenshots refreshed.

Packages made before 3.0.6 do not change: generate and publish them again to get the fixes.

### Download

- `J365-IntunePSApp-3.0.6-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-3.0.6-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Já tem o J365 IntunePSApp? Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão. / Already using it? **Settings > General > Check now** downloads and installs this version.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
