using System.Runtime.InteropServices;

namespace CpuCoreVoltageTray;

internal static class TaskbarIdentity
{
    private const string LiveAppUserModelId = "Junlon.CpuCoreVoltageTray.LiveStatus";

    public static void Apply()
    {
        int result = SetCurrentProcessExplicitAppUserModelID(LiveAppUserModelId);
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }

    [DllImport(
        "shell32.dll",
        EntryPoint = "SetCurrentProcessExplicitAppUserModelID",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);
}
