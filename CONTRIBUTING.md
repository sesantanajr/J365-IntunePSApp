# Contributing

*Português: [abaixo](#como-contribuir).*

Thank you for helping J365 IntunePSApp! Code, database corrections, tests with real installers, translations, documentation and ideas are all welcome. Issues and pull requests can be written in English or Portuguese.

## The quickest way to help: curated application knowledge

The database comes from winget and already has silent switches and URLs. [`catalog/curated.json`](catalog/curated.json) adds what a good enterprise package still needs:

```json
"Notepad++.Notepad++": {
  "processes": ["notepad++"],
  "namePattern": "Notepad++*",
  "uninstallArgs": "/S",
  "notesEn": "Note shown in the catalog (English).",
  "notes": "Observação exibida no catálogo (português)."
}
```

| Field | Description |
|---|---|
| `processes` | Executables that must be closed (without `.exe`) |
| `namePattern` | Name in *Installed apps* (wildcards allowed): used to uninstall, remove previous versions and detect |
| `uninstallArgs` | Silent switches of the `.exe` uninstaller |
| `installArgs` | Replaces the winget install switches when needed |
| `detectionFile` | Recommended file for detection (for example apps that update themselves) |
| `checkVersion` | `false` to detect presence only |
| `removePrevious` | `false` when removing the installed versions is not advised (the package removes them only with *Remove any installed version*) |
| `neverRemove` | `true` for shared components and parts of Windows (Edge, WebView2, VC++ and .NET runtimes): never removed, whatever the choice |
| `notesEn` / `notes` | Important warnings for whoever packages the app, in English / Portuguese |

The key is the **winget ID** (`winget search <name>`). Before opening the pull request, test the generated package: *Test in Sandbox*, or on a test computer with `Invoke-AppDeployToolkit.exe -DeploymentType Install` and `-DeploymentType Uninstall`.

## Code

1. Read [docs/BUILDING.md](docs/BUILDING.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
2. Keep `Core` free of UI code. Code, comments and generated scripts are in English.
3. Every text shown to the user goes into **both** `Resources/Strings.en.json` and `Strings.pt-BR.json` (the tests fail otherwise). Values saved in projects must be language-neutral codes.
4. Anything written into a generated PowerShell script must go through `ScriptGenerator.Lit` and be covered by a test that parses the result.
5. Run `dotnet test` before opening the pull request and describe how you tested (application, installer type, System or User context). The tests never touch the network; `J365_LIVE_TESTS=1` adds the few checks that ask the real GitHub (latest release, latest PSAppDeployToolkit).
6. Interface changes: render every page off-screen with `J365.IntunePSApp.exe --snapshot <folder>` (see [docs/BUILDING.md](docs/BUILDING.md)) in both themes and languages, and look at the PNG files before opening the pull request.

## Reporting problems

Use the issue templates. Attach the application log (Settings > General > **Logs**, or `%LOCALAPPDATA%\J365\IntunePSApp\logs\app-YYYYMMDD.log`; a portable copy keeps it in `IntunePSApp-Data\logs` next to the executable). For a package that failed on a device, also attach `build.log` and the PSAppDeployToolkit log (`C:\ProgramData\Microsoft\IntuneManagementExtension\Logs` or `C:\Windows\Logs\Software`). Remove sensitive data first. Security problems: see [SECURITY.md](SECURITY.md).

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).

---

# Como contribuir

Obrigado por ajudar o J365 IntunePSApp! Código, correções no banco de dados, testes com instaladores reais, traduções, documentação e ideias são bem-vindos. Issues e pull requests podem ser escritos em português ou inglês.

## A forma mais rápida de ajudar: curadoria de aplicativos

O banco de dados vem do winget e já traz os parâmetros silenciosos e os endereços. O [`catalog/curated.json`](catalog/curated.json) acrescenta o que falta para um bom pacote corporativo: processos a fechar (`processes`), nome em *Aplicativos instalados* (`namePattern`), parâmetros do desinstalador (`uninstallArgs`), parâmetros de instalação (`installArgs`), arquivo para detecção (`detectionFile`), `checkVersion`, `removePrevious`, `neverRemove` (componentes compartilhados que nunca são removidos) e observações em português (`notes`) e inglês (`notesEn`). Veja o exemplo e a tabela na seção em inglês acima.

A chave é o **ID do winget** (`winget search <nome>`). Antes de abrir o pull request, teste o pacote gerado: *Testar no Sandbox*, ou em um computador de teste com `Invoke-AppDeployToolkit.exe -DeploymentType Install` e `-DeploymentType Uninstall`.

## Código

1. Leia [docs/BUILDING.md](docs/BUILDING.md) e [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
2. Mantenha o `Core` sem código de interface. Código, comentários e scripts gerados ficam em inglês.
3. Todo texto exibido ao usuário entra nos **dois** arquivos, `Resources/Strings.en.json` e `Strings.pt-BR.json` (os testes falham se faltar). Valores salvos nos projetos devem ser códigos independentes de idioma.
4. Tudo o que for escrito em um script PowerShell gerado deve passar por `ScriptGenerator.Lit` e ter um teste que analisa o resultado.
5. Rode `dotnet test` antes de abrir o pull request e descreva como testou (aplicativo, tipo de instalador, contexto Sistema ou Usuário). Os testes nunca usam a rede; `J365_LIVE_TESTS=1` inclui as poucas verificações que consultam o GitHub de verdade (última versão, último PSAppDeployToolkit).
6. Mudanças de interface: gere todas as telas fora da tela com `J365.IntunePSApp.exe --snapshot <pasta>` (veja o [docs/BUILDING.md](docs/BUILDING.md)) nos dois temas e idiomas e confira os PNGs antes de abrir o pull request.

## Relatando problemas

Use os modelos de issue. Anexe o log do aplicativo (Configurações > Geral > **Logs**, ou `%LOCALAPPDATA%\J365\IntunePSApp\logs\app-AAAAMMDD.log`; uma cópia portátil o guarda em `IntunePSApp-Data\logs`, ao lado do executável). Para um pacote que falhou em um dispositivo, anexe também o `build.log` e o log do PSAppDeployToolkit (`C:\ProgramData\Microsoft\IntuneManagementExtension\Logs` ou `C:\Windows\Logs\Software`). Remova dados sensíveis antes. Problemas de segurança: veja o [SECURITY.md](SECURITY.md).

Ao contribuir, você concorda que a sua contribuição é licenciada sob a [licença MIT](LICENSE).
