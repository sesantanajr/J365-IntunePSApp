## J365 IntunePSApp 2.8.0

### Português

Uma versão nova pode ir para os dispositivos grupo a grupo, e o status de instalação de um app publicado aparece no próprio aplicativo.

- **Substituição (supersedence):** publique a versão nova como um app novo do Intune que substitui o anterior, em vez de trocar o conteúdo dele de uma vez.
  - O Intune leva para a versão nova só os dispositivos onde o app novo estiver atribuído: primeiro um grupo piloto, depois mais grupos. O app anterior continua lá para um rollback.
  - *Atualizar* (padrão) instala por cima da versão anterior, e o pacote remove as versões anteriores sozinho. *Desinstalar antes* faz o Intune remover o app anterior primeiro.
  - Na janela de publicação (a lista de apps com o mesmo nome oferece *App novo que substitui*), no lote e na linha de comando (`--if-exists Supersede`). O `Publish-ToIntune.ps1` ganhou `-IfExists Supersede` e `-SupersedenceType`.
- **Status no Intune:** no cartão de resultado de um pacote publicado e em *Meus pacotes*.
  - Quantos dispositivos têm o app instalado, com falha, pendente, não instalado ou não aplicável.
  - As atribuições e a substituição do app.
  - Cada dispositivo, os com problema primeiro, com o código do erro explicado (`0x87D1041C`: não detectado depois de instalar).
  - Lido com a permissão que a conexão já tem; nada é alterado no tenant. *Copiar relatório* gera uma tabela para um chamado ou uma planilha.
- **A publicação testada de ponta a ponta:** o script de publicação roda nos testes contra um Microsoft Graph simulado, no Windows PowerShell 5.1.
- **Correções:** o resumo do lote concorda com qualquer número ("pulados: 1"), e uma escolha desconhecida em `--if-exists` é recusada em vez de virar uma atualização.

### English

A new version can reach the devices group by group, and the install status of a published app shows in the application itself.

- **Supersedence:** publish the new version as a new Intune app that supersedes the earlier one, instead of replacing the content of that app at once.
  - Intune moves to the new version only the devices where the new app is assigned: a pilot group first, then wider. The earlier app stays for a rollback.
  - *Update* (default) installs over the earlier version, and the package removes earlier versions itself. *Uninstall first* makes Intune remove the earlier app before.
  - In the publishing dialog (the list of apps of the same name offers *New app that supersedes*), the batch window and the command line (`--if-exists Supersede`). `Publish-ToIntune.ps1` has `-IfExists Supersede` and `-SupersedenceType`.
- **Status in Intune:** on the result card of a published package and in *My packages*.
  - How many devices have the app installed, failed, pending, not installed or not applicable.
  - The assignments and the supersedence of the app.
  - Every device, problems first, with its error code explained (`0x87D1041C`: not detected after installing).
  - Read with the permission the connection already has; nothing is changed in the tenant. *Copy report* gives a table for a ticket or a spreadsheet.
- **The publication tested end to end:** the publishing script runs in the tests against a local stand-in of Microsoft Graph, in Windows PowerShell 5.1.
- **Fixes:** the Portuguese summary of a batch agrees with any number ("pulados: 1"), and an unknown choice of `--if-exists` is refused instead of becoming an update.

See the [CHANGELOG](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md) and the user guide ([English](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/USER-GUIDE.md), [Português](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/USER-GUIDE.pt-BR.md)).

### Download

- `J365-IntunePSApp-2.8.0-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-2.8.0-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

**Já tem o J365 IntunePSApp?**
- Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão.
- Uma cópia até a 2.6.0 que insista em "versão mais recente" precisa ser atualizada uma vez manualmente: baixe o zip acima e execute o `J365.IntunePSApp.exe`. Em Configurações > Geral, use **Atualizar a cópia instalada**.

**Already using it?**
- **Settings > General > Check now** downloads and installs this version.
- A copy up to 2.6.0 that keeps saying "up to date" needs one manual update: download the zip above and run `J365.IntunePSApp.exe`. In Settings > General, use **Update the installed copy**.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
