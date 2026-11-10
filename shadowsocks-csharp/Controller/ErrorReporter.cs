using NLog;
using System;
using System.Windows.Forms;

namespace Shadowsocks.Controller
{
    /// <summary>
    /// Central place for catching and reporting exceptions. UI errors should
    /// not terminate the tray client; they are logged and shown to the user,
    /// with a short throttle to avoid an error-dialog storm.
    /// </summary>
    public static class ErrorReporter
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static readonly object sync = new object();
        private static DateTime lastShown = DateTime.MinValue;

        public static void SafeExecute(Action action, string context)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Report(e, context);
            }
        }

        public static void Report(Exception exception, string context, bool fatal = false)
        {
            try
            {
                logger.Error(exception, context);
            }
            catch
            {
            }

            bool show;
            lock (sync)
            {
                DateTime now = DateTime.UtcNow;
                show = fatal || (now - lastShown).TotalSeconds >= 5;
                if (show)
                {
                    lastShown = now;
                }
            }

            if (!show)
            {
                return;
            }

            string message = $"Operation: {context}{Environment.NewLine}{Environment.NewLine}{exception}";
            try
            {
                MessageBox.Show(message,
                    fatal ? "Shadowsocks Fatal Error" : "Shadowsocks Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // Never let error reporting itself crash the process.
            }
        }
    }
}
