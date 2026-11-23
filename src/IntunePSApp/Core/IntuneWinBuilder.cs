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
    /// Both say how far they are (0 to 1) while they work.
    /// </summary>
    public static class IntuneWinBuilder
    {
        public const string SetupFile = "Invoke-AppDeployToolkit.exe";

        /// <summary>Builds the package and returns the name of the tool that produced it.</summary>
        public static async Task<string> BuildAsync(string sourceFolder, string outputFile, bool forceNative, CancellationToken ct, Action<double> progress = null)
        {
            string exe = forceNative ? null : await ToolsService.EnsureIntuneWinAppUtilAsync(ct);
            // A short name: the output folder may already be deep (OneDrive, Documents, a long application name).
            string tmpOut = Path.Combine(Path.GetDirectoryName(outputFile), "_iwau_" + Guid.NewGuid().ToString("N")[..8]);
            if (exe != null && LongestPath(sourceFolder, tmpOut) is int longest && longest > MaxToolPath)
            {
                Log.Info(T("Log_IwauLongPath", longest, MaxToolPath));
                exe = null;
            }
            // What IntuneWinAppUtil reached before failing: the built-in packager carries on from there (the bar never goes back).
            double reached = 0;
            if (exe != null)
            {
                var tool = progress;
                progress = tool == null ? null : f => { reached = Math.Max(reached, f); tool(f); };
                // Its working copies go to a folder of its own (TEMP of the process): watched for the progress, and removed
                // afterwards even when the tool is stopped halfway.
                string work = Path.Combine(AppPaths.Temp, "_iwau_work_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmpOut);
                Directory.CreateDirectory(work);
                try
                {
                    Log.Info(T("Log_IwauBuilding"));
                    var (code, output) = await RunIntuneWinAppUtilAsync(exe, sourceFolder, tmpOut, work, ct, progress);
                    var produced = Directory.GetFiles(tmpOut, "*.intunewin").FirstOrDefault();
                    if (code == 0 && produced != null)
                    {
                        File.Move(produced, outputFile, true);
                        progress?.Invoke(1);
                        return "IntuneWinAppUtil.exe (Microsoft)";
                    }
                    Log.Warn(T("Log_IwauFailed", code, output.Trim()));
                }
                // The tool could not even start (blocked by AppLocker or App Control, removed by an antivirus...): the
                // built-in packager makes the same file, so the build goes on instead of failing.
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Warn(T("Log_IwauFailed", "-", ex.Message));
                }
                finally
                {
                    try { Directory.Delete(tmpOut, true); } catch { }
                    try { Directory.Delete(work, true); } catch { }
                }
                var outer = tool;
                double from = reached;
                progress = outer == null ? null : f => outer(from + (1 - from) * f);
            }
            Log.Info(T("Log_NativeBuilding"));
            progress?.Invoke(0);
            await Task.Run(() => BuildNative(sourceFolder, SetupFile, outputFile, ct, progress), ct);
            return T("Tool_Native");
        }

        /// <summary>
        /// The longest full path IntuneWinAppUtil.exe accepts. It is a .NET Framework program without long path support:
        /// a file at 260 characters or more makes it compress for minutes and then fail ("Could not find a part of the
        /// path"), as a package of 770 MB did in a OneDrive folder with a long name. Such a package goes straight to the
        /// built-in packager, which has no such limit.
        /// </summary>
        public const int MaxToolPath = 259;

        /// <summary>The longest full path the tool would read (the package files) or write (its .intunewin).</summary>
        internal static int LongestPath(string sourceFolder, string outFolder)
        {
            int longest = Path.Combine(Path.GetFullPath(outFolder), Path.GetFileNameWithoutExtension(SetupFile) + ".intunewin").Length;
            try
            {
                foreach (var f in Directory.EnumerateFiles(Path.GetFullPath(sourceFolder), "*", SearchOption.AllDirectories))
                    longest = Math.Max(longest, f.Length);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            return longest;
        }

        /// <summary>
        /// Runs IntuneWinAppUtil.exe. It prints no progress, so the progress is read from what it writes: the zip of the
        /// content and its encrypted copy in its TEMP folder, then the .intunewin file. For an installer (already
        /// compressed) each one is about as large as the content, so the three together are about three times its size.
        /// </summary>
        private static async Task<(int Code, string Output)> RunIntuneWinAppUtilAsync(string exe, string sourceFolder, string outFolder, string work,
            CancellationToken ct, Action<double> progress)
        {
            var psi = new ProcessStartInfo(exe, $"-c \"{sourceFolder.TrimEnd('\\')}\" -s \"{SetupFile}\" -o \"{outFolder}\" -q")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.Environment["TEMP"] = work;
            psi.Environment["TMP"] = work;
            var output = new StringBuilder();
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            long expected = Math.Max(1, BuildProgress.FolderSize(sourceFolder)) * 3;
            long mostInWork = 0;
            var exited = p.WaitForExitAsync(CancellationToken.None);
            try
            {
                while (!exited.IsCompleted)
                {
                    await Task.WhenAny(exited, Task.Delay(300, ct));
                    ct.ThrowIfCancellationRequested();
                    mostInWork = Math.Max(mostInWork, BuildProgress.FolderSize(work));
                    long written = mostInWork + BuildProgress.FolderSize(outFolder);
                    progress?.Invoke(Math.Min(0.97, written / (double)expected));
                }
            }
            catch (OperationCanceledException)
            {
                // Kill only asks Windows to end the tree: waiting lets the cleanup find the files released.
                try { p.Kill(true); p.WaitForExit(10000); } catch { }
                throw;
            }
            await exited;
            // Lets the last lines of the output arrive.
            p.WaitForExit();
            lock (output) return (p.ExitCode, output.ToString());
        }

        /// <summary>
        /// Installers and archives are compressed already: compressing them again took about 100 s of a 770 MB installer to
        /// save 0.6%. Such a file over 8 MB is stored as it is (a zip entry any extractor reads); everything else, the
        /// scripts and the toolkit, is compressed.
        /// </summary>
        internal static CompressionLevel LevelFor(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            bool packed = ext is ".exe" or ".msi" or ".msp" or ".msu" or ".cab" or ".zip" or ".7z" or ".rar" or ".gz" or ".msix" or ".msixbundle"
                or ".appx" or ".appxbundle" or ".nupkg" or ".iso" or ".wim" or ".esd" or ".jar" or ".mp4" or ".jpg" or ".png";
            try { return packed && new FileInfo(file).Length > 8L * 1024 * 1024 ? CompressionLevel.NoCompression : CompressionLevel.Optimal; }
            catch { return CompressionLevel.Optimal; }
        }

        public static void BuildNative(string sourceFolder, string setupFile, string outputFile, CancellationToken ct = default, Action<double> progress = null)
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
                var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories);
                long content = Math.Max(1, files.Sum(f => new FileInfo(f).Length));

                // Three passes over the content: compression (the slow one), encryption, and the outer file.
                const double zipShare = 0.7, encShare = 0.15;
                long zipped = 0;
                using (var fs = new FileStream(plainZip, FileMode.CreateNew))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    // Entry names use "/" like Microsoft's tool (the zip format's separator).
                    foreach (var f in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = zip.CreateEntry(f.Substring(src.Length + 1).Replace('\\', '/'), LevelFor(f));
                        entry.LastWriteTime = ZipDate(File.GetLastWriteTime(f));
                        using var es = entry.Open();
                        using var input = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
                        BuildProgress.CopyStream(input, es, n => { zipped += n; progress?.Invoke(zipShare * zipped / content); }, ct);
                    }
                    foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                        if (!Directory.EnumerateFileSystemEntries(d).Any()) zip.CreateEntry(d.Substring(src.Length + 1).Replace('\\', '/') + "/");
                }
                ct.ThrowIfCancellationRequested();

                // One pass over the zip: its SHA256 digest, the encryption, and the HMAC of IV + ciphertext.
                byte[] encKey = RandomNumberGenerator.GetBytes(32), macKey = RandomNumberGenerator.GetBytes(32), iv = RandomNumberGenerator.GetBytes(16);
                byte[] digest, mac;
                long plainSize;
                using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                using (var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, macKey))
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256; aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using var outFs = new FileStream(encFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 20);
                    outFs.Write(new byte[32], 0, 32); // placeholder for the HMAC
                    outFs.Write(iv, 0, 16);
                    hmac.AppendData(iv);
                    using (var enc = aes.CreateEncryptor())
                    using (var fin = new FileStream(plainZip, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                    {
                        plainSize = fin.Length;
                        const int chunk = 1 << 20;
                        var buf = new byte[chunk];
                        var obuf = new byte[chunk + 32];
                        long read = 0;
                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            int n = 0, r;
                            while (n < chunk && (r = fin.Read(buf, n, chunk - n)) > 0) n += r;
                            sha.AppendData(buf, 0, n);
                            read += n;
                            if (fin.Position < fin.Length)
                            {
                                int w = enc.TransformBlock(buf, 0, n, obuf, 0);
                                outFs.Write(obuf, 0, w);
                                hmac.AppendData(obuf, 0, w);
                                progress?.Invoke(zipShare + encShare * read / Math.Max(1, plainSize));
                            }
                            else
                            {
                                var last = enc.TransformFinalBlock(buf, 0, n);
                                outFs.Write(last, 0, last.Length);
                                hmac.AppendData(last);
                                break;
                            }
                        }
                    }
                    digest = sha.GetHashAndReset();
                    mac = hmac.GetHashAndReset();
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
                long encSize = new FileInfo(encFile).Length, stored = 0;
                using (var ofs = new FileStream(outputFile, FileMode.CreateNew))
                using (var outer = new ZipArchive(ofs, ZipArchiveMode.Create))
                {
                    var contentEntry = outer.CreateEntry("IntuneWinPackage/Contents/IntunePackage.intunewin", CompressionLevel.NoCompression);
                    contentEntry.LastWriteTime = ZipDate(DateTime.Now);
                    using (var es = contentEntry.Open())
                    using (var input = new FileStream(encFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                        BuildProgress.CopyStream(input, es, n => { stored += n; progress?.Invoke(zipShare + encShare + (1 - zipShare - encShare) * stored / Math.Max(1, encSize)); }, ct);
                    var e = outer.CreateEntry("IntuneWinPackage/Metadata/Detection.xml", CompressionLevel.NoCompression);
                    using var xs = e.Open();
                    var xb = new UTF8Encoding(false).GetBytes(xml);
                    xs.Write(xb, 0, xb.Length);
                }
                progress?.Invoke(1);
            }
            finally { try { Directory.Delete(work, true); } catch { } }
        }

        /// <summary>The zip format keeps dates from 1980 to 2107 (CreateEntryFromFile does the same).</summary>
        private static DateTimeOffset ZipDate(DateTime d)
        {
            var min = new DateTime(1980, 1, 1, 0, 0, 0);
            var max = new DateTime(2107, 12, 31, 23, 59, 58);
            return d < min ? min : d > max ? max : d;
        }

        /// <summary>
        /// Validates an .intunewin file in one pass over its content: checks the HMAC while it decrypts, checks the SHA256
        /// digest of what comes out, and counts the files of the package.
        /// </summary>
        public static (bool Valid, int FileCount, string SetupFile) Validate(string path, Action<double> progress = null, CancellationToken ct = default)
        {
            string work = Path.Combine(AppPaths.Temp, "_val_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string plain = Path.Combine(work, "plain.zip");
                XDocument x;
                bool macOk;
                byte[] digest;
                long size;
                using (var z = ZipFile.OpenRead(path))
                {
                    var xe = z.GetEntry("IntuneWinPackage/Metadata/Detection.xml") ?? throw new InvalidDataException(T("Err_IntuneWinNoMetadata"));
                    var ce = z.GetEntry("IntuneWinPackage/Contents/IntunePackage.intunewin") ?? throw new InvalidDataException(T("Err_IntuneWinNoContent"));
                    using (var s = xe.Open()) x = XDocument.Load(s);
                    var ei = x.Root?.Element("EncryptionInfo") ?? throw new InvalidDataException(T("Err_IntuneWinNoMetadata"));
                    string Get(string n) => ei.Element(n)?.Value ?? "";

                    long total = Math.Max(1, ce.Length), read = 0;
                    using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, Convert.FromBase64String(Get("MacKey")));
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    using var raw = ce.Open();
                    var header = new byte[32];
                    raw.ReadExactly(header, 0, 32);
                    var fileIv = new byte[16];
                    raw.ReadExactly(fileIv, 0, 16);
                    hmac.AppendData(fileIv);
                    read = 48;
                    using (var aes = Aes.Create())
                    {
                        aes.Key = Convert.FromBase64String(Get("EncryptionKey"));
                        aes.IV = Convert.FromBase64String(Get("InitializationVector"));
                        using var dec = aes.CreateDecryptor();
                        // What is read is added to the HMAC on its way to the decryptor.
                        using var watched = new ReadObserver(raw, (b, o, n) =>
                        {
                            hmac.AppendData(b, o, n);
                            read += n;
                            progress?.Invoke((double)read / total);
                        });
                        using var cs = new CryptoStream(watched, dec, CryptoStreamMode.Read);
                        using var fout = new FileStream(plain, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
                        var buf = new byte[1 << 20];
                        int n;
                        while ((n = cs.Read(buf, 0, buf.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            sha.AppendData(buf, 0, n);
                            fout.Write(buf, 0, n);
                        }
                        size = fout.Length;
                    }
                    string calc = Convert.ToBase64String(hmac.GetHashAndReset());
                    macOk = calc == Get("Mac") && Convert.ToBase64String(header) == Get("Mac");
                    digest = sha.GetHashAndReset();
                }
                var info = x.Root;
                string fileDigest = info.Element("EncryptionInfo")?.Element("FileDigest")?.Value;
                bool digestOk = Convert.ToBase64String(digest) == fileDigest;
                int count;
                using (var inner = ZipFile.OpenRead(plain)) count = inner.Entries.Count;
                bool sizeOk = size.ToString() == info.Element("UnencryptedContentSize")?.Value;
                progress?.Invoke(1);
                return (macOk && digestOk && sizeOk, count, info.Element("SetupFile")?.Value);
            }
            finally { try { Directory.Delete(work, true); } catch { } }
        }

        /// <summary>A read-only stream that passes on what it reads to a callback (the HMAC, the progress).</summary>
        private sealed class ReadObserver : Stream
        {
            private readonly Stream _inner;
            private readonly Action<byte[], int, int> _seen;
            public ReadObserver(Stream inner, Action<byte[], int, int> seen) { _inner = inner; _seen = seen; }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = _inner.Read(buffer, offset, count);
                if (n > 0) _seen(buffer, offset, n);
                return n;
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
