using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace J365.IntunePSApp.Core
{
    /// <summary>Where a package build is: one percentage for the whole build, the step, and the time left.</summary>
    public sealed class BuildProgressInfo
    {
        public double Percent { get; init; }
        public string Step { get; init; } = "";
        public TimeSpan Elapsed { get; init; }
        /// <summary>Estimated time left; null while it is too early to tell.</summary>
        public TimeSpan? Remaining { get; init; }
    }

    /// <summary>
    /// The progress of a build as one bar. Every step weighs what it costs (estimated seconds, from the sizes of the files)
    /// and says how far it is; a step that is skipped counts as done. Reports are throttled and the percentage never goes
    /// back. Thread-safe: steps report from worker threads.
    /// </summary>
    public sealed class BuildProgress
    {
        private readonly IProgress<BuildProgressInfo> _sink;
        private readonly List<(string Phase, double Weight)> _phases;
        private readonly double _total;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _gate = new();
        private double _before, _weight, _shown;
        private string _step = "";
        private long _lastReport = -1000;

        public BuildProgress(IProgress<BuildProgressInfo> sink, IEnumerable<(string Phase, double Weight)> phases)
        {
            _sink = sink;
            _phases = phases.Select(p => (p.Phase, Math.Max(0, p.Weight))).ToList();
            _total = _phases.Sum(p => p.Weight);
        }

        /// <summary>The last percentage reported (0 to 100).</summary>
        public double Percent { get { lock (_gate) return _shown; } }

        /// <summary>Starts a step: the steps listed before it count as done.</summary>
        public void Begin(string phase, string step)
        {
            lock (_gate)
            {
                int i = _phases.FindIndex(p => p.Phase == phase);
                _before = i < 0 ? _before + _weight : _phases.Take(i).Sum(p => p.Weight);
                _weight = i < 0 ? 0 : _phases[i].Weight;
                if (!string.IsNullOrEmpty(step)) _step = step.TrimEnd('.', '…', ' ');
            }
            Publish(0, true);
        }

        /// <summary>How far the current step is, from 0 to 1.</summary>
        public void Report(double fraction) => Publish(double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1), false);

        /// <summary>A counter of bytes for the current step: each call adds the bytes just processed.</summary>
        public Action<long> Bytes(long total)
        {
            long done = 0;
            return n =>
            {
                long now = Interlocked.Add(ref done, n);
                Report(total <= 0 ? 1 : (double)now / total);
            };
        }

        /// <summary>The build is over: 100%.</summary>
        public void Complete(string step = null)
        {
            lock (_gate)
            {
                _before = _total;
                _weight = 0;
                if (!string.IsNullOrEmpty(step)) _step = step.TrimEnd('.', '…', ' ');
            }
            Publish(1, true);
        }

        private void Publish(double fraction, bool force)
        {
            double pct;
            string step;
            long now = _clock.ElapsedMilliseconds;
            lock (_gate)
            {
                pct = _total <= 0 ? 100 : Math.Clamp((_before + _weight * fraction) / _total * 100, 0, 100);
                if (pct < _shown) pct = _shown;
                // About ten reports a second at most: the interface only needs to look alive.
                if (!force && now - _lastReport < 100) return;
                _shown = pct;
                _lastReport = now;
                step = _step;
            }
            var elapsed = _clock.Elapsed;
            TimeSpan? remaining = null;
            if (pct >= 3 && pct < 100 && elapsed.TotalSeconds >= 2)
                remaining = TimeSpan.FromSeconds(elapsed.TotalSeconds * (100 - pct) / pct);
            _sink?.Report(new BuildProgressInfo { Percent = pct, Step = step, Elapsed = elapsed, Remaining = remaining });
        }

        // ------------------------------------------------------------------ estimates and helpers

        /// <summary>Seconds that <paramref name="bytes"/> take at <paramref name="megabytesPerSecond"/>.</summary>
        public static double Seconds(long bytes, double megabytesPerSecond) => Math.Max(0, bytes) / (megabytesPerSecond * 1024 * 1024);

        /// <summary>Size of every file under a folder (0 when it does not exist), read fresh (a file being written grows).</summary>
        public static long FolderSize(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
            long total = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    // A FileInfo made from the path reads the size of the file itself, not the cached directory entry.
                    try { total += new FileInfo(f).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return total;
        }

        /// <summary>Copies a file in blocks, saying how many bytes went through; keeps its date like File.Copy.</summary>
        public static void CopyFile(string source, string destination, Action<long> bytes, CancellationToken ct)
        {
            const int block = 1 << 20;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, block, FileOptions.SequentialScan))
            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, block))
            {
                if (input.Length > 0) output.SetLength(input.Length);
                var buffer = new byte[block];
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, n);
                    bytes?.Invoke(n);
                }
                output.SetLength(output.Position);
            }
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        }

        /// <summary>Copies a stream to another in blocks, saying how many bytes went through.</summary>
        public static void CopyStream(Stream input, Stream output, Action<long> bytes, CancellationToken ct)
        {
            var buffer = new byte[1 << 20];
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                output.Write(buffer, 0, n);
                bytes?.Invoke(n);
            }
        }

        /// <summary>"45 s", "3 min 05 s", "1 h 02 min".</summary>
        public static string Duration(TimeSpan t)
        {
            if (t.TotalSeconds < 60) return $"{Math.Max(1, (int)Math.Round(t.TotalSeconds))} s";
            if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} min {t.Seconds:00} s";
            return $"{(int)t.TotalHours} h {t.Minutes:00} min";
        }
    }
}
