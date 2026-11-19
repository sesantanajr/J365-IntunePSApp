## J365 IntunePSApp

Empacote qualquer aplicativo para o **Microsoft Intune** com **PSAppDeployToolkit 4**, sem escrever scripts.

### Download

Baixe `J365-IntunePSApp-<versão>-win-x64.zip`, extraia em qualquer pasta e abra `J365.IntunePSApp.exe`. É um **executável único e portátil**: não precisa instalar nem ter o .NET.

### Novidades desta versão

- Catálogo com **mais de 16 mil aplicativos** (winget + curadoria Jornada 365), atualizado diariamente.
- **Baixar e empacotar** direto do catálogo, com verificação de SHA256.
- Reconhecimento automático de instaladores pelo hash, pelo código MSI ou pelo nome, e detecção da tecnologia do `.exe` (NSIS, Inno Setup, WiX Burn...).
- Identidade visual da empresa: logo, nome, cor, estilo e idioma, com prévia ao vivo.
- Experiência do usuário: fechar apps, adiar, bloquear o app durante a instalação, mensagens personalizadas, reinicialização.
- **Ações sem script**: atalhos, arquivos, registro, serviços, tarefas agendadas, variáveis de ambiente, programas e PowerShell.
- Remoção de versões anteriores (MSI e EXE) e script de detecção do Intune.
- `.intunewin` gerado pelo IntuneWinAppUtil oficial ou pelo empacotador interno compatível, sempre validado.
- Guia `INSTRUCOES_INTUNE.html` com todos os campos do portal.
- Linha de comando: `J365.IntunePSApp.exe --build <instalador|projeto.json>`.

Confira a integridade do download pelo arquivo `SHA256SUMS.txt`.

Feito pelo [Jornada 365](https://jornada365.cloud) · Licença MIT
