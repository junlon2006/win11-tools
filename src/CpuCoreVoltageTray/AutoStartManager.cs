using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CpuCoreVoltageTray;

internal static class AutoStartManager
{
    private const int TaskActionExecute = 0;
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskRunLevelHighest = 1;
    private const int TaskTriggerLogon = 9;
    private const int TaskInstancesIgnoreNew = 2;
    private const int FileNotFoundHResult = unchecked((int)0x80070002);
    private const string AutoStartArgument = "--autostart";

    public static bool IsEnabled()
    {
        string userSid = GetCurrentUserSid();
        dynamic root = OpenRootFolder();
        try
        {
            dynamic task = root.GetTask(GetTaskName());
            if (!(bool)task.Enabled)
                return false;

            dynamic definition = task.Definition;
            dynamic principal = definition.Principal;
            if ((int)principal.LogonType != TaskLogonInteractiveToken ||
                (int)principal.RunLevel != TaskRunLevelHighest ||
                !UserIdentityMatchesSid(principal.UserId as string, userSid))
            {
                return false;
            }

            dynamic actions = definition.Actions;
            if ((int)actions.Count < 1)
                return false;

            dynamic action = actions.Item(1);
            string? actionPath = action.Path as string;
            string? actionArguments = action.Arguments as string;
            if ((int)action.Type != TaskActionExecute ||
                string.IsNullOrWhiteSpace(actionPath) ||
                !PathsEqual(actionPath, GetExecutablePath()) ||
                !string.Equals(actionArguments?.Trim(), AutoStartArgument, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            dynamic triggers = definition.Triggers;
            for (int index = 1; index <= (int)triggers.Count; index++)
            {
                dynamic trigger = triggers.Item(index);
                if ((int)trigger.Type == TaskTriggerLogon &&
                    (bool)trigger.Enabled &&
                    UserIdentityMatchesSid(trigger.UserId as string, userSid))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (IsTaskNotFound(exception))
        {
            return false;
        }
    }

    public static void Enable()
    {
        string userSid = GetCurrentUserSid();
        string executablePath = GetExecutablePath();
        dynamic service = OpenService();
        dynamic root = service.GetFolder(@"\");
        dynamic definition = service.NewTask(0);

        definition.RegistrationInfo.Author = WindowsIdentity.GetCurrent().Name;
        definition.RegistrationInfo.Description = "登录 Windows 后显示 CPU 最高核心 VID";

        definition.Principal.UserId = userSid;
        definition.Principal.LogonType = TaskLogonInteractiveToken;
        definition.Principal.RunLevel = TaskRunLevelHighest;

        definition.Settings.Enabled = true;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.AllowDemandStart = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.MultipleInstances = TaskInstancesIgnoreNew;

        dynamic trigger = definition.Triggers.Create(TaskTriggerLogon);
        trigger.Id = "CurrentUserLogon";
        trigger.UserId = userSid;
        trigger.Delay = "PT10S";
        trigger.Enabled = true;

        dynamic action = definition.Actions.Create(TaskActionExecute);
        action.Id = "StartTrayMonitor";
        action.Path = executablePath;
        action.Arguments = AutoStartArgument;
        action.WorkingDirectory = AppContext.BaseDirectory;

        dynamic registeredTask = root.RegisterTaskDefinition(
            GetTaskName(),
            definition,
            TaskCreateOrUpdate,
            userSid,
            null,
            TaskLogonInteractiveToken,
            null);

        if (!(bool)registeredTask.Enabled)
            throw new InvalidOperationException("计划任务已创建，但任务未启用");
    }

    public static void Disable()
    {
        dynamic root = OpenRootFolder();
        try
        {
            root.DeleteTask(GetTaskName(), 0);
        }
        catch (Exception exception) when (IsTaskNotFound(exception))
        {
        }
    }

    private static bool IsTaskNotFound(Exception exception)
    {
        return exception is FileNotFoundException ||
               exception is COMException { HResult: FileNotFoundHResult };
    }

    private static dynamic OpenRootFolder()
    {
        dynamic service = OpenService();
        return service.GetFolder(@"\");
    }

    private static dynamic OpenService()
    {
        Type serviceType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new PlatformNotSupportedException("Windows 任务计划程序不可用");
        dynamic service = Activator.CreateInstance(serviceType)
            ?? throw new InvalidOperationException("无法连接 Windows 任务计划程序");
        service.Connect();
        return service;
    }

    private static string GetTaskName() => $"CpuCoreVoltageTray-{GetCurrentUserSid()}";

    private static string GetCurrentUserSid()
    {
        return WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("无法读取当前用户 SID");
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定程序路径");
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool UserIdentityMatchesSid(string? userId, string expectedSid)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        string candidate = userId.Trim();
        if (string.Equals(candidate, expectedSid, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            SecurityIdentifier expected = new(expectedSid);
            SecurityIdentifier actual = candidate.StartsWith("S-", StringComparison.OrdinalIgnoreCase)
                ? new SecurityIdentifier(candidate)
                : (SecurityIdentifier)new NTAccount(candidate).Translate(typeof(SecurityIdentifier));
            return actual.Equals(expected);
        }
        catch (IdentityNotMappedException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
