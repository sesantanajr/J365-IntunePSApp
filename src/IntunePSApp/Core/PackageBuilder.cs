using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace J365.IntunePSApp.Core
{
    /// <summary>Orquestra a geracao: PSADT + instalador + acoes + visual + deteccao + .intunewin + instrucoes.</summary>
    public static class PackageBuilder
    {
        public static ValidationResult Validate(PackageSettings s)
        {
            var v = new ValidationResult();
            if (string.IsNullOrWhiteSpace(s.InstallerPath) || !File.Exists(s.InstallerPath)) v.Errors.Add("Selecione o instalador (.msi ou .exe) ou escolha um aplicativo no catálogo.");
            else if (!Regex.IsMatch(Path.GetExtension(s.InstallerPath), @"^\.(msi|exe)$", RegexOptions.IgnoreCase)) v.Errors.Add("O instalador precisa ser .msi ou .exe.");
            if (string.IsNullOrWhiteSpace(s.AppName)) v.Errors.Add("Informe o nome do aplicativo.");
            if (string.IsNullOrWhiteSpace(s.AppVersion)) v.Errors.Add("Informe a versão do aplicativo.");

            string pat = s.UninstallPattern ?? "";
            if (string.IsNullOrWhiteSpace(pat)) v.Errors.Add("Informe o nome do programa como aparece em \"Aplicativos instalados\" (usado na desinstalação).");
            else if (Regex.Replace(pat, @"[\*\?\s]", "").Length < 3) v.Errors.Add($"O nome '{pat}' é genérico demais e poderia remover outros programas. Use algo como '7-Zip*'.");

            if (s.InstallerType == "EXE" && string.IsNullOrWhiteSpace(s.InstallArgs)) v.Warnings.Add("Instalador .exe sem parâmetros silenciosos: a instalação pelo Intune pode travar esperando cliques.");
            if (s.InstallerType == "EXE" && string.IsNullOrWhiteSpace(s.UninstallExeArgs)) v.Warnings.Add("Sem parâmetros de desinstalação para .exe: só serão removidos programas que registram desinstalação silenciosa (ou MSI).");
            foreach (var p in s.ProcessesToClose) if (!Regex.IsMatch(p, @"^[\w\.\-\+ ]+$")) v.Errors.Add($"Nome de processo inválido: '{p}'. Use só o nome do executável (ex.: chrome).");
            if (s.AllowDefer && (s.DeferTimes < 1 || s.DeferTimes > 99)) v.Errors.Add("Adiamentos: use um número entre 1 e 99.");
            if (s.CloseAppsCountdown < 0 || s.CloseAppsCountdown > 3000) v.Errors.Add("Contagem para fechar aplicativos: de 0 a 3000 segundos.");
            if (s.RestartPrompt && (s.RestartCountdown < 60 || s.RestartCountdown > 86400)) v.Errors.Add("Contagem para reiniciar: de 60 a 86400 segundos.");
            if (!string.IsNullOrWhiteSpace(s.LogoPath) && !File.Exists(s.LogoPath)) v.Errors.Add($"Logo não encontrado: {s.LogoPath}");
            if (!string.IsNullOrWhiteSpace(s.AccentColor) && !Regex.IsMatch(s.AccentColor.Trim(), "^#?[0-9A-Fa-f]{6}$")) v.Errors.Add("Cor de destaque inválida (use #RRGGBB).");
            if (!string.IsNullOrWhiteSpace(s.ExtraSuccessCodes) && !Regex.IsMatch(s.ExtraSuccessCodes, @"^[\d,;\s\-]+$")) v.Errors.Add("Códigos de sucesso extras: use números separados por vírgula.");
            foreach (var f in s.ExtraFiles) if (!File.Exists(f) && !Directory.Exists(f)) v.Errors.Add($"Arquivo extra não encontrado: {f}");
            foreach (var a in s.Actions) v.Errors.AddRange(ActionCatalog.Validate(a));

            switch (s.DetectionMethod)
            {
                case "Registry":
                    if (Regex.Replace(s.DetectionNamePattern ?? "", @"[\*\?\s]", "").Length < 2) v.Errors.Add("Detecção: informe o nome do programa (ex.: 7-Zip*).");
                    break;
                case "ProductCode":
                    if (string.IsNullOrWhiteSpace(s.MsiProductCode)) v.Errors.Add("Detecção: informe o código do produto / chave de desinstalação.");
                    break;
                case "File":
                    if (string.IsNullOrWhiteSpace(s.DetectionFilePath)) v.Errors.Add("Detecção: informe o caminho do arquivo.");
                    break;
                default: v.Errors.Add("Detecção: método desconhecido."); break;
            }
            if (s.DetectionCheckVersion && string.IsNullOrWhiteSpace(s.DetectionVersion)) v.Errors.Add("Detecção: informe a versão mínima ou desmarque \"Exigir versão\".");
            return v;
        }

        public static async Task<BuildResult> BuildAsync(PackageSettings input, bool forceNative, CancellationToken ct)
        {
            var s = input.Clone();
            if (string.IsNullOrEmpty(s.InstallerType)) s.InstallerType = Path.GetExtension(s.InstallerPath).TrimStart('.').ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(s.DetectionNamePattern)) s.DetectionNamePattern = s.UninstallPattern;
            var check = Validate(s);
            foreach (var w in check.Warnings) Log.Warn(w);
            if (!check.IsValid) throw new InvalidOperationException("Corrija antes de gerar:\n• " + string.Join("\n• ", check.Errors));

            var sw = Stopwatch.StartNew();
            Log.Step($"Iniciando: {s.AppName} {s.AppVersion}");
            Log.Step("Verificando o PSAppDeployToolkit...");
            string template = await ToolsService.EnsurePsadtAsync(ct);
            string psadtVersion = Path.GetFileName(template);
            Log.Ok($"PSAppDeployToolkit {psadtVersion}");

            string outRoot = string.IsNullOrWhiteSpace(s.OutputRoot) ? AppPaths.DefaultOutput : s.OutputRoot;
            Directory.CreateDirectory(outRoot);
            string name = Names.Safe($"{s.AppName}_{s.AppVersion}");
            string pkgRoot = Path.Combine(outRoot, name);
            if (Directory.Exists(pkgRoot)) pkgRoot = Path.Combine(outRoot, $"{name}_{DateTime.Now:yyyyMMdd-HHmmss}");
            string srcDir = Path.Combine(pkgRoot, "Pacote_PSADT");
            string intuneDir = Path.Combine(pkgRoot, "Intune");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(intuneDir);
            Log.PackageLogFile = Path.Combine(pkgRoot, "geracao.log");
            try
            {
                Log.Info($"Pasta do pacote: {pkgRoot}");

                Log.Step("Copiando o template do PSAppDeployToolkit...");
                await Task.Run(() => CopyDirectory(template, srcDir), ct);
                foreach (var ph in new[] { @"Files\Add Setup Files Here.txt", @"SupportFiles\Add Supporting Files Here.txt" })
                {
                    var p = Path.Combine(srcDir, ph);
                    if (File.Exists(p)) File.Delete(p);
                }
                string filesDir = Path.Combine(srcDir, "Files");
                string supportDir = Path.Combine(srcDir, "SupportFiles");
                Directory.CreateDirectory(filesDir);
                Directory.CreateDirectory(supportDir);

                Log.Step("Copiando o instalador...");
                var inst = new FileInfo(s.InstallerPath);
                if (s.IncludeInstallerFolder)
                {
                    string outFull = Path.GetFullPath(outRoot).TrimEnd('\\') + "\\";
                    await Task.Run(() =>
                    {
                        foreach (var f in Directory.GetFiles(inst.DirectoryName, "*", SearchOption.AllDirectories))
                        {
                            if (f.StartsWith(outFull, StringComparison.OrdinalIgnoreCase) || f.StartsWith(AppPaths.Data, StringComparison.OrdinalIgnoreCase)) continue;
                            string dest = Path.Combine(filesDir, Path.GetRelativePath(inst.DirectoryName, f));
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            File.Copy(f, dest, true);
                        }
                    }, ct);
                }
                else File.Copy(inst.FullName, Path.Combine(filesDir, inst.Name), true);

                if (s.ExtraFiles.Count > 0)
                {
                    Log.Step($"Copiando {s.ExtraFiles.Count} arquivo(s) extra(s) para SupportFiles...");
                    foreach (var f in s.ExtraFiles)
                    {
                        if (File.Exists(f)) File.Copy(f, Path.Combine(supportDir, Path.GetFileName(f)), true);
                        else if (Directory.Exists(f)) CopyDirectory(f, Path.Combine(supportDir, Path.GetFileName(f.TrimEnd('\\'))));
                    }
                }

                Log.Step("Gerando o Invoke-AppDeployToolkit.ps1...");
                var (tpl, _) = TextFiles.Read(Path.Combine(template, "Invoke-AppDeployToolkit.ps1"));
                string scriptPath = Path.Combine(srcDir, "Invoke-AppDeployToolkit.ps1");
                TextFiles.Write(scriptPath, ScriptGenerator.DeployScript(tpl, s, inst.Name), true);
                var errors = PowerShellValidator.Validate(scriptPath);
                if (errors.Length > 0) throw new InvalidDataException("O script gerado tem erro de sintaxe (revise as ações de script PowerShell):\n" + string.Join("\n", errors.Take(5)));
                Log.Ok($"Script gerado e validado ({s.Actions.Count} ação(ões) personalizada(s)).");

                Log.Step("Aplicando a identidade visual e as mensagens...");
                ScriptGenerator.UpdateConfig(Path.Combine(srcDir, "Config", "config.psd1"), s);
                int n = ScriptGenerator.UpdateStrings(Path.Combine(srcDir, "Strings"), s);
                if (n > 0) Log.Info($"Mensagem de boas-vindas aplicada em {n} idioma(s).");
                string assets = Path.Combine(srcDir, "Assets");
                Directory.CreateDirectory(assets);
                string logoPng = null;
                if (!string.IsNullOrWhiteSpace(s.LogoPath))
                {
                    logoPng = Path.Combine(assets, "AppIcon.png");
                    ImageTools.SaveLogoPng(s.LogoPath, logoPng, 256);
                    Log.Ok("Logo da empresa aplicado.");
                }
                if (!string.IsNullOrWhiteSpace(s.LogoPath) || !string.IsNullOrWhiteSpace(s.CompanyName))
                    ImageTools.SaveBanner(logoPng ?? Path.Combine(assets, "AppIcon.png"), s.CompanyName, s.AccentColor, Path.Combine(assets, "Banner.Classic.png"));

                Log.Step("Gerando o script de detecção...");
                string detPath = Path.Combine(intuneDir, $"Deteccao_{Names.Safe($"{s.AppName}_{s.AppVersion}")}.ps1");
                File.WriteAllText(detPath, ScriptGenerator.DetectionScript(s), System.Text.Encoding.ASCII);
                var detErr = PowerShellValidator.Validate(detPath);
                if (detErr.Length > 0) throw new InvalidDataException("Script de detecção inválido: " + detErr[0]);
                Log.Ok($"Detecção: {Path.GetFileName(detPath)}");

                string icon = null;
                if (s.InstallerType == "EXE")
                {
                    string ip = Path.Combine(intuneDir, "Icone_App.png");
                    if (ImageTools.SaveExeIcon(inst.FullName, ip)) icon = ip;
                }
                if (icon == null && logoPng != null)
                {
                    icon = Path.Combine(intuneDir, "Icone_App.png");
                    File.Copy(logoPng, icon, true);
                }

                Log.Step("Criando o arquivo .intunewin...");
                string iwPath = Path.Combine(intuneDir, Names.Safe($"{s.AppName}_{s.AppVersion}") + ".intunewin");
                string tool = await IntuneWinBuilder.BuildAsync(srcDir, iwPath, forceNative, ct);
                var val = await Task.Run(() => IntuneWinBuilder.Validate(iwPath), ct);
                if (!val.Valid) throw new InvalidDataException("O .intunewin gerado não passou na validação (HMAC/SHA256).");
                long size = new FileInfo(iwPath).Length;
                Log.Ok($".intunewin criado e validado ({size / 1024d / 1024d:N1} MB, {val.FileCount} itens) — {tool}.");

                var cmds = ScriptGenerator.Commands(s);
                var result = new BuildResult
                {
                    PackageFolder = pkgRoot,
                    PsadtFolder = srcDir,
                    IntuneFolder = intuneDir,
                    IntuneWinFile = iwPath,
                    IntuneWinTool = tool,
                    IntuneWinSize = size,
                    DetectionScript = detPath,
                    AppIcon = icon,
                    InstallCommand = cmds.Install,
                    UninstallCommand = cmds.Uninstall,
                    PsadtVersion = psadtVersion,
                    ProjectFile = Path.Combine(pkgRoot, "projeto.json"),
                    Warnings = check.Warnings
                };
                var (html, txt) = SummaryWriter.Write(s, result, intuneDir);
                result.SummaryHtml = html;
                result.SummaryTxt = txt;
                Json.Save(result.ProjectFile, s);
                Log.Ok($"Pacote concluído em {sw.Elapsed.TotalSeconds:N0} s.");
                return result;
            }
            catch
            {
                Log.Error("A geração foi interrompida. Veja os detalhes acima.");
                throw;
            }
            finally { Log.PackageLogFile = null; }
        }

        private static void CopyDirectory(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(src, d)));
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(f, Path.Combine(dest, Path.GetRelativePath(src, f)), true);
        }

        // ------------------------------------------------------------------ historico e testes locais

        public static List<PackageHistoryItem> History(string outputRoot)
        {
            var list = new List<PackageHistoryItem>();
            if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot)) return list;
            foreach (var dir in Directory.GetDirectories(outputRoot))
            {
                string proj = Path.Combine(dir, "projeto.json");
                if (!File.Exists(proj)) continue;
                try
                {
                    var s = JsonSerializer.Deserialize<PackageSettings>(File.ReadAllText(proj), Json.Options);
                    var intune = Path.Combine(dir, "Intune");
                    list.Add(new PackageHistoryItem
                    {
                        Folder = dir,
                        ProjectFile = proj,
                        AppName = s.AppName,
                        AppVersion = s.AppVersion,
                        Vendor = s.AppVendor,
                        Date = File.GetLastWriteTime(proj),
                        IntuneWin = Directory.Exists(intune) ? Directory.GetFiles(intune, "*.intunewin").FirstOrDefault() : null,
                        SummaryHtml = Path.Combine(intune, "INSTRUCOES_INTUNE.html")
                    });
                }
                catch { }
            }
            return list.OrderByDescending(i => i.Date).ToList();
        }

        public static (bool Detected, string Output) TestDetection(PackageSettings s)
        {
            string tmp = Path.Combine(AppPaths.Temp, $"deteccao_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(tmp, ScriptGenerator.DetectionScript(s), System.Text.Encoding.ASCII);
            try
            {
                var psi = new ProcessStartInfo(ToolsService.WindowsPowerShell, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.Environment["PSModulePath"] = "";
                using var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(60000);
                return (p.ExitCode == 0 && output.Length > 0, output);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static void RunLocalTest(string psadtFolder, string deploymentType)
        {
            var psi = new ProcessStartInfo(Path.Combine(psadtFolder, "Invoke-AppDeployToolkit.exe"), $"-DeploymentType {deploymentType} -DeployMode Interactive")
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = psadtFolder
            };
            Process.Start(psi);
        }
    }
}
