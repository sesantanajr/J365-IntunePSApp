using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace J365.IntunePSApp.Core
{
    /// <summary>Gera o Invoke-AppDeployToolkit.ps1, ajusta config/strings do PSADT e cria o script de deteccao do Intune.</summary>
    public static class ScriptGenerator
    {
        /// <summary>Literal PowerShell entre aspas simples (inclui as aspas tipograficas, que o PowerShell tambem trata como aspas).</summary>
        public static string Lit(string text) => "'" + Regex.Replace(text ?? "", "['\u2018\u2019\u201A\u201B]", "$0$0") + "'";

        public static (string Install, string Uninstall, string Repair) Commands(PackageSettings s)
        {
            string mode = s.DeployMode switch { "Silent" => " -DeployMode Silent", "Interactive" => " -DeployMode Interactive", _ => "" };
            return ($"Invoke-AppDeployToolkit.exe -DeploymentType Install{mode}",
                    $"Invoke-AppDeployToolkit.exe -DeploymentType Uninstall{mode}",
                    $"Invoke-AppDeployToolkit.exe -DeploymentType Repair{mode}");
        }

        // ------------------------------------------------------------------ Invoke-AppDeployToolkit.ps1

        public static string DeployScript(string templateText, PackageSettings s, string installerRelative)
        {
            var mStart = Regex.Match(templateText, @"(?m)^\$adtSession\s*=\s*@\{");
            var mInit = Regex.Match(templateText, @"(?m)^##=+\r?\n## MARK: Initialization");
            var mVer = Regex.Match(templateText, @"DeployAppScriptVersion\s*=\s*'([^']+)'");
            if (!mStart.Success || !mInit.Success || !mVer.Success || mInit.Index < mStart.Index)
                throw new InvalidDataException("O template Invoke-AppDeployToolkit.ps1 não tem a estrutura esperada do PSAppDeployToolkit 4.");
            string header = templateText.Substring(0, mStart.Index);
            string tail = templateText.Substring(mInit.Index);

            var sb = new StringBuilder();
            sb.Append("# Gerado pelo J365 IntunePSApp ").Append(AppInfo.Version).Append(" em ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
              .Append(" - ").Append(s.AppName).Append(' ').Append(s.AppVersion).Append(" - ").Append(AppInfo.RepoUrl).Append("\r\n");
            sb.Append(header);
            sb.Append(Variables(s, mVer.Groups[1].Value)).Append("\r\n\r\n");
            sb.Append(Function("Install-ADTDeployment", InstallBody(s, installerRelative))).Append("\r\n\r\n");
            sb.Append(Function("Uninstall-ADTDeployment", UninstallBody(s))).Append("\r\n\r\n");
            sb.Append(Function("Repair-ADTDeployment", RepairBody(s, installerRelative))).Append("\r\n\r\n\r\n");
            sb.Append(tail);
            return sb.ToString();
        }

        private static string Variables(PackageSettings s, string deployVersion)
        {
            var success = new List<int> { 0 };
            foreach (var c in Regex.Split(s.ExtraSuccessCodes ?? "", @"[,;\s]+"))
                if (int.TryParse(c, out int n) && !success.Contains(n)) success.Add(n);
            string procs = s.ProcessesToClose.Count == 0 ? "@()"
                : "@(" + string.Join(", ", s.ProcessesToClose.Select(p => $"@{{ Name = {Lit(p)}; Description = {Lit(s.AppName)} }}")) + ")";
            string author = string.IsNullOrWhiteSpace(s.Author) ? "J365 IntunePSApp" : s.Author;
            var L = new List<string>
            {
                "$adtSession = @{",
                "    # Variaveis do aplicativo (geradas pelo J365 IntunePSApp).",
                $"    AppVendor = {Lit(s.AppVendor)}",
                $"    AppName = {Lit(s.AppName)}",
                $"    AppVersion = {Lit(s.AppVersion)}",
                $"    AppArch = {Lit(s.AppArch)}",
                "    AppLang = 'PT-BR'",
                "    AppRevision = '01'",
                $"    AppSuccessExitCodes = @({string.Join(", ", success)})",
                "    AppRebootExitCodes = @(1641, 3010)",
                $"    AppProcessesToClose = {procs}",
                "    AppScriptVersion = '1.0.0'",
                $"    AppScriptDate = '{DateTime.Now:yyyy-MM-dd}'",
                $"    AppScriptAuthor = {Lit(author)}",
                $"    RequireAdmin = {(s.InstallBehavior == "User" ? "$false" : "$true")}",
                "",
                "    # Titulos das janelas.",
                "    InstallName = ''",
                $"    InstallTitle = {Lit((s.AppName + " " + s.AppVersion).Trim())}",
                "",
                "    # Variaveis do script (nao alterar).",
                "    DeployAppScriptFriendlyName = $MyInvocation.MyCommand.Name",
                "    DeployAppScriptParameters = $PSBoundParameters",
                $"    DeployAppScriptVersion = '{deployVersion}'",
                "}"
            };
            return string.Join("\r\n", L);
        }

        private static string Function(string name, List<string> body)
        {
            var L = new List<string> { $"function {name}", "{", "    [CmdletBinding()]", "    param", "    (", "    )", "" };
            L.AddRange(body);
            L.Add("}");
            return string.Join("\r\n", L);
        }

        private static void Mark(List<string> L, string title, string phaseExpr)
        {
            L.Add("    ##================================================");
            L.Add($"    ## MARK: {title}");
            L.Add("    ##================================================");
            L.Add($"    $adtSession.InstallPhase = {phaseExpr}");
            L.Add("");
        }

        private static void AddActions(List<string> L, PackageSettings s, string phase)
        {
            foreach (var a in s.Actions.Where(a => a.Phase == phase)) L.AddRange(ActionCatalog.Generate(a));
        }

        private static List<string> InstallBody(PackageSettings s, string installerRel)
        {
            var L = new List<string>();
            Mark(L, "Pre-Install", "\"Pre-$($adtSession.DeploymentType)\"");
            bool hasProc = s.ProcessesToClose.Count > 0;
            bool hasMsg = !string.IsNullOrWhiteSpace(s.WelcomeMessage);
            if (hasProc || s.AllowDefer || hasMsg)
            {
                L.Add("    ## Janela de boas-vindas: fecha os aplicativos em uso, permite adiar e verifica o espaco em disco.");
                L.Add("    $saiwParams = @{");
                L.Add("        PersistPrompt = $true");
                if (s.CheckDiskSpace) L.Add("        CheckDiskSpace = $true");
                if (s.AllowDefer) { L.Add("        AllowDefer = $true"); L.Add($"        DeferTimes = {Math.Clamp(s.DeferTimes, 1, 99)}"); }
                if (hasMsg) L.Add("        CustomText = $true");
                L.Add("    }");
                L.Add("    if ($adtSession.AppProcessesToClose.Count -gt 0)");
                L.Add("    {");
                L.Add("        $saiwParams.Add('CloseProcesses', $adtSession.AppProcessesToClose)");
                if (s.CloseAppsCountdown > 0) L.Add($"        $saiwParams.Add('CloseProcessesCountdown', {Math.Min(s.CloseAppsCountdown, 3000)})");
                if (s.BlockExecution) L.Add("        $saiwParams.Add('BlockExecution', $true)");
                L.Add("    }");
                L.Add("    Show-ADTInstallationWelcome @saiwParams");
                L.Add("");
            }
            else
            {
                L.Add("    ## Janela de boas-vindas desativada (sem aplicativos para fechar, sem adiamento e sem mensagem).");
                L.Add("");
            }
            L.Add("    ## Janela de progresso.");
            L.Add(string.IsNullOrWhiteSpace(s.ProgressMessage) ? "    Show-ADTInstallationProgress" : $"    Show-ADTInstallationProgress -StatusMessage {Lit(s.ProgressMessage)}");
            L.Add("");
            if (s.RemovePreviousVersions) { L.AddRange(RemoveLines(s, true)); L.Add(""); }
            AddActions(L, s, "PreInstall");
            L.Add("");
            Mark(L, "Install", "$adtSession.DeploymentType");
            L.Add($"    ## Instala {s.AppName} {s.AppVersion}.");
            L.Add(InstallCommand(s, installerRel, "Install"));
            L.Add("");
            L.Add("");
            Mark(L, "Post-Install", "\"Post-$($adtSession.DeploymentType)\"");
            AddActions(L, s, "PostInstall");
            if (!string.IsNullOrWhiteSpace(s.CompletionMessage))
            {
                L.Add("    ## Mensagem ao final da instalacao.");
                L.Add($"    Show-ADTInstallationPrompt -Message {Lit(s.CompletionMessage)} -ButtonRightText 'OK' -Icon 'Information' -NoWait");
                L.Add("");
            }
            if (s.RestartPrompt)
            {
                int cd = Math.Clamp(s.RestartCountdown, 60, 86400);
                L.Add("    ## Solicita a reinicializacao do computador.");
                L.Add($"    Show-ADTInstallationRestartPrompt -CountdownSeconds {cd} -CountdownNoHideSeconds {Math.Min(60, cd)}");
            }
            return L;
        }

        private static List<string> UninstallBody(PackageSettings s)
        {
            var L = new List<string>();
            Mark(L, "Pre-Uninstall", "\"Pre-$($adtSession.DeploymentType)\"");
            L.Add("    ## Se houver aplicativos abertos, pede para fechar (fecha automaticamente apos 60 segundos).");
            L.Add("    if ($adtSession.AppProcessesToClose.Count -gt 0)");
            L.Add("    {");
            L.Add("        Show-ADTInstallationWelcome -CloseProcesses $adtSession.AppProcessesToClose -CloseProcessesCountdown 60");
            L.Add("    }");
            L.Add("");
            L.Add("    ## Janela de progresso.");
            L.Add("    Show-ADTInstallationProgress");
            L.Add("");
            AddActions(L, s, "PreUninstall");
            L.Add("");
            Mark(L, "Uninstall", "$adtSession.DeploymentType");
            L.AddRange(RemoveLines(s, false));
            L.Add("");
            L.Add("");
            Mark(L, "Post-Uninstallation", "\"Post-$($adtSession.DeploymentType)\"");
            AddActions(L, s, "PostUninstall");
            return L;
        }

        private static List<string> RepairBody(PackageSettings s, string installerRel)
        {
            var L = new List<string>();
            Mark(L, "Pre-Repair", "\"Pre-$($adtSession.DeploymentType)\"");
            L.Add("    if ($adtSession.AppProcessesToClose.Count -gt 0)");
            L.Add("    {");
            L.Add("        Show-ADTInstallationWelcome -CloseProcesses $adtSession.AppProcessesToClose -CloseProcessesCountdown 60");
            L.Add("    }");
            L.Add("    Show-ADTInstallationProgress");
            L.Add("");
            L.Add("");
            Mark(L, "Repair", "$adtSession.DeploymentType");
            if (s.InstallerType == "MSI") L.Add(InstallCommand(s, installerRel, "Repair"));
            else
            {
                L.Add("    ## Reparo de instaladores .exe = reinstalar por cima.");
                L.Add(InstallCommand(s, installerRel, "Install"));
            }
            L.Add("");
            L.Add("");
            Mark(L, "Post-Repair", "\"Post-$($adtSession.DeploymentType)\"");
            return L;
        }

        private static string InstallCommand(PackageSettings s, string installerRel, string action)
        {
            string rel = Lit(installerRel);
            string args = (s.InstallArgs ?? "").Trim();
            if (s.InstallerType == "MSI")
            {
                string c = $"    Start-ADTMsiProcess -Action '{action}' -FilePath (Join-Path -Path $adtSession.DirFiles -ChildPath {rel})";
                if (args.Length > 0 && action == "Install") c += $" -AdditionalArgumentList {Lit(args)}";
                return c;
            }
            string cmd = $"    Start-ADTProcess -FilePath (Join-Path -Path $adtSession.DirFiles -ChildPath {rel})";
            if (args.Length > 0) cmd += $" -ArgumentList {Lit(args)}";
            if (s.WaitForChildProcesses) cmd += " -WaitForChildProcesses";
            return cmd;
        }

        private static List<string> RemoveLines(PackageSettings s, bool continueOnError)
        {
            string pat = Lit(s.UninstallPattern);
            string msi = $"Uninstall-ADTApplication -Name {pat} -NameMatch 'Wildcard' -ApplicationType 'MSI'";
            string ua = (s.UninstallExeArgs ?? "").Trim();
            string exe = ua.Length > 0
                ? $"Uninstall-ADTApplication -Name {pat} -NameMatch 'Wildcard' -ApplicationType 'EXE' -ArgumentList {Lit(ua)}"
                : $"Uninstall-ADTApplication -Name {pat} -NameMatch 'Wildcard' -ApplicationType 'EXE' -FilterScript {{ ![System.String]::IsNullOrWhiteSpace($_.QuietUninstallString) }}";
            var L = new List<string>();
            if (continueOnError)
            {
                L.Add("    ## Remove qualquer versao ja instalada (MSI e EXE). Uma falha aqui e registrada no log, mas nao impede a instalacao.");
                foreach (var c in new[] { msi, exe })
                {
                    L.Add("    try");
                    L.Add("    {");
                    L.Add("        " + c);
                    L.Add("    }");
                    L.Add("    catch");
                    L.Add("    {");
                    L.Add("        Write-ADTLogEntry -Message \"Falha ao remover versao anterior: $($_.Exception.Message)\" -Severity 2");
                    L.Add("    }");
                }
            }
            else
            {
                L.Add("    ## Remove o aplicativo (todas as versoes encontradas, MSI e EXE).");
                L.Add("    " + msi);
                L.Add("    " + exe);
            }
            return L;
        }

        // ------------------------------------------------------------------ config.psd1 e strings.psd1

        private static string SetPsd1Value(string text, string section, string key, string value)
        {
            var rx = new Regex(@"(?ms)(^\s*" + Regex.Escape(section) + @"\s*=\s*@\{.*?^\s*" + Regex.Escape(key) + @"\s*=[ \t]*)([^\r\n]*)");
            if (!rx.IsMatch(text)) throw new InvalidDataException($"Chave '{section}.{key}' não encontrada no config.psd1 do PSADT.");
            return rx.Replace(text, m => m.Groups[1].Value + value, 1);
        }

        public static void UpdateConfig(string configPath, PackageSettings s)
        {
            var (t, bom) = TextFiles.Read(configPath);
            if (!string.IsNullOrWhiteSpace(s.CompanyName)) t = SetPsd1Value(t, "Toolkit", "CompanyName", Lit(s.CompanyName));
            if (s.LogsInIntuneFolder) t = SetPsd1Value(t, "Toolkit", "LogPath", "'$envProgramData\\Microsoft\\IntuneManagementExtension\\Logs'");
            t = SetPsd1Value(t, "UI", "DialogStyle", s.DialogStyle == "Classic" ? "'Classic'" : "'Fluent'");
            var hex = (s.AccentColor ?? "").Trim().TrimStart('#');
            if (Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$")) t = SetPsd1Value(t, "UI", "FluentAccentColor", "0xFF" + hex.ToUpperInvariant());
            if (!string.IsNullOrEmpty(s.Language) && s.Language != "Auto") t = SetPsd1Value(t, "UI", "LanguageOverride", Lit(s.Language));
            TextFiles.Write(configPath, t, bom);
        }

        public static int UpdateStrings(string stringsDir, PackageSettings s)
        {
            string msg = (s.WelcomeMessage ?? "").Trim();
            if (msg.Length == 0 || !Directory.Exists(stringsDir)) return 0;
            int count = 0;
            foreach (var f in Directory.GetFiles(stringsDir, "strings.psd1", SearchOption.AllDirectories))
            {
                var (t, bom) = TextFiles.Read(f);
                string n = SetCustomMessage(t, "CloseAppsPrompt", msg);
                if (n != t) { TextFiles.Write(f, n, bom); count++; }
            }
            return count;
        }

        private static string SetCustomMessage(string text, string section, string message)
        {
            var start = Regex.Match(text, @"(?m)^[ \t]*" + Regex.Escape(section) + @"[ \t]*=[ \t]*@\{");
            if (!start.Success) return text;
            int from = start.Index + start.Length;
            var next = Regex.Match(text.Substring(from), @"(?m)^ {4}\w+[ \t]*=[ \t]*@\{");
            int limit = next.Success ? from + next.Index : text.Length;
            var m = new Regex("CustomMessage[ \\t]*=[ \\t]*('(?:[^']|'')*'|\"[^\"]*\"|\\$null)").Match(text, start.Index);
            if (!m.Success || m.Index >= limit) return text;
            return text.Substring(0, m.Index) + "CustomMessage = " + Lit(message) + text.Substring(m.Index + m.Length);
        }

        // ------------------------------------------------------------------ deteccao (Intune)

        public static string DetectionScript(PackageSettings s)
        {
            bool check = s.DetectionCheckVersion && !string.IsNullOrWhiteSpace(s.DetectionVersion);
            string title = Ascii($"{s.AppName} {s.AppVersion}");
            var sb = new StringBuilder();
            sb.AppendLine("<#");
            sb.AppendLine("    Script de deteccao - Microsoft Intune (Win32)");
            sb.AppendLine($"    Aplicativo : {title}");
            sb.AppendLine($"    Gerado em  : {DateTime.Now:yyyy-MM-dd HH:mm} por J365 IntunePSApp ({AppInfo.RepoUrl})");
            sb.AppendLine("    No Intune  : Regras de deteccao > Usar script de deteccao personalizado");
            sb.AppendLine("                 - Executar script como processo de 32 bits em clientes de 64 bits: Nao");
            sb.AppendLine("                 - Impor verificacao de assinatura do script: Nao");
            sb.AppendLine("    Resultado  : escreve na saida e retorna 0 quando o aplicativo esta instalado.");
            sb.AppendLine("#>");
            sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            sb.AppendLine($"$CheckVersion = {(check ? "$true" : "$false")}");
            sb.AppendLine($"$MinimumVersion = {Lit(Ascii(s.DetectionVersion ?? ""))}");
            sb.AppendLine();
            sb.AppendLine(@"function ConvertTo-VersionParts([string]$Value)
{
    $parts = @()
    foreach ($m in [regex]::Matches([string]$Value, '\d+')) { if ($parts.Count -lt 4) { $parts += [decimal]$m.Value } }
    while ($parts.Count -lt 4) { $parts += [decimal]0 }
    return ,$parts
}

function Test-VersionOk([string]$Installed)
{
    if (-not $CheckVersion) { return $true }
    if ([string]::IsNullOrWhiteSpace($Installed)) { return $false }
    $a = ConvertTo-VersionParts $Installed
    $b = ConvertTo-VersionParts $MinimumVersion
    for ($i = 0; $i -lt 4; $i++)
    {
        if ($a[$i] -gt $b[$i]) { return $true }
        if ($a[$i] -lt $b[$i]) { return $false }
    }
    return $true
}
");
            if (s.DetectionMethod == "File")
            {
                sb.AppendLine("# Regra: o arquivo existe (e a versao do arquivo e igual ou superior a minima).");
                sb.AppendLine($"$FilePath = [Environment]::ExpandEnvironmentVariables({Lit(Ascii(s.DetectionFilePath))})");
                sb.AppendLine(@"if (Test-Path -LiteralPath $FilePath -PathType Leaf)
{
    $fv = (Get-Item -LiteralPath $FilePath).VersionInfo
    $installed = '{0}.{1}.{2}.{3}' -f $fv.FileMajorPart, $fv.FileMinorPart, $fv.FileBuildPart, $fv.FilePrivatePart
    if (Test-VersionOk $installed)
    {
        Write-Output ""Detectado: $FilePath ($installed)""
        exit 0
    }
}
exit 1");
            }
            else
            {
                string match;
                if (s.DetectionMethod == "ProductCode")
                {
                    string code = (s.MsiProductCode ?? "").Trim();
                    if (Guid.TryParse(code, out var g)) code = g.ToString("B").ToUpperInvariant();
                    sb.AppendLine("# Regra: chave de desinstalacao (codigo do produto) presente (e versao igual ou superior a minima).");
                    sb.AppendLine($"$ProductCode = {Lit(Ascii(code))}");
                    match = "($sub -eq $ProductCode)";
                }
                else
                {
                    sb.AppendLine("# Regra: programa instalado cujo nome corresponde ao padrao (e versao igual ou superior a minima).");
                    sb.AppendLine($"$NamePattern = {Lit(Ascii(s.DetectionNamePattern, '?'))}");
                    match = "($dn -and ($dn -like $NamePattern))";
                }
                sb.AppendLine($"$IncludeCurrentUser = {(s.InstallBehavior == "User" ? "$true" : "$false")}");
                sb.AppendLine(@"
$targets = @(
    @([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64),
    @([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry32)
)
if ($IncludeCurrentUser) { $targets += ,@([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Default) }

foreach ($t in $targets)
{
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($t[0], $t[1])
    $key = $base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')
    if (-not $key) { continue }
    foreach ($sub in $key.GetSubKeyNames())
    {
        $k = $key.OpenSubKey($sub)
        if (-not $k) { continue }
        $dn = [string]$k.GetValue('DisplayName')
        $dv = [string]$k.GetValue('DisplayVersion')
        $k.Close()
        if (" + match + @" -and (Test-VersionOk $dv))
        {
            Write-Output ""Detectado: $dn $dv""
            exit 0
        }
    }
    $key.Close()
}
exit 1");
            }
            return Ascii(sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }

        /// <summary>Mantem o texto 100% ASCII (remove acentos; caracteres restantes viram '?').</summary>
        public static string Ascii(string text, char replacement = '?')
        {
            var norm = (text ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(norm.Length);
            foreach (char c in norm)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c < 128 ? c : replacement);
            }
            return sb.ToString();
        }
    }
}
