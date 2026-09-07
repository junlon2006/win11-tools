namespace CpuCoreVoltageTray;

internal enum CpuMetricKind
{
    Clock,
    Temperature,
    Power
}

internal readonly record struct CpuMetricSensorSample(
    CpuMetricKind Kind,
    string Name,
    int Index,
    float? Value);

internal readonly record struct CpuMetricSelection(
    float? MaximumCoreClockMHz,
    float? MaximumCoreTemperatureCelsius,
    float? PackagePowerWatts);

internal static class CpuMetricSelector
{
    private const float MaximumPlausibleClockMHz = 10_000f;
    private const float MinimumPlausibleTemperatureCelsius = -20f;
    private const float MaximumPlausibleTemperatureCelsius = 125f;
    private const float MaximumPlausiblePowerWatts = 1_000f;

    public static CpuMetricSelection Select(
        IEnumerable<CpuMetricSensorSample> samples,
        bool includePower)
    {
        float? maximumCoreClockMHz = null;
        float? maximumCoreTemperatureCelsius = null;
        float? packagePowerWatts = null;

        foreach (CpuMetricSensorSample sample in samples)
        {
            if (sample.Value is not float value || !float.IsFinite(value))
                continue;

            switch (sample.Kind)
            {
                // LibreHardwareMonitor uses index 1..N for per-core clocks.
                case CpuMetricKind.Clock
                    when sample.Index > 0 &&
                         value > 0 &&
                         value <= MaximumPlausibleClockMHz:
                    maximumCoreClockMHz = Maximum(maximumCoreClockMHz, value);
                    break;

                case CpuMetricKind.Temperature
                    when sample.Index == 0 &&
                         string.Equals(sample.Name, "Core Max", StringComparison.Ordinal) &&
                         value >= MinimumPlausibleTemperatureCelsius &&
                         value <= MaximumPlausibleTemperatureCelsius:
                    maximumCoreTemperatureCelsius = Maximum(maximumCoreTemperatureCelsius, value);
                    break;

                case CpuMetricKind.Power
                    when includePower &&
                         sample.Index == 0 &&
                         string.Equals(sample.Name, "CPU Package", StringComparison.Ordinal) &&
                         value >= 0 &&
                         value <= MaximumPlausiblePowerWatts:
                    packagePowerWatts = Maximum(packagePowerWatts, value);
                    break;
            }
        }

        return new CpuMetricSelection(
            maximumCoreClockMHz,
            maximumCoreTemperatureCelsius,
            packagePowerWatts);
    }

    private static float Maximum(float? current, float candidate)
    {
        return current is float value && value >= candidate ? value : candidate;
    }
}
