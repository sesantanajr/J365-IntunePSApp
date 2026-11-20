## J365 IntunePSApp 2.4.5

Package any Windows application for Microsoft Intune in minutes, with no scripting. Built on [PSAppDeployToolkit](https://psappdeploytoolkit.com). Created by [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

### Why 2.4.5

A test of the Adobe Acrobat Reader package in a real tenant asked whether a required installation really shows the dialog of the preview. It did not quite: the preview used sentences of its own and listed every program. The generation showed no progress. And publishing failed because the Intune subscription of the tenant had expired, which the application now shows plan by plan.

- **The preview is the dialog of the toolkit:** the texts and the layout of PSAppDeployToolkit 4.1.8 in the language of the dialogs, at three moments of a required installation (an application open, none open, the last notice after the deferrals). The preview also says when the dialog does not appear at all.
- **Progress while generating:** the step, the percentage and the time left, also on the status bar and the taskbar.
- **Test like Intune:** in Windows Sandbox the package runs as SYSTEM, so its dialogs appear as in a required installation.
- **The plans of the tenant:** *Check the subscription* lists the plans of Intune with their status and says how to reactivate them. Publishing retries the upload more, waits for the app to be published before assigning it, keeps the icon on an update, and never creates an app twice.
- **Validity of the certificate:** 6, 12 or 24 months, chosen in the connection window when connecting or renewing (12 by default). A policy of the tenant that limits it is explained.

Full list: [CHANGELOG.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md). How to connect: [docs/INTUNE-PUBLISH.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/INTUNE-PUBLISH.md).

On Windows 11 with **Smart App Control** on, an unsigned release is blocked: see [docs/SIGNING.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md).

### Download

| File | For |
|---|---|
| `J365-IntunePSApp-<version>-win-x64.zip` | **The application.** Extract and run `J365.IntunePSApp.exe`. Windows 10/11 x64, nothing to install. |
| `J365-IntunePSApp-<version>-source.zip` | **Source package** of this exact release, to audit, build or fork. |
| `SHA256SUMS.txt` | Checksums of both files (also used by the update inside the application). |

Verify: `Get-FileHash <file> -Algorithm SHA256` and `gh attestation verify <file> --repo sesantanajr/J365-IntunePSApp`.

---

## J365 IntunePSApp 2.4.5 (Português)

Empacote qualquer aplicativo Windows para o Microsoft Intune em minutos, sem escrever scripts. Baseado no [PSAppDeployToolkit](https://psappdeploytoolkit.com). Criado por [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

**Por que a 2.4.5:** um teste do pacote do Adobe Acrobat Reader num tenant real perguntou se a instalação obrigatória mostra mesmo a janela da prévia. Não exatamente: a prévia usava frases próprias e listava todos os programas. A geração não mostrava progresso. E a publicação falhou porque a assinatura do Intune do tenant expirou, o que o aplicativo agora mostra plano a plano.

- **A prévia é a janela do toolkit:** os textos e o layout do PSAppDeployToolkit 4.1.8, no idioma das janelas, em três momentos de uma instalação obrigatória (um aplicativo aberto, nada aberto, o último aviso depois dos adiamentos). A prévia também avisa quando a janela nem aparece.
- **Progresso ao gerar:** a etapa, a porcentagem e o tempo restante, também na barra de status e na barra de tarefas.
- **Teste como o Intune:** no Windows Sandbox o pacote roda como SYSTEM, e as janelas aparecem como numa instalação obrigatória.
- **Os planos do tenant:** *Verificar a assinatura* lista os planos do Intune com o status deles e diz como reativar. A publicação tenta o envio de novo mais vezes, espera o app ficar publicado antes de atribuir, mantém o ícone numa atualização e nunca cria um app duas vezes.
- **Validade do certificado:** 6, 12 ou 24 meses, escolhida na janela de conexão ao conectar ou renovar (12 por padrão). Uma política do tenant que a limite é explicada.

Lista completa: [CHANGELOG.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md). Como conectar: [docs/INTUNE-PUBLISH.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/INTUNE-PUBLISH.md#publicar-no-intune).

No Windows 11 com o **Controle Inteligente de Aplicativos** ligado, uma versão sem assinatura é bloqueada: veja o [docs/SIGNING.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/docs/SIGNING.md).
