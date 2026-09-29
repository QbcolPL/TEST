using System.IO;
using System.Text;
using System.Windows;

namespace FieldTakHub.Builder;

public partial class App : Application
{
    private static string StartupLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FieldTakHub", "Builder-startup.log");

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            LogFatal(e.Exception);
            MessageBox.Show(
                "Field TAK Hub Builder nie może się uruchomić.\n\n" +
                "Szczegóły zapisano w:\n" + StartupLogPath + "\n\n" +
                DescribeException(e.Exception),
                "Field TAK Hub Builder 2.3.0",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) LogFatal(ex);
            else LogFatal(new Exception(Convert.ToString(e.ExceptionObject)));
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal(e.Exception);
            e.SetObserved();
        };
    }

    private static string DescribeException(Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null) current = current.InnerException;
        return current.Message;
    }

    private static void LogFatal(Exception ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(StartupLogPath)!;
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                StartupLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]\r\n{ex}\r\n\r\n",
                new UTF8Encoding(false));
        }
        catch
        {
            // Never allow diagnostic logging to become another startup failure.
        }
    }
}
