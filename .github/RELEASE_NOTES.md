## J365 IntunePSApp 2.6.0

### Português

Gere e publique vários aplicativos de uma vez, com instalações realmente silenciosas.

- **Deploy em lote:** marque vários aplicativos no catálogo e gere todos com as mesmas configurações (nome da empresa, proprietário, modo de exibição, notificações do Intune e adiamento). Logo, cores, estilo, idioma e autor vêm do padrão da empresa. Cada instalador é baixado do link oficial, conferido pelo SHA256 e empacotado como no editor, com o andamento de cada aplicativo na tela. Quando um pacote precisaria de uma decisão sua (um `.exe` sem parâmetros silenciosos conhecidos, ou um nome que também corresponde a outros aplicativos), ele é pulado com o motivo, nunca adivinhado. Opcionalmente, cada pacote é **publicado no Intune** assim que é gerado: atualiza o app com o mesmo nome (ou cria), e os apps criados recebem a mesma atribuição (disponível ou obrigatória, para um grupo, todos os usuários ou dispositivos), com confirmação do tenant antes de começar. Itens pulados abrem no editor com o instalador já baixado, e o relatório do lote pode ser copiado como tabela. Também por linha de comando: `--batch id1,id2 --mode Silent --publish --assign required --assign-to Group --group <id>`.
- **Silencioso sem notificar o usuário:** a publicação criava a atribuição sempre com todas as notificações do Intune. Agora o modo **Silencioso** oculta as notificações (`hideAll`), não reinicia o computador e não bloqueia os aplicativos durante a instalação (o bloqueio mostraria um aviso). As notificações podem ser escolhidas por pacote e como padrão da empresa.
- **Menos campos repetidos:** a opção de desinstalar versões anteriores fica só na etapa Desinstalação, e o nome e a versão da detecção acompanham as etapas anteriores até serem alterados.
- **Ícones corretos:** ícones genéricos de instaladores (Inno Setup, WiX Burn e os ícones de fábrica do NSIS) não vão mais para o Portal da Empresa.
- **Correções:** switches silenciosos que a análise encontra no próprio instalador (NSIS, InstallShield, Advanced Installer, Squirrel) não são mais descartados quando o banco de dados não traz switches; instaladores Inno Setup por usuário recebem `/CURRENTUSER` (432 aplicativos); a detecção do .NET Desktop Runtime 10 volta a funcionar.

Projetos anteriores continuam compatíveis. Atribuições já existentes no Intune mantêm as notificações configuradas nelas; a escolha vale para as atribuições criadas pela publicação. Teste em um grupo piloto antes de ampliar.

### English

Generate and publish several applications at once, with truly silent installations.

- **Batch deploy:** tick several applications in the catalog and generate them all with the same settings (company name, owner, display mode, Intune notifications and deferral). Logo, colors, style, language and author come from the company defaults. Each installer is downloaded from its official link, checked against its SHA256 and packaged as in the editor, with per-application progress. A package that would need your decision (an `.exe` without known silent switches, or a name that also matches other applications) is skipped with the reason, never guessed. Optionally, each package is **published to Intune** as soon as it is generated: the app with the same name is updated (or created), and the apps created get the same assignment (available or required, for a group, all users or all devices), with the tenant confirmed before it starts. Skipped items open in the editor with the installer already downloaded, and the batch report can be copied as a table. Command line: `--batch id1,id2 --mode Silent --publish --assign required --assign-to Group --group <id>`.
- **Silent without notifying the user:** the publication always created the assignment with every Intune toast. **Silent** mode now hides them (`hideAll`), does not restart the device and does not block the applications during the installation (blocking shows a notice). Notifications can be chosen per package and as a company default.
- **Fewer repeated fields:** the uninstall-earlier-versions switch lives on the Uninstall step only; the detection name and version follow the earlier steps until changed.
- **Right icons:** generic installer icons (Inno Setup, WiX Burn and the NSIS stock icons) no longer reach the Company Portal.
- **Fixes:** silent switches the analysis finds in the installer itself (NSIS, InstallShield, Advanced Installer, Squirrel) are no longer discarded when the database has none; per-user Inno Setup installers get `/CURRENTUSER` (432 applications); the .NET Desktop Runtime 10 detection works again.

Earlier projects remain compatible. Assignments already in Intune keep their own notification setting; the choice applies to assignments created by the publication. Test with a pilot group before widening.

### Download

- `J365-IntunePSApp-2.6.0-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-2.6.0-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
