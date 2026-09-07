using System.Runtime.InteropServices;

namespace CpuCoreVoltageTray;

internal static class ShortcutManager
{
    private const int ShellChangeCreate = 0x00000002;
    private const int ShellChangeUpdateItem = 0x00002000;
    private const uint ShellNotifyPathUnicode = 0x0005;

    public static string CreateDesktopShortcut()
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定程序路径");
        string desktopDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonDesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopDirectory))
            throw new InvalidOperationException("无法确定桌面目录");

        string shortcutPath = Path.Combine(desktopDirectory, "CPU 核心电压.lnk");
        string iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        bool shortcutExisted = File.Exists(shortcutPath);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new PlatformNotSupportedException("Windows 快捷方式服务不可用");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("无法创建 Windows 快捷方式服务");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = executablePath;
        shortcut.WorkingDirectory = AppContext.BaseDirectory;
        shortcut.Description = "CPU 核心电压监测";
        shortcut.IconLocation = File.Exists(iconPath) ? $"{iconPath},0" : $"{executablePath},0";
        shortcut.Save();

        if (!File.Exists(shortcutPath))
            throw new IOException("快捷方式保存失败");

        SHChangeNotify(
            shortcutExisted ? ShellChangeUpdateItem : ShellChangeCreate,
            ShellNotifyPathUnicode,
            shortcutPath,
            IntPtr.Zero);
        return shortcutPath;
    }

    [DllImport(
        "shell32.dll",
        EntryPoint = "SHChangeNotify",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern void SHChangeNotify(
        int eventId,
        uint flags,
        [MarshalAs(UnmanagedType.LPWStr)] string item1,
        IntPtr item2);
}
