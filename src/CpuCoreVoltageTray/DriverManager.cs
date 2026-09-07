using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace CpuCoreVoltageTray;

internal readonly record struct DriverStatus(bool IsInstalled, bool NeedsUpdate, Version? Version)
{
    public bool IsReady => IsInstalled && !NeedsUpdate;
}

internal readonly record struct DriverInstallResult(
    bool Succeeded,
    bool RestartRequired,
    string Error);

internal static class DriverManager
{
    private const int ErrorSuccessRebootRequired = 3010;
    private const string InstallerResourceName = "CpuCoreVoltageTray.Resources.PawnIO_setup.exe";
    private const string InstallerSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private static readonly Version MinimumVersion = new(2, 2);

    public static DriverStatus GetStatus()
    {
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? key = baseKey.OpenSubKey(UninstallKeyPath);
                if (Version.TryParse(key?.GetValue("DisplayVersion") as string, out Version? version))
                    return new DriverStatus(true, version < MinimumVersion, version);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return new DriverStatus(false, false, null);
            }
        }

        return new DriverStatus(false, false, null);
    }

    public static DriverInstallResult Install()
    {
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "CpuCoreVoltageTray",
            Guid.NewGuid().ToString("N"));
        string installerPath = Path.Combine(temporaryDirectory, "PawnIO_setup.exe");

        try
        {
            byte[] installer = ReadInstallerResource();
            string actualHash = Convert.ToHexString(SHA256.HashData(installer));
            if (!string.Equals(actualHash, InstallerSha256, StringComparison.Ordinal))
                throw new InvalidDataException("内置驱动安装程序校验失败");

            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllBytes(installerPath, installer);

            ProcessStartInfo startInfo = new()
            {
                FileName = installerPath,
                Arguments = "-install",
                WorkingDirectory = temporaryDirectory,
                UseShellExecute = true
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
                throw new InvalidOperationException("无法启动 PawnIO 安装程序");

            process.WaitForExit();
            bool restartRequired = process.ExitCode == ErrorSuccessRebootRequired;
            if (process.ExitCode != 0 && !restartRequired)
                throw new InvalidOperationException($"PawnIO 安装程序返回错误代码 {process.ExitCode}");

            DriverStatus status = GetStatus();
            if (!status.IsReady && !restartRequired)
                throw new InvalidOperationException("安装程序已结束，但未检测到可用驱动");

            return new DriverInstallResult(true, restartRequired, string.Empty);
        }
        catch (Exception exception)
        {
            return new DriverInstallResult(false, false, exception.Message);
        }
        finally
        {
            TryDelete(installerPath);
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    private static byte[] ReadInstallerResource()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(InstallerResourceName)
            ?? throw new InvalidOperationException("程序中缺少 PawnIO 驱动安装资源");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void TryDelete(string path)
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: false);
        }
        catch
        {
        }
    }
}
