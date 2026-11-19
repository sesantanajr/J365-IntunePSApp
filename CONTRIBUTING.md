# Como contribuir

Obrigado por ajudar o J365 IntunePSApp! Toda contribuição é bem-vinda: código, correções no banco de dados, testes com instaladores reais, documentação e ideias.

## Curadoria de aplicativos (a forma mais rápida de ajudar)

O banco de dados vem do winget e já traz os parâmetros silenciosos e as URLs. A curadoria em [`catalog/curated.json`](catalog/curated.json) acrescenta o que falta para um pacote corporativo perfeito:

```json
"Notepad++.Notepad++": {
  "processes": ["notepad++"],
  "namePattern": "Notepad++*",
  "uninstallArgs": "/S",
  "notes": "Observação exibida no catálogo."
}
```

| Campo | Descrição |
|---|---|
| `processes` | Executáveis que precisam estar fechados (sem `.exe`) |
| `namePattern` | Nome em "Aplicativos instalados" (aceita `*`), usado para desinstalar, remover versões anteriores e detectar |
| `uninstallArgs` | Parâmetros silenciosos do desinstalador `.exe` |
| `installArgs` | Substitui os parâmetros de instalação do winget, se necessário |
| `detectionFile` | Arquivo recomendado para detecção (ex.: apps que se atualizam sozinhos) |
| `checkVersion` | `false` para detectar apenas a presença |
| `removePrevious` | `false` para bibliotecas compartilhadas (ex.: VC++ Redistributable) |
| `notes` | Avisos importantes para quem empacota |

A chave é o **ID do winget** (procure em https://winget.run ou com `winget search`). Antes de abrir o pull request, teste o pacote gerado com:

```powershell
.\Invoke-AppDeployToolkit.exe -DeploymentType Install -DeployMode Interactive
.\Invoke-AppDeployToolkit.exe -DeploymentType Uninstall -DeployMode Interactive
```

## Código

1. Instale o [.NET SDK 10](https://dotnet.microsoft.com/download).
2. `dotnet run --project src/IntunePSApp`
3. Use o modo de captura para revisar a interface sem cliques:
   `J365.IntunePSApp.exe --snapshot <pasta> --snapshot-installer <instalador> [--snapshot-build]`
4. Para testar com dados isolados, defina a variável `J365_INTUNEPSAPP_DATA` com uma pasta temporária.

Padrões do projeto:

- Textos da interface em português do Brasil.
- O script de detecção deve continuar 100% ASCII e compatível com o Windows PowerShell 5.1.
- Todo `Invoke-AppDeployToolkit.ps1` gerado é validado pelo parser do PowerShell. Novas ações devem passar nessa validação.

## Relatando problemas

Abra uma *issue* com:

- o app e o arquivo do instalador (link oficial);
- o `geracao.log` do pacote;
- o log do PSADT (`C:\ProgramData\Microsoft\IntuneManagementExtension\Logs` ou `C:\Windows\Logs\Software`).

Não publique dados sensíveis (chaves de licença, nomes de servidores internos) nas issues.
