using LibreHardwareMonitor.Hardware;
using Win11Monitor.Core;

namespace Win11Monitor.App.Services;

public sealed class HardwareMonitorService : IDisposable
{
    private readonly object _sync = new();
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsMotherboardEnabled = true
    };

    private bool _isOpen;
    private bool _isDisposed;

    public IReadOnlyList<RawSensorReading> ReadSensors()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            EnsureOpen();
            _computer.Accept(UpdateVisitor.Instance);

            var readings = new List<RawSensorReading>();
            foreach (var hardware in _computer.Hardware)
            {
                var rootKind = MapRootHardwareKind(hardware.HardwareType);
                CollectReadings(hardware, rootKind, readings);
            }

            return readings;
        }
    }

    private void EnsureOpen()
    {
        if (_isOpen)
        {
            return;
        }

        _computer.Open();
        _isOpen = true;
    }

    private static void CollectReadings(
        IHardware hardware,
        HardwareKind rootKind,
        ICollection<RawSensorReading> destination)
    {
        foreach (var sensor in hardware.Sensors)
        {
            var sensorKind = sensor.SensorType switch
            {
                SensorType.Temperature => SensorKind.Temperature,
                SensorType.Voltage => SensorKind.Voltage,
                _ => SensorKind.Unknown
            };

            if (sensorKind == SensorKind.Unknown)
            {
                continue;
            }

            destination.Add(new RawSensorReading(
                hardware.Identifier.ToString(),
                hardware.Name,
                rootKind,
                sensor.Identifier.ToString(),
                sensor.Name,
                sensorKind,
                sensor.Value));
        }

        foreach (var child in hardware.SubHardware)
        {
            CollectReadings(child, rootKind, destination);
        }
    }

    private static HardwareKind MapRootHardwareKind(HardwareType type) => type switch
    {
        HardwareType.Cpu => HardwareKind.Cpu,
        HardwareType.Motherboard => HardwareKind.Motherboard,
        _ => HardwareKind.Unknown
    };

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            if (_isOpen)
            {
                _computer.Close();
            }

            _isDisposed = true;
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public static UpdateVisitor Instance { get; } = new();

        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var child in hardware.SubHardware)
            {
                child.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}
