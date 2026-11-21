# Revisão de qualidade da linha 2.5

Esta revisão examina os limites entre projeto, geração, interface e publicação. O objetivo é reduzir resultados incorretos e tornar o estado do trabalho compreensível.

| Área | Constatação no código | Alteração |
| --- | --- | --- |
| Consistência | O editor reutilizava o último resultado após alterações; salvar podia parecer equivalente a atualizar o pacote. | Identificação das configurações geradas, bloqueio de publicação/testes de resultado antigo e aviso de nova geração necessária. |
| Reabertura | A existência de um `.intunewin` ao lado do projeto era suficiente para oferecer o resultado. | Novos pacotes guardam `build-revision.json`; projeto alterado ou identificação ilegível não libera o resultado anterior. |
| Concorrência | O sufixo de pasta tinha resolução de um segundo; duas gerações podiam compartilhar a saída. | Reserva exclusiva com nome alternativo único. Testes com 32 reservas concorrentes e pacote preexistente. |
| Estado global | O log de pacote e as ferramentas são compartilhados no processo. | Uma geração por vez no núcleo; cancelamento enquanto aguarda. |
| Editor | Os campos permaneciam editáveis durante a geração; o sucesso marcava a tela atual como salva. | Captura independente das configurações e edição pausada até a conclusão/cancelamento. |
| Salvamento | Ctrl+S sempre abria a caixa de escolha de arquivo. | Arquivo atual lembrado, Salvar como, Ctrl+Shift+S e estado de salvamento visível. |
| Entrada | Texto inválido em números virava valor padrão na coleta dos campos. | Validação de sintaxe na revisão, geração e salvamento; texto digitado também participa da detecção de alterações. |
| Biblioteca | A varredura de arquivos ocorria na thread da interface e filtros sem resultados deixavam a tela vazia. | Leitura assíncrona cancelável, ordenação e estado sem resultados com ação de recuperação. |
| Catálogo | Uma busca anterior podia aparecer durante a espera da próxima consulta. | Invalidação imediata dos resultados em andamento ao digitar. |
| Apresentação | Nome e versão ainda apareciam como um único título na biblioteca. | Nome e indicador de versão separados, estados do projeto persistentes e avisos sem depender apenas de cor. |
| Entrega | Compilar XAML não verificava o comportamento da tela. | Capturas nos temas claro/escuro, janela compacta e regressões do fluxo editar/salvar/reabrir no Windows do CI. |

## Decisões de arquitetura

A aplicação já usa WPF e .NET 10, integra recursos nativos do Windows e gera scripts para PowerShell 5.1. Esta revisão mantém essa base e os formatos existentes. Uma troca de framework exigiria revalidar diálogos, acessibilidade, DPI, execução e integrações; não resolveria os problemas de consistência encontrados.

A lógica nova de revisão e reserva de saída fica em `Core/PackageRevision.cs`, com testes independentes da interface. O editor ainda concentra muita lógica em code-behind; separar navegação, projeto e publicação em componentes testáveis continua sendo uma evolução recomendada, feita gradualmente, sem reescrever todo o aplicativo durante uma correção.

A API do Intune tem campos distintos para nome, versão e informações adicionais. Referência: [win32LobApp no Microsoft Graph](https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-win32lobapp?view=graph-rest-1.0). Novos requisitos de sistema devem ser confirmados no serviço antes de ampliar os valores enviados pela integração.

## Limites e próximos critérios de validação

- A revisão identifica configurações, não mudanças externas no instalador, ícones ou arquivos extras. Os arquivos de origem alterados exigem nova geração. O SHA-256 do `.intunewin` continua sendo conferido na publicação.
- Pacotes antigos sem registro de revisão continuam legíveis. Somente regenerá-los cria o registro; não há como reconstruir com certeza o estado histórico ausente.
- As verificações de interface exercitam estado e renderização; não substituem uma avaliação com leitor de tela, múltiplos monitores e todas as escalas de DPI.
- Não houve publicação de aplicativo nem instalação em tenant/dispositivo real nesta revisão. Deve-se validar em grupo piloto os contextos System/User, atualização/desinstalação, adiamentos, reinício e detecção.
- A distribuição continua Windows x64. A opção de arquitetura ARM64 dos pacotes não equivale a um executável nativo ARM64 do editor.
- A assinatura de código depende da configuração de uma identidade de assinatura pelo mantenedor. Checksums e atestação de procedência não são uma assinatura Authenticode.
- Não é prometida compatibilidade universal com instaladores: parâmetros silenciosos, dependências e comportamento de reinício pertencem ao instalador e precisam ser testados.
