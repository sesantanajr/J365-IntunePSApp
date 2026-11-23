## J365 IntunePSApp 2.6.0

### Português

Gere vários pacotes de uma vez e publique instalações realmente silenciosas.

- **Pacotes em lote:** marque vários aplicativos no catálogo e gere todos com as mesmas configurações (nome da empresa, proprietário, modo de exibição, notificações do Intune e adiamento). Logo, cores, estilo, idioma e autor vêm do padrão da empresa. Cada instalador é baixado do link oficial, conferido pelo SHA256 e empacotado como no editor, com o andamento de cada aplicativo na tela. Quando um pacote precisaria de uma decisão sua (um `.exe` sem parâmetros silenciosos conhecidos, ou um nome que também corresponde a outros aplicativos), ele é pulado com o motivo, nunca adivinhado. Também por linha de comando: `--batch id1,id2 --mode Silent`.
- **Silencioso sem notificar o usuário:** a publicação criava a atribuição sempre com todas as notificações do Intune. Agora o modo **Silencioso** oculta as notificações (`hideAll`) e não reinicia o computador. As notificações podem ser escolhidas por pacote e como padrão da empresa.
- **Menos campos repetidos:** a opção de desinstalar versões anteriores fica só na etapa Desinstalação, e o nome e a versão da detecção acompanham as etapas anteriores até serem alterados.
- **Ícones corretos:** o ícone genérico de instaladores (computador com CD) não vai mais para o Portal da Empresa.
- **Correções:** instaladores Inno Setup por usuário recebem `/CURRENTUSER` (432 aplicativos do banco de dados) e a detecção do .NET Desktop Runtime 10 volta a funcionar.

Projetos anteriores continuam compatíveis. Atribuições já existentes no Intune mantêm as notificações configuradas nelas; a escolha vale para as atribuições criadas pela publicação. Teste em um grupo piloto antes de ampliar.

### English

Generate several packages at once and publish truly silent installations.

- **Batch packaging:** tick several applications in the catalog and generate them all with the same settings (company name, owner, display mode, Intune notifications and deferral). Logo, colors, style, language and author come from the company defaults. Each installer is downloaded from its official link, checked against its SHA256 and packaged as in the editor, with per-application progress. A package that would need your decision (an `.exe` without known silent switches, or a name that also matches other applications) is skipped with the reason, never guessed. Command line: `--batch id1,id2 --mode Silent`.
- **Silent without notifying the user:** the publication always created the assignment with every Intune toast. **Silent** mode now hides them (`hideAll`) and does not restart the device. Notifications can be chosen per package and as a company default.
- **Fewer repeated fields:** the uninstall-earlier-versions switch lives on the Uninstall step only; the detection name and version follow the earlier steps until changed.
- **Right icons:** the generic setup icon (a computer with a CD) no longer reaches the Company Portal.
- **Fixes:** per-user Inno Setup installers get `/CURRENTUSER` (432 applications of the database) and the .NET Desktop Runtime 10 detection works again.

Earlier projects remain compatible. Assignments already in Intune keep their own notification setting; the choice applies to assignments created by the publication. Test with a pilot group before widening.

### Download

- `J365-IntunePSApp-2.6.0-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-2.6.0-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
