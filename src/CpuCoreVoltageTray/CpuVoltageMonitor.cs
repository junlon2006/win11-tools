using LibreHardwareMonitor.Hardware;

namespace CpuCoreVoltageTray;

internal readonly record struct VoltageReading(
    bool IsAvailable,
    float Volts,
    string SensorName,
    int SensorCount,
    float? MaximumCoreClockMHz,
    float? MaximumCoreTemperatureCelsius,
    float? PackagePowerWatts,
    string Error)
{
    public static VoltageReading Available(
        VoltageSelection selection,
        CpuMetricSelection metrics) => new(
        true,
        selection.Volts,
        selection.SensorName,
        selection.SensorCount,
        metrics.MaximumCoreClockMHz,
        metrics.MaximumCoreTemperatureCelsius,
        metrics.PackagePowerWatts,
        string.Empty);

    public static VoltageReading Unavailable(
        string error,
        CpuMetricSelection metrics = default) => new(
        false,
        0,
        string.Empty,
        0,
        metrics.MaximumCoreClockMHz,
        metrics.MaximumCoreTemperatureCelsius,
        metrics.PackagePowerWatts,
        error);
}

internal sealed class CpuVoltageMonitor : IDisposable
{
    private static readonly TimeSpan EmptyReadingResetInterval = TimeSpan.FromSeconds(30);

    private Computer? _computer;
    private int _consecutiveEmptyReadings;
    private DateTime _nextEmptyReadingResetUtc = DateTime.MinValue;
    private bool _skipNextPowerSample = true;

    public VoltageReading Read()
    {
        try
        {
            if (_computer is null && !DriverManager.GetStatus().IsReady)
                return VoltageReading.Unavailable("需要安装 PawnIO 传感器驱动");

            EnsureOpen();

            List<VoltageSensorSample> voltageSamples = [];
            List<CpuMetricSensorSample> metricSamples = [];
            foreach (IHardware hardware in _computer!.Hardware.Where(
                         item => item.HardwareType == HardwareType.Cpu))
            {
                hardware.Accept(UpdateVisitor.Instance);
                CollectSamples(hardware, voltageSamples, metricSamples);
            }

            CpuMetricSelection metrics = CpuMetricSelector.Select(
                metricSamples,
                includePower: !_skipNextPowerSample);
            _skipNextPowerSample = false;

            VoltageSelection? selection = VoltageSensorSelector.Select(voltageSamples);
            if (selection is VoltageSelection available)
            {
                _consecutiveEmptyReadings = 0;
                return VoltageReading.Available(available, metrics);
            }

            HandleEmptyReading();
            return VoltageReading.Unavailable("未读取到有效的逐核心 VID", metrics);
        }
        catch (Exception exception)
        {
            CloseComputer();
            return VoltageReading.Unavailable($"读取失败：{exception.Message}");
        }
    }

    public void Reset()
    {
        CloseComputer();
        _consecutiveEmptyReadings = 0;
        _nextEmptyReadingResetUtc = DateTime.MinValue;
        _skipNextPowerSample = true;
    }

    public void Dispose() => CloseComputer();

    private void EnsureOpen()
    {
        if (_computer is not null)
            return;

        Computer computer = new()
        {
            IsCpuEnabled = true
        };

        try
        {
            computer.Open();
            _computer = computer;
            _skipNextPowerSample = true;
        }
        catch
        {
            computer.Close();
            throw;
        }
    }

    private void HandleEmptyReading()
    {
        _consecutiveEmptyReadings++;
        DateTime now = DateTime.UtcNow;
        if (_consecutiveEmptyReadings < 3 || now < _nextEmptyReadingResetUtc)
            return;

        CloseComputer();
        _consecutiveEmptyReadings = 0;
        _nextEmptyReadingResetUtc = now + EmptyReadingResetInterval;
    }

    private void CloseComputer()
    {
        Computer? computer = _computer;
        _computer = null;
        if (computer is null)
            return;

        try
        {
            computer.Close();
        }
        catch
        {
            // The process is exiting or rebuilding the sensor graph; there is no
            // useful recovery action if the driver handle is already unavailable.
        }
    }

    private static void CollectSamples(
        IHardware hardware,
        ICollection<VoltageSensorSample> voltageDestination,
        ICollection<CpuMetricSensorSample> metricDestination)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Voltage)
            {
                voltageDestination.Add(new VoltageSensorSample(
                    hardware.Name,
                    sensor.Name,
                    sensor.Index,
                    sensor.Value));
                continue;
            }

            CpuMetricKind? kind = sensor.SensorType switch
            {
                SensorType.Clock => CpuMetricKind.Clock,
                SensorType.Temperature => CpuMetricKind.Temperature,
                SensorType.Power => CpuMetricKind.Power,
                _ => null
            };

            if (kind is CpuMetricKind metricKind)
            {
                metricDestination.Add(new CpuMetricSensorSample(
                    metricKind,
                    sensor.Name,
                    sensor.Index,
                    sensor.Value));
            }
        }

        foreach (IHardware subHardware in hardware.SubHardware)
            CollectSamples(subHardware, voltageDestination, metricDestination);
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public static UpdateVisitor Instance { get; } = new();

        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware)
                subHardware.Accept(this);
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}
