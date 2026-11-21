## J365 IntunePSApp 2.5.1

### Português

Uma evolução do fluxo completo de empacotamento para Microsoft Intune: preparar, revisar, gerar, testar e publicar, com uma distinção clara entre o projeto editável e o pacote gerado.

- **Projeto e pacote sincronizados:** alterações nas configurações exigem uma nova geração antes de publicar ou testar. Salvar o projeto não transforma um pacote antigo em um pacote atualizado. A verificação continua ao reabrir os pacotes gerados nesta versão.
- **Trabalho mais prático:** Ctrl+S salva no arquivo atual; Salvar como e Ctrl+Shift+S criam outra cópia. O editor mostra o arquivo aberto, as alterações não salvas e a necessidade de gerar novamente.
- **Geração mais confiável:** configurações protegidas durante a geração, cancelamento com liberação de recursos, isolamento das pastas de saída e serialização das gerações dentro do processo.
- **Biblioteca mais fluida:** leitura de pacotes em segundo plano, cancelamento de buscas antigas, ordenação por data, nome ou atualizações disponíveis e uma mensagem com ação para limpar filtros quando não há resultados.
- **Interface mais clara:** nome e versão separados também nos cartões de pacotes, estados visíveis no editor e avisos contextualizados. A busca do catálogo descarta resultados de consultas anteriores enquanto você digita.
- **Validação antes da ação:** entradas numéricas inválidas não são substituídas silenciosamente por valores padrão ao salvar ou gerar.
- **Qualidade contínua:** testes de regressão de concorrência e revisão de pacotes, além de capturas e verificações automatizadas do editor nos temas claro e escuro, incluindo janela compacta.

Inclui as melhorias da 2.5.0: nome separado da versão no Intune, campos de desenvolvedor/proprietário/privacidade, validação de URLs e salvamento atômico.

Projetos anteriores continuam legíveis. A identificação da revisão acompanha novos pacotes; pacotes antigos sem essa identificação permanecem compatíveis. A identificação compara configurações: não é assinatura digital nem monitoramento dos arquivos de origem. Aplicativos já publicados no tenant só mudam quando você escolhe atualizá-los explicitamente. A validação em tenant real continua pendente.

### English

This update improves the complete packaging workflow. The editor distinguishes saved projects from current build output and prevents publishing or testing a stale build after settings change. New build revision records preserve that check when reopening a package. Ctrl+S saves to the current file; Save As and Ctrl+Shift+S create a copy. Project state and version badges improve clarity.

Build directories are reserved independently, builds in the same process are serialized, and editing is paused during generation. Package history loads asynchronously with cancellation, sorting and actionable empty results. Catalog search discards superseded results. Invalid numeric input is rejected before saving or generating. CI now renders light/dark interfaces and checks editing, saving and reopening behavior.

Earlier projects remain compatible. Legacy packages without a revision record remain readable. Revision records compare settings; they are not digital signatures or source-file monitoring. Live-tenant validation remains pending.

### Download

- `J365-IntunePSApp-2.5.1-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-2.5.1-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
