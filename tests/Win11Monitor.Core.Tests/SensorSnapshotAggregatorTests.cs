using Win11Monitor.Core;
using Xunit;

namespace Win11Monitor.Core.Tests;

public sealed class SensorSnapshotAggregatorTests
{
    private readonly SensorSnapshotAggregator _aggregator = new();
    private readonly DateTimeOffset _capturedAt = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateSnapshot_AggregatesOnlyValidPerCoreTemperatures()
    {
        RawSensorReading[] readings =
        [
            CpuTemperature("core-0", "CPU Core #1", 52),
            CpuTemperature("core-1", "P-Core 2", 68),
            CpuTemperature("core-2", "E Core 10", 60),
            CpuTemperature("package", "CPU Package", 75),
            CpuTemperature("core-max", "Core Max", 70),
            CpuTemperature("distance", "CPU Core #1 Distance to TjMax", 32),
            CpuTemperature("invalid-null", "CPU Core #3", null),
            CpuTemperature("invalid-nan", "CPU Core #4", double.NaN),
            CpuTemperature("invalid-infinity", "CPU Core #5", double.PositiveInfinity),
            MotherboardTemperature("board-core", "CPU Core #6", 99),
        ];

        var snapshot = _aggregator.CreateSnapshot(readings, _capturedAt);

        Assert.NotNull(snapshot.CpuTemperature);
        Assert.Equal(60, snapshot.CpuTemperature.Average);
        Assert.Equal(68, snapshot.CpuTemperature.Maximum);
        Assert.Equal(3, snapshot.CpuTemperature.SensorCount);
        Assert.Equal(_capturedAt, snapshot.CapturedAt);
    }

    [Fact]
    public void CreateSnapshot_PrefersValidVidReadingsWithoutMixingVcore()
    {
        RawSensorReading[] readings =
        [
            CpuVoltage("vid-0", "CPU Core #1", 1.10),
            CpuVoltage("vid-1", "CPU Core #2", 1.30),
            CpuVoltage("vcore", "CPU Core", 1.40),
        ];

        var snapshot = _aggregator.CreateSnapshot(readings, _capturedAt);

        Assert.Equal(CpuVoltageSource.Vid, snapshot.CpuVoltageSource);
        Assert.NotNull(snapshot.CpuVoltage);
        Assert.Equal(1.20, snapshot.CpuVoltage.Average, 5);
        Assert.Equal(1.30, snapshot.CpuVoltage.Maximum, 5);
        Assert.Equal(2, snapshot.CpuVoltage.SensorCount);
    }

    [Fact]
    public void CreateSnapshot_FallsBackToVcoreWhenAllVidReadingsAreInvalid()
    {
        RawSensorReading[] readings =
        [
            CpuVoltage("vid-0", "Core #0 VID", double.NaN),
            CpuVoltage("vid-1", "Core #1 VID", null),
            MotherboardVoltage("vcore", "Vcore", 1.25),
            MotherboardVoltage("offset", "Vcore Offset", 0.05),
            CpuVoltage("unrelated", "System Agent", 0.90),
        ];

        var snapshot = _aggregator.CreateSnapshot(readings, _capturedAt);

        Assert.Equal(CpuVoltageSource.Vcore, snapshot.CpuVoltageSource);
        Assert.NotNull(snapshot.CpuVoltage);
        Assert.Equal(1.25, snapshot.CpuVoltage.Average, 5);
        Assert.Single(snapshot.CpuVoltage.Sensors);
    }

    [Fact]
    public void CreateSnapshot_PrefersAggregateCpuVidOverMotherboardVcore()
    {
        RawSensorReading[] readings =
        [
            CpuVoltage("aggregate", "CPU Core", 1.18),
            MotherboardVoltage("vcore", "Vcore", 1.25),
        ];

        var snapshot = _aggregator.CreateSnapshot(readings, _capturedAt);

        Assert.Equal(CpuVoltageSource.Vid, snapshot.CpuVoltageSource);
        Assert.NotNull(snapshot.CpuVoltage);
        Assert.Equal(1.18, snapshot.CpuVoltage.Average, 5);
        Assert.Single(snapshot.CpuVoltage.Sensors);
    }

    [Fact]
    public void CreateSnapshot_ReturnsNoCpuMetricsWhenMatchingSensorsAreAbsent()
    {
        RawSensorReading[] readings =
        [
            CpuTemperature("package", "CPU Package", 70),
            CpuVoltage("agent", "System Agent", 0.95),
        ];

        var snapshot = _aggregator.CreateSnapshot(readings, _capturedAt);

        Assert.Null(snapshot.CpuTemperature);
        Assert.Null(snapshot.CpuVoltage);
        Assert.Equal(CpuVoltageSource.None, snapshot.CpuVoltageSource);
    }

    [Theory]
    [InlineData("PCH")]
    [InlineData("PCH Temperature")]
    [InlineData("Chipset")]
    [InlineData("Chipset Temperature")]
    [InlineData("Platform Controller Hub")]
    [InlineData("Platform Controller Hub Temperature")]
    public void CreateSnapshot_SelectsKnownPchAlias(string sensorName)
    {
        var reading = MotherboardTemperature("pch", sensorName, 48);

        var snapshot = _aggregator.CreateSnapshot([reading], _capturedAt);

        Assert.Same(reading, snapshot.PchTemperature);
    }

    [Fact]
    public void CreateSnapshot_ConfiguredPchSensorTakesPrecedenceOverAliases()
    {
        var alias = MotherboardTemperature("pch", "PCH", 48);
        var configured = MotherboardTemperature("temp-7", "Temperature #7", 53);
        var options = new SensorSelectionOptions { PchSensorId = "TEMP-7" };

        var snapshot = _aggregator.CreateSnapshot([alias, configured], _capturedAt, options);

        Assert.Same(configured, snapshot.PchTemperature);
    }

    [Fact]
    public void CreateSnapshot_FallsBackToAliasWhenConfiguredPchSensorIsMissingOrInvalid()
    {
        var alias = MotherboardTemperature("pch", "PCH", 48);
        var invalidConfigured = MotherboardTemperature("temp-7", "Temperature #7", double.NaN);
        var options = new SensorSelectionOptions { PchSensorId = "temp-7" };

        var snapshot = _aggregator.CreateSnapshot([invalidConfigured, alias], _capturedAt, options);

        Assert.Same(alias, snapshot.PchTemperature);
    }

    [Fact]
    public void CreateSnapshot_DoesNotSelectPchAliasFromCpuOrWrongSensorKind()
    {
        RawSensorReading[] readings =
        [
            CpuTemperature("pch-cpu", "PCH", 70),
            new("board", "Z690", HardwareKind.Motherboard, "pch-voltage", "PCH", SensorKind.Voltage, 1.05),
        ];

        var snapshot = _aggregator.CreateSnapshot(readings, _capturedAt);

        Assert.Null(snapshot.PchTemperature);
    }

    private static RawSensorReading CpuTemperature(string id, string name, double? value) =>
        new("cpu", "Intel Core i9-13900K", HardwareKind.Cpu, id, name, SensorKind.Temperature, value);

    private static RawSensorReading CpuVoltage(string id, string name, double? value) =>
        new("cpu", "Intel Core i9-13900K", HardwareKind.Cpu, id, name, SensorKind.Voltage, value);

    private static RawSensorReading MotherboardTemperature(string id, string name, double? value) =>
        new("board", "Z690", HardwareKind.Motherboard, id, name, SensorKind.Temperature, value);

    private static RawSensorReading MotherboardVoltage(string id, string name, double? value) =>
        new("board", "Z690", HardwareKind.Motherboard, id, name, SensorKind.Voltage, value);
}
