using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

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
                if (r != 0) throw new IOException($"Não foi possível abrir o MSI (erro {r}). O arquivo pode estar corrompido ou em uso.");
                r = NativeMethods.MsiDatabaseOpenView(db, "SELECT `Property`, `Value` FROM `Property`", out view);
                if (r != 0) throw new IOException($"MSI sem tabela Property (erro {r}).");
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
