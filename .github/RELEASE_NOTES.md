## J365 IntunePSApp 2.6.0

### Português

Deploy de vários aplicativos de uma vez, apps da Microsoft Store e instalações realmente silenciosas.

- **Apps da Microsoft Store:** o catálogo também pesquisa a Microsoft Store ao vivo, pelo nome ou pelo ID (`9PM860492SZD` encontra o Microsoft PC Manager). Esses apps são publicados no Intune como **Microsoft Store app (new)**: o Intune instala pela Store e mantém atualizado, sem pacote e sem regra de detecção, com a descrição em português e o logo oficial como ícone no Portal da Empresa. Funciona sozinho, em lote e por linha de comando (`--batch 9PM860492SZD --publish`).
- **Deploy em lote:** marque vários aplicativos e gere todos com as mesmas configurações (empresa, proprietário, modo de exibição, notificações, adiamento); opcionalmente, cada pacote é publicado no Intune com a mesma atribuição, com confirmação do tenant. Itens pulados abrem no editor e o relatório do lote pode ser copiado.
- **Silencioso sem notificar o usuário:** sem janelas, sem bloqueio de aplicativos, notificações do Intune ocultas (`hideAll`) e sem reinicialização pelo Intune.
- **Ícones e parâmetros corretos:** ícones genéricos de instaladores não vão mais para o Portal da Empresa; switches detectados no próprio instalador não são mais descartados; Inno Setup por usuário recebe `/CURRENTUSER`; a detecção do .NET Desktop Runtime 10 foi corrigida.
- **Atualização confiável:** "Verificar agora" só diz que está atualizado quando consultou o GitHub, mostra a versão e usa a página de versões se a API do GitHub estiver bloqueada.
- **Nada parece faltar:** a busca informa quantos apps o filtro "Somente compatíveis" ocultou (MSIX, ZIP, portáteis) e oferece procurar na Microsoft Store.

### English

Deploy several applications at once, apps of the Microsoft Store and truly silent installations.

- **Apps of the Microsoft Store:** the catalog also searches the Microsoft Store live, by name or product id (`9PM860492SZD` finds Microsoft PC Manager). They are published to Intune as **Microsoft Store app (new)**: Intune installs them from the Store and keeps them current, with no package and no detection rule, with the description of the Store and its official logo as the Company Portal icon. Alone, in a batch and from the command line (`--batch 9PM860492SZD --publish`).
- **Batch deploy:** tick several applications and generate them with the same settings (company, owner, display mode, notifications, deferral); optionally each package is published to Intune with the same assignment, the tenant confirmed first. Skipped items open in the editor and the batch report can be copied.
- **Silent without notifying the user:** no dialogs, no blocked applications, Intune toasts hidden (`hideAll`) and no restart by Intune.
- **Right icons and switches:** generic installer icons no longer reach the Company Portal; switches found in the installer itself are no longer discarded; per-user Inno Setup gets `/CURRENTUSER`; the .NET Desktop Runtime 10 detection is fixed.
- **Reliable updates:** "Check now" only says up to date when it reached GitHub, names the version, and uses the releases page when the GitHub API is blocked.
- **Nothing seems missing:** a search says how many apps "Only supported" hid (MSIX, ZIP, portable) and offers to search the Microsoft Store.

Earlier projects remain compatible. Assignments already in Intune keep their own notification setting. Test with a pilot group before widening.

### Download

- `J365-IntunePSApp-2.6.0-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-2.6.0-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
