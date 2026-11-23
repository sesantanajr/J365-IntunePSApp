# Revisão de qualidade — versão 2.7

*English summary below.*

## Como cada versão é validada

| Etapa | O que verifica |
| --- | --- |
| Testes automatizados (Windows, a cada push) | **409 testes**: geração do script do PSAppDeployToolkit (validado pelo Windows PowerShell 5.1), detecção, marca do pacote, `.intunewin`, metadados do Intune, textos da publicação, leitura da Microsoft Store, planejamento e regras do lote, ícones genéricos, idiomas (chaves e marcadores nos dois idiomas), verificação de versões sem a API do GitHub, versões mantidas pelo pacote (executadas no Windows PowerShell 5.1), caminhos longos, publicação sem duplicatas. `J365_LIVE_TESTS=1` inclui as verificações contra o GitHub real. |
| Verificações de interface | O aplicativo renderiza início, catálogo, janela de lote, as 8 etapas do editor, pacotes, configurações e conexão, em português e inglês, temas claro e escuro, em dois tamanhos de janela; também exercita editar/salvar/reabrir e o aviso de pacote desatualizado. Uma captura ausente reprova a versão. |
| Executável publicado | Teste de fumaça (`--version` igual à versão da release), `SHA256SUMS.txt` e atestado de procedência da compilação. |
| Banco de dados (diário) | O gerador avisa quando um padrão de nome curado não corresponde mais ao que os instaladores registram. |

## O que a 2.7 mudou e como foi verificado

A 2.7 partiu dos logs de uma máquina real (aplicativo, `build.log`, logs do PSAppDeployToolkit e do Intune Management Extension) e de uma revisão de todo o código em quatro frentes: empacotamento, publicação, infraestrutura e interface.

| Área | Constatação | Alteração e verificação |
| --- | --- | --- |
| Verificação de versões | Cópias até a 2.6.0 diziam "versão mais recente" com a 2.6.1 publicada: a API do GitHub (60 consultas por hora por endereço) estava esgotada na rede corporativa, que divide um único endereço público. O contador de limite medido começou a janela 12 s depois da verificação que falhou. | Versão lida no `SHA256SUMS.txt` da última release (github.com, sem esse limite), com API e página como alternativas; limite atingido explicado; cada verificação no log; nova verificação a cada 4 h com a janela aberta. Teste ao vivo: a release é encontrada pelos arquivos, sem a API. |
| Mesma versão instalada | No Intune, o pacote do Adobe Acrobat Reader removeu a versão idêntica (4 min) e a instalou de novo (5 min), depois de pedir para fechar o app. | A mesma versão do mesmo produto (mesmo código de produto MSI ou mesma arquitetura) é mantida e marcada, com as ações pós-instalação. O bloco gerado foi executado no Windows PowerShell 5.1 com instalações simuladas (mesma versão, mais nova, mais antiga, outro código, x86 × x64, ARM64, por usuário). |
| Caminhos longos | A segunda geração do mesmo pacote numa pasta do OneDrive chegou a 274 caracteres; o IntuneWinAppUtil falhou depois de 100 s. | Nome de pasta curto, nomes longos encurtados, caminho conferido antes da ferramenta, empacotador interno sem recompactar instaladores. Teste com 32 reservas concorrentes repetido 5 vezes. |
| Microsoft Store | A publicação chamava o Graph v1.0, mas o `winGetApp` só existe no beta (documentação e SDKs conferidos). | Publicação pelo beta; criação sem repetição após timeout ou 5xx, com busca do app criado. Teste com servidor local: 429 é repetido, 500 não. |
| Configurações | Duas janelas sobrescreviam as configurações uma da outra; o `config.json` não era forçado ao disco e um arquivo danificado era substituído em silêncio. | Uma janela por pasta de dados; gravação com flush; cópia do arquivo danificado. |
| Interface | Lote iniciável duas vezes durante o login; duplo clique na barra de rolagem baixava um app; catálogo com dados antigos após atualizar o banco; edição perdida nas Configurações; textos cortados entre 1024 e 1366 px. | Correções nos quatro fluxos, botões que quebram linha, indicadores em duas colunas abaixo de 1150 px, Esc nas caixas de diálogo, contraste do tema claro. |
| Textos | Referências a botões que não existem mais, textos da linha de comando sem acentos, "locatário" e "tenant" misturados. | 37 textos em português revisados; os testes conferem chaves e marcadores nos dois idiomas. |
| Entrega | Um push só de documentação logo após o aumento de versão impediria a publicação da release. | O workflow publica mesmo assim; o banco de dados não é publicado com mais de 10% menos aplicativos. |

## Limites

- Não houve publicação em um tenant real nesta revisão: a publicação de apps da Microsoft Store pelo Graph beta, o lote com publicação e a manutenção da mesma versão em dispositivos devem ser validados com um grupo piloto.
- Cópias até a 2.6.0 só consultam a API do GitHub: numa rede com o limite esgotado, precisam ser atualizadas uma vez manualmente.
- A verificação dos arquivos do PSAppDeployToolkit cobre o módulo e o inicializador assinados; os arquivos não assinados do template são recopiados da release oficial quando a cópia é reinstalada.
- As verificações de interface não substituem leitor de tela, múltiplos monitores e todas as escalas de DPI. O executável ainda não tem assinatura Authenticode.

## English summary

Each release runs 409 automated tests on Windows, renders every page (including the batch window) in both languages and themes, smoke-tests the published executable and publishes checksums and a provenance attestation. 2.7 started from the logs of a real machine and a review of the whole code: the update check no longer depends on the GitHub API (whose limit a company network shares), the same version already installed is kept instead of reinstalled, long OneDrive paths no longer break IntuneWinAppUtil, the built-in packager stores installers instead of recompressing them, apps of the Microsoft Store are published through Graph beta without duplicates, one window runs per data folder with safely written settings, and many interface and text issues were fixed. Publishing to a live tenant still has to be validated with a pilot group.

## Revisão anterior — versão 2.6

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
