using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Optional per-user installation (no administrator rights): copies the executable to
    /// %LOCALAPPDATA%\Programs, creates the shortcuts and registers the uninstall entry in "Installed apps".
    /// The application keeps working as a portable executable when this is never used.
    /// </summary>
    public static class InstallService
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\J365.IntunePSApp";
        private const string ExeName = "J365.IntunePSApp.exe";
        private const string ShortcutName = "J365 IntunePSApp.lnk";

        public static string InstalledExe => Path.Combine(AppPaths.InstallDir, ExeName);
        public static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
        public static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);
        public static bool IsInstalled => File.Exists(InstalledExe);
        public static bool HasDesktopShortcut => File.Exists(DesktopShortcut);

        /// <summary>Version registered in "Installed apps", or null.</summary>
        public static string InstalledVersion
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(UninstallKey);
                    return k?.GetValue("DisplayVersion") as string;
                }
                catch { return null; }
            }
        }

        /// <summary>Installs (or updates) the per-user copy and returns the path of the installed executable.</summary>
        public static string Install(bool desktopShortcut)
        {
            Directory.CreateDirectory(AppPaths.InstallDir);
            string target = InstalledExe;
            if (!string.Equals(Path.GetFullPath(AppPaths.ExePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                // The installed copy may be open: it cannot be overwritten, but it can be renamed (AppUpdater.Replace).
                // It goes on running from the old file until it is closed; the next start uses the new one.
                string staged = target + ".new";
                try
                {
                    File.Copy(AppPaths.ExePath, staged, true);
                    AppUpdater.Replace(staged, target);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    try { File.Delete(staged); } catch { }
                    throw new IOException(T("Err_InstallInUse"), ex);
                }
                MigrateData();
            }
            string description = T("App_Tagline");
            CreateShortcut(StartMenuShortcut, target, "", description);
            if (desktopShortcut) CreateShortcut(DesktopShortcut, target, "", description);
            else TryDelete(DesktopShortcut);

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppInfo.Name);
                k.SetValue("DisplayVersion", AppInfo.Version);
                k.SetValue("Publisher", AppInfo.Brand);
                k.SetValue("DisplayIcon", target);
                k.SetValue("InstallLocation", AppPaths.InstallDir);
                k.SetValue("UninstallString", $"\"{target}\" --uninstall");
                k.SetValue("QuietUninstallString", $"\"{target}\" --uninstall --quiet");
                k.SetValue("URLInfoAbout", AppInfo.RepoUrl);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(target).Length / 1024), RegistryValueKind.DWord);
            }
            Log.Ok(T("Log_Installed", AppPaths.InstallDir));
            return target;
        }

        /// <summary>
        /// The installed copy updated itself (or was replaced): "Installed apps" shows the version that is running.
        /// </summary>
        public static void RefreshRegistration()
        {
            if (AppPaths.IsInstalledCopy && InstalledVersion is string v && v != AppInfo.Version) SetRegisteredVersion(AppInfo.Version);
        }

        /// <summary>Version (and size) shown in "Installed apps" for the installed copy.</summary>
        public static void SetRegisteredVersion(string version)
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(UninstallKey, true);
                if (k == null) return;
                k.SetValue("DisplayVersion", version);
                k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
            catch { }
        }

        /// <summary>
        /// Takes the settings, database and tools already downloaded by the portable copy to the per-user data folder,
        /// so nothing is downloaded twice. Existing data of a previous installation is never overwritten.
        /// </summary>
        private static void MigrateData()
        {
            string from = AppPaths.Data, to = AppPaths.UserDataDir;
            if (string.Equals(Path.GetFullPath(from).TrimEnd('\\'), Path.GetFullPath(to).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            string targetConfig = Path.Combine(to, "config.json");
            if (File.Exists(targetConfig)) return;
            try
            {
                Directory.CreateDirectory(to);
                foreach (var sub in new[] { "catalog", "tools", "branding" })
                {
                    string src = Path.Combine(from, sub);
                    if (!Directory.Exists(src)) continue;
                    foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    {
                        string dest = Path.Combine(to, sub, Path.GetRelativePath(src, f));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(f, dest, true);
                    }
                }
                var cfg = Json.Load<AppConfig>(AppPaths.ConfigFile);
                string oldRoot = Path.GetFullPath(from).TrimEnd('\\') + "\\";
                // Packages already created stay where they are; an empty default folder moves to Documents.
                bool hasPackages = Directory.Exists(cfg.OutputFolder) && Directory.EnumerateDirectories(cfg.OutputFolder).Any();
                if (string.IsNullOrWhiteSpace(cfg.OutputFolder) || (!hasPackages && Path.GetFullPath(cfg.OutputFolder).StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase)))
                    cfg.OutputFolder = "";
                if (!string.IsNullOrWhiteSpace(cfg.LogoPath) && Path.GetFullPath(cfg.LogoPath).StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase))
                    cfg.LogoPath = Path.Combine(to, Path.GetRelativePath(from, cfg.LogoPath));
                Json.Save(targetConfig, cfg);
            }
            catch (Exception ex) { Log.Warn(T("Log_MigrateFailed", ex.Message)); }
        }

        /// <summary>
        /// Removes the shortcuts, the uninstall entry and the installed copy. Packages created by the user are never
        /// touched; settings and downloaded tools are removed only when <paramref name="removeData"/> is true.
        /// </summary>
        public static void Uninstall(bool removeData)
        {
            TryDelete(StartMenuShortcut);
            TryDelete(DesktopShortcut);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

            var folders = new List<string>();
            if (IsOwnFolder(AppPaths.InstallDir, "J365 IntunePSApp")) folders.Add(AppPaths.InstallDir);
            if (removeData && IsOwnFolder(AppPaths.UserDataDir, "IntunePSApp"))
            {
                RemoveConnectionCertificate();
                // Packages are never removed: an output folder inside the data folder ("Pacotes" of 1.x, or one the user
                // chose there) stays, and only the rest of the data folder goes.
                var keep = PackageFoldersIn(AppPaths.UserDataDir);
                if (keep.Count == 0) folders.Add(AppPaths.UserDataDir);
                else
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(AppPaths.UserDataDir))
                    {
                        if (keep.Any(k => k.Equals(entry.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) continue;
                        if (Directory.Exists(entry)) folders.Add(entry);
                        else TryDelete(entry);
                    }
                }
            }

            bool runningInstalledCopy = AppPaths.IsInstalledCopy;
            foreach (var f in folders.ToList())
            {
                if (runningInstalledCopy) continue; // handled below: a running executable cannot delete itself
                try { if (Directory.Exists(f)) Directory.Delete(f, true); folders.Remove(f); } catch { }
            }
            foreach (var folder in folders)
            {
                // A running executable cannot delete itself: a detached cmd keeps trying (about two minutes)
                // until this process has exited and the folder is gone.
                string cmd = $"/c for /l %i in (1,1,120) do @(rmdir /s /q \"{folder}\" 2>nul & if not exist \"{folder}\" exit 0 & ping -n 2 127.0.0.1 >nul)";
                Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetTempPath()
                });
            }
        }

        /// <summary>
        /// Top-level folders of <paramref name="dataDir"/> that hold packages: the output folder of its settings when it is
        /// inside, and "Pacotes" (the default of 1.x). Only folders with something in them are kept.
        /// </summary>
        internal static List<string> PackageFoldersIn(string dataDir)
        {
            var keep = new List<string>();
            string root = Path.GetFullPath(dataDir).TrimEnd('\\');
            var candidates = new List<string> { Path.Combine(root, "Pacotes"), Path.Combine(root, "Packages") };
            try
            {
                string output = Json.Load<AppConfig>(Path.Combine(root, "config.json")).OutputFolder;
                if (!string.IsNullOrWhiteSpace(output)) candidates.Add(Path.GetFullPath(output));
            }
            catch { }
            foreach (var c in candidates)
            {
                string full = c.TrimEnd('\\');
                if (!full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                // The top-level folder of the data folder that contains it.
                string top = Path.Combine(root, full.Substring(root.Length + 1).Split('\\')[0]);
                try
                {
                    if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any() && !keep.Contains(top, StringComparer.OrdinalIgnoreCase)) keep.Add(top);
                }
                catch { }
            }
            return keep;
        }

        /// <summary>
        /// "Remove everything" also removes the certificate this computer used to connect to Intune (its key gives access
        /// to the apps of the tenant until it expires). Not when the copy running this has the same certificate in use.
        /// </summary>
        private static void RemoveConnectionCertificate()
        {
            try
            {
                string installed = AppCertificate.Normalize(Json.Load<AppConfig>(Path.Combine(AppPaths.UserDataDir, "config.json")).IntuneCertificateThumbprint);
                if (string.IsNullOrEmpty(installed)) return;
                bool sameData = string.Equals(Path.GetFullPath(AppPaths.Data).TrimEnd('\\'), Path.GetFullPath(AppPaths.UserDataDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                if (!sameData && AppCertificate.Normalize(ConfigStore.Current.IntuneCertificateThumbprint) == installed) return;
                AppCertificate.Delete(installed);
            }
            catch (Exception ex) { Log.Warn(T("Cert_ErrDelete", ex.Message)); }
        }

        /// <summary>Safety net before deleting: the folder must be the expected one, under %LOCALAPPDATA%.</summary>
        private static bool IsOwnFolder(string path, string expectedName)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
            string full = Path.GetFullPath(path).TrimEnd('\\');
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).TrimEnd('\\');
            return local.Length > 3
                && full.StartsWith(local + "\\", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(full), expectedName, StringComparison.OrdinalIgnoreCase);
        }

        public static void SetDesktopShortcut(bool enabled)
        {
            if (enabled) CreateShortcut(DesktopShortcut, IsInstalled ? InstalledExe : AppPaths.ExePath, "", T("App_Tagline"));
            else TryDelete(DesktopShortcut);
        }

        private static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }

        // ------------------------------------------------------------------ shortcuts (IShellLink: no dependency on Windows Script Host)

        public static void CreateShortcut(string lnkPath, string target, string arguments, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(target);
                link.SetArguments(arguments ?? "");
                link.SetWorkingDirectory(Path.GetDirectoryName(target));
                link.SetDescription(description ?? "");
                link.SetIconLocation(target, 0);
                ((IPersistFile)link).Save(lnkPath, false);
            }
            finally { Marshal.ReleaseComObject(link); }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
