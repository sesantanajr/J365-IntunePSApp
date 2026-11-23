using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Generates Invoke-AppDeployToolkit.ps1 from the PSAppDeployToolkit v4 template, patches the toolkit
    /// config/strings and writes the Intune detection script. Generated code is commented in English and the
    /// detection script is pure ASCII (Windows PowerShell 5.1 reads BOM-less files as ANSI).
    /// </summary>
    public static class ScriptGenerator
    {
        /// <summary>Single-quoted PowerShell literal (typographic quotes are doubled too: PowerShell treats them as quotes).</summary>
        public static string Lit(string text) => "'" + Regex.Replace(text ?? "", "['\u2018\u2019\u201A\u201B]", "$0$0") + "'";

        public static (string Install, string Uninstall, string Repair) Commands(PackageSettings s)
        {
            string mode = s.DeployMode switch { "Silent" => " -DeployMode Silent", "Interactive" => " -DeployMode Interactive", _ => "" };
            return ($"Invoke-AppDeployToolkit.exe -DeploymentType Install{mode}",
                    $"Invoke-AppDeployToolkit.exe -DeploymentType Uninstall{mode}",
                    $"Invoke-AppDeployToolkit.exe -DeploymentType Repair{mode}");
        }

        /// <summary>Exit codes the installer may return on success (0 plus the extra ones typed by the user).</summary>
        public static List<int> SuccessCodes(PackageSettings s)
        {
            var codes = new List<int> { 0 };
            foreach (var c in Regex.Split(s.ExtraSuccessCodes ?? "", @"[,;\s]+"))
                if (int.TryParse(c, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) && !codes.Contains(n)) codes.Add(n);
            return codes;
        }

        /// <summary>
        /// True when the text is empty or a list of whole numbers separated by commas, semicolons or spaces.
        /// Anything else ("3010-3015", "0x10") would be dropped without notice when the script is written.
        /// </summary>
        public static bool IsCodeList(string text)
            => Regex.Split((text ?? "").Trim(), @"[,;\s]+").All(c => c.Length == 0 || Regex.IsMatch(c, @"^-?\d{1,10}$"));

        /// <summary>Text for a comment line: one line, and never the end of a block comment.</summary>
        private static string Comment(string text) => Ascii(Regex.Replace(text ?? "", @"[\r\n]+", " ").Replace("#>", "# >"));

        /// <summary>
        /// Wildcard pattern in pure ASCII: every other character becomes "?", which matches exactly one character.
        /// Accents are NOT removed first: "Aplicacao*" does not match "Aplicação 1.0", "Aplica??o*" does.
        /// </summary>
        public static string AsciiWildcard(string pattern)
        {
            var sb = new StringBuilder();
            foreach (char c in (pattern ?? "").Normalize(NormalizationForm.FormC)) sb.Append(c < 128 ? c : '?');
            return sb.ToString();
        }

        /// <summary>
        /// PowerShell expression, written in pure ASCII, that evaluates to the exact text: 'Sped Cont' + [char]0x00E1 + 'bil'.
        /// Used where a wildcard is not acceptable (file paths).
        /// </summary>
        public static string AsciiExpression(string text)
        {
            text = (text ?? "").Normalize(NormalizationForm.FormC);
            if (text.All(c => c < 128)) return Lit(text);
            var parts = new List<string>();
            var run = new StringBuilder();
            foreach (char c in text)
            {
                if (c < 128) { run.Append(c); continue; }
                if (run.Length > 0) { parts.Add(Lit(run.ToString())); run.Clear(); }
                parts.Add("[char]0x" + ((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            if (run.Length > 0) parts.Add(Lit(run.ToString()));
            return "(" + string.Join(" + ", parts) + ")";
        }

        // ------------------------------------------------------------------ package mark

        /// <summary>Registry key, under HKLM (64-bit view) or HKCU, where every package writes its mark.</summary>
        public const string MarkRoot = @"SOFTWARE\J365\IntunePSApp\Apps";

        /// <summary>
        /// Name of the mark key of an application, in ASCII, without characters a key name cannot have: the id of the
        /// database entry when there is one (side-by-side products such as .NET 8 and .NET 9 share a name), otherwise the
        /// name of the application.
        /// </summary>
        public static string MarkId(PackageSettings s)
        {
            string source = !string.IsNullOrWhiteSpace(s.CatalogId) ? s.CatalogId : s.AppName;
            string id = Regex.Replace(Ascii(source ?? "", '_'), @"[^A-Za-z0-9 ._+()\-]", "_");
            id = Regex.Replace(id, @"\s+", " ").Trim().TrimEnd('.', ' ');
            if (id.Length > 80) id = id.Substring(0, 80).TrimEnd('.', ' ');
            return id.Length == 0 ? "App" : id;
        }

        public static string MarkKey(PackageSettings s) => MarkRoot + "\\" + MarkId(s);

        /// <summary>Where the mark is, as shown to people: HKLM\SOFTWARE\J365\IntunePSApp\Apps\7-Zip.</summary>
        public static string MarkDisplayPath(PackageSettings s) => (s.InstallBehavior == "User" ? "HKCU\\" : "HKLM\\") + MarkKey(s);

        /// <summary>Version written in the mark (compared with the minimum version of the detection).</summary>
        public static string MarkVersion(PackageSettings s)
            => Ascii(string.IsNullOrWhiteSpace(s.DetectionVersion) ? s.AppVersion ?? "" : s.DetectionVersion).Trim();

        /// <summary>
        /// Version that the package installs, for the check that keeps a newer copy: the greater of the application
        /// version and the minimum version of the detection. The minimum may be lower on purpose (older copies count as
        /// installed); compared with it, a copy older than the package would count as newer and never be updated.
        /// </summary>
        public static string PackageVersion(PackageSettings s)
        {
            string app = Ascii(s.AppVersion ?? "").Trim(), min = Ascii(s.DetectionVersion ?? "").Trim();
            bool hasApp = Regex.IsMatch(app, @"\d"), hasMin = Regex.IsMatch(min, @"\d");
            if (!hasApp) return hasMin ? min : "";
            if (!hasMin) return app;
            return VersionUtil.Compare(app, min) >= 0 ? app : min;
        }

        /// <summary>
        /// PowerShell expression that opens the hive of the mark. The 64-bit view is explicit: the package and the
        /// detection script read the same key whatever the bitness of the PowerShell that runs them.
        /// </summary>
        private static string MarkHive(PackageSettings s) => s.InstallBehavior == "User"
            ? "[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Default)"
            : "[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)";

        private static List<string> WriteMarkLines(PackageSettings s)
        {
            string key = Lit(MarkKey(s)), shown = MarkDisplayPath(s), version = MarkVersion(s);
            return new List<string>
            {
                "    ## Package mark: tells the Intune detection that this package installed the application. A copy installed",
                "    ## some other way (by hand, by another tool) has no mark, so Intune runs the package there and replaces it.",
                $"    $markRoot = {MarkHive(s)}",
                $"    $markKey = $markRoot.CreateSubKey({key})",
                $"    $markKey.SetValue('Version', {Lit(version)})",
                $"    $markKey.SetValue('Name', {Lit(s.AppName)})",
                $"    $markKey.SetValue('Vendor', {Lit(s.AppVendor)})",
                $"    $markKey.SetValue('PackageVersion', {Lit(s.AppVersion)})",
                "    $markKey.SetValue('InstalledUtc', [System.DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [System.Globalization.CultureInfo]::InvariantCulture))",
                $"    $markKey.SetValue('Generator', {Lit(AppInfo.Name + " " + AppInfo.Version)})",
                "    $markKey.Close()",
                "    $markRoot.Close()",
                $"    Write-ADTLogEntry -Message {Lit($"Package mark written: {shown} (version {version}).")}",
                ""
            };
        }

        private static List<string> RemoveMarkLines(PackageSettings s) => new List<string>
        {
            "    ## Removes the package mark: the Intune detection no longer reports the application as installed.",
            $"    $markRoot = {MarkHive(s)}",
            $"    $markRoot.DeleteSubKeyTree({Lit(MarkKey(s))}, $false)",
            "    $markRoot.Close()",
            $"    Write-ADTLogEntry -Message {Lit($"Package mark removed: {MarkDisplayPath(s)}.")}",
            ""
        };

        // ------------------------------------------------------------------ Invoke-AppDeployToolkit.ps1

        public static string DeployScript(string templateText, PackageSettings s, string installerRelative)
        {
            var mStart = Regex.Match(templateText, @"(?m)^\$adtSession\s*=\s*@\{");
            var mInit = Regex.Match(templateText, @"(?m)^##=+\r?\n## MARK: Initialization");
            var mVer = Regex.Match(templateText, @"DeployAppScriptVersion\s*=\s*'([^']+)'");
            if (!mStart.Success || !mInit.Success || !mVer.Success || mInit.Index < mStart.Index)
                throw new InvalidDataException(T("Err_TemplateStructure"));
            string header = templateText.Substring(0, mStart.Index);
            string tail = templateText.Substring(mInit.Index);

            var sb = new StringBuilder();
            sb.Append("# Generated by ").Append(AppInfo.Name).Append(' ').Append(AppInfo.Version).Append(" on ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
              .Append(" - ").Append(Comment(s.AppName + " " + s.AppVersion)).Append(" - ").Append(AppInfo.RepoUrl).Append("\r\n");
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
            string procs = s.ProcessesToClose.Count == 0 ? "@()"
                : "@(" + string.Join(", ", s.ProcessesToClose.Select(p => $"@{{ Name = {Lit(p)}; Description = {Lit(s.AppName)} }}")) + ")";
            string author = string.IsNullOrWhiteSpace(s.Author) ? AppInfo.Name : s.Author;
            string lang = string.IsNullOrEmpty(s.Language) || s.Language == "Auto" ? "EN" : s.Language.ToUpperInvariant();
            var L = new List<string>
            {
                "$adtSession = @{",
                $"    # Application variables (generated by {AppInfo.Name}).",
                $"    AppVendor = {Lit(s.AppVendor)}",
                $"    AppName = {Lit(s.AppName)}",
                $"    AppVersion = {Lit(s.AppVersion)}",
                $"    AppArch = {Lit(s.AppArch)}",
                $"    AppLang = {Lit(lang)}",
                "    AppRevision = '01'",
                $"    AppSuccessExitCodes = @({string.Join(", ", SuccessCodes(s))})",
                "    AppRebootExitCodes = @(1641, 3010)",
                $"    AppProcessesToClose = {procs}",
                "    AppScriptVersion = '1.0.0'",
                $"    AppScriptDate = '{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'",
                $"    AppScriptAuthor = {Lit(author)}",
                $"    RequireAdmin = {(s.InstallBehavior == "User" ? "$false" : "$true")}",
            };
            // "Automatic": the toolkit's Auto mode turns the deployment silent when none of the applications to close is
            // running, so users only saw Intune's notification. Without that check the branded dialogs are shown whenever
            // someone is signed in; with no user, or during OOBE/ESP, the deployment stays silent.
            if (IsAutoWithDialogs(s.DeployMode))
            {
                L.Add("    # Dialogs whenever a user is signed in (silent with nobody signed in or during OOBE/ESP).");
                L.Add("    NoProcessDetection = $true");
            }
            L.AddRange(new[]
            {
                "",
                "    # Dialog titles.",
                "    InstallName = ''",
                $"    InstallTitle = {Lit(s.AppName.Trim())}",
                "",
                "    # Script variables (do not change).",
                "    DeployAppScriptFriendlyName = $MyInvocation.MyCommand.Name",
                "    DeployAppScriptParameters = $PSBoundParameters",
                $"    DeployAppScriptVersion = '{deployVersion}'",
                "}"
            });
            return string.Join("\r\n", L);
        }

        /// <summary>
        /// Display modes: "Auto" (dialogs whenever a user is signed in), "WhenNeeded" (the toolkit's own Auto: dialogs only
        /// when an application to close is running), "Interactive" and "Silent". Unknown values behave as "Auto".
        /// </summary>
        public static bool IsAutoWithDialogs(string mode) => mode is not ("WhenNeeded" or "Interactive" or "Silent");

        /// <summary>The package never shows anything to the user (the toolkit's Silent mode, and Intune's toasts hidden by default).</summary>
        public static bool IsSilent(PackageSettings s) => s?.DeployMode == "Silent";

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
            foreach (var a in s.Actions.Where(a => a.Phase == phase)) L.AddRange(ActionCatalog.Generate(a, s.InstallBehavior == "User"));
        }

        private static List<string> InstallBody(PackageSettings s, string installerRel)
        {
            var L = new List<string>();
            bool userContext = s.InstallBehavior == "User";
            Mark(L, "Pre-Install", "\"Pre-$($adtSession.DeploymentType)\"");
            L.AddRange(KeepNewerLines(s));
            // A silent package shows nothing: no deferral, no message, no countdown (the toolkit closes the applications
            // in use without a dialog). Writing them anyway would only suggest a dialog that never appears.
            bool silent = IsSilent(s);
            bool hasProc = s.ProcessesToClose.Count > 0;
            bool allowDefer = s.AllowDefer && !silent;
            bool hasMsg = !silent && !string.IsNullOrWhiteSpace(s.WelcomeMessage);
            if (hasProc || allowDefer || hasMsg)
            {
                L.Add(silent
                    ? "    ## Silent package: the applications in use are closed without a dialog and the free disk space is checked."
                    : "    ## Welcome dialog: closes the applications in use, lets the user defer and checks the free disk space.");
                L.Add("    $saiwParams = @{");
                if (!silent) L.Add("        PersistPrompt = $true");
                if (s.CheckDiskSpace) L.Add("        CheckDiskSpace = $true");
                if (allowDefer) { L.Add("        AllowDefer = $true"); L.Add($"        DeferTimes = {Math.Clamp(s.DeferTimes, 1, 99)}"); }
                if (hasMsg) L.Add("        CustomText = $true");
                L.Add("    }");
                L.Add("    if ($adtSession.AppProcessesToClose.Count -gt 0)");
                L.Add("    {");
                L.Add("        $saiwParams.Add('CloseProcesses', $adtSession.AppProcessesToClose)");
                if (s.CloseAppsCountdown > 0 && !silent) L.Add($"        $saiwParams.Add('CloseProcessesCountdown', {Math.Min(s.CloseAppsCountdown, 3000)})");
                // Blocking execution writes to HKLM, so it is only available when the package runs elevated. A silent package
                // does not block: PSAppDeployToolkit shows a dialog to whoever starts a blocked application, in any mode.
                if (s.BlockExecution && !userContext && !silent) L.Add("        $saiwParams.Add('BlockExecution', $true)");
                L.Add("    }");
                L.Add("    Show-ADTInstallationWelcome @saiwParams");
                L.Add("");
            }
            else
            {
                L.Add("    ## Welcome dialog disabled (no applications to close, no deferral and no custom message).");
                if (s.CheckDiskSpace)
                {
                    L.Add("    ## Free disk space is still checked (no dialog is shown for it).");
                    L.Add("    Show-ADTInstallationWelcome -CheckDiskSpace");
                }
                L.Add("");
            }
            L.Add("    ## Progress dialog.");
            L.Add(string.IsNullOrWhiteSpace(s.ProgressMessage) ? "    Show-ADTInstallationProgress" : $"    Show-ADTInstallationProgress -StatusMessage {Lit(s.ProgressMessage)}");
            L.Add("");
            // Pre-install actions prepare the device (stop a service, install a dependency): they run before the
            // removal of the versions already installed, so they can help it.
            AddActions(L, s, "PreInstall");
            if (s.RemovePreviousVersions) { L.AddRange(RemoveLines(s, true)); L.Add(""); }
            L.Add("");
            Mark(L, "Install", "$adtSession.DeploymentType");
            L.Add("    ## Installs " + Comment($"{s.AppName} {s.AppVersion}".Trim()) + ".");
            L.Add(InstallCommand(s, installerRel, "Install"));
            L.Add("");
            L.Add("");
            Mark(L, "Post-Install", "\"Post-$($adtSession.DeploymentType)\"");
            AddActions(L, s, "PostInstall");
            // Written after the post-install actions: a failed action leaves no mark, so Intune tries again.
            if (s.DetectionRequireMark) L.AddRange(WriteMarkLines(s));
            if (silent && (!string.IsNullOrWhiteSpace(s.CompletionMessage) || s.RestartPrompt))
            {
                L.Add("    ## Silent package: no completion message and no restart prompt (a restart the installer needs is left to the user).");
                L.Add("");
            }
            else if (!string.IsNullOrWhiteSpace(s.CompletionMessage))
            {
                L.Add("    ## Message shown when the installation finishes.");
                L.Add($"    Show-ADTInstallationPrompt -Message {Lit(s.CompletionMessage)} -ButtonRightText 'OK' -Icon 'Information' -NoWait");
                L.Add("");
            }
            if (s.RestartPrompt && !silent)
            {
                int cd = Math.Clamp(s.RestartCountdown, 60, 86400);
                L.Add("    ## Asks the user to restart the computer.");
                L.Add($"    Show-ADTInstallationRestartPrompt -CountdownSeconds {cd} -CountdownNoHideSeconds {Math.Min(60, cd)}");
            }
            return L;
        }

        /// <summary>
        /// A newer version than the package is already installed (the application updated itself, or another tool put
        /// it there): the package keeps it, because installing would downgrade it, and only records its mark so the
        /// detection reports it as installed. Browsers and similar applications refuse a profile written by a newer
        /// version, so a downgrade is never done silently.
        /// </summary>
        private static List<string> KeepNewerLines(PackageSettings s)
        {
            var L = new List<string>();
            string version = PackageVersion(s);
            string pattern = (string.IsNullOrWhiteSpace(s.UninstallPattern) ? s.DetectionNamePattern : s.UninstallPattern)?.Trim() ?? "";
            if (!s.KeepNewerVersion || !Regex.IsMatch(version, @"\d") || pattern.Trim('*', '?', ' ').Length < 3) return L;
            string pat = Lit(pattern);
            L.Add("    ## A newer version than this package is already installed (the application updated itself, or it was installed");
            L.Add("    ## some other way): it is kept, because installing this package would downgrade it.");
            L.Add("    function Test-J365NewerVersion([string]$Installed, [string]$Package)");
            L.Add("    {");
            L.Add("        $a = @([regex]::Matches([string]$Installed, '\\d+') | Select-Object -First 4 | ForEach-Object { [decimal]$_.Value })");
            L.Add("        $b = @([regex]::Matches([string]$Package, '\\d+') | Select-Object -First 4 | ForEach-Object { [decimal]$_.Value })");
            L.Add("        if ($a.Count -eq 0 -or $b.Count -eq 0) { return $false }");
            L.Add("        for ($i = 0; $i -lt 4; $i++)");
            L.Add("        {");
            L.Add("            $x = 0; if ($i -lt $a.Count) { $x = $a[$i] }");
            L.Add("            $y = 0; if ($i -lt $b.Count) { $y = $b[$i] }");
            L.Add("            if ($x -gt $y) { return $true }");
            L.Add("            if ($x -lt $y) { return $false }");
            L.Add("        }");
            L.Add("        return $false");
            L.Add("    }");
            L.Add($"    $newerApps = @(Get-ADTApplication -Name {pat} -NameMatch 'Wildcard' | Where-Object {{ Test-J365NewerVersion -Installed $_.DisplayVersion -Package {Lit(version)} }})");
            L.Add("    if ($newerApps.Count -gt 0)");
            L.Add("    {");
            L.Add("        Write-ADTLogEntry -Message ('A newer version is already installed and is kept (this package would downgrade it): ' + (($newerApps | ForEach-Object { \"$($_.DisplayName) $($_.DisplayVersion)\".Trim() }) -join '; '))");
            if (s.DetectionRequireMark) L.AddRange(WriteMarkLines(s).Select(x => x.Length > 0 ? "    " + x : x));
            L.Add("        return");
            L.Add("    }");
            L.Add("");
            return L;
        }

        private static List<string> UninstallBody(PackageSettings s)
        {
            var L = new List<string>();
            Mark(L, "Pre-Uninstall", "\"Pre-$($adtSession.DeploymentType)\"");
            L.Add(IsSilent(s)
                ? "    ## Silent package: the application is closed without a dialog when it is open."
                : "    ## Asks the user to close the application when it is open (closes automatically after 60 seconds).");
            L.Add("    if ($adtSession.AppProcessesToClose.Count -gt 0)");
            L.Add("    {");
            L.Add(CloseProcessesLine(s));
            L.Add("    }");
            L.Add("");
            L.Add("    ## Progress dialog.");
            L.Add("    Show-ADTInstallationProgress");
            L.Add("");
            AddActions(L, s, "PreUninstall");
            L.Add("");
            Mark(L, "Uninstall", "$adtSession.DeploymentType");
            L.AddRange(RemoveLines(s, false));
            L.Add("");
            L.AddRange(VerifyRemovedLines(s));
            if (s.DetectionRequireMark) L.AddRange(RemoveMarkLines(s));
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
            L.Add(CloseProcessesLine(s));
            L.Add("    }");
            L.Add("    Show-ADTInstallationProgress");
            L.Add("");
            L.Add("");
            Mark(L, "Repair", "$adtSession.DeploymentType");
            if (s.InstallerType == "MSI") L.Add(InstallCommand(s, installerRel, "Repair"));
            else
            {
                L.Add("    ## Repairing an .exe installer = installing it again over the current version.");
                L.Add(InstallCommand(s, installerRel, "Install"));
            }
            L.Add("");
            L.Add("");
            Mark(L, "Post-Repair", "\"Post-$($adtSession.DeploymentType)\"");
            if (s.DetectionRequireMark) L.AddRange(WriteMarkLines(s));
            return L;
        }

        /// <summary>Closing the applications in use before an uninstallation or a repair (no countdown for a silent package).</summary>
        private static string CloseProcessesLine(PackageSettings s) => IsSilent(s)
            ? "        Show-ADTInstallationWelcome -CloseProcesses $adtSession.AppProcessesToClose"
            : "        Show-ADTInstallationWelcome -CloseProcesses $adtSession.AppProcessesToClose -CloseProcessesCountdown 60";

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

        /// <summary>
        /// After the uninstallation: anything still registered with the name makes the deployment fail, instead of
        /// reporting "uninstalled" to Intune while the program stays (an .exe that registers no silent uninstaller is
        /// skipped by the toolkit). The package mark stays, so the detection keeps telling the truth.
        /// </summary>
        private static List<string> VerifyRemovedLines(PackageSettings s)
        {
            string pat = Lit(s.UninstallPattern);
            string find = $"@(Get-ADTApplication -Name {pat} -NameMatch 'Wildcard')";
            return new List<string>
            {
                "    ## Checks that nothing is left (a few seconds are allowed for uninstallers that finish in the background).",
                $"    $remainingApps = {find}",
                "    for ($wait = 0; $wait -lt 3 -and $remainingApps.Count -gt 0; $wait++)",
                "    {",
                "        Start-Sleep -Seconds 5",
                $"        $remainingApps = {find}",
                "    }",
                "    if ($remainingApps.Count -gt 0)",
                "    {",
                "        throw ('Still installed after the uninstallation (no silent uninstaller was found, or it failed): ' + (($remainingApps | ForEach-Object { \"$($_.DisplayName) $($_.DisplayVersion)\".Trim() }) -join '; '))",
                "    }",
                ""
            };
        }

        /// <summary>
        /// The Windows Installer product code of the package, as {GUID}: the MSI itself, or the MSI a setup program installs
        /// (Adobe Reader, InstallShield setups), known from the catalog. Null when the field holds no GUID (an uninstall key
        /// name such as "{GUID}_is1" or "7-Zip").
        /// </summary>
        public static string MsiCode(PackageSettings s)
        {
            if (s == null || !(s.InstallerType is "MSI" or "EXE")) return null;
            return Guid.TryParse((s.MsiProductCode ?? "").Trim(), out var g) && g != Guid.Empty ? g.ToString("B").ToUpperInvariant() : null;
        }

        /// <summary>
        /// An .exe of the catalog that installs an MSI, with no uninstall switches of its own: the product code its removal
        /// relies on (msiexec /x), which the catalog gave. Null for an .exe outside the catalog, where a code typed for the
        /// detection may be the key of a setup that is not an MSI, so its warning about switches stays.
        /// </summary>
        public static string ExeMsiCode(PackageSettings s)
            => s != null && s.InstallerType == "EXE" && !string.IsNullOrEmpty(s.CatalogId) && string.IsNullOrWhiteSpace(s.UninstallExeArgs) ? MsiCode(s) : null;

        private static List<string> RemoveLines(PackageSettings s, bool continueOnError)
        {
            string pat = Lit(s.UninstallPattern);
            string msi = $"Uninstall-ADTApplication -Name {pat} -NameMatch 'Wildcard' -ApplicationType 'MSI'";
            // The product of this package by its code first (msiexec /x): it goes even when its name does not match the
            // pattern, and a setup program that installs an MSI needs no switches. Only a Windows Installer product with
            // this code is touched (-ApplicationType 'MSI'); when none is installed, the toolkit only logs it.
            string code = MsiCode(s);
            string byCode = code == null ? null : $"Uninstall-ADTApplication -ProductCode {Lit(code)} -ApplicationType 'MSI'";
            string ua = (s.UninstallExeArgs ?? "").Trim();
            // -WaitForChildProcesses: many uninstallers (NSIS, 7-Zip) copy themselves to %TEMP%, start the copy and exit at once.
            // Without waiting for that copy the removal "finishes" while the program is still installed, and the new
            // version is installed at the same time.
            string exe = ua.Length > 0
                ? $"Uninstall-ADTApplication -Name {pat} -NameMatch 'Wildcard' -ApplicationType 'EXE' -ArgumentList {Lit(ua)} -WaitForChildProcesses"
                : $"Uninstall-ADTApplication -Name {pat} -NameMatch 'Wildcard' -ApplicationType 'EXE' -FilterScript {{ ![System.String]::IsNullOrWhiteSpace($_.QuietUninstallString) }} -WaitForChildProcesses";
            var L = new List<string>();
            if (continueOnError)
            {
                // Uninstall first, install after: no detection rule is consulted, so the same version is replaced too
                // and two versions of the application never end up side by side.
                string find = $"@(Get-ADTApplication -Name {pat} -NameMatch 'Wildcard')";
                const string describe = "(($_ | ForEach-Object { \"$($_.DisplayName) $($_.DisplayVersion)\".Trim() }) -join '; ')";
                L.Add("    ## Existing versions: whatever is already installed is removed first, then the new version is installed.");
                L.Add(code == null
                    ? "    ## No detection rule is consulted here: the removal runs whenever a program with this name is found (MSI and EXE)."
                    : "    ## No detection rule is consulted here: the removal runs whenever a program with this name (MSI and EXE), or the MSI with this product code, is found.");
                L.Add("    ## A failure is logged but does not stop the installation.");
                L.Add($"    $existingApps = {find}");
                // The product of this package also when its name does not match the pattern (counted once when it does).
                if (code != null)
                    L.Add($"    $existingApps += @(Get-ADTApplication -ProductCode {Lit(code)} -ApplicationType 'MSI' | Where-Object {{ $existingApps.ProductCode -notcontains $_.ProductCode }})");
                L.Add("    if ($existingApps.Count -eq 0)");
                L.Add("    {");
                L.Add("        Write-ADTLogEntry -Message 'No version already installed was found: nothing to remove.'");
                L.Add("    }");
                L.Add("    else");
                L.Add("    {");
                L.Add("        Write-ADTLogEntry -Message ('Versions already installed, removed before installing: ' + " + describe.Replace("$_ |", "$existingApps |") + ")");
                // The toolkit records the exit code of a failed process before it throws: restored, so a failed
                // removal that is only logged does not make the whole installation report that code.
                L.Add("        $j365ExitCode = $adtSession.GetExitCode()");
                foreach (var c in byCode == null ? new[] { msi, exe } : new[] { byCode, msi, exe })
                {
                    L.Add("        try");
                    L.Add("        {");
                    L.Add("            " + c);
                    L.Add("        }");
                    L.Add("        catch");
                    L.Add("        {");
                    L.Add("            Write-ADTLogEntry -Message \"Could not remove the previous version: $($_.Exception.Message)\" -Severity 2");
                    L.Add("            $adtSession.SetExitCode($j365ExitCode)");
                    L.Add("        }");
                }
                L.Add($"        $remainingApps = {find}");
                L.Add("        if ($remainingApps.Count -gt 0)");
                L.Add("        {");
                L.Add("            Write-ADTLogEntry -Message ('Still installed after the removal (the new version is installed over it): ' + " + describe.Replace("$_ |", "$remainingApps |") + ") -Severity 2");
                L.Add("        }");
                L.Add("    }");
            }
            else
            {
                L.Add("    ## Removes the application (every version found, MSI and EXE).");
                if (byCode != null)
                {
                    L.Add("    ## The Windows Installer product of this package, by its product code (msiexec /x).");
                    L.Add("    " + byCode);
                    L.Add("    ## Other versions with the same name.");
                }
                L.Add("    " + msi);
                L.Add("    " + exe);
            }
            return L;
        }

        // ------------------------------------------------------------------ config.psd1 and strings.psd1

        private static string SetPsd1Value(string text, string section, string key, string value)
        {
            var rx = new Regex(@"(?ms)(^\s*" + Regex.Escape(section) + @"\s*=\s*@\{.*?^\s*" + Regex.Escape(key) + @"\s*=[ \t]*)([^\r\n]*)");
            if (!rx.IsMatch(text)) throw new InvalidDataException(T("Err_ConfigKeyMissing", section + "." + key));
            return rx.Replace(text, m => m.Groups[1].Value + value, 1);
        }

        /// <summary>
        /// A value that PowerShell expands as a double-quoted string reaches it as plain text: "$" and "`" are escaped,
        /// and so are the typographic double quotes, which PowerShell also takes as quotes (unescaped, they cut the text
        /// short). Straight double quotes stay as they are: ExpandString keeps them, and escaped they cut the text.
        /// </summary>
        public static string EscapeExpandable(string text)
            => Regex.Replace(text ?? "", "[`$\u201C\u201D\u201E]", "`$0");

        public static void UpdateConfig(string configPath, PackageSettings s)
        {
            var (t, bom) = TextFiles.Read(configPath);
            // PSAppDeployToolkit expands variables in every value of the config: "$" and "`" must reach it as plain text.
            if (!string.IsNullOrWhiteSpace(s.CompanyName)) t = SetPsd1Value(t, "Toolkit", "CompanyName", Lit(EscapeExpandable(s.CompanyName)));
            if (s.LogsInIntuneFolder) t = SetPsd1Value(t, "Toolkit", "LogPath", "'$envProgramData\\Microsoft\\IntuneManagementExtension\\Logs'");
            t = SetPsd1Value(t, "UI", "DialogStyle", s.DialogStyle == "Classic" ? "'Classic'" : "'Fluent'");
            var hex = (s.AccentColor ?? "").Trim().TrimStart('#');
            if (Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$")) t = SetPsd1Value(t, "UI", "FluentAccentColor", "0xFF" + hex.ToUpperInvariant());
            if (!string.IsNullOrEmpty(s.Language) && s.Language != "Auto") t = SetPsd1Value(t, "UI", "LanguageOverride", Lit(s.Language));
            TextFiles.Write(configPath, t, bom);
        }

        /// <summary>Writes the custom welcome text into every language file of the toolkit. Returns the number of files changed.</summary>
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

        // ------------------------------------------------------------------ Intune detection script

        /// <summary>First words the detection script writes when the application is there but the package mark is not.</summary>
        public const string NoMarkPrefix = "Not detected:";

        public static string DetectionScript(PackageSettings s)
        {
            bool check = s.DetectionCheckVersion && !string.IsNullOrWhiteSpace(s.DetectionVersion);
            bool mark = s.DetectionRequireMark;
            string title = Comment($"{s.AppName} {s.AppVersion}");
            var sb = new StringBuilder();
            sb.AppendLine("<#");
            sb.AppendLine("    Detection script - Microsoft Intune (Win32 app)");
            sb.AppendLine($"    Application : {title}");
            sb.AppendLine($"    Generated   : {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} by {AppInfo.Name} ({AppInfo.RepoUrl})");
            sb.AppendLine("    In Intune   : Detection rules > Use a custom detection script");
            sb.AppendLine("                  - Run script as 32-bit process on 64-bit clients: No");
            sb.AppendLine("                  - Enforce script signature check: No");
            sb.AppendLine("    Result      : writes to STDOUT and exits with 0 when the application is installed.");
            if (mark)
            {
                sb.AppendLine($"    Package mark: {Comment(MarkDisplayPath(s))}");
                sb.AppendLine("                  Written by the package when the installation finishes. An application installed some");
                sb.AppendLine("                  other way has no mark: it is reported as not installed, so Intune runs the package,");
                sb.AppendLine("                  which removes that copy (when set to) and installs this version.");
            }
            sb.AppendLine("#>");
            sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            sb.AppendLine($"$CheckVersion = {(check ? "$true" : "$false")}");
            sb.AppendLine($"$MinimumVersion = {Lit(Ascii(s.DetectionVersion ?? ""))}");
            sb.AppendLine($"$RequireMark = {(mark ? "$true" : "$false")}");
            sb.AppendLine($"$MarkPath = {Lit(MarkKey(s))}");
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

function Test-PackageMark
{
    # The package writes this key when the installation finishes. A mark of any version counts: the version is checked
    # on the installed application, which may have updated itself since an older package wrote the mark.
    $base = " + MarkHive(s) + @"
    $k = $base.OpenSubKey($MarkPath)
    if (-not $k) { return $false }
    $k.Close()
    return $true
}
");
            if (s.DetectionMethod == "File")
            {
                sb.AppendLine("# Rule: the file exists (and its file version is equal to or higher than the minimum).");
                sb.AppendLine($"$FilePath = [Environment]::ExpandEnvironmentVariables({AsciiExpression(s.DetectionFilePath)})");
                sb.AppendLine(@"
function Find-Application
{
    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) { return $null }
    $fv = (Get-Item -LiteralPath $FilePath).VersionInfo
    $installed = '{0}.{1}.{2}.{3}' -f $fv.FileMajorPart, $fv.FileMinorPart, $fv.FileBuildPart, $fv.FilePrivatePart
    if (Test-VersionOk $installed) { return ""$FilePath ($installed)"" }
    return $null
}");
            }
            else
            {
                string match;
                if (s.DetectionMethod == "ProductCode")
                {
                    string code = (s.MsiProductCode ?? "").Trim();
                    if (Guid.TryParse(code, out var g)) code = g.ToString("B").ToUpperInvariant();
                    sb.AppendLine("# Rule: the uninstall key (product code) exists (and the version is equal to or higher than the minimum).");
                    sb.AppendLine($"$ProductCode = {Lit(Ascii(code))}");
                    match = "($sub -eq $ProductCode)";
                }
                else
                {
                    sb.AppendLine("# Rule: an installed program whose name matches the pattern (and its version is equal to or higher than the minimum).");
                    sb.AppendLine($"$NamePattern = {Lit(AsciiWildcard(s.DetectionNamePattern))}");
                    match = "($dn -and ($dn -like $NamePattern))";
                }
                sb.AppendLine($"$IncludeCurrentUser = {(s.InstallBehavior == "User" ? "$true" : "$false")}");
                sb.AppendLine(@"
function Find-Application
{
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
                $key.Close()
                if ($dv -and -not $dn.Contains($dv)) { return ""$dn $dv"" }
                return $dn
            }
        }
        $key.Close()
    }
    return $null
}");
            }
            sb.AppendLine(@"
$found = Find-Application
if (-not $found) { exit 1 }
if ($RequireMark -and -not (Test-PackageMark))
{
    # Not installed by this package: a nonzero exit code means ""not installed"" for Intune, whatever is written.
    Write-Output """ + NoMarkPrefix + @" $found is installed, but not by this package (no package mark in $MarkPath). Intune runs the package.""
    exit 1
}
Write-Output ""Detected: $found""
exit 0");
            return Ascii(sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }

        /// <summary>Keeps the text 100% ASCII (accents are removed; any other character becomes the replacement).</summary>
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
