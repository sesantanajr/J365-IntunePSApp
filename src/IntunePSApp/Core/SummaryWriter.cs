using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using J365.IntunePSApp.Localization;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Writes Intune-Instructions.html/.txt: every field of the Intune "Windows app (Win32)" wizard, ready to copy.
    /// The files are written in the language the application is using.
    /// </summary>
    public static class SummaryWriter
    {
        public static List<(string Group, string Field, string Value)> Rows(PackageSettings s, BuildResult r)
        {
            var cmd = ScriptGenerator.Commands(s);
            string arch = s.AppArch switch { "x64" => T("Sum_V_ArchX64"), "x86" => T("Sum_V_ArchX86"), "arm64" => "ARM64", _ => T("Sum_V_ArchAny") };
            string det = s.DetectionMethod switch
            {
                "File" => T("Sum_V_DetFile", s.DetectionFilePath),
                "ProductCode" => T("Sum_V_DetCode", s.MsiProductCode),
                _ => T("Sum_V_DetName", s.DetectionNamePattern)
            };
            if (s.DetectionCheckVersion && !string.IsNullOrWhiteSpace(s.DetectionVersion)) det += T("Sum_V_DetVersion", s.DetectionVersion);
            if (s.DetectionRequireMark) det += T("Sum_V_DetMark", ScriptGenerator.MarkDisplayPath(s));
            string desc = !string.IsNullOrWhiteSpace(s.Description) ? s.Description : $"{s.AppName} {s.AppVersion}".Trim();
            if (!string.IsNullOrWhiteSpace(s.CompanyName)) desc += T("Sum_V_CompanyPackage", s.CompanyName);

            string g1 = T("Sum_G1"), g2 = T("Sum_G2"), g3 = T("Sum_G3"), g4 = T("Sum_G4"), g5 = T("Sum_G5"), g6 = T("Sum_G6");
            var rows = new List<(string, string, string)>
            {
                (g1, T("Sum_F_File"), Path.GetFileName(r.IntuneWinFile)),
                (g2, T("Sum_F_Name"), s.AppName.Trim()),
                (g2, T("Sum_F_Description"), desc),
                (g2, T("Sum_F_Publisher"), string.IsNullOrWhiteSpace(s.AppVendor) ? s.AppName : s.AppVendor),
                (g2, T("Sum_F_AppVersion"), s.AppVersion),
            };
            if (!string.IsNullOrWhiteSpace(s.Homepage)) rows.Add((g2, T("Sum_F_InfoUrl"), s.Homepage));
            if (!string.IsNullOrWhiteSpace(s.PrivacyUrl)) rows.Add((g2, T("Ed_PrivacyUrl"), s.PrivacyUrl));
            rows.Add((g2, T("Ed_Developer"), string.IsNullOrWhiteSpace(s.Developer) ? s.AppVendor : s.Developer));
            rows.Add((g2, T("Ed_Owner"), string.IsNullOrWhiteSpace(s.Owner) ? s.CompanyName : s.Owner));
            rows.Add((g2, T("Sum_F_Logo"), r.AppIcon != null ? T("Sum_V_LogoFile", Path.GetFileName(r.AppIcon)) : T("Sum_V_Optional")));
            rows.Add((g3, T("Sum_F_InstallCmd"), cmd.Install));
            rows.Add((g3, T("Sum_F_UninstallCmd"), cmd.Uninstall));
            rows.Add((g3, T("Sum_F_InstallTime"), "60"));
            rows.Add((g3, T("Sum_F_AllowUninstall"), T("Common_Yes")));
            rows.Add((g3, T("Sum_F_InstallBehavior"), s.InstallBehavior == "User" ? T("Sum_V_User") : T("Sum_V_System")));
            rows.Add((g3, T("Sum_F_RestartBehavior"), PackageBuilder.RestartBehavior(s) == "suppress" ? T("Sum_V_RestartSuppress") : T("Sum_V_RestartBehavior")));
            rows.Add((g3, T("Sum_F_ReturnCodes"), T("Sum_V_ReturnCodes")));
            rows.Add((g4, T("Sum_F_Architecture"), arch));
            rows.Add((g4, T("Sum_F_MinOs"), PackageBuilder.MinimumOsLabel(s.MinimumOs)));
            rows.Add((g5, T("Sum_F_RulesFormat"), T("Sum_V_RulesFormat")));
            rows.Add((g5, T("Sum_F_ScriptFile"), Path.GetFileName(r.DetectionScript)));
            rows.Add((g5, T("Sum_F_Run32"), T("Common_No")));
            rows.Add((g5, T("Sum_F_Signature"), T("Common_No")));
            rows.Add((g5, T("Sum_F_WhatChecks"), det));
            if (s.InstallerType == "MSI" && !string.IsNullOrWhiteSpace(s.MsiProductCode) && s.MsiProductCode.StartsWith("{"))
                rows.Add((g5, T("Sum_F_MsiAlt"), T("Sum_V_MsiAlt", s.MsiProductCode, s.AppVersion) + (s.DetectionRequireMark ? T("Sum_V_MsiAltNoMark") : "")));
            rows.Add((g6, T("Sum_F_Notifications"), PackageBuilder.IntuneNotifications(s) == "hideAll" ? T("Sum_V_NotifyHideAll") : T("Sum_V_NotifyShowAll")));
            rows.Add((g6, T("Sum_F_Suggestion"), T("Sum_V_Suggestion")));
            return rows;
        }

        public static (string Html, string Txt) Write(PackageSettings s, BuildResult r, string intuneDir)
        {
            var rows = Rows(s, r);
            var culture = Loc.Instance.Culture;
            string logDir = s.InstallBehavior == "User" ? @"C:\ProgramData\Logs\Software"
                : s.LogsInIntuneFolder ? @"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs" : @"C:\Windows\Logs\Software";
            string title = $"{s.AppName} {s.AppVersion}".Trim();
            string when = DateTime.Now.ToString("g", culture);
            string generated = T("Sum_Generated", when, AppInfo.Name, AppInfo.Version, AppInfo.PsadtName, r.PsadtVersion);
            const string installTest = @".\Invoke-AppDeployToolkit.exe -DeploymentType Install -DeployMode Interactive";
            const string uninstallTest = @".\Invoke-AppDeployToolkit.exe -DeploymentType Uninstall -DeployMode Interactive";

            var t = new StringBuilder();
            t.AppendLine(T("Sum_Title", title).ToUpper(culture));
            t.AppendLine(generated);
            t.AppendLine(T("Sum_Path"));
            string g = null;
            foreach (var (grp, field, value) in rows)
            {
                if (grp != g) { g = grp; t.AppendLine(); t.AppendLine($"== {grp} =="); }
                t.AppendLine($"{field}: {value}");
            }
            t.AppendLine();
            t.AppendLine($"== {T("Sum_TestTitle")} ==");
            t.AppendLine(T("Sum_TestTxt", PackageLayout.PackageDir));
            t.AppendLine("  " + installTest);
            t.AppendLine("  " + uninstallTest);
            t.AppendLine(T("Sum_TestLogs", logDir));
            t.AppendLine();
            t.AppendLine($"== {T("Sum_MoreTitle")} ==");
            t.AppendLine(T("Sum_MoreSandbox", PackageLayout.SandboxDir));
            t.AppendLine(T("Sum_MorePublish", PackageLayout.PublishScript, AppInfo.PublishDocsUrl));
            string txtPath = Path.Combine(intuneDir, PackageLayout.InstructionsTxt);
            File.WriteAllText(txtPath, t.ToString(), TextFiles.Utf8Bom);

            string accent = Regex.IsMatch((s.AccentColor ?? "").TrimStart('#'), "^[0-9A-Fa-f]{6}$") ? "#" + s.AccentColor.TrimStart('#') : "#0A5CA8";
            // The file is UTF-8: only the markup characters are escaped, so accented text stays readable in the source.
            static string E(string v) => (v ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
            var h = new StringBuilder();
            h.Append($@"<!DOCTYPE html>
<html lang=""{Loc.Instance.Language}""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Intune · {E(title)}</title>
<style>
:root{{--accent:{accent};--bg:#f4f6f9;--card:#fff;--text:#1b2230;--muted:#5d6878;--line:#e3e7ee}}
@media (prefers-color-scheme:dark){{:root{{--bg:#11151c;--card:#1a2029;--text:#e6e9ef;--muted:#9aa5b4;--line:#2b3340}}}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--bg);color:var(--text);font:15px/1.55 ""Segoe UI"",system-ui,sans-serif}}
header{{background:linear-gradient(120deg,#0B1F3A,var(--accent));color:#fff;padding:30px 32px}}
header h1{{margin:0;font-size:26px}}header p{{margin:6px 0 0;opacity:.9}}
main{{max-width:1080px;margin:0 auto;padding:24px 16px 48px}}
section{{background:var(--card);border:1px solid var(--line);border-radius:12px;margin-bottom:18px;overflow:hidden}}
section h2{{margin:0;padding:12px 18px;font-size:15px;border-bottom:1px solid var(--line)}}
table{{width:100%;border-collapse:collapse}}td{{padding:10px 18px;border-bottom:1px solid var(--line);vertical-align:top}}
tr:last-child td{{border-bottom:0}}td.f{{width:34%;color:var(--muted)}}td.v{{font-family:Consolas,monospace;font-size:14px;word-break:break-word}}
td.c{{width:96px;text-align:right}}button{{border:1px solid var(--line);background:transparent;color:var(--text);border-radius:6px;padding:4px 10px;cursor:pointer;font:inherit;font-size:13px}}
button:hover{{border-color:var(--accent);color:var(--accent)}}.note{{color:var(--muted);font-size:14px;padding:12px 18px}}.note p{{margin:0 0 8px}}.note p:last-child{{margin:0}}
code{{font-family:Consolas,monospace;background:rgba(127,127,127,.14);padding:1px 5px;border-radius:4px}}
footer{{text-align:center;color:var(--muted);font-size:13px;padding:8px}}footer a{{color:var(--accent)}}
</style></head><body>
<header><h1>{E(title)}</h1><p>{E(T("Sum_Path"))}</p></header>
<main>");
            g = null;
            foreach (var (grp, field, value) in rows)
            {
                if (grp != g)
                {
                    if (g != null) h.Append("</table></section>");
                    g = grp;
                    h.Append($"<section><h2>{E(grp)}</h2><table>");
                }
                h.Append($"<tr><td class=\"f\">{E(field)}</td><td class=\"v\">{E(value)}</td><td class=\"c\"><button onclick=\"cp(this)\">{E(T("Common_Copy"))}</button></td></tr>");
            }
            h.Append("</table></section>");
            h.Append($@"<section><h2>{E(T("Sum_TestTitle"))}</h2><div class=""note""><p>{E(T("Sum_TestTxt", PackageLayout.PackageDir))}</p>
<p><code>{E(installTest)}</code><br><code>{E(uninstallTest)}</code></p>
<p>{E(T("Sum_TestLogs", logDir))} {E(T("Sum_TestAuto"))}</p></div></section>
<section><h2>{E(T("Sum_MoreTitle"))}</h2><div class=""note""><p>{E(T("Sum_MoreSandbox", PackageLayout.SandboxDir))}</p>
<p>{E(T("Sum_MorePublish", PackageLayout.PublishScript, AppInfo.PublishDocsUrl))}</p></div></section>
</main>
<footer>{E(T("Sum_Footer", when))} <a href=""{AppInfo.RepoUrl}"">{E(AppInfo.Name)}</a> {AppInfo.Version} · <a href=""{AppInfo.PsadtUrl}"">{AppInfo.PsadtName}</a> {E(r.PsadtVersion)} · <a href=""{AppInfo.BlogUrl}"">{AppInfo.Brand}</a></footer>
<script>
var COPIED={System.Text.Json.JsonSerializer.Serialize(T("Common_Copied"))};
function cp(b){{var t=b.parentNode.previousSibling.innerText;function ok(){{var o=b.innerText;b.innerText=COPIED;setTimeout(function(){{b.innerText=o}},1200)}}
if(navigator.clipboard&&window.isSecureContext){{navigator.clipboard.writeText(t).then(ok,function(){{fb(t);ok()}})}}else{{fb(t);ok()}}}}
function fb(t){{var a=document.createElement('textarea');a.value=t;document.body.appendChild(a);a.select();try{{document.execCommand('copy')}}catch(e){{}}document.body.removeChild(a)}}
</script></body></html>");
            string htmlPath = Path.Combine(intuneDir, PackageLayout.InstructionsHtml);
            File.WriteAllText(htmlPath, h.ToString(), TextFiles.Utf8Bom);
            return (htmlPath, txtPath);
        }
    }
}
