using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Win11Monitor.App.Models;
using Win11Monitor.App.Services;
using Win11Monitor.Core;

namespace Win11Monitor.App.ViewModels;

public sealed class MonitorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly HardwareMonitorService _monitor;
    private readonly SettingsService _settingsService;
    private readonly StartupTaskService _startupTaskService;
    private readonly SensorSnapshotAggregator _aggregator = new();
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly System.Windows.Threading.Dispatcher _dispatcher;

    private IReadOnlyList<RawSensorReading> _lastReadings = [];
    private Task? _pollTask;
    private MonitorSnapshot? _snapshot;
    private PchSensorOption _selectedPchSensor = PchSensorOption.Automatic;
    private bool _showTaskbarWidget;
    private bool _startWithWindows;
    private string _statusText = "正在读取硬件传感器...";
    private bool _isStarted;
    private bool _isDisposed;

    public MonitorViewModel(
        HardwareMonitorService monitor,
        SettingsService settingsService,
        StartupTaskService startupTaskService,
        AppSettings settings)
    {
        _monitor = monitor;
        _settingsService = settingsService;
        _startupTaskService = startupTaskService;
        _settings = settings;
        _dispatcher = System.Windows.Application.Current.Dispatcher;
        _showTaskbarWidget = settings.ShowTaskbarWidget;
        _startWithWindows = startupTaskService.IsEnabled();

        PchSensors.Add(PchSensorOption.Automatic);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<MonitorSnapshot>? SnapshotUpdated;

    public ObservableCollection<PchSensorOption> PchSensors { get; } = [];

    public bool IsExiting { get; set; }

    public string CpuName => _lastReadings
        .FirstOrDefault(reading => reading.HardwareKind == HardwareKind.Cpu)?.HardwareName
        ?? "Intel CPU";

    public string CpuTemperatureAverageText => Format(_snapshot?.CpuTemperature?.Average, "F1", " °C");

    public string CpuTemperatureMaximumText => Format(_snapshot?.CpuTemperature?.Maximum, "F1", " °C");

    public string CpuTemperatureCompactText => FormatPair(
        _snapshot?.CpuTemperature?.Maximum,
        _snapshot?.CpuTemperature?.Average,
        "F0",
        "°C");

    public string CpuVoltageAverageText => Format(_snapshot?.CpuVoltage?.Average, "F3", " V");

    public string CpuVoltageMaximumText => Format(_snapshot?.CpuVoltage?.Maximum, "F3", " V");

    public string CpuVoltageCompactText => FormatPair(
        _snapshot?.CpuVoltage?.Maximum,
        _snapshot?.CpuVoltage?.Average,
        "F3",
        "V");

    public string PchTemperatureText => Format(_snapshot?.PchTemperature?.Value, "F1", " °C");

    public string PchTemperatureCompactText => Format(_snapshot?.PchTemperature?.Value, "F0", "°C");

    public string VoltageSourceText => _snapshot?.CpuVoltageSource switch
    {
        CpuVoltageSource.Vid => "CPU VID（请求电压）",
        CpuVoltageSource.Vcore => "CPU Vcore（主板传感器）",
        _ => "CPU 电压"
    };

    public string VoltageSourceShortText => _snapshot?.CpuVoltageSource switch
    {
        CpuVoltageSource.Vid => "VID",
        CpuVoltageSource.Vcore => "Vcore",
        _ => "V"
    };

    public string PchSourceText => _snapshot?.PchTemperature is { } reading
        ? $"{reading.HardwareName} · {reading.SensorName}"
        : "未检测到 PCH 温度传感器";

    public string LastUpdatedText => _snapshot is null
        ? "--:--:--"
        : _snapshot.CapturedAt.ToLocalTime().ToString("HH:mm:ss");

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool ShowTaskbarWidget
    {
        get => _showTaskbarWidget;
        set
        {
            if (!SetField(ref _showTaskbarWidget, value))
            {
                return;
            }

            _settings.ShowTaskbarWidget = value;
            SaveSettings();
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value)
            {
                return;
            }

            var result = _startupTaskService.SetEnabled(value);
            StatusText = result.Message;
            if (result.Success)
            {
                SetField(ref _startWithWindows, value);
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public PchSensorOption SelectedPchSensor
    {
        get => _selectedPchSensor;
        set
        {
            if (value is null || !SetField(ref _selectedPchSensor, value))
            {
                return;
            }

            _settings.PreferredPchSensorIdentifier = value.Identifier;
            SaveSettings();
            RebuildSnapshot();
        }
    }

    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        _pollTask = Task.Run(PollAsync);
    }

    public void ExportDiagnostics(string path)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Z690 Monitor sensor report");
        builder.AppendLine($"Captured: {DateTimeOffset.Now:O}");
        builder.AppendLine("HardwareKind\tHardware\tSensorKind\tSensor\tValue\tIdentifier");

        foreach (var reading in _lastReadings
                     .OrderBy(item => item.HardwareKind)
                     .ThenBy(item => item.HardwareName)
                     .ThenBy(item => item.SensorKind)
                     .ThenBy(item => item.SensorName))
        {
            builder.Append(reading.HardwareKind).Append('\t')
                .Append(Sanitize(reading.HardwareName)).Append('\t')
                .Append(reading.SensorKind).Append('\t')
                .Append(Sanitize(reading.SensorName)).Append('\t')
                .Append(reading.Value?.ToString("G") ?? "null").Append('\t')
                .AppendLine(Sanitize(reading.SensorId));
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        StatusText = "传感器报告已导出。";
    }

    private async Task PollAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                var readings = _monitor.ReadSensors();
                if (_cancellation.IsCancellationRequested)
                {
                    break;
                }

                await _dispatcher.InvokeAsync(() => ApplyReadings(readings));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!_cancellation.IsCancellationRequested)
                {
                    await _dispatcher.InvokeAsync(() => StatusText = DescribeReadError(ex));
                }
            }

            try
            {
                await Task.Delay(
                    Math.Clamp(_settings.RefreshIntervalMilliseconds, 500, 5000),
                    _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void ApplyReadings(IReadOnlyList<RawSensorReading> readings)
    {
        _lastReadings = readings;
        UpdatePchOptions(readings);
        RebuildSnapshot();

        var cpuCount = readings.Count(reading => reading.HardwareKind == HardwareKind.Cpu);
        var boardCount = readings.Count(reading => reading.HardwareKind == HardwareKind.Motherboard);
        StatusText = cpuCount == 0
            ? "未读取到 CPU 传感器。请检查驱动是否被 Windows 安全策略阻止。"
            : $"运行正常 · CPU {cpuCount} 项 · 主板 {boardCount} 项";
    }

    private void RebuildSnapshot()
    {
        var snapshot = _aggregator.CreateSnapshot(
            _lastReadings,
            DateTimeOffset.Now,
            new SensorSelectionOptions { PchSensorId = _settings.PreferredPchSensorIdentifier });
        _snapshot = snapshot;

        OnPropertyChanged(nameof(CpuName));
        OnPropertyChanged(nameof(CpuTemperatureAverageText));
        OnPropertyChanged(nameof(CpuTemperatureMaximumText));
        OnPropertyChanged(nameof(CpuTemperatureCompactText));
        OnPropertyChanged(nameof(CpuVoltageAverageText));
        OnPropertyChanged(nameof(CpuVoltageMaximumText));
        OnPropertyChanged(nameof(CpuVoltageCompactText));
        OnPropertyChanged(nameof(PchTemperatureText));
        OnPropertyChanged(nameof(PchTemperatureCompactText));
        OnPropertyChanged(nameof(VoltageSourceText));
        OnPropertyChanged(nameof(VoltageSourceShortText));
        OnPropertyChanged(nameof(PchSourceText));
        OnPropertyChanged(nameof(LastUpdatedText));
        SnapshotUpdated?.Invoke(this, snapshot);
    }

    private void UpdatePchOptions(IReadOnlyList<RawSensorReading> readings)
    {
        var selectedId = _settings.PreferredPchSensorIdentifier;
        var candidates = readings
            .Where(reading =>
                reading.HardwareKind == HardwareKind.Motherboard &&
                reading.SensorKind == SensorKind.Temperature)
            .OrderBy(reading => reading.HardwareName)
            .ThenBy(reading => reading.SensorName)
            .Select(reading => new PchSensorOption(
                reading.SensorId,
                $"{reading.HardwareName} · {reading.SensorName}"))
            .ToArray();

        var existingIds = PchSensors.Select(item => item.Identifier).ToArray();
        var nextIds = candidates.Select(item => item.Identifier).Prepend((string?)null).ToArray();
        if (!existingIds.SequenceEqual(nextIds, StringComparer.OrdinalIgnoreCase))
        {
            PchSensors.Clear();
            PchSensors.Add(PchSensorOption.Automatic);
            foreach (var candidate in candidates)
            {
                PchSensors.Add(candidate);
            }
        }

        _selectedPchSensor = PchSensors.FirstOrDefault(item =>
            string.Equals(item.Identifier, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? PchSensorOption.Automatic;
        OnPropertyChanged(nameof(SelectedPchSensor));
    }

    private void SaveSettings()
    {
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"保存设置失败：{ex.Message}";
        }
    }

    private static string DescribeReadError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "硬件访问被拒绝。请确认程序以管理员身份运行。",
        System.ComponentModel.Win32Exception => "LibreHardwareMonitor 驱动无法加载，可能被 Windows 安全策略阻止。",
        _ => $"传感器读取失败：{exception.Message}"
    };

    private static string Format(double? value, string format, string suffix) =>
        value is double number && double.IsFinite(number) ? number.ToString(format) + suffix : "--";

    private static string FormatPair(double? maximum, double? average, string format, string suffix) =>
        $"{Format(maximum, format, string.Empty)}/{Format(average, format, string.Empty)}{suffix}";

    private static string Sanitize(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cancellation.Cancel();
        if (_pollTask is null)
        {
            _cancellation.Dispose();
        }
        else
        {
            _ = _pollTask.ContinueWith(
                _ => _cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
