using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using J365.IntunePSApp.Core;

namespace J365.IntunePSApp
{
    /// <summary>
    /// One window of the application per data folder: a second start brings the open window to the front instead of
    /// opening another one that would write the same settings and package history. A portable copy (its own data folder)
    /// and the installed copy can still run side by side.
    /// </summary>
    public static class SingleInstance
    {
        private static Mutex _mutex;
        private static EventWaitHandle _activate;

        private static string Key
        {
            get
            {
                string folder = (AppPaths.Data ?? "").TrimEnd('\\').ToLowerInvariant();
                return @"Local\J365.IntunePSApp." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..16];
            }
        }

        /// <summary>
        /// True when this process is the window of the application for its data folder; it then listens for later starts.
        /// <paramref name="waitForPrevious"/>: the restart after an update, while the previous version is still closing.
        /// </summary>
        public static bool Claim(bool waitForPrevious)
        {
            _mutex = new Mutex(false, Key + ".instance");
            bool owned;
            try { owned = _mutex.WaitOne(waitForPrevious ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
            // The previous window ended without letting go (it was stopped): this one takes over.
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) return false;
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, Key + ".activate");
            var listener = new Thread(() =>
            {
                while (true)
                {
                    _activate.WaitOne();
                    Application.Current?.Dispatcher.BeginInvoke(new Action(BringToFront));
                }
            }) { IsBackground = true, Name = "J365 single instance" };
            listener.Start();
            return true;
        }

        /// <summary>Asks the window already open to come to the front (this process is about to end).</summary>
        public static void ActivateOther()
        {
            try
            {
                // The process started by the user may give the focus away; the open window takes it.
                AllowSetForegroundWindow(-1);
                using var other = EventWaitHandle.OpenExisting(Key + ".activate");
                other.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void BringToFront()
        {
            var w = Application.Current?.MainWindow;
            if (w == null) return;
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Show();
            w.Activate();
            // Over the other windows for a moment, then back to normal.
            w.Topmost = true;
            w.Topmost = false;
            w.Focus();
        }

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
