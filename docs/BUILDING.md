# Building from source

*Português: [abaixo](#compilando-a-partir-do-código-fonte).*

## Requirements

- Windows 10 or 11 (the application is WPF, so it builds and runs on Windows only).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Windows PowerShell 5.1 (part of Windows) for the tests that parse the generated scripts.

Get the source by cloning the repository or by downloading `J365-IntunePSApp-<version>-source.zip` from a release.

## Commands

```powershell
# Run from source
dotnet run --project src/IntunePSApp

# Unit tests
dotnet test tests/IntunePSApp.Tests

# Portable release build: one self-contained executable in .\publish
dotnet publish src/IntunePSApp -c Release -r win-x64 -o publish

# Application database (the same command the daily workflow runs; about one minute)
dotnet run --project tools/CatalogBuilder -c Release -- --out out --curated catalog/curated.json
```

To use a database you built yourself: *Settings > Application database > Import file* and pick `out/catalog.json.gz`, or host it and change the *Update address*.

## Repository layout

| Path | Content |
|---|---|
| `src/IntunePSApp` | The application (WPF, .NET 10). `Core` has no UI code; `Views` are the pages; `Resources` holds the two language files and the scripts copied to each package. |
| `tests/IntunePSApp.Tests` | xUnit tests. |
| `tools/CatalogBuilder` | Console tool that builds the application database from winget-pkgs. |
| `catalog/curated.json` | Curated knowledge merged into the database (and embedded in the executable). |
| `.github/workflows` | `build.yml` (tests, release, source package, attestation) and `catalog.yml` (daily database). |
| `docs` | Documentation, cover and screenshots. |

## Notes

- **Long paths.** Windows cannot start an executable whose full path is longer than 260 characters. If `dotnet run` or `dotnet test` fails with a "file name too long" error, clone the repository into a shorter folder.
- **Smart App Control.** On computers with Smart App Control turned on, Windows may block freshly built, unsigned executables. That protection cannot be configured per application; build and test on a computer (or virtual machine) where it is off.
- **Development switches.** `--snapshot <folder>` renders every page to PNG files (it is how the screenshots of this repository are made); see `SnapshotRunner.cs`. `--snapshot-size 900x580` renders them at another window size (the window cannot be larger than the screen), and steps taller than the window get a second file ending in `-end`. The data folder can be redirected with the environment variable `J365_INTUNEPSAPP_DATA`: point it to a folder with a neutral path, because the output folder appears in the screenshots.
- **Release.** Pushing to `main` with a new `<Version>` in `src/IntunePSApp/IntunePSApp.csproj` (and the text of `.github/RELEASE_NOTES.md`), or pushing a tag `vX.Y.Z`, makes GitHub Actions run the tests and the interface checks, build the executable and publish the release with checksums, the source package and the provenance attestation. A version that already has a release is left unchanged; changes to documentation only (`*.md`, `docs/`) do not start a build. The applications in use offer the new release in *Settings > General > Check now*.
- **Live checks.** The tests use local mocks instead of online services. `J365_LIVE_TESTS=1` adds the few checks that ask the real GitHub (the latest release found through its files, without the API; the latest PSAppDeployToolkit).

## Documentation captures

Every build renders the current application with an isolated demonstration project in English and Portuguese, light and dark themes, including the batch window with sample applications in every state. It also exercises edit/save/reopen and empty-filter recovery; a missing capture fails the build. The `interface-captures` artifact contains PNGs and state-check results. These captures do not perform a tenant deployment. The screenshots of the README and of the [user guide](USER-GUIDE.md) come from [`tools/Render-Screenshots.ps1`](../tools/Render-Screenshots.ps1), which renders the same pages with a demonstration company, a real package and a sample connection; how to run it and the filename map are in [screenshots/README.md](screenshots/README.md).

---

# Compilando a partir do código-fonte

## Requisitos

- Windows 10 ou 11 (o aplicativo é WPF, então compila e roda somente no Windows).
- [SDK do .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).
- Windows PowerShell 5.1 (já vem no Windows) para os testes que analisam os scripts gerados.

Obtenha o código clonando o repositório ou baixando o `J365-IntunePSApp-<versão>-source.zip` de uma versão.

## Comandos

```powershell
# Executar a partir do código
dotnet run --project src/IntunePSApp

# Testes unitários
dotnet test tests/IntunePSApp.Tests

# Versão portátil: um executável único e autossuficiente em .\publish
dotnet publish src/IntunePSApp -c Release -r win-x64 -o publish

# Banco de dados de aplicativos (o mesmo comando do workflow diário; cerca de um minuto)
dotnet run --project tools/CatalogBuilder -c Release -- --out out --curated catalog/curated.json
```

Para usar um banco de dados gerado por você: *Configurações > Banco de dados de aplicativos > Importar arquivo* e escolha `out/catalog.json.gz`, ou hospede o arquivo e altere o *Endereço de atualização*.

## Estrutura do repositório

| Caminho | Conteúdo |
|---|---|
| `src/IntunePSApp` | O aplicativo (WPF, .NET 10). `Core` não tem código de interface; `Views` são as páginas; `Resources` guarda os dois arquivos de idioma e os scripts copiados para cada pacote. |
| `tests/IntunePSApp.Tests` | Testes xUnit. |
| `tools/CatalogBuilder` | Ferramenta de console que gera o banco de dados a partir do winget-pkgs. |
| `catalog/curated.json` | Curadoria mesclada ao banco de dados (e embutida no executável). |
| `.github/workflows` | `build.yml` (testes, release, pacote de código-fonte, atestado) e `catalog.yml` (banco de dados diário). |
| `docs` | Documentação, capa e capturas de tela. |

## Observações

- **Caminhos longos.** O Windows não inicia um executável cujo caminho completo passe de 260 caracteres. Se `dotnet run` ou `dotnet test` falhar com erro de nome de arquivo muito longo, clone o repositório em uma pasta mais curta.
- **Controle Inteligente de Aplicativos (Smart App Control).** Em computadores com essa proteção ativada, o Windows pode bloquear executáveis recém-compilados e sem assinatura. Ela não pode ser configurada por aplicativo; compile e teste em um computador (ou máquina virtual) em que esteja desativada.
- **Opções de desenvolvimento.** `--snapshot <pasta>` renderiza todas as páginas em arquivos PNG (é assim que as capturas deste repositório são feitas); veja o `SnapshotRunner.cs`. `--snapshot-size 900x580` as renderiza em outro tamanho de janela (a janela não pode ser maior que a tela), e etapas mais altas que a janela ganham um segundo arquivo terminado em `-end`. A pasta de dados pode ser redirecionada com a variável de ambiente `J365_INTUNEPSAPP_DATA`: aponte para uma pasta de caminho neutro, porque a pasta de saída aparece nas capturas.
- **Versão.** Um push no `main` com nova `<Version>` em `src/IntunePSApp/IntunePSApp.csproj` (e o texto de `.github/RELEASE_NOTES.md`), ou uma tag `vX.Y.Z`, faz o GitHub Actions rodar os testes e as verificações de interface, compilar o executável e publicar a versão com checksums, pacote de código-fonte e atestado de procedência. Uma versão que já tem release não é alterada; mudanças só de documentação (`*.md`, `docs/`) não iniciam compilação. Os aplicativos em uso oferecem a nova versão em *Configurações > Geral > Verificar agora*.
- **Verificações ao vivo.** Os testes usam simulações locais no lugar dos serviços online. `J365_LIVE_TESTS=1` inclui as poucas verificações que consultam o GitHub de verdade (a última versão encontrada pelos arquivos da release, sem a API; o último PSAppDeployToolkit).

## Capturas da documentação

Cada compilação renderiza o aplicativo atual com um projeto demonstrativo isolado, em inglês e português, nos temas claro e escuro, incluindo a janela de lote com aplicativos de exemplo em todos os estados. Ela também exercita editar/salvar/reabrir e a recuperação de um filtro sem resultados; uma captura que falta faz a compilação falhar. O artefato `interface-captures` traz os PNGs e o resultado das verificações de estado. Essas capturas não fazem nenhuma implantação em tenant. As imagens do README e do [guia de uso](USER-GUIDE.pt-BR.md) vêm do [`tools/Render-Screenshots.ps1`](../tools/Render-Screenshots.ps1), que renderiza as mesmas telas com uma empresa demonstrativa, um pacote real e uma conexão de exemplo; como executá-lo e o mapa de nomes de arquivo estão em [screenshots/README.md](screenshots/README.md).
