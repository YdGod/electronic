using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace BatteryPet
{
    internal interface ITemperatureProvider
    {
        string Id { get; }
        string Kind { get; } // CPU or GPU
        IEnumerable<TemperatureReading> Read(CancellationToken cancellation);
    }

    internal static class TemperatureProviderRegistry
    {
        // A new CPU/GPU backend only needs its own provider and one registration.
        internal static readonly IList<ITemperatureProvider> All = Array.AsReadOnly(new ITemperatureProvider[] {
            new CpuServiceTemperature(), new NvidiaTemperature()
        });
    }

    internal static class ThermalProvider
    {
        internal static readonly string CpuCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BatteryPet", "temperatures.json");
        internal static ThermalSnapshot Read(CancellationToken cancellation = default(CancellationToken))
        { return Collect(TemperatureProviderRegistry.All, cancellation); }

        internal static ThermalSnapshot Collect(IEnumerable<ITemperatureProvider> providers, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var result = new ThermalSnapshot();
            foreach (var provider in providers)
            {
                cancellation.ThrowIfCancellationRequested();
                // Collect into a temporary list: a failed provider cannot leave a partial snapshot.
                var batch = new List<TemperatureReading>();
                try
                {
                    foreach (var reading in provider.Read(cancellation) ?? Enumerable.Empty<TemperatureReading>())
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (reading == null || reading.Kind != provider.Kind) throw new InvalidDataException("传感器类型与提供器不匹配。");
                        if (batch.Count >= 64) throw new InvalidDataException("传感器数量超出限制。");
                        reading.Celsius = TemperatureRules.Valid(reading.Celsius);
                        if (reading.Celsius.HasValue && !TemperatureRules.Fresh(reading.ReadAt, DateTime.UtcNow))
                        { reading.Celsius = null; reading.Note = "传感器数据缺少有效采样时间或已过期。"; }
                        if (!reading.Celsius.HasValue) reading.ReadAt = null;
                        batch.Add(reading);
                    }
                    if (batch.Count == 0) batch.Add(Unknown(provider, "未读取到受支持的温度传感器。"));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLog.Write("温度提供器 " + provider.Id + "：" + ex.Message);
                    batch.Clear(); batch.Add(Unknown(provider, "温度读取失败：" + ex.Message));
                }
                result.Readings.AddRange(batch);
            }
            // Another backend may support the GPU for which NVML returned an unknown placeholder.
            var knownKinds = new HashSet<string>(result.Readings.Where(r => r.Celsius.HasValue).Select(r => r.Kind));
            result.Readings.RemoveAll(r => r.IsPlaceholder && !r.Celsius.HasValue && knownKinds.Contains(r.Kind));
            result.ReadAt = DateTime.UtcNow;
            return result;
        }

        private static TemperatureReading Unknown(ITemperatureProvider provider, string note)
        { return new TemperatureReading { Kind = provider.Kind, Name = provider.Kind == "GPU" ? "显卡" : "CPU", Sensor = provider.Kind + " 温度", Source = provider.Id, Note = note, IsPlaceholder = true }; }
    }
}
