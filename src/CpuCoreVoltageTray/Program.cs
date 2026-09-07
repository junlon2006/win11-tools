using System.Text;

namespace CpuCoreVoltageTray;

internal static class Program
{
    private const string MutexName = @"Local\CpuCoreVoltageTray-5D5A5ED5-9ECB-4C38-9D59-171355D3D54A";
    private const string InstallAutoStartArgument = "--install-autostart";
    private const string RemoveAutoStartArgument = "--remove-autostart";
    private const string InstallAutoStartErrorLogFileName = "autostart-install-error.log";
    private const string RemoveAutoStartErrorLogFileName = "autostart-remove-error.log";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(argument => string.Equals(
                argument,
                InstallAutoStartArgument,
                StringComparison.OrdinalIgnoreCase)))
        {
            return RunSilentAutoStartCommand(AutoStartManager.Enable, InstallAutoStartErrorLogFileName);
        }

        if (args.Any(argument => string.Equals(
                argument,
                RemoveAutoStartArgument,
                StringComparison.OrdinalIgnoreCase)))
        {
            return RunSilentAutoStartCommand(AutoStartManager.Disable, RemoveAutoStartErrorLogFileName);
        }

        using Mutex mutex = new(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
            return 0;

        try
        {
            TaskbarIdentity.Apply();
            ApplicationConfiguration.Initialize();
            bool startedAutomatically = args.Any(
                argument => string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase));

            using TrayApplicationContext context = new(startedAutomatically);
            Application.Run(context);
            return 0;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"程序无法启动。\n\n{exception.Message}",
                "CPU 核心电压",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static int RunSilentAutoStartCommand(Action command, string errorLogFileName)
    {
        string errorLogPath = Path.Combine(AppContext.BaseDirectory, errorLogFileName);
        TryDeleteErrorLog(errorLogPath);

        try
        {
            command();
            return 0;
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(errorLogPath, exception);
            return 1;
        }
    }

    private static void TryDeleteErrorLog(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryWriteErrorLog(string path, Exception exception)
    {
        try
        {
            File.WriteAllText(
                path,
                exception.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch
        {
        }
    }
}
