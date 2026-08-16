using System.Text.RegularExpressions;

namespace Win11Monitor.Core;

public sealed partial class SensorSnapshotAggregator
{
    private static readonly string[] PchAliases =
    [
        "pch",
        "chipset",
        "platformcontrollerhub",
    ];

    public MonitorSnapshot CreateSnapshot(
        IEnumerable<RawSensorReading> readings,
        DateTimeOffset capturedAt,
        SensorSelectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(readings);

        var validReadings = readings
            .Where(IsValid)
            .ToArray();

        var cpuTemperatures = validReadings
            .Where(IsPerCoreTemperature)
            .OrderBy(reading => reading.SensorId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var vidReadings = validReadings
            .Where(IsPerCoreCpuVid)
            .OrderBy(reading => reading.SensorId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (vidReadings.Length == 0)
        {
            vidReadings = validReadings
                .Where(IsAggregateCpuVid)
                .OrderBy(reading => reading.SensorId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        RawSensorReading[] voltageReadings;
        CpuVoltageSource voltageSource;

        if (vidReadings.Length > 0)
        {
            voltageReadings = vidReadings;
            voltageSource = CpuVoltageSource.Vid;
        }
        else
        {
            voltageReadings = validReadings
                .Where(IsCpuVcore)
                .OrderBy(reading => reading.SensorId, StringComparer.OrdinalIgnoreCase)
                .Take(1)
                .ToArray();
            voltageSource = voltageReadings.Length > 0
                ? CpuVoltageSource.Vcore
                : CpuVoltageSource.None;
        }

        return new MonitorSnapshot(
            capturedAt,
            Aggregate(cpuTemperatures),
            Aggregate(voltageReadings),
            voltageSource,
            SelectPchTemperature(validReadings, options));
    }

    private static bool IsValid(RawSensorReading reading) =>
        reading.Value is double value && double.IsFinite(value);

    private static bool IsPerCoreTemperature(RawSensorReading reading) =>
        reading.HardwareKind == HardwareKind.Cpu &&
        reading.SensorKind == SensorKind.Temperature &&
        PerCoreSensorName().IsMatch(reading.SensorName);

    private static bool IsPerCoreCpuVid(RawSensorReading reading) =>
        reading.HardwareKind == HardwareKind.Cpu &&
        reading.SensorKind == SensorKind.Voltage &&
        PerCoreSensorName().IsMatch(reading.SensorName);

    private static bool IsAggregateCpuVid(RawSensorReading reading)
    {
        if (reading.HardwareKind != HardwareKind.Cpu ||
            reading.SensorKind != SensorKind.Voltage)
        {
            return false;
        }

        var normalizedName = Normalize(reading.SensorName);
        return normalizedName is "vid" or "corevid" or "cpuvid" or "cpucore" or "corevoltage";
    }

    private static bool IsCpuVcore(RawSensorReading reading)
    {
        if (reading.HardwareKind != HardwareKind.Motherboard ||
            reading.SensorKind != SensorKind.Voltage)
        {
            return false;
        }

        var normalizedName = Normalize(reading.SensorName);
        return normalizedName is "vcore" or "cpucore" or "corevoltage" or "cpuvcore";
    }

    private static RawSensorReading? SelectPchTemperature(
        IReadOnlyCollection<RawSensorReading> validReadings,
        SensorSelectionOptions? options)
    {
        var motherboardTemperatures = validReadings
            .Where(reading =>
                reading.HardwareKind == HardwareKind.Motherboard &&
                reading.SensorKind == SensorKind.Temperature)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(options?.PchSensorId))
        {
            var configured = motherboardTemperatures.FirstOrDefault(reading =>
                string.Equals(
                    reading.SensorId,
                    options.PchSensorId,
                    StringComparison.OrdinalIgnoreCase));

            if (configured is not null)
            {
                return configured;
            }
        }

        return motherboardTemperatures
            .Select(reading => new
            {
                Reading = reading,
                AliasRank = GetPchAliasRank(Normalize(reading.SensorName)),
            })
            .Where(candidate => candidate.AliasRank >= 0)
            .OrderBy(candidate => candidate.AliasRank)
            .ThenBy(candidate => candidate.Reading.SensorName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Reading.SensorId, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Reading)
            .FirstOrDefault();
    }

    private static int GetPchAliasRank(string normalizedName)
    {
        for (var index = 0; index < PchAliases.Length; index++)
        {
            var alias = PchAliases[index];
            if (normalizedName == alias || normalizedName == alias + "temperature")
            {
                return index;
            }
        }

        return -1;
    }

    private static SensorAggregate? Aggregate(IReadOnlyList<RawSensorReading> readings)
    {
        if (readings.Count == 0)
        {
            return null;
        }

        var values = readings.Select(reading => reading.Value!.Value).ToArray();
        return new SensorAggregate(
            values.Average(),
            values.Max(),
            values.Length,
            readings);
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    [GeneratedRegex(
        @"^(?:cpu\s*)?(?:p\s*-?\s*|e\s*-?\s*)?core\s*(?:#\s*)?\d+(?:\s+vid)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PerCoreSensorName();
}
