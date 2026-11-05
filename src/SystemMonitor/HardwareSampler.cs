using LibreHardwareMonitor.Hardware;

namespace SystemMonitor;

public readonly record struct HardwareReading(float? CpuLoad, float? CpuTemp, float? GpuLoad, float? GpuTemp);

/// <summary>Polls CPU and NVIDIA GPU load/temperature through LibreHardwareMonitor.</summary>
public sealed class HardwareSampler : IDisposable
{
    private readonly Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };

    public HardwareSampler() => _computer.Open();

    public HardwareReading Read()
    {
        float? cpuLoad = null, cpuTemp = null, gpuLoad = null, gpuTemp = null;

        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    cpuLoad ??= Find(hw, SensorType.Load, "CPU Total");
                    cpuTemp ??= Find(hw, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core Max", "CPU Total");
                    break;
                case HardwareType.GpuNvidia:
                    gpuLoad ??= Find(hw, SensorType.Load, "GPU Core");
                    gpuTemp ??= Find(hw, SensorType.Temperature, "GPU Core");
                    break;
            }
        }

        // Without the SMU driver the AMD temperature sensor reports 0 instead of null.
        if (cpuTemp is <= 0f) cpuTemp = null;

        return new HardwareReading(cpuLoad, cpuTemp, gpuLoad, gpuTemp);
    }

    // First sensor of the given type matching a preferred name, in priority order.
    private static float? Find(IHardware hw, SensorType type, params string[] names)
    {
        foreach (var name in names)
        {
            var sensor = hw.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name);
            if (sensor?.Value is float v) return v;
        }
        return null;
    }

    public void Dispose() => _computer.Close();
}
