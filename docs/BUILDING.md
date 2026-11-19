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
- **Release.** Pushing a tag `vX.Y.Z` makes GitHub Actions run the tests, build the executable and publish the release with checksums, the source package and the provenance attestation.

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

## Observações

- **Caminhos longos.** O Windows não inicia um executável cujo caminho completo passe de 260 caracteres. Se `dotnet run` ou `dotnet test` falhar com erro de nome de arquivo muito longo, clone o repositório em uma pasta mais curta.
- **Controle Inteligente de Aplicativos (Smart App Control).** Em computadores com essa proteção ativada, o Windows pode bloquear executáveis recém-compilados e sem assinatura. Ela não pode ser configurada por aplicativo; compile e teste em um computador (ou máquina virtual) em que esteja desativada.
- **Versão.** Enviar uma tag `vX.Y.Z` faz o GitHub Actions rodar os testes, compilar o executável e publicar a versão com checksums, pacote de código-fonte e atestado de procedência.
