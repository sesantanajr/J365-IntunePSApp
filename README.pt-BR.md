<p align="center"><a href="README.md">English</a> · <strong>Português (Brasil)</strong></p>

<p align="center"><img src="docs/assets/hero-pt-BR.svg" alt="J365 IntunePSApp 2.7.0 — do instalador ao Intune, com controle em cada etapa" width="100%"></p>

<p align="center"><b>Menos tarefas repetitivas. Mais consistência nos seus pacotes.</b><br>Uma interface visual para preparar, personalizar, testar e publicar aplicativos Windows.</p>
<p align="center"><a href="https://github.com/sesantanajr/J365-IntunePSApp/releases/download/v2.7.0/J365-IntunePSApp-2.7.0-win-x64.zip"><img src="docs/assets/download-pt-BR.svg" alt="Baixar J365 IntunePSApp 2.7.0 — Windows x64" width="400"></a></p>
<p align="center"><a href="#recursos">Conheça os recursos</a> · <a href="#comecar">Como começar</a> · <a href="docs/USER-GUIDE.pt-BR.md">Guia de uso</a> · <a href="https://github.com/sesantanajr/J365-IntunePSApp/releases/tag/v2.7.0">Notas da versão</a></p>
<p align="center"><a href="https://github.com/sesantanajr/J365-IntunePSApp/actions/workflows/build.yml"><img alt="Build and tests" src="https://github.com/sesantanajr/J365-IntunePSApp/actions/workflows/build.yml/badge.svg"></a> <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-315CF5?style=flat-square"></a> <img alt="Windows x64" src="https://img.shields.io/badge/Windows-x64-0A5CA8?style=flat-square"></p>

## Seu próximo pacote começa aqui

Transforme um instalador MSI ou EXE — ou qualquer aplicativo do catálogo do winget e da Microsoft Store — em um pacote organizado para o Microsoft Intune. O **J365 IntunePSApp** reúne a configuração da instalação, a experiência do usuário, as ações, a detecção e a publicação em um fluxo guiado baseado no **PSAppDeployToolkit**.

| Prepare com mais agilidade | Padronize a experiência | Mantenha o controle |
| --- | --- | --- |
| Catálogo do winget e da Microsoft Store, análise de instaladores, sugestões de parâmetros silenciosos e deploy em lote. | Logo, cores, mensagens, adiamentos e padrões reutilizáveis da sua empresa. | Revisão antes de gerar, testes, logs e proteção contra publicação de pacotes desatualizados. |

<a href="docs/screenshots/pt-BR/home-dark.png"><img src="docs/screenshots/pt-BR/home-dark.png" alt="J365 IntunePSApp 2.7: banco de dados, pacotes, toolkit e conexão com o Intune num relance" width="100%"></a>

**Um fluxo completo:** catálogo ou instalador → aplicativo → experiência → ações → desinstalação → detecção → visual → geração → teste → publicação. Cada tela está explicada no **[guia de uso](docs/USER-GUIDE.pt-BR.md)**.

<a id="recursos"></a>
## Conheça o que você pode fazer

Capturas reais da **2.7** com uma empresa demonstrativa (Contoso) e o MSI do 7-Zip, empacotado de verdade. Clique nas imagens para ampliar. A conexão com o Intune é um exemplo: nada foi publicado em um tenant.

### 01 · Encontre qualquer aplicativo

Pesquise mais de 16 mil aplicativos do winget pelo nome, fabricante ou ID — com cada instalador, os parâmetros silenciosos que serão usados e a curadoria do Jornada 365 — e a Microsoft Store ao vivo, pelo nome ou ID do produto. **Baixar e empacotar** confere o instalador pelo SHA256 e o abre no editor já preenchido.

<a href="docs/screenshots/pt-BR/catalog.png"><img src="docs/screenshots/pt-BR/catalog.png" alt="Busca no catálogo com instaladores, parâmetros silenciosos e curadoria" width="100%"></a>

### 02 · Apresente melhor seus aplicativos no Intune

O instalador é analisado e reconhecido; nome, versão, arquitetura, contexto e ícone são preenchidos. Descrição, fabricante, desenvolvedor, proprietário e links de informações e privacidade vão para o Intune nos campos próprios.

<table><tr><td width="50%"><b>Aplicativo, arquitetura e contexto</b><br><a href="docs/screenshots/pt-BR/application.png"><img src="docs/screenshots/pt-BR/application.png" alt="Etapa Aplicativo" width="100%"></a></td><td width="50%"><b>Informações exibidas no Intune e no Portal da Empresa</b><br><a href="docs/screenshots/pt-BR/metadata.png"><img src="docs/screenshots/pt-BR/metadata.png" alt="Informações do aplicativo para o Intune" width="100%"></a></td></tr></table>

### 03 · Cuide da experiência de quem recebe a instalação

Escolha o modo de exibição, o fechamento de aplicativos, os adiamentos e as mensagens — ou uma instalação **silenciosa**, sem janelas e sem notificações do Intune —, com a prévia ao vivo da janela real do PSAppDeployToolkit. Adicione ações antes e depois da instalação para arquivos, registro, serviços, dependências e comandos.

<table><tr><td width="50%"><b>Experiência do usuário e adiamentos</b><br><a href="docs/screenshots/pt-BR/experience.png"><img src="docs/screenshots/pt-BR/experience.png" alt="Experiência do usuário e adiamentos" width="100%"></a></td><td width="50%"><b>Ações prontas e arquivos extras</b><br><a href="docs/screenshots/pt-BR/actions.png"><img src="docs/screenshots/pt-BR/actions.png" alt="Ações prontas e arquivos extras" width="100%"></a></td></tr></table>

### 04 · Planeje a atualização e a detecção

Remova versões anteriores, mantenha uma mais nova e — novidade da 2.7 — mantenha a mesma versão já instalada em vez de reinstalá-la. Detecte por nome/versão, código do produto ou arquivo, com a marca do pacote, e teste a regra no seu computador.

<table><tr><td width="50%"><b>Versões anteriores e versões mantidas</b><br><a href="docs/screenshots/pt-BR/uninstall.png"><img src="docs/screenshots/pt-BR/uninstall.png" alt="Tratamento de versões anteriores" width="100%"></a></td><td width="50%"><b>Regras de detecção e testes locais</b><br><a href="docs/screenshots/pt-BR/detection.png"><img src="docs/screenshots/pt-BR/detection.png" alt="Regras de detecção e testes locais" width="100%"></a></td></tr></table>

### 05 · Dê aos pacotes a identidade da sua empresa

Use o nome, o logo e a cor de destaque da sua empresa nas janelas do PSAppDeployToolkit, salve tudo como padrão da empresa e trabalhe no tema claro, escuro ou igual ao Windows.

<table><tr><td width="50%"><b>Identidade visual e cores</b><br><a href="docs/screenshots/pt-BR/branding.png"><img src="docs/screenshots/pt-BR/branding.png" alt="Identidade visual e cores" width="100%"></a></td><td width="50%"><b>Aparência do editor</b><br><a href="docs/screenshots/pt-BR/appearance.png"><img src="docs/screenshots/pt-BR/appearance.png" alt="Aparência do editor" width="100%"></a></td></tr></table>

### 06 · Gere, teste e saiba quando gerar de novo

Um clique monta o pacote do PSAppDeployToolkit, o script de detecção e o `.intunewin`, com os comandos e as instruções para o Intune. Teste aqui ou no Windows Sandbox. Configurações alteradas marcam o resultado como desatualizado e desligam publicação e testes até você gerar de novo.

<table><tr><td width="50%"><b>Pacote pronto para o Intune</b><br><a href="docs/screenshots/pt-BR/generated.png"><img src="docs/screenshots/pt-BR/generated.png" alt="Pacote pronto para o Intune" width="100%"></a></td><td width="50%"><b>Pacote desatualizado: gere novamente</b><br><a href="docs/screenshots/pt-BR/needs-rebuild.png"><img src="docs/screenshots/pt-BR/needs-rebuild.png" alt="Aviso de pacote desatualizado" width="100%"></a></td></tr></table>

### 07 · Conecte e publique

Conecte com a sua conta, com um certificado do aplicativo guardado no TPM, com um registro que já existe ou pelo Azure Cloud Shell. Crie ou atualize o app no Intune com o progresso do envio, o ícone, a detecção, os requisitos e uma atribuição opcional. [Guia de publicação](docs/INTUNE-PUBLISH.md).

<table><tr><td width="50%"><b>Formas de conectar</b><br><a href="docs/screenshots/pt-BR/connection.png"><img src="docs/screenshots/pt-BR/connection.png" alt="Opções de conexão com o Microsoft Intune" width="100%"></a></td><td width="50%"><b>Publicar no Intune</b><br><a href="docs/screenshots/pt-BR/publish.png"><img src="docs/screenshots/pt-BR/publish.png" alt="Janela de publicação" width="100%"></a></td></tr></table>

### 08 · Implante vários aplicativos de uma vez

Marque aplicativos do catálogo (winget ou Microsoft Store), defina o que eles têm em comum e gere — e, se quiser, publique — todos. O que precisaria de um palpite é pulado com o motivo e pode ser terminado no editor.

<a href="docs/screenshots/pt-BR/batch.png"><img src="docs/screenshots/pt-BR/batch.png" alt="Geração e deploy em lote com o estado de cada aplicativo" width="100%"></a>

### 09 · Encontre seus pacotes e retome o trabalho

Reabra projetos, pesquise pelo nome ou fabricante, ordene por data, nome ou atualizações disponíveis e saia de uma busca vazia com um clique.

<table><tr><td width="50%"><b>Biblioteca de pacotes e ordenação</b><br><a href="docs/screenshots/pt-BR/packages.png"><img src="docs/screenshots/pt-BR/packages.png" alt="Biblioteca de pacotes e ordenação" width="100%"></a></td><td width="50%"><b>Busca sem resultados com ação de recuperação</b><br><a href="docs/screenshots/pt-BR/no-results.png"><img src="docs/screenshots/pt-BR/no-results.png" alt="Busca sem resultados com ação de recuperação" width="100%"></a></td></tr></table>

### 10 · Trabalhe do seu jeito

Português ou inglês, claro ou escuro, portátil ou instalado para o seu usuário, atualizações sem sair do aplicativo e padrões que todo pacote novo segue.

<table><tr><td width="50%"><b>Configurações gerais, atualizações e pastas</b><br><a href="docs/screenshots/pt-BR/settings.png"><img src="docs/screenshots/pt-BR/settings.png" alt="Configurações gerais" width="100%"></a></td><td width="50%"><b>Configurações e estado da conexão</b><br><a href="docs/screenshots/pt-BR/intune.png"><img src="docs/screenshots/pt-BR/intune.png" alt="Configurações e estado da conexão" width="100%"></a></td></tr></table>

## O que você leva ao gerar um pacote

| Entrega | Para que serve |
| --- | --- |
| **Arquivo `.intunewin`** | Conteúdo preparado para o upload ao Microsoft Intune. |
| **Pacote PSAppDeployToolkit** | Estrutura de implantação com instalação, desinstalação, ações e experiência do usuário. |
| **Script de detecção** | Regra para verificar se o aplicativo está instalado conforme a configuração. |
| **Comandos e instruções** | Referência para instalação, desinstalação e preenchimento dos campos do Intune. |
| **Metadados e projeto** | Informações do aplicativo e configurações para reabrir e evoluir o pacote. |

O teste pode ser executado localmente ou no **Windows Sandbox**, quando disponível, com resultados e logs. Também há comandos de linha para automação. [Consulte a documentação](docs/BUILDING.md).

<a id="comecar"></a>
## Comece em três passos

1. Use o botão de download no topo e extraia o ZIP para uma pasta.
2. Abra **`J365.IntunePSApp.exe`** e conclua a preparação das ferramentas. O runtime está incluído; não é necessário instalar o SDK .NET.
3. Escolha um instalador ou um aplicativo do catálogo, configure o projeto e revise antes de gerar — ou marque vários aplicativos (winget ou Microsoft Store) e faça o deploy de todos juntos. Valide em um grupo piloto antes de distribuir. O [guia de uso](docs/USER-GUIDE.pt-BR.md) percorre cada tela.

## Novidades da 2.7

- **Atualização que funciona em redes corporativas:** a versão mais recente é lida nos arquivos da última release no github.com, e não mais na API do GitHub, cujas 60 consultas por hora por endereço são divididas por toda a rede da empresa. Uma verificação que falhou diz o motivo (e quando o limite libera), cada verificação fica no log, e uma janela aberta por dias verifica de novo a cada poucas horas. Cópias até a 2.6.0 dependem da API: se uma continuar dizendo "versão mais recente", baixe esta versão uma vez.
- **A mesma versão já instalada é mantida:** um dispositivo que já tem exatamente esta versão do mesmo produto não é mais desinstalado e reinstalado (9 minutos e um pedido para fechar o Adobe Acrobat Reader numa implantação real): o pacote aplica as ações pós-instalação e grava a sua marca. Uma opção na etapa Desinstalação.
- **Pacotes mais rápidos e robustos:** nomes de pasta curtos e verificação do caminho antes do IntuneWinAppUtil (caminhos longos no OneDrive o faziam falhar), e um empacotador interno que não recompacta mais os instaladores (segundos em vez de minutos). As Configurações avisam quando a pasta de pacotes está no OneDrive.
- **Correções na publicação:** apps da Microsoft Store são publicados pelo Microsoft Graph beta (onde o `winGetApp` existe), uma resposta perdida não cria mais duplicatas, e a lista de versões anteriores nunca pré-seleciona um app de outro nome.
- **Uma janela por vez** e configurações gravadas com segurança: abrir o aplicativo de novo traz a janela aberta para a frente, em vez de uma segunda janela que sobrescrevia as configurações da primeira.
- **Interface e textos:** o lote não inicia duas vezes durante o login, o catálogo se atualiza após uma atualização do banco de dados e ignora cliques duplos na barra de rolagem, as Configurações mantêm o que foi digitado, Esc fecha as caixas de diálogo e os textos em português foram revisados.

A 2.6 trouxe os apps da Microsoft Store, o deploy em lote no Intune, instalações silenciosas de verdade, ícones e parâmetros corretos. Todas as mudanças: [CHANGELOG](CHANGELOG.md).

## Evolução com qualidade

Cada release passa por **409 testes automatizados** no Windows (scripts gerados validados pelo Windows PowerShell 5.1, empacotamento, detecção, textos da publicação, leitura da Microsoft Store, planejamento do lote, idiomas), além de verificações que renderizam o editor, as configurações e as janelas de conexão e de lote nos dois idiomas e temas. A pesquisa na Microsoft Store e a verificação de atualizações também foram exercitadas nos serviços reais. Veja a [revisão de qualidade](docs/QUALITY.md).

Projetos antigos continuam legíveis. Atualizar o editor não altera automaticamente aplicativos já publicados no tenant. A identificação da revisão acompanha novos pacotes; regenere os antigos para recebê-la.

**Compatibilidade:** editor WPF/.NET 10 para Windows x64. A arquitetura ARM64 nos pacotes não equivale a um editor nativo ARM64. Parâmetros silenciosos, detecção e reinício dependem do instalador e precisam de teste. A publicação em tenant real (pacotes e apps da Microsoft Store) deve ser validada com um grupo piloto.

**Assinatura:** o executável desta release ainda não é assinado com Authenticode. Consulte as [orientações de assinatura](docs/SIGNING.md) se uma política do Windows impedir a execução.

## Documentação e comunidade

[Guia de uso](docs/USER-GUIDE.pt-BR.md) · [Novidades por versão](CHANGELOG.md) · [Análise técnica](docs/QUALITY.md) · [Publicação no Intune](docs/INTUNE-PUBLISH.md) · [Compilar](docs/BUILDING.md) · [Arquitetura / Architecture](docs/ARCHITECTURE.md)

[Contribuir](CONTRIBUTING.md) · [Relatar um problema](https://github.com/sesantanajr/J365-IntunePSApp/issues) · [Security](SECURITY.md) · [SHA256SUMS.txt](https://github.com/sesantanajr/J365-IntunePSApp/releases/download/v2.7.0/SHA256SUMS.txt)

---

<p align="center"><b>Criado por <a href="https://www.linkedin.com/in/sergiosantanacloud/">Sérgio Sant'Ana Junior</a></b><br><a href="https://jornada365.cloud">Jornada 365</a> · Sua jornada começa aqui.</p>
<p align="center"><a href="https://jornada365.cloud"><picture><source media="(prefers-color-scheme: dark)" srcset="docs/assets/j365-blog-logo-dark.png"><img src="docs/assets/j365-blog-logo.png" alt="Jornada 365" width="220"></picture></a></p>

Baseado no [PSAppDeployToolkit](https://psappdeploytoolkit.com), com catálogo derivado de [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). Projeto independente, sem afiliação com a Microsoft.

[MIT License](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md) · [Code signing policy](CODE_SIGNING_POLICY.md)
