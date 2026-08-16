namespace Win11Monitor.App.ViewModels;

public sealed record PchSensorOption(string? Identifier, string DisplayName)
{
    public static PchSensorOption Automatic { get; } = new(null, "自动识别 PCH / Chipset");
}
