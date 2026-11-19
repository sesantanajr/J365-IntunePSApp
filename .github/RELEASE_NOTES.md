## J365 IntunePSApp 2.0

Package any Windows application for Microsoft Intune in minutes, with no scripting. Built on [PSAppDeployToolkit](https://psappdeploytoolkit.com). Created by [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

### Highlights

- **English and Portuguese** interface, switchable at any time.
- **Automatic first-run setup**: the application downloads and verifies its own dependencies.
- **Dependencies**: Visual C++ Redistributable, .NET Desktop Runtime and others are downloaded, shipped and installed first with one click.
- **Package update tracking**: see which packages have a newer version and update them keeping every customisation.
- **Test in Windows Sandbox** and **Publish to Intune (preview)**.
- 4 new actions (20 in total), optional per-user installation, smarter database updates, many fixes.

Full list: [CHANGELOG.md](https://github.com/sesantanajr/J365-IntunePSApp/blob/main/CHANGELOG.md).

### Download

| File | For |
|---|---|
| `J365-IntunePSApp-<version>-win-x64.zip` | **The application.** Extract and run `J365.IntunePSApp.exe`. Windows 10/11 x64, nothing to install. |
| `J365-IntunePSApp-<version>-source.zip` | **Source package** of this exact release, to audit, build or fork. |
| `SHA256SUMS.txt` | Checksums of both files. |

Verify: `Get-FileHash <file> -Algorithm SHA256` and `gh attestation verify <file> --repo sesantanajr/J365-IntunePSApp`.

The executable is not code-signed: Windows SmartScreen may ask for confirmation (*More info > Run anyway*), and computers with Smart App Control turned on may block it. Upgrading from 1.x: replace the executable; your settings, packages and projects keep working.

---

## J365 IntunePSApp 2.0 (Português)

Empacote qualquer aplicativo Windows para o Microsoft Intune em minutos, sem escrever scripts. Baseado no [PSAppDeployToolkit](https://psappdeploytoolkit.com). Criado por [Sérgio Sant'Ana Junior](https://www.linkedin.com/in/sergiosantanacloud/) - [Jornada 365](https://jornada365.cloud).

- Interface em **inglês e português**, com troca a qualquer momento.
- **Preparação automática na primeira execução**: o aplicativo baixa e verifica as próprias dependências.
- **Dependências** (Visual C++ Redistributable, .NET Desktop Runtime...) baixadas, incluídas no pacote e instaladas antes, com um clique.
- **Acompanhamento de versões**: veja quais pacotes têm versão nova e atualize mantendo todas as personalizações.
- **Teste no Windows Sandbox** e **Publicar no Intune (prévia)**.
- 4 ações novas (20 no total), instalação opcional por usuário, atualização mais inteligente do banco de dados e muitas correções.

Baixe o `...-win-x64.zip`, extraia e execute o `J365.IntunePSApp.exe`. O `...-source.zip` é o pacote de código-fonte desta versão, para auditar, compilar ou fazer um fork. O executável não tem assinatura de código: o SmartScreen pode pedir confirmação (*Mais informações > Executar assim mesmo*). Vindo da 1.x: basta substituir o executável; configurações, pacotes e projetos continuam funcionando.
