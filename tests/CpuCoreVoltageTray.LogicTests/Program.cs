using CpuCoreVoltageTray;

List<(string Name, Action Test)> tests =
[
    ("selects maximum across P and E cores", SelectsMaximumAcrossCores),
    ("ignores aggregate CPU Core reading", IgnoresAggregateReading),
    ("ignores invalid readings", IgnoresInvalidReadings),
    ("does not accept unrelated index-zero voltage", RejectsUnrelatedAggregate),
    ("does not depend on display names", DoesNotDependOnDisplayNames),
    ("returns unavailable when no usable reading exists", ReturnsUnavailable),
    ("selects maximum per-core clock", SelectsMaximumPerCoreClock),
    ("selects only Core Max temperature", SelectsOnlyCoreMaxTemperature),
    ("selects only index-zero CPU Package power", SelectsOnlyPackagePower),
    ("skips package power on the first sample", SkipsPowerWhenDisabled),
    ("ignores invalid CPU metric readings", IgnoresInvalidCpuMetricReadings)
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed");
return failures == 0 ? 0 : 1;

static void SelectsMaximumAcrossCores()
{
    VoltageSelection result = Select(
        new VoltageSensorSample("Intel Core i5-13600K", "P-Core #1", 1, 1.16f),
        new VoltageSensorSample("Intel Core i5-13600K", "P-Core #2", 2, 1.31f),
        new VoltageSensorSample("Intel Core i5-13600K", "E-Core #1", 7, 1.24f));

    Equal(1.31f, result.Volts);
    Equal("P-Core #2", result.SensorName);
    Equal(3, result.SensorCount);
}

static void IgnoresAggregateReading()
{
    VoltageSelection result = Select(
        new VoltageSensorSample("Intel Core i5-13600K", "CPU Core", 0, 1.49f),
        new VoltageSensorSample("Intel Core i5-13600K", "P-Core #1", 1, 1.20f));

    Equal(1.20f, result.Volts);
    Equal("P-Core #1", result.SensorName);
    Equal(1, result.SensorCount);
}

static void IgnoresInvalidReadings()
{
    VoltageSelection result = Select(
        new VoltageSensorSample("Intel Core i5-13600K", "P-Core #1", 1, null),
        new VoltageSensorSample("Intel Core i5-13600K", "P-Core #2", 2, float.NaN),
        new VoltageSensorSample("Intel Core i5-13600K", "P-Core #3", 3, float.PositiveInfinity),
        new VoltageSensorSample("Intel Core i5-13600K", "P-Core #4", 4, 0),
        new VoltageSensorSample("Intel Core i5-13600K", "P-Core #5", 5, 2.01f),
        new VoltageSensorSample("Intel Core i5-13600K", "E-Core #1", 7, 0.82f));

    Equal(0.82f, result.Volts);
    Equal(1, result.SensorCount);
}

static void RejectsUnrelatedAggregate()
{
    VoltageSelection? result = VoltageSensorSelector.Select([
        new VoltageSensorSample("Intel Core i5-13600K", "CPU Package", 0, 1.25f),
        new VoltageSensorSample("Intel Core i5-13600K", "CPU Core", 0, 1.22f)
    ]);

    Null(result);
}

static void DoesNotDependOnDisplayNames()
{
    VoltageSelection result = Select(
        new VoltageSensorSample("Intel Core i5-13600K", "自定义核心名称", 4, 1.11f));

    Equal(1.11f, result.Volts);
    Equal("自定义核心名称", result.SensorName);
}

static void ReturnsUnavailable()
{
    VoltageSelection? result = VoltageSensorSelector.Select([
        new VoltageSensorSample("Intel Core i5-13600K", "P-Core #1", 1, null)
    ]);

    Null(result);
}

static void SelectsMaximumPerCoreClock()
{
    CpuMetricSelection result = SelectMetrics(
        includePower: true,
        new CpuMetricSensorSample(CpuMetricKind.Clock, "Bus Speed", 0, 100f),
        new CpuMetricSensorSample(CpuMetricKind.Clock, "CPU Core #1", 1, 5_100f),
        new CpuMetricSensorSample(CpuMetricKind.Clock, "CPU Core #2", 2, 4_900f));

    Equal(5_100f, Value(result.MaximumCoreClockMHz));
}

static void SelectsOnlyCoreMaxTemperature()
{
    CpuMetricSelection result = SelectMetrics(
        includePower: true,
        new CpuMetricSensorSample(CpuMetricKind.Temperature, "Core Max", 0, 91f),
        new CpuMetricSensorSample(CpuMetricKind.Temperature, "Core Max", 1, 122f),
        new CpuMetricSensorSample(CpuMetricKind.Temperature, "Core Distance to TjMax", 0, 110f),
        new CpuMetricSensorSample(CpuMetricKind.Temperature, "CPU Package", 0, 120f),
        new CpuMetricSensorSample(CpuMetricKind.Temperature, "core max", 0, 125f));

    Equal(91f, Value(result.MaximumCoreTemperatureCelsius));
}

static void SelectsOnlyPackagePower()
{
    CpuMetricSelection result = SelectMetrics(
        includePower: true,
        new CpuMetricSensorSample(CpuMetricKind.Power, "CPU Package", 0, 181f),
        new CpuMetricSensorSample(CpuMetricKind.Power, "CPU Package", 0, 195f),
        new CpuMetricSensorSample(CpuMetricKind.Power, "CPU Cores", 0, 250f),
        new CpuMetricSensorSample(CpuMetricKind.Power, "CPU Package", 1, 300f));

    Equal(195f, Value(result.PackagePowerWatts));
}

static void SkipsPowerWhenDisabled()
{
    CpuMetricSelection result = SelectMetrics(
        includePower: false,
        new CpuMetricSensorSample(CpuMetricKind.Clock, "CPU Core #1", 1, 5_000f),
        new CpuMetricSensorSample(CpuMetricKind.Power, "CPU Package", 0, 180f));

    Equal(5_000f, Value(result.MaximumCoreClockMHz));
    Null(result.PackagePowerWatts);
}

static void IgnoresInvalidCpuMetricReadings()
{
    CpuMetricSelection result = SelectMetrics(
        includePower: true,
        new CpuMetricSensorSample(CpuMetricKind.Clock, "CPU Core #1", 1, null),
        new CpuMetricSensorSample(CpuMetricKind.Clock, "CPU Core #2", 2, float.NaN),
        new CpuMetricSensorSample(CpuMetricKind.Clock, "CPU Core #3", 3, float.PositiveInfinity),
        new CpuMetricSensorSample(CpuMetricKind.Clock, "CPU Core #4", 4, 0f),
        new CpuMetricSensorSample(CpuMetricKind.Clock, "CPU Core #5", 5, 10_000.01f),
        new CpuMetricSensorSample(CpuMetricKind.Clock, "CPU Core #6", 6, 10_000f),
        new CpuMetricSensorSample(CpuMetricKind.Temperature, "Core Max", 0, float.NegativeInfinity),
        new CpuMetricSensorSample(CpuMetricKind.Temperature, "Core Max", 0, -20.01f),
        new CpuMetricSensorSample(CpuMetricKind.Temperature, "Core Max", 0, 125.01f),
        new CpuMetricSensorSample(CpuMetricKind.Temperature, "Core Max", 0, -20f),
        new CpuMetricSensorSample(CpuMetricKind.Temperature, "Core Max", 0, 125f),
        new CpuMetricSensorSample(CpuMetricKind.Power, "CPU Package", 0, float.NaN),
        new CpuMetricSensorSample(CpuMetricKind.Power, "CPU Package", 0, -0.01f),
        new CpuMetricSensorSample(CpuMetricKind.Power, "CPU Package", 0, 1_000.01f),
        new CpuMetricSensorSample(CpuMetricKind.Power, "CPU Package", 0, 0f),
        new CpuMetricSensorSample(CpuMetricKind.Power, "CPU Package", 0, 1_000f));

    Equal(10_000f, Value(result.MaximumCoreClockMHz));
    Equal(125f, Value(result.MaximumCoreTemperatureCelsius));
    Equal(1_000f, Value(result.PackagePowerWatts));
}

static VoltageSelection Select(params VoltageSensorSample[] samples)
{
    return VoltageSensorSelector.Select(samples)
        ?? throw new InvalidOperationException("Expected an available selection.");
}

static CpuMetricSelection SelectMetrics(
    bool includePower,
    params CpuMetricSensorSample[] samples)
{
    return CpuMetricSelector.Select(samples, includePower);
}

static T Value<T>(T? value) where T : struct
{
    return value ?? throw new InvalidOperationException("Expected a value, got null.");
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void Null<T>(T? value) where T : struct
{
    if (value.HasValue)
        throw new InvalidOperationException($"Expected null, got {value.Value}.");
}
