using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace CCSuitePro
{
    public partial class App : Application
    {
        // Prevent a second modal dialog if WPF raises another exception while reporting the first.
        private int _fatalUiErrorInProgress;
        // Basic privacy: do not write the Windows account name to new crash reports.
        // This does not delete historical logs or mask every possible sensitive value.
        private static string RedactCrashDetails(Exception exception)
        {
            var details = exception.ToString();
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile))
            {
                details = details.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
                details = details.Replace(profile.Replace('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
            }
            return Regex.Replace(details, @"(?i)\b[A-Z]:\\Users\\[^\\\s""<>|]+", "%USERPROFILE%");
        }

        private static void WriteCrashLog(string context, Exception exception)
        {
            try
            {
                var logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CCSuitePro", "Logs");
                Directory.CreateDirectory(logFolder);
                string log = Path.Combine(logFolder, "crashes.log");
                if (File.Exists(log) && new FileInfo(log).Length > 2 * 1024 * 1024)
                {
                    string rotated = Path.Combine(logFolder, "crashes.previous.log");
                    File.Move(log, rotated, overwrite: true);
                }
                File.AppendAllText(log,
                    $"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{RedactCrashDetails(exception)}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { /* Logging must never cause a second crash. */ }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SplashWindow? splash = null;
            try
            {
                Services.SettingsService.Load();
                Services.LanguageService.Instance.CurrentLanguage = Services.SettingsService.Current.Language;
                Services.ThemeManager.Initialize();
                splash = new SplashWindow();
                splash.Show();
                await splash.RunStartupSequenceAsync();

                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
                splash.Close();
                splash = null;
                // A new Windows user profile sees the tutorial only on its first launch.
                _ = mainWindow.Dispatcher.BeginInvoke(new Action(() => FirstRunTutorial.ShowIfNew(mainWindow)), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                WriteCrashLog("Startup failure", ex);
                try { splash?.Close(); } catch { }
                MessageBox.Show(Services.LanguageService.Instance["ui_startup_error"],
                    "CC Suite Pro", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            WriteCrashLog("Background task error", e.Exception);
            e.SetObserved();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Mark the exception handled BEFORE opening any modal UI, otherwise nested
            // WPF dispatcher exceptions can generate a cascade of MessageBox windows.
            e.Handled = true;
            if (System.Threading.Interlocked.Exchange(ref _fatalUiErrorInProgress, 1) != 0)
                return;

            try
            {
                WriteCrashLog("UI error (fatal, first occurrence)", e.Exception);
                MessageBox.Show(Services.LanguageService.Instance["ui_fatal_error"],
                    "CC Suite Pro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Stop rather than continuing after potentially corrupted UI state.
                Shutdown(-1);
            }
        }
    }
}
