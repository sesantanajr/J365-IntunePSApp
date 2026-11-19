# Avisos de terceiros

O J365 IntunePSApp é distribuído sob a licença MIT. Ele usa os componentes abaixo, cada um sob a sua própria licença.

## PSAppDeployToolkit

- Projeto: https://github.com/PSAppDeployToolkit/PSAppDeployToolkit
- Licença: GNU Lesser General Public License v3.0 (LGPL-3.0)
- Uso: o template v4 oficial é **baixado em tempo de execução** a partir das releases do projeto no GitHub, tem a assinatura digital verificada e é copiado, sem alterações no módulo, para cada pacote gerado. O J365 IntunePSApp gera apenas o script `Invoke-AppDeployToolkit.ps1` e personaliza os arquivos de configuração, textos e imagens previstos pelo toolkit. O código-fonte do PSADT está disponível no link acima.

## Microsoft Win32 Content Prep Tool (IntuneWinAppUtil.exe)

- Projeto: https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool
- Licença: Microsoft Software License Terms (veja o repositório)
- Uso: **baixado em tempo de execução** do repositório oficial da Microsoft e usado somente depois que a assinatura digital da Microsoft é verificada. Ele **não é redistribuído** por este projeto. Se não estiver disponível, o J365 IntunePSApp usa um empacotador próprio, compatível com o formato `.intunewin`.

## Windows Package Manager Community Repository (winget-pkgs)

- Projeto: https://github.com/microsoft/winget-pkgs
- Licença: MIT
- Uso: o banco de dados de aplicativos (`catalog.json.gz`) é derivado dos manifestos públicos deste repositório (nome, fabricante, versão, URLs oficiais, SHA256, parâmetros silenciosos, códigos de produto e entradas de "Aplicativos instalados").

## YamlDotNet

- Projeto: https://github.com/aaubry/YamlDotNet
- Licença: MIT
- Uso: leitura dos manifestos YAML no gerador do banco de dados (`tools/CatalogBuilder`).

## Marcas

Microsoft, Windows, Intune e Microsoft 365 são marcas da Microsoft Corporation. Os nomes e logotipos de aplicativos exibidos no catálogo pertencem aos seus respectivos donos. Este projeto não é afiliado à Microsoft nem ao projeto PSAppDeployToolkit.

A marca e o logotipo Jornada 365 pertencem ao Jornada 365 (https://jornada365.cloud).
