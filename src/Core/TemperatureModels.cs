using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace BatteryPet
{
    public static class CpuTemperatureProtocol
    {
        public const string PipeName = "BatteryPet.CpuTemperature.v2";
        public const int MaximumResponseBytes = 65536;
        public static bool IsReadRequest(byte[] value)
        { return value != null && value.Length == 4 && value[0] == 82 && value[1] == 69 && value[2] == 65 && value[3] == 68; }
    }
    [DataContract]
    public sealed class TemperatureReading
    {
        // Runtime-only marker for an unsupported backend, not an individual failed sensor.
        internal bool IsPlaceholder { get; set; }
        [DataMember] public string Kind;
        [DataMember] public string Name;
        [DataMember] public string Sensor;
        [DataMember] public double? Celsius;
        [DataMember] public string Source;
        [DataMember] public string Note;
        [DataMember] public DateTime? ReadAt;
        public string DisplayValue { get { return Celsius.HasValue ? Celsius.Value.ToString("0", CultureInfo.InvariantCulture) + "°C" : "—"; } }
    }

    [DataContract]
    public sealed class ThermalSnapshot
    {
        [DataMember] public DateTime ReadAt = DateTime.UtcNow;
        [DataMember] public List<TemperatureReading> Readings = new List<TemperatureReading>();
    }

    public static class TemperatureRules
    {
        public static double? Valid(double? value)
        {
            return value.HasValue && !Double.IsNaN(value.Value) && !Double.IsInfinity(value.Value) && value.Value >= 0 && value.Value <= 150 ? value : null;
        }
        public static bool Fresh(DateTime? readAt, DateTime now)
        {
            if (!readAt.HasValue) return false;
            double age = (now.ToUniversalTime() - readAt.Value.ToUniversalTime()).TotalSeconds;
            return age >= -5 && age <= 15;
        }
        public static TemperatureReading SelectCpu(string name, IEnumerable<TemperatureReading> sensors)
        {
            var values = sensors.Where(s => Valid(s.Celsius).HasValue && !(s.Sensor ?? "").Contains("Distance to TjMax")).ToList();
            var chosen = values.FirstOrDefault(s => s.Sensor == "CPU Package" || s.Sensor == "CPU (Tctl/Tdie)" || s.Sensor == "CPU (Tdie)")
                ?? values.Where(s => s.Sensor == "Core Max" || (s.Sensor ?? "").StartsWith("CPU Core") || (s.Sensor ?? "").StartsWith("P-Core #") || (s.Sensor ?? "").StartsWith("E-Core #")).OrderByDescending(s => s.Celsius).FirstOrDefault();
            return new TemperatureReading {
                Kind = "CPU", Name = name, Celsius = chosen == null ? null : chosen.Celsius,
                Sensor = chosen == null ? "CPU 温度" : chosen.Sensor,
                Source = "LibreHardwareMonitor 0.9.6 / PawnIO",
                Note = chosen == null ? "未读取到 CPU 温度；请启用 CPU 温度采集。" : chosen.Sensor == "CPU Package" ? "CPU 封装温度" : chosen.Sensor.Contains("Core") ? "核心最高温度" : "CPU 温度",
                ReadAt = chosen == null ? null : (DateTime?)DateTime.UtcNow
            };
        }
    }
}
