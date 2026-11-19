using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using J365.IntunePSApp.Localization;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>One input of an action. Values are stored as language-neutral codes; labels come from the string tables.</summary>
    public class ActionField
    {
        public string Owner { get; set; }
        public string Key { get; set; }
        public string Kind { get; set; } = "text"; // text | multiline | choice | bool | number | code
        public string[] Options { get; set; }
        public string Default { get; set; } = "";
        public bool Required { get; set; }

        public string Label => T($"ActF_{Owner}_{Key}");
        public string Hint => Loc.Instance.Has($"ActH_{Owner}_{Key}") ? T($"ActH_{Owner}_{Key}") : null;
        public string OptionLabel(string code) => ActionCatalog.OptionLabel(Owner, Key, code);
    }

    public class ActionDef
    {
        public string Type { get; set; }
        public string Glyph { get; set; }
        public string Category { get; set; }
        public string DefaultPhase { get; set; } = "PostInstall";
        public List<ActionField> Fields { get; set; } = new List<ActionField>();

        public string Label => T("Act_" + Type);
        public string LabelEn => En("Act_" + Type);
        public string Description => T("ActD_" + Type);
        public string CategoryLabel => T("ActCat_" + Category);
    }

    /// <summary>
    /// Ready-made actions (no scripting required). Each one becomes PSAppDeployToolkit / PowerShell calls
    /// inside the generated Invoke-AppDeployToolkit.ps1.
    /// </summary>
    public static class ActionCatalog
    {
        public static readonly string[] Phases = { "PreInstall", "PostInstall", "PreUninstall", "PostUninstall" };

        public static string PhaseLabel(string phase) => T("Phase_" + (Phases.Contains(phase) ? phase : "PostInstall"));

        private static ActionField F(string key, string kind = "text", bool required = false, string def = "", params string[] options)
            => new ActionField { Key = key, Kind = kind, Required = required, Default = def, Options = options.Length > 0 ? options : null };

        public static readonly List<ActionDef> All = Build();

        private static List<ActionDef> Build()
        {
            var list = new List<ActionDef>
            {
                new ActionDef { Type = "InstallDependency", Category = "Apps", Glyph = "", DefaultPhase = "PreInstall",
                    Fields = { F("Source", required: true), F("Arguments"), F("SuccessCodes", def: "0, 1638") } },
                new ActionDef { Type = "UninstallApp", Category = "Apps", Glyph = "", DefaultPhase = "PreInstall",
                    Fields = { F("NamePattern", required: true), F("ExeArguments") } },
                new ActionDef { Type = "RemoveDesktopShortcut", Category = "Shortcuts", Glyph = "",
                    Fields = { F("Name", required: true) } },
                new ActionDef { Type = "RemoveStartMenuShortcut", Category = "Shortcuts", Glyph = "",
                    Fields = { F("Name", required: true) } },
                new ActionDef { Type = "CreateShortcut", Category = "Shortcuts", Glyph = "",
                    Fields = { F("Location", "choice", def: "Desktop", options: new[] { "Desktop", "StartMenu" }), F("Name", required: true), F("Target", required: true), F("Arguments"), F("Icon") } },
                new ActionDef { Type = "CopyFile", Category = "Files", Glyph = "",
                    Fields = { F("Source", required: true), F("Destination", required: true) } },
                new ActionDef { Type = "CopyToUserProfiles", Category = "Files", Glyph = "",
                    Fields = { F("Source", required: true), F("BasePath", "choice", def: "AppData", options: new[] { "AppData", "LocalAppData", "Desktop", "Documents", "StartMenu", "Profile" }), F("Destination", required: true) } },
                new ActionDef { Type = "DeleteFile", Category = "Files", Glyph = "",
                    Fields = { F("Path", required: true) } },
                new ActionDef { Type = "DeleteFolder", Category = "Files", Glyph = "",
                    Fields = { F("Path", required: true) } },
                new ActionDef { Type = "SetRegistry", Category = "Registry", Glyph = "",
                    Fields = { F("Key", required: true), F("Name", required: true), F("Value"), F("Type", "choice", def: "DWord", options: new[] { "DWord", "String", "ExpandString", "QWord", "MultiString" }) } },
                new ActionDef { Type = "RemoveRegistry", Category = "Registry", Glyph = "",
                    Fields = { F("Key", required: true), F("Name") } },
                new ActionDef { Type = "ImportRegFile", Category = "Registry", Glyph = "",
                    Fields = { F("Source", required: true) } },
                new ActionDef { Type = "StopProcess", Category = "System", Glyph = "", DefaultPhase = "PreInstall",
                    Fields = { F("Names", required: true) } },
                new ActionDef { Type = "StopService", Category = "System", Glyph = "",
                    Fields = { F("Name", required: true), F("Mode", "choice", def: "StopDisable", options: new[] { "StopDisable", "StopManual", "StopOnly" }) } },
                new ActionDef { Type = "DisableScheduledTask", Category = "System", Glyph = "",
                    Fields = { F("Name", required: true) } },
                new ActionDef { Type = "SetEnvironment", Category = "System", Glyph = "",
                    Fields = { F("Name", required: true), F("Value") } },
                new ActionDef { Type = "RunProgram", Category = "Run", Glyph = "",
                    Fields = { F("Source", "choice", def: "Path", options: new[] { "Path", "Package" }), F("FilePath", required: true), F("Arguments"), F("IgnoreExitCode", "bool", def: "true") } },
                new ActionDef { Type = "RunPowerShell", Category = "Run", Glyph = "",
                    Fields = { F("Code", "code", required: true) } },
                new ActionDef { Type = "ShowMessage", Category = "User", Glyph = "",
                    Fields = { F("Text", "multiline", required: true), F("Wait", "bool", def: "false") } },
                new ActionDef { Type = "Wait", Category = "User", Glyph = "",
                    Fields = { F("Seconds", "number", required: true, def: "10") } },
            };
            foreach (var d in list) foreach (var f in d.Fields) f.Owner = d.Type;
            return list;
        }

        public static ActionDef Get(string type) => All.FirstOrDefault(d => d.Type == type);

        /// <summary>Display text of a stored option code (codes without a translation, such as registry types, are shown as they are).</summary>
        public static string OptionLabel(string type, string field, string code)
        {
            string key = $"ActO_{type}_{field}_{code}";
            return Loc.Instance.Has(key) ? T(key) : code ?? "";
        }

        public static string Summarize(PackageAction a)
        {
            switch (a.Type)
            {
                case "InstallDependency": return $"{a.P("Source")} {a.P("Arguments")}".Trim();
                case "UninstallApp": return a.P("NamePattern");
                case "RemoveDesktopShortcut":
                case "RemoveStartMenuShortcut": return a.P("Name");
                case "CreateShortcut": return $"{a.P("Name")} → {a.P("Target")}";
                case "CopyFile": return $"{a.P("Source")} → {a.P("Destination")}";
                case "CopyToUserProfiles": return $"{a.P("Source")} → %{a.P("BasePath")}%\\{a.P("Destination")}";
                case "DeleteFile":
                case "DeleteFolder": return a.P("Path");
                case "SetRegistry": return $"{a.P("Key")}\\{a.P("Name")} = {a.P("Value")} ({a.P("Type")})";
                case "RemoveRegistry": return string.IsNullOrEmpty(a.P("Name")) ? a.P("Key") : $"{a.P("Key")}\\{a.P("Name")}";
                case "ImportRegFile": return a.P("Source");
                case "StopProcess": return a.P("Names");
                case "StopService": return $"{a.P("Name")} ({OptionLabel("StopService", "Mode", a.P("Mode"))})";
                case "DisableScheduledTask": return a.P("Name");
                case "SetEnvironment": return $"{a.P("Name")} = {a.P("Value")}";
                case "RunProgram": return $"{a.P("FilePath")} {a.P("Arguments")}".Trim();
                case "RunPowerShell": return FirstLine(a.P("Code"));
                case "ShowMessage": return FirstLine(a.P("Text"));
                case "Wait": return T("Act_Wait_Summary", a.P("Seconds"));
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
            if (def == null) { errs.Add(T("Val_ActionUnknown", a.Type)); return errs; }
            foreach (var f in def.Fields.Where(f => f.Required))
                if (string.IsNullOrWhiteSpace(a.P(f.Key))) errs.Add(T("Val_ActionField", def.Label, f.Label));
            if (a.Type == "Wait" && !int.TryParse(a.P("Seconds"), out _)) errs.Add(T("Val_ActionSeconds"));
            if ((a.Type == "SetRegistry" || a.Type == "RemoveRegistry") && !string.IsNullOrWhiteSpace(a.P("Key"))
                && !Regex.IsMatch(a.P("Key"), @"^(HKLM|HKCU|HKCR|HKU|HKEY_[A-Z_]+):?\\", RegexOptions.IgnoreCase)) errs.Add(T("Val_ActionRegistryKey"));
            if (a.Type == "UninstallApp" && Regex.Replace(a.P("NamePattern"), @"[\*\?\s]", "").Length < 3) errs.Add(T("Val_PatternTooGeneric", a.P("NamePattern")));
            if (a.Type == "InstallDependency" && !string.IsNullOrWhiteSpace(a.P("SuccessCodes")) && !Regex.IsMatch(a.P("SuccessCodes"), @"^[\d,;\s\-]+$")) errs.Add(T("Val_SuccessCodes"));
            if (a.Type == "RunPowerShell")
            {
                var perr = PowerShellValidator.QuickCheck(a.P("Code"));
                if (perr != null) errs.Add(T("Val_ActionScript", perr));
            }
            return errs;
        }

        /// <summary>Converts values saved by version 1.x (Portuguese display texts) into language-neutral codes.</summary>
        public static void Normalize(PackageAction a)
        {
            if (a?.Params == null) return;
            void Map(string key, params (string Old, string Code)[] map)
            {
                if (!a.Params.TryGetValue(key, out var v) || v == null) return;
                foreach (var (old, code) in map) if (v.StartsWith(old, StringComparison.OrdinalIgnoreCase)) { a.Params[key] = code; return; }
            }
            switch (a.Type)
            {
                case "CreateShortcut": Map("Location", ("Área de trabalho", "Desktop"), ("Menu Iniciar", "StartMenu")); break;
                case "StopService": Map("Mode", ("Parar e desativar", "StopDisable"), ("Parar e deixar manual", "StopManual"), ("Somente parar", "StopOnly")); break;
                case "RunProgram": Map("Source", ("Arquivo extra", "Package"), ("Caminho no computador", "Path")); break;
            }
            if (!Phases.Contains(a.Phase)) a.Phase = Get(a.Type)?.DefaultPhase ?? "PostInstall";
        }

        // ------------------------------------------------------------------ code generation

        private static string L(string s) => ScriptGenerator.Lit(s);
        private static string Exp(string s) => $"([Environment]::ExpandEnvironmentVariables({L(s)}))";
        private static string Support(string name) => $"(Join-Path -Path $adtSession.DirSupportFiles -ChildPath {L(name)})";

        private static string Lnk(string name)
        {
            name = (name ?? "").Trim();
            return name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || name.EndsWith("*") ? name : name + ".lnk";
        }

        private static string CodeList(string text, string fallback)
        {
            var codes = Regex.Split(text ?? "", @"[,;\s]+").Where(c => Regex.IsMatch(c, @"^-?\d+$")).Distinct().ToList();
            return codes.Count == 0 ? fallback : string.Join(", ", codes);
        }

        /// <summary>PowerShell lines (4-space indented) for one action, including the optional try/catch wrapper.</summary>
        public static List<string> Generate(PackageAction a)
        {
            var body = new List<string>();
            switch (a.Type)
            {
                case "InstallDependency":
                    {
                        string file = a.P("Source");
                        string args = a.P("Arguments").Trim();
                        string codes = CodeList(a.P("SuccessCodes"), "0, 1638");
                        if (Path.GetExtension(file).Equals(".msi", StringComparison.OrdinalIgnoreCase))
                            body.Add($"Start-ADTMsiProcess -Action 'Install' -FilePath {Support(file)}" + (args.Length > 0 ? $" -AdditionalArgumentList {L(args)}" : "") + $" -SuccessExitCodes @({codes})");
                        else
                            body.Add($"Start-ADTProcess -FilePath {Support(file)}" + (args.Length > 0 ? $" -ArgumentList {L(args)}" : "") + $" -SuccessExitCodes @({codes})");
                        break;
                    }
                case "UninstallApp":
                    {
                        string pat = L(a.P("NamePattern"));
                        string ua = a.P("ExeArguments").Trim();
                        body.Add($"Uninstall-ADTApplication -Name {pat} -NameMatch 'Wildcard' -ApplicationType 'MSI'");
                        body.Add(ua.Length > 0
                            ? $"Uninstall-ADTApplication -Name {pat} -NameMatch 'Wildcard' -ApplicationType 'EXE' -ArgumentList {L(ua)}"
                            : $"Uninstall-ADTApplication -Name {pat} -NameMatch 'Wildcard' -ApplicationType 'EXE' -FilterScript {{ ![System.String]::IsNullOrWhiteSpace($_.QuietUninstallString) }}");
                        break;
                    }
                case "RemoveDesktopShortcut":
                    body.Add($"Remove-ADTFile -Path (Join-Path -Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) -ChildPath {L(Lnk(a.P("Name")))})");
                    break;
                case "RemoveStartMenuShortcut":
                    body.Add($"Get-ChildItem -LiteralPath ([Environment]::GetFolderPath('CommonPrograms')) -Filter {L(Lnk(a.P("Name")))} -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue");
                    break;
                case "CreateShortcut":
                    {
                        string folder = a.P("Location") == "StartMenu" ? "CommonPrograms" : "CommonDesktopDirectory";
                        string cmd = $"New-ADTShortcut -LiteralPath (Join-Path -Path ([Environment]::GetFolderPath('{folder}')) -ChildPath {L(Lnk(a.P("Name")))}) -TargetPath {Exp(a.P("Target"))}";
                        if (!string.IsNullOrWhiteSpace(a.P("Arguments"))) cmd += $" -Arguments {L(a.P("Arguments"))}";
                        if (!string.IsNullOrWhiteSpace(a.P("Icon"))) cmd += $" -IconLocation {Exp(a.P("Icon"))}";
                        body.Add(cmd);
                        break;
                    }
                case "CopyFile":
                    body.Add($"$j365Dest = {Exp(a.P("Destination"))}");
                    body.Add("if (-not (Test-Path -LiteralPath $j365Dest)) { $null = New-Item -Path $j365Dest -ItemType Directory -Force }");
                    body.Add($"Copy-ADTFile -Path {Support(a.P("Source"))} -Destination $j365Dest -Recurse");
                    break;
                case "CopyToUserProfiles":
                    body.Add($"Copy-ADTFileToUserProfiles -Path {Support(a.P("Source"))} -Destination {L(a.P("Destination"))} -BasePath {L(string.IsNullOrEmpty(a.P("BasePath")) ? "AppData" : a.P("BasePath"))} -Recurse");
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
                            "DWord" or "QWord" => long.TryParse(v.Trim(), out var n) ? n.ToString() : (Regex.IsMatch(v.Trim(), "^0x[0-9A-Fa-f]+$") ? v.Trim() : "0"),
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
                case "ImportRegFile":
                    body.Add($"$j365Reg = {Support(a.P("Source"))}");
                    body.Add("Start-ADTProcess -FilePath (Join-Path -Path $env:SystemRoot -ChildPath 'System32\\reg.exe') -ArgumentList ('import \"' + $j365Reg + '\"')");
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
                        if (a.P("Mode") == "StopDisable") body.Add($"Set-Service -Name {n} -StartupType Disabled");
                        else if (a.P("Mode") == "StopManual") body.Add($"Set-Service -Name {n} -StartupType Manual");
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
                        string fp = a.P("Source") == "Package" ? Support(a.P("FilePath")) : Exp(a.P("FilePath"));
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

            var def = Get(a.Type);
            string label = def?.LabelEn ?? a.Type;
            var lines = new List<string>();
            string comment = Regex.Replace($"{label} - {Summarize(a)}", @"[\r\n]+", " ").Replace("→", "->");
            lines.Add("    ## Action: " + ScriptGenerator.Ascii(comment));
            if (a.ContinueOnError)
            {
                lines.Add("    try");
                lines.Add("    {");
                lines.AddRange(body.Select(b => "        " + b));
                lines.Add("    }");
                lines.Add("    catch");
                lines.Add("    {");
                lines.Add($"        Write-ADTLogEntry -Message ({L("Action failed [" + label + "]: ")} + $_.Exception.Message) -Severity 2");
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
            key = Regex.Replace(key, @"^HKEY_CLASSES_ROOT\\", "HKCR:\\", RegexOptions.IgnoreCase);
            key = Regex.Replace(key, @"^HKEY_USERS\\", "HKU:\\", RegexOptions.IgnoreCase);
            key = Regex.Replace(key, @"^(HKLM|HKCU|HKCR|HKU)\\", "$1:\\", RegexOptions.IgnoreCase);
            return key;
        }
    }
}
