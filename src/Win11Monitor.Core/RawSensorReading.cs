namespace Win11Monitor.Core;

public enum HardwareKind
{
    Unknown,
    Cpu,
    Motherboard,
}

public enum SensorKind
{
    Unknown,
    Temperature,
    Voltage,
}

public sealed record RawSensorReading(
    string HardwareId,
    string HardwareName,
    HardwareKind HardwareKind,
    string SensorId,
    string SensorName,
    SensorKind SensorKind,
    double? Value);
