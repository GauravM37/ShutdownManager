using System.Threading;
using ShutdownManager.Services;
using ShutdownManager.UI;

namespace ShutdownManager;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\ScheduledShutdownManager.SingleInstance",
            createdNew: out var createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "Shutdown Manager is already running. Check the Windows notification area (system tray).",
                "Shutdown Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var logger = new Logger();

        Application.ThreadException += (_, e) =>
        {
            logger.Error("Unhandled Windows Forms exception.", e.Exception);
            MessageBox.Show(
                "An unexpected application error occurred. Details were written to the application log.",
                "Shutdown Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
                logger.Error("Unhandled application-domain exception.", exception);
            else
                logger.Error($"Unhandled application-domain exception: {e.ExceptionObject}");
        };

        try
        {
            var store = new JsonConfigStore(logger);
            var repository = new AppRepository(store, logger);
            repository.Load();

            var powerActions = new PowerActionService(logger);
            var startupManager = new StartupManager(logger);
            var startMinimized = args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

            Application.Run(new MainForm(repository, powerActions, startupManager, logger, startMinimized));
        }
        catch (Exception exception)
        {
            logger.Error("Application startup failed.", exception);
            MessageBox.Show(
                $"Shutdown Manager could not start.\n\n{exception.Message}\n\nSee the application log for details.",
                "Shutdown Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
