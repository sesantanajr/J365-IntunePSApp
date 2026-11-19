using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    internal static class NativeMethods
    {
        // ---------------- msi.dll
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiOpenDatabase(string szDatabasePath, IntPtr szPersist, out IntPtr phDatabase);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiDatabaseOpenView(IntPtr hDatabase, string szQuery, out IntPtr phView);
        [DllImport("msi.dll")] public static extern uint MsiViewExecute(IntPtr hView, IntPtr hRecord);
        [DllImport("msi.dll")] public static extern uint MsiViewFetch(IntPtr hView, out IntPtr phRecord);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiRecordGetString(IntPtr hRecord, uint iField, StringBuilder szValueBuf, ref uint pcchValueBuf);
        [DllImport("msi.dll")] public static extern uint MsiCloseHandle(IntPtr hAny);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] public static extern uint MsiGetSummaryInformation(IntPtr hDatabase, string szDatabasePath, uint uiUpdateCount, out IntPtr phSummaryInfo);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        public static extern uint MsiSummaryInfoGetProperty(IntPtr hSummaryInfo, uint uiProperty, out uint puiDataType, out int piValue, ref System.Runtime.InteropServices.ComTypes.FILETIME pftValue, StringBuilder szValueBuf, ref uint pcchValueBuf);
        [DllImport("msi.dll")] public static extern uint MsiRecordDataSize(IntPtr hRecord, uint iField);
        [DllImport("msi.dll")] public static extern uint MsiRecordReadStream(IntPtr hRecord, uint iField, byte[] szDataBuf, ref uint pcbDataBuf);

        // ---------------- wintrust.dll
        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        public static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

        // ---------------- user32 / kernel32
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("kernel32.dll")] public static extern bool AttachConsole(int dwProcessId);

        // ---------------- crypt32.dll (DPAPI: data readable only by the same Windows user)
        [StructLayout(LayoutKind.Sequential)] public struct DATA_BLOB { public int cbData; public IntPtr pbData; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);
        [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr hMem);

        /// <summary>PF_VIRT_FIRMWARE_ENABLED: hardware virtualization is enabled in the firmware.</summary>
        public const uint PF_VIRT_FIRMWARE_ENABLED = 21;
        [DllImport("kernel32.dll")] public static extern bool IsProcessorFeaturePresent(uint processorFeature);
    }

    public static class MsiReader
    {
        public static (Dictionary<string, string> Properties, string Template) Read(string path)
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string template = "";
            IntPtr db = IntPtr.Zero, view = IntPtr.Zero, sum = IntPtr.Zero;
            try
            {
                uint r = NativeMethods.MsiOpenDatabase(path, IntPtr.Zero, out db);
                if (r != 0) throw new IOException(T("Err_MsiOpen", r));
                r = NativeMethods.MsiDatabaseOpenView(db, "SELECT `Property`, `Value` FROM `Property`", out view);
                if (r != 0) throw new IOException(T("Err_MsiNoProperty", r));
                NativeMethods.MsiViewExecute(view, IntPtr.Zero);
                while (NativeMethods.MsiViewFetch(view, out IntPtr rec) == 0)
                {
                    try { props[GetString(rec, 1)] = GetString(rec, 2); }
                    finally { NativeMethods.MsiCloseHandle(rec); }
                }
                if (NativeMethods.MsiGetSummaryInformation(db, null, 0, out sum) == 0)
                {
                    var ft = new System.Runtime.InteropServices.ComTypes.FILETIME();
                    uint len = 256; var sb = new StringBuilder((int)len);
                    if (NativeMethods.MsiSummaryInfoGetProperty(sum, 7, out _, out _, ref ft, sb, ref len) == 0) template = sb.ToString();
                }
            }
            finally
            {
                if (sum != IntPtr.Zero) NativeMethods.MsiCloseHandle(sum);
                if (view != IntPtr.Zero) NativeMethods.MsiCloseHandle(view);
                if (db != IntPtr.Zero) NativeMethods.MsiCloseHandle(db);
            }
            return (props, template);
        }

        /// <summary>
        /// The product icon stored inside an MSI (table Icon): the one named by the ARPPRODUCTICON property, otherwise the
        /// first one. The data is an .ico file or a small .exe/.dll that carries the icon; the name tells which (its extension).
        /// Returns null when the package has no icon.
        /// </summary>
        public static (string Name, byte[] Data)? ReadIcon(string path)
        {
            const uint maxIconBytes = 8 * 1024 * 1024;
            IntPtr db = IntPtr.Zero, view = IntPtr.Zero;
            try
            {
                if (NativeMethods.MsiOpenDatabase(path, IntPtr.Zero, out db) != 0) return null;
                string wanted = null;
                try { if (Read(path).Properties.TryGetValue("ARPPRODUCTICON", out var v)) wanted = v; } catch { }
                // A package without the Icon table makes this fail: no icon.
                if (NativeMethods.MsiDatabaseOpenView(db, "SELECT `Name`, `Data` FROM `Icon`", out view) != 0) return null;
                if (NativeMethods.MsiViewExecute(view, IntPtr.Zero) != 0) return null;
                (string Name, byte[] Data)? first = null;
                while (NativeMethods.MsiViewFetch(view, out IntPtr rec) == 0)
                {
                    try
                    {
                        string name = GetString(rec, 1);
                        uint size = NativeMethods.MsiRecordDataSize(rec, 2);
                        if (size == 0 || size > maxIconBytes) continue;
                        var data = new byte[size];
                        uint read = size;
                        if (NativeMethods.MsiRecordReadStream(rec, 2, data, ref read) != 0 || read == 0) continue;
                        if (read < size) Array.Resize(ref data, (int)read);
                        if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) return (name, data);
                        first ??= (name, data);
                    }
                    finally { NativeMethods.MsiCloseHandle(rec); }
                }
                return first;
            }
            catch { return null; }
            finally
            {
                if (view != IntPtr.Zero) NativeMethods.MsiCloseHandle(view);
                if (db != IntPtr.Zero) NativeMethods.MsiCloseHandle(db);
            }
        }

        /// <summary>A program inside an MSI (row of the File table) and the cabinet that holds its compressed copy.</summary>
        public sealed class PackedFile
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public long Size { get; set; }
            /// <summary>"#name": cabinet embedded in the .msi; "name.cab": next to the .msi; empty: not compressed.</summary>
            public string Cabinet { get; set; } = "";
            public bool FromShortcut { get; set; }
        }

        /// <summary>
        /// Programs of an MSI that probably carry the icon of the application, best first: what the shortcuts of the
        /// package open, then the other programs, largest first. Uninstallers, setups and updaters are left out.
        /// </summary>
        public static List<PackedFile> IconCandidates(string path, int max = 3)
        {
            var result = new List<PackedFile>();
            IntPtr db = IntPtr.Zero;
            try
            {
                if (NativeMethods.MsiOpenDatabase(path, IntPtr.Zero, out db) != 0) return result;
                var files = Rows(db, "SELECT `File`, `FileName`, `FileSize`, `Sequence` FROM `File`", 4)
                    .Select(r => new { Key = r[0], Name = LongName(r[1]), Size = Number(r[2]), Sequence = Number(r[3]) })
                    .Where(f => f.Key.Length > 0)
                    .ToList();
                var byKey = new Dictionary<string, (string Name, long Size, long Sequence)>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in files) byKey.TryAdd(f.Key, (f.Name, f.Size, f.Sequence));
                var keyFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in Rows(db, "SELECT `Component`, `KeyPath` FROM `Component`", 2)) keyFiles.TryAdd(r[0], r[1]);
                var media = Rows(db, "SELECT `LastSequence`, `Cabinet` FROM `Media`", 2)
                    .Select(r => (Last: Number(r[0]), Cabinet: r[1] ?? "")).OrderBy(m => m.Last).ToList();

                var order = new List<(string Key, bool Shortcut)>();
                foreach (var r in Rows(db, "SELECT `Name`, `Component_`, `Target` FROM `Shortcut`", 3))
                {
                    if (IsHelperShortcut(LongName(r[0]))) continue;
                    // "[#file]" (or "[!file]") names the file; an advertised shortcut names a feature and opens the key file of its component.
                    var m = Regex.Match(r[2] ?? "", @"\[[#!]([^\]]+)\]");
                    string key = m.Success ? m.Groups[1].Value : keyFiles.TryGetValue(r[1] ?? "", out var kp) ? kp : null;
                    if (!string.IsNullOrEmpty(key)) order.Add((key, true));
                }
                foreach (var f in files.OrderByDescending(f => f.Size)) order.Add((f.Key, false));

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, shortcut) in order)
                {
                    if (result.Count >= max) break;
                    if (!seen.Add(key) || !byKey.TryGetValue(key, out var f)) continue;
                    if (!f.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || IsHelperProgram(f.Name)) continue;
                    string cabinet = media.FirstOrDefault(x => x.Last >= f.Sequence).Cabinet ?? "";
                    result.Add(new PackedFile { Key = key, Name = f.Name, Size = f.Size, Cabinet = cabinet, FromShortcut = shortcut });
                }
            }
            catch { }
            finally { if (db != IntPtr.Zero) NativeMethods.MsiCloseHandle(db); }
            return result;
        }

        /// <summary>Shortcuts that do not open the application itself.</summary>
        public static bool IsHelperShortcut(string name)
            => Regex.IsMatch(name ?? "", @"(?i)uninstall|\bremove\b|\bhelp\b|read ?me|manual|licen[cs]e|web ?site|\burl\b|documentation|release notes|change ?log|desinstal");

        /// <summary>Programs that are not the application (uninstallers, setups, updaters, crash reporters, runtimes).</summary>
        public static bool IsHelperProgram(string fileName)
            => Regex.IsMatch(Path.GetFileNameWithoutExtension(fileName ?? ""), @"(?i)^(unins|uninst|remove|setup|install|update|updater|autoupdate|crash|bugreport|elevat|repair|vc_?redist|dotnet|ndp\d|msiexec|regasm)");

        /// <summary>
        /// Copies a stream of the _Streams table (a cabinet embedded in the .msi) to a file.
        /// False when there is no such stream or it is larger than <paramref name="maxBytes"/>.
        /// </summary>
        public static bool SaveStream(string path, string name, string destination, long maxBytes)
        {
            // Stream names are identifiers: the name is checked instead of quoted in the query.
            if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"^[\w.\-]+$")) return false;
            IntPtr db = IntPtr.Zero, view = IntPtr.Zero, rec = IntPtr.Zero;
            try
            {
                if (NativeMethods.MsiOpenDatabase(path, IntPtr.Zero, out db) != 0) return false;
                if (NativeMethods.MsiDatabaseOpenView(db, "SELECT `Data` FROM `_Streams` WHERE `Name`='" + name + "'", out view) != 0) return false;
                if (NativeMethods.MsiViewExecute(view, IntPtr.Zero) != 0 || NativeMethods.MsiViewFetch(view, out rec) != 0) return false;
                uint size = NativeMethods.MsiRecordDataSize(rec, 1);
                if (size == 0 || size > maxBytes) return false;
                using var fs = File.Create(destination);
                var buffer = new byte[1 << 20];
                while (true)
                {
                    uint n = (uint)buffer.Length;
                    if (NativeMethods.MsiRecordReadStream(rec, 1, buffer, ref n) != 0) return false;
                    if (n == 0) break;
                    fs.Write(buffer, 0, (int)n);
                }
                return fs.Length > 0;
            }
            catch { return false; }
            finally
            {
                if (rec != IntPtr.Zero) NativeMethods.MsiCloseHandle(rec);
                if (view != IntPtr.Zero) NativeMethods.MsiCloseHandle(view);
                if (db != IntPtr.Zero) NativeMethods.MsiCloseHandle(db);
            }
        }

        /// <summary>Rows of a query as text (integers come back as their digits). Empty when the table does not exist.</summary>
        private static List<string[]> Rows(IntPtr db, string query, int fields)
        {
            var rows = new List<string[]>();
            if (NativeMethods.MsiDatabaseOpenView(db, query, out IntPtr view) != 0) return rows;
            try
            {
                if (NativeMethods.MsiViewExecute(view, IntPtr.Zero) != 0) return rows;
                while (NativeMethods.MsiViewFetch(view, out IntPtr rec) == 0)
                {
                    try
                    {
                        var row = new string[fields];
                        for (uint i = 0; i < fields; i++) row[i] = GetString(rec, i + 1);
                        rows.Add(row);
                    }
                    finally { NativeMethods.MsiCloseHandle(rec); }
                }
            }
            finally { NativeMethods.MsiCloseHandle(view); }
            return rows;
        }

        /// <summary>"7ZFM~1.EXE|7zFM.exe" (short|long) → "7zFM.exe".</summary>
        public static string LongName(string value)
        {
            value ??= "";
            int bar = value.IndexOf('|');
            return bar >= 0 ? value.Substring(bar + 1) : value;
        }

        private static long Number(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : 0;

        private static string GetString(IntPtr rec, uint field)
        {
            uint len = 512;
            var sb = new StringBuilder((int)len);
            uint r = NativeMethods.MsiRecordGetString(rec, field, sb, ref len);
            if (r == 234) // ERROR_MORE_DATA
            {
                len++;
                sb = new StringBuilder((int)len);
                NativeMethods.MsiRecordGetString(rec, field, sb, ref len);
            }
            return sb.ToString();
        }
    }

    public static class Signature
    {
        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        /// <summary>Verifica a assinatura Authenticode (sem consulta de revogacao online).</summary>
        public static bool IsValid(string path)
        {
            IntPtr pPath = Marshal.StringToHGlobalUni(path);
            IntPtr pFile = IntPtr.Zero, pData = IntPtr.Zero;
            try
            {
                var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = pPath };
                pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
                Marshal.StructureToPtr(fi, pFile, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = 2,          // WTD_UI_NONE
                    fdwRevocationChecks = 0, // WTD_REVOKE_NONE
                    dwUnionChoice = 1,       // WTD_CHOICE_FILE
                    pFile = pFile,
                    dwStateAction = 0,
                    dwProvFlags = 0x00001000 // WTD_CACHE_ONLY_URL_RETRIEVAL
                };
                pData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
                Marshal.StructureToPtr(data, pData, false);
                return NativeMethods.WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, pData) == 0;
            }
            catch { return false; }
            finally
            {
                if (pData != IntPtr.Zero) Marshal.FreeHGlobal(pData);
                if (pFile != IntPtr.Zero) Marshal.FreeHGlobal(pFile);
                Marshal.FreeHGlobal(pPath);
            }
        }

        public static (string Name, string Subject) Signer(string path)
        {
            try
            {
#pragma warning disable SYSLIB0057
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
                return (cert.GetNameInfo(X509NameType.SimpleName, false), cert.Subject);
            }
            catch { return (null, null); }
        }
    }
}
