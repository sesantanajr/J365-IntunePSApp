## J365 IntunePSApp 2.5.0

### Português

- **Nome limpo no Intune:** Amazon WorkSpaces; versão 5.37.0.3866 no campo próprio. A prévia e a janela de instalação também mostram apenas o nome.
- **Metadados completos:** Desenvolvedor, Proprietário e URL de privacidade editáveis. Desenvolvedor usa o editor como padrão; proprietário usa a empresa. O catálogo importa a URL de privacidade quando o manifesto a informa.
- **Interface refinada:** versão em um indicador separado, contraste melhor no tema escuro, campos consistentes e foco visível. Revisão antes de gerar com nome, versão e metadados separados.
- **Praticidade:** Ctrl+S para salvar, Ctrl+O para abrir e Ctrl+N para novo projeto, dentro do editor.
- **Estabilidade:** validação de URLs antes de gerar e correção de conflitos entre salvamentos simultâneos.

Projetos antigos continuam legíveis. A atualização do programa **não renomeia automaticamente os aplicativos já publicados no tenant**. Gere novamente e escolha explicitamente o aplicativo existente na publicação, ou altere seu nome no Intune.

A URL de privacidade do catálogo depende da atualização da base e da existência do campo no manifesto. Não foi feita validação em tenant real nesta revisão. A inspeção visual manual ficou pendente por bloqueio do ambiente de execução.

### English

Intune app names and versions are now separate. New editable developer, owner and privacy metadata flow through projects, generated packages and publishing. The editor has a separate version badge, improved dark contrast, consistent input surfaces and project keyboard shortcuts. URL validation and concurrent atomic-save handling improve reliability. Existing projects remain compatible.

Existing tenant apps are not automatically renamed. Regenerate the package and explicitly select the existing app when publishing. Privacy URLs depend on refreshed catalog data. Live-tenant and manual visual validation were not performed in this revision.

### Download

- `J365-IntunePSApp-2.5.0-win-x64.zip`: portable Windows x64 application; extract and run `J365.IntunePSApp.exe`.
- `J365-IntunePSApp-2.5.0-source.zip`: source code for this release.
- `SHA256SUMS.txt`: checksums.

Based on PSAppDeployToolkit. Created by Sérgio Sant'Ana Junior — Jornada 365.
