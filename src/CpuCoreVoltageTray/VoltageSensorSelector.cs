namespace CpuCoreVoltageTray;

internal readonly record struct VoltageSensorSample(
    string HardwareName,
    string SensorName,
    int Index,
    float? Volts);

internal readonly record struct VoltageSelection(
    float Volts,
    string HardwareName,
    string SensorName,
    int SensorCount);

internal static class VoltageSensorSelector
{
    private const float MaximumPlausibleCpuVoltage = 2.0f;

    public static VoltageSelection? Select(IEnumerable<VoltageSensorSample> samples)
    {
        // LibreHardwareMonitor 0.9.6 assigns index 1..N to Intel per-core VID.
        // Index 0 is an aggregate sample, not a cross-core maximum.
        List<VoltageSensorSample> perCore = samples
            .Where(sample => sample.Index > 0 && IsValid(sample.Volts))
            .ToList();
        if (perCore.Count == 0)
            return null;

        VoltageSensorSample maximum = perCore[0];
        foreach (VoltageSensorSample sample in perCore.Skip(1))
        {
            if (sample.Volts!.Value > maximum.Volts!.Value)
                maximum = sample;
        }

        return new VoltageSelection(
            maximum.Volts!.Value,
            maximum.HardwareName,
            maximum.SensorName,
            perCore.Count);
    }

    private static bool IsValid(float? volts)
    {
        return volts is float value &&
               float.IsFinite(value) &&
               value > 0 &&
               value <= MaximumPlausibleCpuVoltage;
    }
}

