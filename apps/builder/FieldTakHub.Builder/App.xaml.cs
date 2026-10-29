#nullable disable
using System;
using System.Windows;
using System.Windows.Threading;

namespace FieldTakHub.Builder
{
    /// <summary>
    /// Punkt startowy aplikacji Field TAK Hub v4.0.
    /// Jawnie inicjalizuje i wyświetla główne okno dowodzenia MainWindow podczas startu procesu.
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
                mainWindow.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Błąd podczas inicjalizacji okna głównego Field TAK Hub v4.0:\n\n" + ex,
                    "Field TAK Hub — Błąd Startu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                "Wystąpił nieoczekiwany wyjątek w interfejsie użytkownika:\n\n" + e.Exception.Message,
                "Field TAK Hub v4.0",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    "Krytyczny wyjątek środowiska uruchomieniowego:\n\n" + ex.Message,
                    "Field TAK Hub v4.0",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
