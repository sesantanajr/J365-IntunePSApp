## J365 IntunePSApp 3.0.1

### Português

Reinstalação limpa: remover qualquer versão instalada e instalar de novo.

- **Versões já instaladas no dispositivo** é agora uma escolha única, igual no editor (etapa 5), nos padrões da empresa e na janela de lote (e `--existing` na linha de comando):
  - **Remover qualquer versão instalada (mais antiga, a mesma ou mais nova) e instalar esta** — reinstalação limpa, no modo silencioso ou com notificação. Até agora uma versão mais nova era sempre mantida, e o lote e os padrões não tinham como mudar isso.
  - **Remover versões mais antigas e a mesma; manter uma versão mais nova** — o comportamento da 3.0.0, ainda o padrão.
  - **Não remover: instalar por cima do que existir.**
- **Componentes compartilhados nunca são removidos**: runtimes do Visual C++ e do .NET, WebView2 e Edge. Um app que o banco de dados só recomenda não remover (navegadores que se atualizam sozinhos, OneDrive, Adobe Acrobat 64 bits, cujo nome é também o do Acrobat Pro) é removido quando você escolhe *remover qualquer versão*, com aviso.
- **Botão *Gerar em lote* do catálogo**: a barra fica em uma linha, com a contagem, os nomes resumidos e os botões alinhados; ele não estica mais com muitos aplicativos marcados.
- **Contagens no singular**: "Gerar 1 pacote", "1 aplicativo selecionado".

### English

Clean reinstall: remove any installed version and install again.

- **Versions already installed on the device** is now one choice, the same in the editor (step 5), the company defaults and the batch window (and `--existing` on the command line):
  - **Remove any installed version (older, the same or newer) and install this one** — a clean reinstall, silent or with notification. Until now a newer version was always kept, and the batch and the defaults could not change that.
  - **Remove older versions and the same one; keep a newer version** — the behaviour of 3.0.0, still the default.
  - **Do not remove: install over what is there.**
- **Shared components are never removed**: Visual C++ and .NET runtimes, WebView2 and Edge. An app the database only advises not to remove (self-updating browsers, OneDrive, Adobe Acrobat 64-bit, whose name is also Acrobat Pro's) is removed when *remove any version* is chosen, with a note.
- **The *Batch package* button of the catalog**: the bar keeps one line, with the count, the names trimmed and the buttons aligned; it no longer stretches with many applications ticked.
- **Counts of one in the singular.**

### Download

- `J365-IntunePSApp-3.0.1-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-3.0.1-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

Já tem o J365 IntunePSApp? Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão. / Already using it? **Settings > General > Check now** downloads and installs this version.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
