# Publish to Intune (preview)

*Português: [abaixo](#publicar-no-intune-prévia).*

Every generated package includes `Intune\Publish-ToIntune.ps1`. It creates the Windows app (Win32) in your tenant from `intune-app.json`: name, description, command lines, detection script, requirements and icon. The app is created **without assignments**, so nothing reaches devices until you assign it.

> **Preview.** The script was written against version 1.5.0 of the community module [IntuneWin32App](https://github.com/MSEndpointMgr/IntuneWin32App) and its validation mode is tested, but the author has not run the upload against a real tenant yet. The manual steps of `Intune-Instructions.html` remain the reference path. Please report results in the issues.

## What you need

1. **Windows PowerShell 5.1** (part of Windows).
2. **An account** allowed to create apps in Intune (for example the *Intune Administrator* role).
3. **Your own app registration** in Microsoft Entra ID. The IntuneWin32App module requires a client ID; there is no shared default application.

### Creating the app registration (once per tenant)

1. [Microsoft Entra admin center](https://entra.microsoft.com) > *Identity > Applications > App registrations > New registration*.
2. Name: for example `Intune app publishing`. Supported account types: *this organizational directory only*.
3. Redirect URI: platform **Public client/native (mobile & desktop)**, value `http://localhost`. Register.
4. *API permissions > Add a permission > Microsoft Graph > Delegated permissions*: `DeviceManagementApps.ReadWrite.All`. Then *Grant admin consent*.
5. *Authentication > Advanced settings*: *Allow public client flows* = **Yes** (needed for the device-code sign-in).
6. Copy the **Application (client) ID** and the **Directory (tenant) ID** from *Overview*.

No client secret is created: you sign in with your own account and the permissions are delegated, so the script can never do more than you can.

## Publishing

**From the application:** after generating a package, *Publish to Intune (preview)* asks for the tenant and the client ID (remembered in Settings; no secret is stored) and opens a PowerShell window.

**By hand:**

```powershell
cd "<Output>\<App>_<Version>\Intune"

# Checks the files and shows what would be published, without connecting
powershell -ExecutionPolicy Bypass -File .\Publish-ToIntune.ps1 -ValidateOnly

# Publishes (a sign-in window opens)
powershell -ExecutionPolicy Bypass -File .\Publish-ToIntune.ps1 -TenantId contoso.onmicrosoft.com -ClientId 00000000-0000-0000-0000-000000000000

# Signs in with a device code instead of the browser window
powershell -ExecutionPolicy Bypass -File .\Publish-ToIntune.ps1 -TenantId contoso.onmicrosoft.com -ClientId <id> -DeviceCode
```

What the script does, in order: reads `intune-app.json`; checks that the files exist and that the `.intunewin` matches the SHA256 recorded when it was generated; asks before installing the IntuneWin32App module for the current user if it is missing; signs in; builds the detection and requirement rules; asks for confirmation; uploads. `-Force` skips the questions.

Afterwards: open the [Intune admin center](https://intune.microsoft.com) > *Apps > Windows*, review the app and assign it to a pilot group.

## Troubleshooting

| Message | What to check |
|---|---|
| `AADSTS7000218` / asks for a client secret | *Allow public client flows* is not enabled, or the redirect URI is not under *Mobile and desktop applications*. |
| `AADSTS65001` (consent) | Admin consent was not granted for `DeviceManagementApps.ReadWrite.All`. |
| `Forbidden` when uploading | Your account has no Intune role that can create apps. |
| "does not accept the requirement values" | Old module: `Update-Module IntuneWin32App`. |

---

# Publicar no Intune (prévia)

Todo pacote gerado inclui o `Intune\Publish-ToIntune.ps1`. Ele cria o aplicativo do Windows (Win32) no seu locatário a partir do `intune-app.json`: nome, descrição, comandos, script de detecção, requisitos e ícone. O aplicativo é criado **sem atribuições**, então nada chega aos dispositivos até você atribuir.

> **Prévia.** O script foi escrito com base na versão 1.5.0 do módulo da comunidade [IntuneWin32App](https://github.com/MSEndpointMgr/IntuneWin32App) e o modo de validação foi testado, mas o autor ainda não executou o envio contra um locatário real. O passo a passo manual do `Intune-Instructions.html` continua sendo o caminho de referência. Conte como foi nas issues.

## O que você precisa

1. **Windows PowerShell 5.1** (já vem no Windows).
2. **Uma conta** com permissão para criar aplicativos no Intune (por exemplo a função *Administrador do Intune*).
3. **Um registro de aplicativo seu** no Microsoft Entra ID. O módulo IntuneWin32App exige um ID de cliente; não existe um aplicativo padrão compartilhado.

### Criando o registro de aplicativo (uma vez por locatário)

1. [Centro de administração do Microsoft Entra](https://entra.microsoft.com) > *Identidade > Aplicativos > Registros de aplicativo > Novo registro*.
2. Nome: por exemplo `Publicação de apps no Intune`. Tipos de conta: *somente este diretório organizacional*.
3. URI de redirecionamento: plataforma **Cliente público/nativo (móvel e desktop)**, valor `http://localhost`. Registre.
4. *Permissões de API > Adicionar uma permissão > Microsoft Graph > Permissões delegadas*: `DeviceManagementApps.ReadWrite.All`. Depois *Conceder consentimento do administrador*.
5. *Autenticação > Configurações avançadas*: *Permitir fluxos de cliente público* = **Sim** (necessário para o login com código de dispositivo).
6. Copie o **ID do aplicativo (cliente)** e o **ID do diretório (locatário)** em *Visão geral*.

Nenhum segredo de cliente é criado: você entra com a sua própria conta e as permissões são delegadas, então o script nunca consegue fazer mais do que você.

## Publicando

**Pelo aplicativo:** depois de gerar um pacote, *Publicar no Intune (prévia)* pede o locatário e o ID do cliente (ficam lembrados em Configurações; nenhum segredo é guardado) e abre uma janela do PowerShell.

**Manualmente:** use os mesmos comandos mostrados na seção em inglês acima (`-ValidateOnly` para conferir sem conectar, `-DeviceCode` para entrar com código de dispositivo).

Depois: abra o [centro de administração do Intune](https://intune.microsoft.com) > *Aplicativos > Windows*, revise o aplicativo e atribua a um grupo piloto.
