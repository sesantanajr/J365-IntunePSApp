using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace J365.IntunePSApp.Core
{
    /// <summary>Gera INSTRUCOES_INTUNE.html/.txt com todos os campos do Intune prontos para copiar.</summary>
    public static class SummaryWriter
    {
        public static List<(string Group, string Field, string Value)> Rows(PackageSettings s, BuildResult r)
        {
            var cmd = ScriptGenerator.Commands(s);
            string arch = s.AppArch switch { "x64" => "64 bits (x64)", "x86" => "32 bits e 64 bits (x86 roda em ambos)", "arm64" => "ARM64", _ => "32 bits e 64 bits" };
            string det = s.DetectionMethod switch
            {
                "File" => $"Arquivo {s.DetectionFilePath}",
                "ProductCode" => $"Chave de desinstalação {s.MsiProductCode}",
                _ => $"Programa instalado com nome '{s.DetectionNamePattern}'"
            };
            if (s.DetectionCheckVersion && !string.IsNullOrWhiteSpace(s.DetectionVersion)) det += $" e versão >= {s.DetectionVersion}";
            string desc = !string.IsNullOrWhiteSpace(s.Description) ? s.Description : $"{s.AppName} {s.AppVersion}";
            if (!string.IsNullOrWhiteSpace(s.CompanyName)) desc += $" — pacote {s.CompanyName}";

            var rows = new List<(string, string, string)>
            {
                ("1. Arquivo do pacote", "Arquivo .intunewin", Path.GetFileName(r.IntuneWinFile)),
                ("2. Informações do aplicativo", "Nome", $"{s.AppName} {s.AppVersion}".Trim()),
                ("2. Informações do aplicativo", "Descrição", desc),
                ("2. Informações do aplicativo", "Editor (Publisher)", s.AppVendor),
                ("2. Informações do aplicativo", "Versão do aplicativo", s.AppVersion),
            };
            if (!string.IsNullOrWhiteSpace(s.Homepage)) rows.Add(("2. Informações do aplicativo", "URL de informações", s.Homepage));
            rows.Add(("2. Informações do aplicativo", "Logotipo", r.AppIcon != null ? Path.GetFileName(r.AppIcon) + " (pasta Intune)" : "(opcional)"));
            rows.Add(("3. Programa", "Comando de instalação", cmd.Install));
            rows.Add(("3. Programa", "Comando de desinstalação", cmd.Uninstall));
            rows.Add(("3. Programa", "Tempo necessário para a instalação (min)", "60"));
            rows.Add(("3. Programa", "Permitir desinstalação disponível", "Sim"));
            rows.Add(("3. Programa", "Comportamento de instalação", s.InstallBehavior == "User" ? "Usuário" : "Sistema"));
            rows.Add(("3. Programa", "Comportamento de reinicialização do dispositivo", "Determinar o comportamento com base nos códigos de retorno"));
            rows.Add(("3. Programa", "Códigos de retorno", "0 Sucesso · 1707 Sucesso · 3010 Reinicialização suave · 1641 Reinicialização forçada · 1618 Repetir (1602 = usuário adiou: falha, repetida no próximo ciclo)"));
            rows.Add(("4. Requisitos", "Arquitetura do sistema operacional", arch));
            rows.Add(("4. Requisitos", "Sistema operacional mínimo", "Windows 10 22H2 (ou conforme a política da empresa)"));
            rows.Add(("5. Regras de detecção", "Formato das regras", "Usar um script de detecção personalizado"));
            rows.Add(("5. Regras de detecção", "Arquivo de script", Path.GetFileName(r.DetectionScript)));
            rows.Add(("5. Regras de detecção", "Executar script como processo de 32 bits em clientes de 64 bits", "Não"));
            rows.Add(("5. Regras de detecção", "Impor verificação de assinatura do script", "Não"));
            rows.Add(("5. Regras de detecção", "O que o script verifica", det));
            if (s.InstallerType == "MSI" && !string.IsNullOrWhiteSpace(s.MsiProductCode) && s.MsiProductCode.StartsWith("{"))
                rows.Add(("5. Regras de detecção", "Alternativa (regra MSI nativa)", $"Tipo MSI · Código do produto {s.MsiProductCode} · Versão do produto maior ou igual a {s.AppVersion}"));
            rows.Add(("6. Atribuições", "Sugestão", "Teste primeiro em um grupo piloto (Obrigatório) e depois publique como Disponível no Portal da Empresa e/ou Obrigatório."));
            return rows;
        }

        public static (string Html, string Txt) Write(PackageSettings s, BuildResult r, string intuneDir)
        {
            var rows = Rows(s, r);
            string logDir = s.LogsInIntuneFolder ? @"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs" : @"C:\Windows\Logs\Software";

            var t = new StringBuilder();
            t.AppendLine($"CONFIGURAÇÃO DO APLICATIVO NO INTUNE - {s.AppName} {s.AppVersion}");
            t.AppendLine($"Gerado em {DateTime.Now:dd/MM/yyyy HH:mm} pelo J365 IntunePSApp {AppInfo.Version} (PSAppDeployToolkit {r.PsadtVersion})");
            t.AppendLine("Intune: Aplicativos > Windows > Adicionar > Aplicativo do Windows (Win32)");
            string g = null;
            foreach (var (grp, field, value) in rows)
            {
                if (grp != g) { g = grp; t.AppendLine(); t.AppendLine($"== {grp} =="); }
                t.AppendLine($"{field}: {value}");
            }
            t.AppendLine();
            t.AppendLine("== Teste local antes de publicar ==");
            t.AppendLine("PowerShell como Administrador, na pasta Pacote_PSADT:");
            t.AppendLine("  .\\Invoke-AppDeployToolkit.exe -DeploymentType Install -DeployMode Interactive");
            t.AppendLine("  .\\Invoke-AppDeployToolkit.exe -DeploymentType Uninstall -DeployMode Interactive");
            t.AppendLine($"Logs do PSADT: {logDir}");
            string txtPath = Path.Combine(intuneDir, "INSTRUCOES_INTUNE.txt");
            File.WriteAllText(txtPath, t.ToString(), TextFiles.Utf8Bom);

            string accent = Regex.IsMatch((s.AccentColor ?? "").TrimStart('#'), "^[0-9A-Fa-f]{6}$") ? "#" + s.AccentColor.TrimStart('#') : "#0A5CA8";
            string E(string v) => WebUtility.HtmlEncode(v ?? "");
            var h = new StringBuilder();
            h.Append($@"<!DOCTYPE html>
<html lang=""pt-BR""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Intune · {E(s.AppName + " " + s.AppVersion)}</title>
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
button:hover{{border-color:var(--accent);color:var(--accent)}}.note{{color:var(--muted);font-size:14px;padding:12px 18px}}
code{{font-family:Consolas,monospace;background:rgba(127,127,127,.14);padding:1px 5px;border-radius:4px}}
footer{{text-align:center;color:var(--muted);font-size:13px;padding:8px}}footer a{{color:var(--accent)}}
</style></head><body>
<header><h1>{E(s.AppName + " " + s.AppVersion)}</h1><p>Configuração para o Microsoft Intune · Aplicativos &gt; Windows &gt; Adicionar &gt; Aplicativo do Windows (Win32)</p></header>
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
                h.Append($"<tr><td class=\"f\">{E(field)}</td><td class=\"v\">{E(value)}</td><td class=\"c\"><button onclick=\"cp(this)\">Copiar</button></td></tr>");
            }
            h.Append("</table></section>");
            h.Append($@"<section><h2>Teste local antes de publicar</h2><div class=""note"">Abra o PowerShell <b>como Administrador</b> na pasta <code>Pacote_PSADT</code> e execute
<code>.\Invoke-AppDeployToolkit.exe -DeploymentType Install -DeployMode Interactive</code> e depois
<code>.\Invoke-AppDeployToolkit.exe -DeploymentType Uninstall -DeployMode Interactive</code>.
Logs do PSADT: <code>{E(logDir)}</code>. No modo Automático as janelas só aparecem quando há um usuário conectado e, se houver aplicativos configurados para fechar, quando algum deles estiver aberto.</div></section>
</main>
<footer>Gerado em {DateTime.Now:dd/MM/yyyy HH:mm} pelo <a href=""{AppInfo.RepoUrl}"">J365 IntunePSApp</a> {AppInfo.Version} · PSAppDeployToolkit {E(r.PsadtVersion)} · <a href=""{AppInfo.BlogUrl}"">Jornada 365</a></footer>
<script>
function cp(b){{var t=b.parentNode.previousSibling.innerText;function ok(){{var o=b.innerText;b.innerText='Copiado';setTimeout(function(){{b.innerText=o}},1200)}}
if(navigator.clipboard&&window.isSecureContext){{navigator.clipboard.writeText(t).then(ok,function(){{fb(t);ok()}})}}else{{fb(t);ok()}}}}
function fb(t){{var a=document.createElement('textarea');a.value=t;document.body.appendChild(a);a.select();try{{document.execCommand('copy')}}catch(e){{}}document.body.removeChild(a)}}
</script></body></html>");
            string htmlPath = Path.Combine(intuneDir, "INSTRUCOES_INTUNE.html");
            File.WriteAllText(htmlPath, h.ToString(), TextFiles.Utf8Bom);
            return (htmlPath, txtPath);
        }
    }
}
