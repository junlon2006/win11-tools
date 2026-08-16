namespace Win11Monitor.App.Models;

public sealed class AppSettings
{
    public bool ShowTaskbarWidget { get; set; } = true;

    public string? PreferredPchSensorIdentifier { get; set; }

    public int RefreshIntervalMilliseconds { get; set; } = 1000;
}
