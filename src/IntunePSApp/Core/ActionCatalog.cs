using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace J365.IntunePSApp.Core
{
    public class ActionField
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Hint { get; set; }
        public string Kind { get; set; } = "text"; // text | multiline | choice | bool | number | code
        public string[] Options { get; set; }
        public string Default { get; set; } = "";
        public bool Required { get; set; }
    }

    public class ActionDef
    {
        public string Type { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public string Glyph { get; set; }
        public string Category { get; set; }
        public string DefaultPhase { get; set; } = "PostInstall";
        public List<ActionField> Fields { get; set; } = new List<ActionField>();
    }

    /// <summary>Acoes prontas (sem script) que viram chamadas do PSAppDeployToolkit no pacote.</summary>
    public static class ActionCatalog
    {
        public static readonly string[] Phases = { "PreInstall", "PostInstall", "PreUninstall", "PostUninstall" };

        public static string PhaseLabel(string p) => p switch
        {
            "PreInstall" => "Antes de instalar",
            "PostInstall" => "Depois de instalar",
            "PreUninstall" => "Antes de desinstalar",
            "PostUninstall" => "Depois de desinstalar",
            _ => p
        };

        public static readonly List<ActionDef> All = new List<ActionDef>
        {
            new ActionDef { Type = "RemoveDesktopShortcut", Category = "Atalhos", Label = "Remover atalho da área de trabalho", Glyph = "",
                Description = "Remove atalhos criados pelo instalador na área de trabalho pública.",
                Fields = { new ActionField { Key = "Name", Label = "Nome do atalho", Hint = "Ex.: 7-Zip File Manager (aceita *). A extensão .lnk é adicionada automaticamente.", Required = true } } },
            new ActionDef { Type = "CreateShortcut", Category = "Atalhos", Label = "Criar atalho", Glyph = "",
                Description = "Cria um atalho na área de trabalho ou no Menu Iniciar de todos os usuários.",
                Fields = {
                    new ActionField { Key = "Location", Label = "Local", Kind = "choice", Options = new[] { "Área de trabalho (todos os usuários)", "Menu Iniciar (todos os usuários)" }, Default = "Área de trabalho (todos os usuários)" },
                    new ActionField { Key = "Name", Label = "Nome do atalho", Required = true },
                    new ActionField { Key = "Target", Label = "Destino", Hint = "Aceita variáveis: %ProgramFiles%\\7-Zip\\7zFM.exe", Required = true },
                    new ActionField { Key = "Arguments", Label = "Argumentos (opcional)" },
                    new ActionField { Key = "Icon", Label = "Ícone (opcional)", Hint = "Caminho de um .exe ou .ico" } } },
            new ActionDef { Type = "CopyFile", Category = "Arquivos", Label = "Copiar arquivo extra para o computador", Glyph = "",
                Description = "Copia um arquivo ou pasta adicionado em 'Arquivos extras' (ex.: licença, configuração) para uma pasta do computador.",
                Fields = {
                    new ActionField { Key = "Source", Label = "Arquivo ou pasta (dos arquivos extras)", Hint = "Ex.: rarreg.key", Required = true },
                    new ActionField { Key = "Destination", Label = "Pasta de destino", Hint = "Ex.: %ProgramFiles%\\WinRAR", Required = true } } },
            new ActionDef { Type = "CopyToUserProfiles", Category = "Arquivos", Label = "Copiar arquivo extra para todos os perfis", Glyph = "",
                Description = "Copia um arquivo extra para o perfil de cada usuário do computador (AppData, Área de trabalho...).",
                Fields = {
                    new ActionField { Key = "Source", Label = "Arquivo ou pasta (dos arquivos extras)", Required = true },
                    new ActionField { Key = "BasePath", Label = "Base", Kind = "choice", Options = new[] { "AppData", "LocalAppData", "Desktop", "Documents", "StartMenu", "Profile" }, Default = "AppData" },
                    new ActionField { Key = "Destination", Label = "Subpasta", Hint = "Ex.: Notepad++ (fica em %AppData%\\Notepad++)", Required = true } } },
            new ActionDef { Type = "DeleteFile", Category = "Arquivos", Label = "Excluir arquivos", Glyph = "",
                Description = "Exclui arquivos (aceita * e variáveis de ambiente).",
                Fields = { new ActionField { Key = "Path", Label = "Caminho", Hint = "Ex.: %Public%\\Desktop\\*.url", Required = true } } },
            new ActionDef { Type = "DeleteFolder", Category = "Arquivos", Label = "Excluir pasta", Glyph = "",
                Description = "Exclui uma pasta e todo o seu conteúdo.",
                Fields = { new ActionField { Key = "Path", Label = "Pasta", Hint = "Ex.: %ProgramData%\\App\\Cache", Required = true } } },
            new ActionDef { Type = "SetRegistry", Category = "Registro", Label = "Definir valor no registro", Glyph = "",
                Description = "Cria ou altera um valor do registro (ex.: desativar atualização automática).",
                Fields = {
                    new ActionField { Key = "Key", Label = "Chave", Hint = "Ex.: HKLM:\\SOFTWARE\\Policies\\Fabricante\\App", Required = true },
                    new ActionField { Key = "Name", Label = "Nome do valor", Required = true },
                    new ActionField { Key = "Value", Label = "Dados" },
                    new ActionField { Key = "Type", Label = "Tipo", Kind = "choice", Options = new[] { "DWord", "String", "ExpandString", "QWord", "MultiString" }, Default = "DWord" } } },
            new ActionDef { Type = "RemoveRegistry", Category = "Registro", Label = "Remover chave ou valor do registro", Glyph = "",
                Description = "Remove um valor (se o nome for informado) ou a chave inteira.",
                Fields = {
                    new ActionField { Key = "Key", Label = "Chave", Required = true },
                    new ActionField { Key = "Name", Label = "Nome do valor (opcional)", Hint = "Em branco: remove a chave inteira." } } },
            new ActionDef { Type = "StopProcess", Category = "Sistema", Label = "Encerrar processos", Glyph = "", DefaultPhase = "PreInstall",
                Description = "Fecha processos sem perguntar (ex.: serviços de bandeja, atualizadores).",
                Fields = { new ActionField { Key = "Names", Label = "Processos", Hint = "Separados por vírgula, sem .exe", Required = true } } },
            new ActionDef { Type = "StopService", Category = "Sistema", Label = "Parar ou desativar serviço", Glyph = "",
                Description = "Para um serviço do Windows e altera o tipo de inicialização (ex.: serviço de atualização do fabricante).",
                Fields = {
                    new ActionField { Key = "Name", Label = "Nome do serviço", Hint = "Ex.: AdobeARMservice, gupdate", Required = true },
                    new ActionField { Key = "Mode", Label = "Ação", Kind = "choice", Options = new[] { "Parar e desativar", "Parar e deixar manual", "Somente parar" }, Default = "Parar e desativar" } } },
            new ActionDef { Type = "DisableScheduledTask", Category = "Sistema", Label = "Desativar tarefa agendada", Glyph = "",
                Description = "Desativa tarefas agendadas (ex.: atualizadores automáticos). Aceita *.",
                Fields = { new ActionField { Key = "Name", Label = "Nome da tarefa", Hint = "Ex.: GoogleUpdateTaskMachine*", Required = true } } },
            new ActionDef { Type = "SetEnvironment", Category = "Sistema", Label = "Definir variável de ambiente", Glyph = "",
                Description = "Cria ou altera uma variável de ambiente do sistema.",
                Fields = {
                    new ActionField { Key = "Name", Label = "Nome", Required = true },
                    new ActionField { Key = "Value", Label = "Valor" } } },
            new ActionDef { Type = "RunProgram", Category = "Execução", Label = "Executar programa", Glyph = "",
                Description = "Executa um programa do computador ou um arquivo extra do pacote.",
                Fields = {
                    new ActionField { Key = "Source", Label = "Origem", Kind = "choice", Options = new[] { "Arquivo extra do pacote", "Caminho no computador" }, Default = "Caminho no computador" },
                    new ActionField { Key = "FilePath", Label = "Programa", Hint = "Ex.: %ProgramFiles%\\App\\config.exe ou nome do arquivo extra", Required = true },
                    new ActionField { Key = "Arguments", Label = "Argumentos (opcional)" },
                    new ActionField { Key = "IgnoreExitCode", Label = "Ignorar o código de saída", Kind = "bool", Default = "true" } } },
            new ActionDef { Type = "RunPowerShell", Category = "Execução", Label = "Executar script PowerShell", Glyph = "",
                Description = "Código PowerShell livre (pode usar as funções do PSAppDeployToolkit).",
                Fields = { new ActionField { Key = "Code", Label = "Código", Kind = "code", Required = true, Hint = "Ex.: Set-ADTRegistryKey -LiteralPath 'HKLM:\\SOFTWARE\\X' -Name 'Y' -Value 1 -Type DWord" } } },
            new ActionDef { Type = "ShowMessage", Category = "Usuário", Label = "Mostrar mensagem ao usuário", Glyph = "",
                Description = "Exibe uma mensagem na janela do PSADT (não aparece no modo silencioso).",
                Fields = {
                    new ActionField { Key = "Text", Label = "Mensagem", Kind = "multiline", Required = true },
                    new ActionField { Key = "Wait", Label = "Aguardar o usuário clicar em OK", Kind = "bool", Default = "false" } } },
            new ActionDef { Type = "Wait", Category = "Usuário", Label = "Aguardar alguns segundos", Glyph = "",
                Description = "Pausa a execução (útil depois de instaladores que continuam trabalhando em segundo plano).",
                Fields = { new ActionField { Key = "Seconds", Label = "Segundos", Kind = "number", Default = "10", Required = true } } },
        };

        public static ActionDef Get(string type) => All.FirstOrDefault(d => d.Type == type);

        public static string Summarize(PackageAction a)
        {
            switch (a.Type)
            {
                case "RemoveDesktopShortcut": return a.P("Name");
                case "CreateShortcut": return $"{a.P("Name")} → {a.P("Target")}";
                case "CopyFile": return $"{a.P("Source")} → {a.P("Destination")}";
                case "CopyToUserProfiles": return $"{a.P("Source")} → %{a.P("BasePath")}%\\{a.P("Destination")}";
                case "DeleteFile":
                case "DeleteFolder": return a.P("Path");
                case "SetRegistry": return $"{a.P("Key")}\\{a.P("Name")} = {a.P("Value")} ({a.P("Type")})";
                case "RemoveRegistry": return string.IsNullOrEmpty(a.P("Name")) ? a.P("Key") : $"{a.P("Key")}\\{a.P("Name")}";
                case "StopProcess": return a.P("Names");
                case "StopService": return $"{a.P("Name")} ({a.P("Mode")})";
                case "DisableScheduledTask": return a.P("Name");
                case "SetEnvironment": return $"{a.P("Name")} = {a.P("Value")}";
                case "RunProgram": return $"{a.P("FilePath")} {a.P("Arguments")}".Trim();
                case "RunPowerShell": return FirstLine(a.P("Code"));
                case "ShowMessage": return FirstLine(a.P("Text"));
                case "Wait": return $"{a.P("Seconds")} segundos";
                default: return "";
            }
        }

        private static string FirstLine(string s)
        {
            var l = (s ?? "").Split('\n').FirstOrDefault()?.Trim() ?? "";
            return l.Length > 80 ? l[..80] + "…" : l;
        }

        public static List<string> Validate(PackageAction a)
        {
            var errs = new List<string>();
            var def = Get(a.Type);
            if (def == null) { errs.Add($"Ação desconhecida: {a.Type}"); return errs; }
            foreach (var f in def.Fields.Where(f => f.Required))
                if (string.IsNullOrWhiteSpace(a.P(f.Key))) errs.Add($"Ação \"{def.Label}\": preencha \"{f.Label}\".");
            if (a.Type == "Wait" && !int.TryParse(a.P("Seconds"), out _)) errs.Add("Ação \"Aguardar\": informe um número de segundos.");
            if (a.Type == "SetRegistry" && !Regex.IsMatch(a.P("Key"), @"^(HKLM|HKCU|HKCR|HKU|HKEY_[A-Z_]+):?\\", RegexOptions.IgnoreCase)) errs.Add("Ação \"Registro\": a chave deve começar com HKLM:\\ ou HKCU:\\.");
            if (a.Type == "RunPowerShell")
            {
                var perr = PowerShellValidator.QuickCheck(a.P("Code"));
                if (perr != null) errs.Add("Ação \"Script PowerShell\": " + perr);
            }
            return errs;
        }

        // ------------------------------------------------------------------ geracao de codigo

        private static string L(string s) => ScriptGenerator.Lit(s);
        private static string Exp(string s) => $"([Environment]::ExpandEnvironmentVariables({L(s)}))";

        private static string Lnk(string name)
        {
            name = (name ?? "").Trim();
            return name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || name.EndsWith("*") ? name : name + ".lnk";
        }

        public static List<string> Generate(PackageAction a)
        {
            var body = new List<string>();
            switch (a.Type)
            {
                case "RemoveDesktopShortcut":
                    body.Add($"Remove-ADTFile -Path (Join-Path -Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) -ChildPath {L(Lnk(a.P("Name")))})");
                    break;
                case "CreateShortcut":
                    {
                        string folder = a.P("Location").StartsWith("Menu", StringComparison.OrdinalIgnoreCase) ? "CommonPrograms" : "CommonDesktopDirectory";
                        string cmd = $"New-ADTShortcut -LiteralPath (Join-Path -Path ([Environment]::GetFolderPath('{folder}')) -ChildPath {L(Lnk(a.P("Name")))}) -TargetPath {Exp(a.P("Target"))}";
                        if (!string.IsNullOrWhiteSpace(a.P("Arguments"))) cmd += $" -Arguments {L(a.P("Arguments"))}";
                        if (!string.IsNullOrWhiteSpace(a.P("Icon"))) cmd += $" -IconLocation {Exp(a.P("Icon"))}";
                        body.Add(cmd);
                        break;
                    }
                case "CopyFile":
                    body.Add($"$j365Dest = {Exp(a.P("Destination"))}");
                    body.Add("if (-not (Test-Path -LiteralPath $j365Dest)) { $null = New-Item -Path $j365Dest -ItemType Directory -Force }");
                    body.Add($"Copy-ADTFile -Path (Join-Path -Path $adtSession.DirSupportFiles -ChildPath {L(a.P("Source"))}) -Destination $j365Dest -Recurse");
                    break;
                case "CopyToUserProfiles":
                    body.Add($"Copy-ADTFileToUserProfiles -Path (Join-Path -Path $adtSession.DirSupportFiles -ChildPath {L(a.P("Source"))}) -Destination {L(a.P("Destination"))} -BasePath {L(string.IsNullOrEmpty(a.P("BasePath")) ? "AppData" : a.P("BasePath"))} -Recurse");
                    break;
                case "DeleteFile":
                    body.Add($"Remove-ADTFile -Path {Exp(a.P("Path"))}");
                    break;
                case "DeleteFolder":
                    body.Add($"$j365Folder = {Exp(a.P("Path"))}");
                    body.Add("if (Test-Path -LiteralPath $j365Folder) { Remove-ADTFolder -LiteralPath $j365Folder }");
                    break;
                case "SetRegistry":
                    {
                        string type = string.IsNullOrEmpty(a.P("Type")) ? "String" : a.P("Type");
                        string v = a.P("Value");
                        string valueExpr = type switch
                        {
                            "DWord" or "QWord" => long.TryParse(v.Trim(), out var n) ? n.ToString() : (v.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? v.Trim() : "0"),
                            "MultiString" => "@(" + string.Join(", ", v.Split(';').Select(x => L(x.Trim()))) + ")",
                            _ => L(v)
                        };
                        body.Add($"Set-ADTRegistryKey -LiteralPath {L(NormalizeKey(a.P("Key")))} -Name {L(a.P("Name"))} -Value {valueExpr} -Type {type}");
                        break;
                    }
                case "RemoveRegistry":
                    if (string.IsNullOrWhiteSpace(a.P("Name"))) body.Add($"Remove-ADTRegistryKey -LiteralPath {L(NormalizeKey(a.P("Key")))} -Recurse");
                    else body.Add($"Remove-ADTRegistryKey -LiteralPath {L(NormalizeKey(a.P("Key")))} -Name {L(a.P("Name"))}");
                    break;
                case "StopProcess":
                    {
                        var names = Names.ParseProcessList(a.P("Names"));
                        body.Add($"Get-Process -Name @({string.Join(", ", names.Select(L))}) -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue");
                        break;
                    }
                case "StopService":
                    {
                        string n = L(a.P("Name"));
                        body.Add($"Stop-Service -Name {n} -Force -ErrorAction SilentlyContinue");
                        if (a.P("Mode").Contains("desativar")) body.Add($"Set-Service -Name {n} -StartupType Disabled");
                        else if (a.P("Mode").Contains("manual")) body.Add($"Set-Service -Name {n} -StartupType Manual");
                        break;
                    }
                case "DisableScheduledTask":
                    body.Add($"Get-ScheduledTask -TaskName {L(a.P("Name"))} -ErrorAction SilentlyContinue | Disable-ScheduledTask | Out-Null");
                    break;
                case "SetEnvironment":
                    body.Add($"[Environment]::SetEnvironmentVariable({L(a.P("Name"))}, {L(a.P("Value"))}, 'Machine')");
                    break;
                case "RunProgram":
                    {
                        string fp = a.P("Source").StartsWith("Arquivo", StringComparison.OrdinalIgnoreCase)
                            ? $"(Join-Path -Path $adtSession.DirSupportFiles -ChildPath {L(a.P("FilePath"))})"
                            : Exp(a.P("FilePath"));
                        string cmd = $"Start-ADTProcess -FilePath {fp}";
                        if (!string.IsNullOrWhiteSpace(a.P("Arguments"))) cmd += $" -ArgumentList {L(a.P("Arguments"))}";
                        if (!string.Equals(a.P("IgnoreExitCode"), "false", StringComparison.OrdinalIgnoreCase)) cmd += " -IgnoreExitCodes '*'";
                        body.Add(cmd);
                        break;
                    }
                case "RunPowerShell":
                    body.AddRange(a.P("Code").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()));
                    break;
                case "ShowMessage":
                    body.Add($"Show-ADTInstallationPrompt -Message {L(a.P("Text"))} -ButtonRightText 'OK' -Icon 'Information'" + (a.P("Wait") == "true" ? "" : " -NoWait"));
                    break;
                case "Wait":
                    body.Add($"Start-Sleep -Seconds {(int.TryParse(a.P("Seconds"), out var s) ? Math.Clamp(s, 1, 3600) : 10)}");
                    break;
            }

            var lines = new List<string>();
            string title = Regex.Replace($"{a.TypeLabel}: {Summarize(a)}", @"[\r\n]+", " ");
            lines.Add($"    ## Ação - {title}");
            if (a.ContinueOnError)
            {
                lines.Add("    try");
                lines.Add("    {");
                lines.AddRange(body.Select(b => "        " + b));
                lines.Add("    }");
                lines.Add("    catch");
                lines.Add("    {");
                lines.Add($"        Write-ADTLogEntry -Message ({L("Falha na ação [" + a.TypeLabel + "]: ")} + $_.Exception.Message) -Severity 2");
                lines.Add("    }");
            }
            else lines.AddRange(body.Select(b => "    " + b));
            lines.Add("");
            return lines;
        }

        private static string NormalizeKey(string key)
        {
            key = (key ?? "").Trim();
            key = Regex.Replace(key, @"^HKEY_LOCAL_MACHINE\\", "HKLM:\\", RegexOptions.IgnoreCase);
            key = Regex.Replace(key, @"^HKEY_CURRENT_USER\\", "HKCU:\\", RegexOptions.IgnoreCase);
            key = Regex.Replace(key, @"^(HKLM|HKCU|HKCR|HKU)\\", "$1:\\", RegexOptions.IgnoreCase);
            return key;
        }
    }
}
