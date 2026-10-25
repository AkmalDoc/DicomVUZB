using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DicomVUZB
{
    internal static class Program
    {
        /// <summary>
        /// Главная точка входа для приложения.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            LogUnhandledException("Application.ThreadException", e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            LogUnhandledException("AppDomain.CurrentDomain.UnhandledException", exception);
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogUnhandledException("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private static void LogUnhandledException(string source, Exception exception)
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DicomVUZB");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "unhandled-exceptions.log");
                var text = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}{2}{3}{2}{2}",
                    DateTime.Now,
                    source,
                    Environment.NewLine,
                    exception == null ? "<null>" : exception.ToString());
                File.AppendAllText(logPath, text);
            }
            catch
            {
                // no-op
            }
        }
    }
}
