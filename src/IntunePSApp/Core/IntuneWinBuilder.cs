using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Creates the .intunewin file with Microsoft's IntuneWinAppUtil.exe or with the built-in packager, which writes
    /// the same format:
    ///   IntuneWinPackage/Contents/IntunePackage.intunewin -> [HMAC-SHA256 32 bytes][IV 16 bytes][AES-256-CBC(zip of the content)]
    ///   IntuneWinPackage/Metadata/Detection.xml           -> keys, SHA256 digest and setup file
    /// </summary>
    public static class IntuneWinBuilder
    {
        public const string SetupFile = "Invoke-AppDeployToolkit.exe";

        /// <summary>Builds the package and returns the name of the tool that produced it.</summary>
        public static async Task<string> BuildAsync(string sourceFolder, string outputFile, bool forceNative, CancellationToken ct)
        {
            string exe = forceNative ? null : await ToolsService.EnsureIntuneWinAppUtilAsync(ct);
            if (exe != null)
            {
                string tmpOut = Path.Combine(Path.GetDirectoryName(outputFile), "_iwau_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmpOut);
                try
                {
                    Log.Info(T("Log_IwauBuilding"));
                    var psi = new ProcessStartInfo(exe, $"-c \"{sourceFolder.TrimEnd('\\')}\" -s \"{SetupFile}\" -o \"{tmpOut}\" -q")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    var stdout = p.StandardOutput.ReadToEndAsync();
                    var stderr = p.StandardError.ReadToEndAsync();
                    try { await p.WaitForExitAsync(ct); }
                    catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
                    var produced = Directory.GetFiles(tmpOut, "*.intunewin").FirstOrDefault();
                    if (p.ExitCode == 0 && produced != null)
                    {
                        File.Move(produced, outputFile, true);
                        return "IntuneWinAppUtil.exe (Microsoft)";
                    }
                    Log.Warn(T("Log_IwauFailed", p.ExitCode, (await stdout + await stderr).Trim()));
                }
                finally { try { Directory.Delete(tmpOut, true); } catch { } }
            }
            Log.Info(T("Log_NativeBuilding"));
            await Task.Run(() => BuildNative(sourceFolder, SetupFile, outputFile, ct), ct);
            return T("Tool_Native");
        }

        public static void BuildNative(string sourceFolder, string setupFile, string outputFile, CancellationToken ct = default)
        {
            // The two working copies (zip and encrypted zip) go to the application's temporary folder, not next to the
            // package: the output folder is often synchronised (OneDrive) and a cancelled build must not leave gigabytes there.
            string work = Path.Combine(AppPaths.Temp, "_intunewin_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string plainZip = Path.Combine(work, "plain.zip");
                string encFile = Path.Combine(work, "IntunePackage.intunewin");
                string src = Path.GetFullPath(sourceFolder).TrimEnd('\\');

                using (var fs = new FileStream(plainZip, FileMode.CreateNew))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    // Entry names use "/" like Microsoft's tool (the zip format's separator).
                    foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        zip.CreateEntryFromFile(f, f.Substring(src.Length + 1).Replace('\\', '/'), CompressionLevel.Optimal);
                    }
                    foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                        if (!Directory.EnumerateFileSystemEntries(d).Any()) zip.CreateEntry(d.Substring(src.Length + 1).Replace('\\', '/') + "/");
                }
                ct.ThrowIfCancellationRequested();

                byte[] encKey = RandomNumberGenerator.GetBytes(32), macKey = RandomNumberGenerator.GetBytes(32), iv = RandomNumberGenerator.GetBytes(16);
                byte[] digest; long plainSize;
                using (var fin = File.OpenRead(plainZip)) { digest = SHA256.HashData(fin); plainSize = fin.Length; }

                byte[] mac;
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256; aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using var outFs = new FileStream(encFile, FileMode.CreateNew, FileAccess.ReadWrite);
                    outFs.Write(new byte[32], 0, 32); // placeholder for the HMAC
                    outFs.Write(iv, 0, 16);
                    using (var enc = aes.CreateEncryptor())
                    using (var fin = File.OpenRead(plainZip))
                    {
                        const int chunk = 1 << 20;
                        var buf = new byte[chunk];
                        var obuf = new byte[chunk + 32];
                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            int n = 0, r;
                            while (n < chunk && (r = fin.Read(buf, n, chunk - n)) > 0) n += r;
                            if (fin.Position < fin.Length)
                            {
                                int w = enc.TransformBlock(buf, 0, n, obuf, 0);
                                outFs.Write(obuf, 0, w);
                            }
                            else
                            {
                                var last = enc.TransformFinalBlock(buf, 0, n);
                                outFs.Write(last, 0, last.Length);
                                break;
                            }
                        }
                    }
                    outFs.Flush();
                    outFs.Position = 32;
                    using (var hmac = new HMACSHA256(macKey)) mac = hmac.ComputeHash(outFs); // HMAC over IV + ciphertext
                    outFs.Position = 0;
                    outFs.Write(mac, 0, 32);
                }

                string esc(string s) => SecurityElement.Escape(s);
                string xml =
                    "<ApplicationInfo xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" ToolVersion=\"1.8.7.0\">\r\n" +
                    $"  <Name>{esc(setupFile)}</Name>\r\n" +
                    $"  <UnencryptedContentSize>{plainSize}</UnencryptedContentSize>\r\n" +
                    "  <FileName>IntunePackage.intunewin</FileName>\r\n" +
                    $"  <SetupFile>{esc(setupFile)}</SetupFile>\r\n" +
                    "  <EncryptionInfo>\r\n" +
                    $"    <EncryptionKey>{Convert.ToBase64String(encKey)}</EncryptionKey>\r\n" +
                    $"    <MacKey>{Convert.ToBase64String(macKey)}</MacKey>\r\n" +
                    $"    <InitializationVector>{Convert.ToBase64String(iv)}</InitializationVector>\r\n" +
                    $"    <Mac>{Convert.ToBase64String(mac)}</Mac>\r\n" +
                    "    <ProfileIdentifier>ProfileVersion1</ProfileIdentifier>\r\n" +
                    $"    <FileDigest>{Convert.ToBase64String(digest)}</FileDigest>\r\n" +
                    "    <FileDigestAlgorithm>SHA256</FileDigestAlgorithm>\r\n" +
                    "  </EncryptionInfo>\r\n" +
                    "</ApplicationInfo>";

                if (File.Exists(outputFile)) File.Delete(outputFile);
                using (var ofs = new FileStream(outputFile, FileMode.CreateNew))
                using (var outer = new ZipArchive(ofs, ZipArchiveMode.Create))
                {
                    outer.CreateEntryFromFile(encFile, "IntuneWinPackage/Contents/IntunePackage.intunewin", CompressionLevel.NoCompression);
                    var e = outer.CreateEntry("IntuneWinPackage/Metadata/Detection.xml", CompressionLevel.NoCompression);
                    using var es = e.Open();
                    var xb = new UTF8Encoding(false).GetBytes(xml);
                    es.Write(xb, 0, xb.Length);
                }
            }
            finally { try { Directory.Delete(work, true); } catch { } }
        }

        /// <summary>Validates an .intunewin file: checks the HMAC, decrypts it, checks the SHA256 digest and counts the files.</summary>
        public static (bool Valid, int FileCount, string SetupFile) Validate(string path)
        {
            string work = Path.Combine(AppPaths.Temp, "_val_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string enc = Path.Combine(work, "enc.bin"), plain = Path.Combine(work, "plain.zip");
                XDocument x;
                using (var z = ZipFile.OpenRead(path))
                {
                    var xe = z.GetEntry("IntuneWinPackage/Metadata/Detection.xml") ?? throw new InvalidDataException(T("Err_IntuneWinNoMetadata"));
                    var ce = z.GetEntry("IntuneWinPackage/Contents/IntunePackage.intunewin") ?? throw new InvalidDataException(T("Err_IntuneWinNoContent"));
                    using (var s = xe.Open()) x = XDocument.Load(s);
                    ce.ExtractToFile(enc, true);
                }
                var info = x.Root;
                var ei = info.Element("EncryptionInfo");
                string Get(string n) => ei.Element(n)?.Value;

                bool macOk;
                using (var fs = File.OpenRead(enc))
                {
                    var header = new byte[32];
                    fs.ReadExactly(header, 0, 32);
                    using var hmac = new HMACSHA256(Convert.FromBase64String(Get("MacKey")));
                    string calc = Convert.ToBase64String(hmac.ComputeHash(fs));
                    macOk = calc == Get("Mac") && Convert.ToBase64String(header) == Get("Mac");
                }
                using (var aes = Aes.Create())
                {
                    aes.Key = Convert.FromBase64String(Get("EncryptionKey"));
                    aes.IV = Convert.FromBase64String(Get("InitializationVector"));
                    using var fin = File.OpenRead(enc);
                    fin.Position = 48;
                    using var dec = aes.CreateDecryptor();
                    using var cs = new CryptoStream(fin, dec, CryptoStreamMode.Read);
                    using var fout = File.Create(plain);
                    cs.CopyTo(fout);
                }
                bool digestOk;
                long size;
                using (var fs = File.OpenRead(plain)) { digestOk = Convert.ToBase64String(SHA256.HashData(fs)) == Get("FileDigest"); size = fs.Length; }
                int count;
                using (var inner = ZipFile.OpenRead(plain)) count = inner.Entries.Count;
                bool sizeOk = size.ToString() == info.Element("UnencryptedContentSize")?.Value;
                return (macOk && digestOk && sizeOk, count, info.Element("SetupFile")?.Value);
            }
            finally { try { Directory.Delete(work, true); } catch { } }
        }
    }
}
