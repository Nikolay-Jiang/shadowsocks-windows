using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommandLine;
using Microsoft.Win32;
using NLog;
using Shadowsocks.Controller;
using Shadowsocks.Controller.Hotkeys;
using Shadowsocks.Util;
using Shadowsocks.View;

namespace Shadowsocks
{
    internal static class Program
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static ShadowsocksController MainController { get; private set; }
        public static MenuViewController MenuController { get; private set; }
        public static CommandLineOption Options { get; private set; }
        public static string[] Args { get; private set; }

        // https://github.com/dotnet/runtime/issues/13051#issuecomment-510267727
        public static readonly string ExecutablePath = Process.GetCurrentProcess().MainModule?.FileName;
        public static readonly string WorkingDirectory = Path.GetDirectoryName(ExecutablePath);

        private static readonly Mutex mutex = new Mutex(true, $"Shadowsocks_{ExecutablePath.GetHashCode()}");

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            #region Single Instance and IPC
            bool hasAnotherInstance = !mutex.WaitOne(TimeSpan.Zero, true);

            // store args for further use
            Args = args;
            Parser.Default.ParseArguments<CommandLineOption>(args)
                .WithParsed(opt => Options = opt)
                .WithNotParsed(e => e.Output());

            if (hasAnotherInstance)
            {
                if (!string.IsNullOrWhiteSpace(Options.OpenUrl))
                {
                    IPCService.RequestOpenUrl(Options.OpenUrl);
                }
                else
                {
                    MessageBox.Show(I18N.GetString("Find Shadowsocks icon in your notify tray.")
                                    + Environment.NewLine
                                    + I18N.GetString("If you want to start multiple Shadowsocks, make a copy in another directory."),
                        I18N.GetString("Shadowsocks is already running."));
                }
                return;
            }
            #endregion

            #region Enviroment Setup
            Directory.SetCurrentDirectory(WorkingDirectory);
            // todo: initialize the NLog configuartion
            Model.NLogConfig.TouchAndApplyNLogConfig();

            #endregion

            #region Compactibility Check
            // Check OS since we are using dual-mode socket
            if (!Utils.IsWinVistaOrHigher())
            {
                MessageBox.Show(I18N.GetString("Unsupported operating system, use Windows Vista at least."),
                "Shadowsocks Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                MessageBox.Show("Shadowsocks requires Windows 10 version 2004 or later.",
                    "Shadowsocks Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            #endregion

            #region Event Handlers Setup
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            // handle UI exceptions
            Application.ThreadException += Application_ThreadException;
            // handle non-UI exceptions
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            Application.ApplicationExit += Application_ApplicationExit;
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AutoStartup.RegisterForRestart(true);
            #endregion

#if DEBUG
            // truncate privoxy log file while debugging
            string privoxyLogFilename = Utils.GetTempPath("privoxy.log");
            if (File.Exists(privoxyLogFilename))
                using (new FileStream(privoxyLogFilename, FileMode.Truncate)) { }
#endif
            MainController = new ShadowsocksController();
            MenuController = new MenuViewController(MainController);

            HotKeys.Init(MainController);
            MainController.Start();

            // Update online config 
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(10 * 1000);
                    await MainController.UpdateAllOnlineConfig();
                }
                catch (Exception e)
                {
                    logger.LogUsefulException(e);
                }
            });

#region IPC Handler and Arguement Process
            IPCService ipcService = new IPCService();
            Task.Run(() => ipcService.RunServer());
            ipcService.OpenUrlRequested += (_1, e) => MainController.AskAddServerBySSURL(e.Url);

            if (!string.IsNullOrWhiteSpace(Options.OpenUrl))
            {
                MainController.AskAddServerBySSURL(Options.OpenUrl);
            }
#endregion
            
            Application.Run();

        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            logger.LogUsefulException(e.Exception);
            e.SetObserved();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception
                                  ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown non-UI exception");
            ErrorReporter.Report(exception, "Non-UI thread", fatal: true);
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            // Keep the tray client alive; the failed operation is reported and
            // the rest of the application continues to run.
            ErrorReporter.Report(e.Exception, "UI thread");
        }

        private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Resume:
                    logger.Info("os wake up");
                    if (MainController != null)
                    {
                        Task.Factory.StartNew(() =>
                        {
                            Thread.Sleep(10 * 1000);
                            try
                            {
                                MainController.Start(true);
                                logger.Info("controller started");
                            }
                            catch (Exception ex)
                            {
                                logger.LogUsefulException(ex);
                            }
                        });
                    }
                    break;
                case PowerModes.Suspend:
                    if (MainController != null)
                    {
                        MainController.Stop();
                        logger.Info("controller stopped");
                    }
                    logger.Info("os suspend");
                    break;
            }
        }

        private static void Application_ApplicationExit(object sender, EventArgs e)
        {
            // detach static event handlers
            Application.ApplicationExit -= Application_ApplicationExit;
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            Application.ThreadException -= Application_ThreadException;
            TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
            HotKeys.Destroy();
            if (MainController != null)
            {
                MainController.Stop();
                MainController = null;
            }
        }
    }
}
