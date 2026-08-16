namespace Win11Monitor.Core;

public enum CpuVoltageSource
{
    None,
    Vid,
    Vcore,
}

public sealed record SensorAggregate(
    double Average,
    double Maximum,
    int SensorCount,
    IReadOnlyList<RawSensorReading> Sensors);

public sealed record MonitorSnapshot(
    DateTimeOffset CapturedAt,
    SensorAggregate? CpuTemperature,
    SensorAggregate? CpuVoltage,
    CpuVoltageSource CpuVoltageSource,
    RawSensorReading? PchTemperature);

public sealed record SensorSelectionOptions
{
    public string? PchSensorId { get; init; }
}
