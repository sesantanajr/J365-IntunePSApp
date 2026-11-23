# Revisão de qualidade — versão 2.6

*English summary below.*

## Como cada versão é validada

| Etapa | O que verifica |
| --- | --- |
| Testes automatizados (Windows, a cada push) | **349 testes**: geração do script do PSAppDeployToolkit (validado pelo Windows PowerShell 5.1), detecção, marca do pacote, `.intunewin`, metadados do Intune, textos da publicação, leitura da Microsoft Store, planejamento e regras do lote, ícones genéricos, idiomas (chaves e marcadores nos dois idiomas). |
| Verificações de interface | O aplicativo renderiza início, catálogo, janela de lote, as 8 etapas do editor, pacotes, configurações e conexão, em português e inglês, temas claro e escuro, em dois tamanhos de janela; também exercita editar/salvar/reabrir e o aviso de pacote desatualizado. Uma captura ausente reprova a versão. |
| Executável publicado | Teste de fumaça (`--version` igual à versão da release), `SHA256SUMS.txt` e atestado de procedência da compilação. |
| Banco de dados (diário) | O gerador avisa quando um padrão de nome curado não corresponde mais ao que os instaladores registram. |

## O que a 2.6 mudou e como foi verificado

| Área | Constatação | Alteração |
| --- | --- | --- |
| Modo silencioso | A atribuição criada na publicação usava sempre `showAll`: o Intune notificava o usuário; o bloqueio de aplicativos do PSAppDeployToolkit mostrava aviso em qualquer modo. | Notificações por pacote (automático: `hideAll` no silencioso), reinicialização *Nenhuma ação específica*, sem adiamento, mensagens nem bloqueio no silencioso. Comportamento do bloqueio conferido no código do PSAppDeployToolkit 4.1.8. |
| Ícones | Instaladores sem ícone próprio levavam o ícone genérico de instalador ao Portal da Empresa (3 de 14 instaladores amostrados). | 22 impressões de ícones genéricos (Inno Setup, WiX Burn, ícones de fábrica do NSIS); ícones reais de apps ficaram a 41 ou mais do limite de 24. |
| Parâmetros | Switches detectados no próprio instalador eram descartados quando a linha do winget não trazia switches (9 de 27 amostrados); Inno por usuário sem `/CURRENTUSER` (432 apps); padrão curado do .NET Desktop Runtime 10 não correspondia ao nome registrado. | Switches detectados mantidos; `/CURRENTUSER` como o winget; padrão corrigido e aviso automático no gerador do banco (rodou sem avisos com os 84 apps curados). |
| Campos repetidos | Remoção de versões anteriores em duas etapas; nome e versão da detecção digitados de novo. | Uma única chave na etapa Desinstalação; detecção acompanha as etapas anteriores. |
| Deploy em lote | Não havia como gerar ou publicar vários apps com as mesmas escolhas. | Janela de lote com publicação, confirmação do tenant, nova tentativa só da publicação, itens pulados abrindo no editor, relatório e `--batch`. A janela de lote é renderizada nas verificações de interface. |
| Microsoft Store | Apps da origem msstore do winget (ex.: Microsoft PC Manager, 9PM860492SZD) não existiam no banco. | Pesquisa ao vivo pelo nome ou ID e publicação como *Microsoft Store app (new)*; conferida contra o serviço real (busca, manifesto, catálogo, logo, apps UWP e Win32 da Store). |
| Atualização | Uma verificação sem resposta do GitHub dizia "você tem a versão mais recente". | O motivo é mostrado; com a API bloqueada, a versão vem da página de releases; o caminho de atualização foi repetido contra a release real (SHA-256 e versão do executável). |
| Projetos existentes | A nova escolha de notificações entraria na identificação da revisão e marcaria pacotes da 2.5.1 como desatualizados. | Ausente quando automática: projetos e registros de revisão da 2.5.1 continuam válidos. |

## Limites

- Não houve publicação em um tenant real nesta revisão: criação e atribuição de apps da Microsoft Store e a publicação em lote devem ser validadas com um grupo piloto.
- A disponibilidade de um app na Microsoft Store depende do mercado e das políticas do tenant; o contexto de instalação (sistema para apps UWP/MSIX) pode ser ajustado no Intune se um app não o suportar.
- As impressões de ícones genéricos cobrem os ícones de fábrica conhecidos; um instalador com ícone de instalador próprio (que não é o ícone do app) não é detectado.
- As verificações de interface não substituem leitor de tela, múltiplos monitores e todas as escalas de DPI. O executável ainda não tem assinatura Authenticode.

## English summary

Each release runs 349 automated tests on Windows, renders every page (including the batch window) in both languages and themes, smoke-tests the published executable and publishes checksums and a provenance attestation. 2.6 fixed Intune toasts and blocking in silent mode, generic installer icons, discarded silent switches, per-user Inno Setup, the .NET Desktop Runtime 10 detection, the misleading "up to date" after a failed update check and the layout of the batch window, and added batch deploy and apps of the Microsoft Store (checked against the live Store). Publishing to a live tenant still has to be validated with a pilot group.

## Revisão anterior — versão 2.5.1

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

### Decisões de arquitetura

A aplicação já usa WPF e .NET 10, integra recursos nativos do Windows e gera scripts para PowerShell 5.1. Esta revisão mantém essa base e os formatos existentes. Uma troca de framework exigiria revalidar diálogos, acessibilidade, DPI, execução e integrações; não resolveria os problemas de consistência encontrados.

A lógica nova de revisão e reserva de saída fica em `Core/PackageRevision.cs`, com testes independentes da interface. O editor ainda concentra muita lógica em code-behind; separar navegação, projeto e publicação em componentes testáveis continua sendo uma evolução recomendada, feita gradualmente, sem reescrever todo o aplicativo durante uma correção.

A API do Intune tem campos distintos para nome, versão e informações adicionais. Referência: [win32LobApp no Microsoft Graph](https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-win32lobapp?view=graph-rest-1.0). Novos requisitos de sistema devem ser confirmados no serviço antes de ampliar os valores enviados pela integração.

### Limites registrados na 2.5.1

- A revisão identifica configurações, não mudanças externas no instalador, ícones ou arquivos extras. Os arquivos de origem alterados exigem nova geração. O SHA-256 do `.intunewin` continua sendo conferido na publicação.
- Pacotes antigos sem registro de revisão continuam legíveis. Somente regenerá-los cria o registro; não há como reconstruir com certeza o estado histórico ausente.
- As verificações de interface exercitam estado e renderização; não substituem uma avaliação com leitor de tela, múltiplos monitores e todas as escalas de DPI.
- Não houve publicação de aplicativo nem instalação em tenant/dispositivo real nesta revisão. Deve-se validar em grupo piloto os contextos System/User, atualização/desinstalação, adiamentos, reinício e detecção.
- A distribuição continua Windows x64. A opção de arquitetura ARM64 dos pacotes não equivale a um executável nativo ARM64 do editor.
- A assinatura de código depende da configuração de uma identidade de assinatura pelo mantenedor. Checksums e atestação de procedência não são uma assinatura Authenticode.
- Não é prometida compatibilidade universal com instaladores: parâmetros silenciosos, dependências e comportamento de reinício pertencem ao instalador e precisam ser testados.
