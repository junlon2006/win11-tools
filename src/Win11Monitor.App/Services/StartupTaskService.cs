using System.Diagnostics;

namespace Win11Monitor.App.Services;

public sealed class StartupTaskService
{
    private const string TaskName = "Z690 Monitor";

    public bool IsEnabled()
    {
        var result = Run("/Query", "/TN", TaskName);
        return result.ExitCode == 0;
    }

    public StartupTaskResult SetEnabled(bool enabled)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return new StartupTaskResult(false, "无法确定程序路径。");
        }

        ProcessResult result;
        if (enabled)
        {
            result = Run(
                "/Create",
                "/SC", "ONLOGON",
                "/RL", "HIGHEST",
                "/IT",
                "/TN", TaskName,
                "/TR", $"\"{executable}\" --startup",
                "/F");
        }
        else
        {
            result = Run("/Delete", "/TN", TaskName, "/F");
        }

        if (result.ExitCode == 0)
        {
            return new StartupTaskResult(true, enabled ? "已启用开机启动。" : "已关闭开机启动。");
        }

        var message = string.IsNullOrWhiteSpace(result.Error)
            ? "计划任务设置失败。"
            : result.Error.Trim();
        return new StartupTaskResult(false, message);
    }

    private static ProcessResult Run(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("schtasks.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessResult(-1, "无法启动任务计划程序。");
            }

            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, error);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ProcessResult(-1, ex.Message);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Error);
}

public sealed record StartupTaskResult(bool Success, string Message);
