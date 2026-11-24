## J365 IntunePSApp 3.0.0

### Português

Uma escolha no lugar de quatro modos e uma contagem de adiamentos, a remoção do que já está instalado garantida antes de instalar, e uma release que dá menos motivos de dúvida aos antivírus.

- **Instalação silenciosa ou com notificação.** Os modos *Automático*, *Discreto*, *Interativo* e *Silencioso*, a escolha separada das notificações do Intune e os adiamentos viraram uma escolha só:
  - **Com notificação:** as janelas do pacote (aviso de que um aplicativo será fechado, progresso e conclusão) para quem estiver conectado, e as notificações do Intune.
  - **Silenciosa:** nenhuma janela, nenhuma notificação do Intune, nenhum pedido de reinicialização.
  - Projetos e configurações antigos continuam abrindo: *Automático*, *Discreto* e *Interativo* são lidos como *Com notificação*.
- **Sem adiamento.** A opção de o usuário adiar a instalação (3 vezes, por padrão) saiu do editor, dos padrões da empresa, do lote e do script gerado.
- **Tudo o que estiver instalado é removido antes de instalar**, inclusive a mesma versão (a única cópia mantida é uma versão mais nova).
  - Cada cópia sai do jeito que dá para remover sem janela: MSI pelo código do produto; .exe pelos parâmetros do pacote, pelo comando silencioso registrado ou, na falta dele, pelos parâmetros do Inno Setup e do NSIS, reconhecidos no dispositivo.
  - **Se uma cópia continuar instalada, a instalação para com erro** e o log diz qual: a versão nova nunca fica ao lado da antiga. *Se a remoção falhar, instalar por cima mesmo assim* devolve o comportamento da 2.x.
- **Antivírus e Controle Inteligente de Aplicativos:** o executável deixou de ser comprimido e o aplicativo não remove mais a "marca da web" dos arquivos que baixa, dois comportamentos que motores heurísticos associam a programas maliciosos. O bloqueio do Controle Inteligente de Aplicativos só deixa de acontecer com a release **assinada** ([como assinar](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md)).
- **Linha de comando:** `--mode Silent|Notify`; `--notifications` foi removido (é recusado com a explicação).

### English

One choice instead of four modes and a deferral count, the removal of what is already installed guaranteed before installing, and a release that gives antivirus engines less to doubt.

- **Silent installation or with notification.** The *Automatic*, *Discreet*, *Interactive* and *Silent* modes, the separate choice of Intune notifications and the deferrals became one choice:
  - **With notification:** the windows of the package (the notice that an application will be closed, progress and completion) for whoever is signed in, and the notifications of Intune.
  - **Silent:** no window, no notification of Intune, no restart asked.
  - Older projects and settings still open: *Automatic*, *Discreet* and *Interactive* are read as *With notification*.
- **No deferral.** The option to let the user defer the installation (3 times by default) left the editor, the company defaults, the batch and the generated script.
- **Whatever is installed is removed before installing**, the same version included (the only copy kept is a newer version).
  - Each copy goes the way it can be removed without a window: an MSI by its product code; an .exe by the switches of the package, by the silent command it registered or, without one, by the switches of Inno Setup and NSIS, recognised on the device.
  - **If a copy is still installed, the installation stops with an error** and the log names it: the new version never ends up next to the old one. *If the removal fails, install over the existing copy anyway* restores the behaviour of 2.x.
- **Antivirus and Smart App Control:** the executable is no longer compressed and the application no longer removes the "mark of the web" of the files it downloads, two behaviours heuristic engines associate with malicious programs. Smart App Control only stops blocking once the release is **signed** ([how to sign](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md)).
- **Command line:** `--mode Silent|Notify`; `--notifications` was removed (it is refused with the explanation).

See the [CHANGELOG](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md) and the user guide ([English](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/USER-GUIDE.md), [Português](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/USER-GUIDE.pt-BR.md)).

### Download

- `J365-IntunePSApp-3.0.0-win-x64.zip`: extraia e execute `J365.IntunePSApp.exe` / extract and run the portable application.
- `J365-IntunePSApp-3.0.0-source.zip`: código-fonte / source code.
- `SHA256SUMS.txt`: verificações SHA-256 / SHA-256 checksums.

**Já tem o J365 IntunePSApp?**
- Em **Configurações > Geral > Verificar agora** ele baixa e instala esta versão.
- Pacotes gerados por versões anteriores continuam funcionando nos dispositivos. Gere-os de novo para que passem a seguir as regras da 3.0.

**Already using it?**
- **Settings > General > Check now** downloads and installs this version.
- Packages generated by earlier versions keep working on the devices. Generate them again so they follow the rules of 3.0.

Baseado no PSAppDeployToolkit. Criado por Sérgio Sant'Ana Junior — Jornada 365.
