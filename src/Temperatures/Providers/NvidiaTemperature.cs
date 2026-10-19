using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace BatteryPet
{
    internal sealed class NvidiaTemperature : ITemperatureProvider
    {
        public string Id { get { return "nvidia-nvml"; } }
        public string Kind { get { return "GPU"; } }
        IEnumerable<TemperatureReading> ITemperatureProvider.Read(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return Read();
        }
        // NVML is installed with the NVIDIA driver. It needs no administrator rights.
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlInit_v2();
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlShutdown();
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetCount_v2(out uint count);
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetTemperature(IntPtr device, uint sensorType, out uint temperature);
        private static readonly object Gate = new object();
        internal static List<TemperatureReading> Read()
        {
            var readings = new List<TemperatureReading>(); bool opened = false; string failure = "当前显卡驱动未提供受支持的温度接口。";
            lock (Gate)
            {
                try
                {
                    int status = nvmlInit_v2();
                    if (status != 0) throw new InvalidOperationException("NVML 初始化返回 " + status);
                    opened = true; uint count;
                    status = nvmlDeviceGetCount_v2(out count);
                    if (status != 0) throw new InvalidOperationException("NVML 设备查询返回 " + status);
                    for (uint i = 0; i < Math.Min(count, 16); i++)
                    {
                        IntPtr handle;
                        if (nvmlDeviceGetHandleByIndex_v2(i, out handle) != 0) continue;
                        var name = new StringBuilder(128); nvmlDeviceGetName(handle, name, 128);
                        uint value; status = nvmlDeviceGetTemperature(handle, 0, out value);
                        double? valid = status == 0 ? TemperatureRules.Valid(value) : null;
                        readings.Add(new TemperatureReading { Kind = "GPU", Name = name.Length > 0 ? name.ToString() : "NVIDIA GPU " + i, Sensor = "GPU Core", Celsius = valid, Source = "NVIDIA NVML", Note = valid.HasValue ? "显卡核心温度" : "显卡温度暂不可用（NVML " + status + "）", ReadAt = valid.HasValue ? (DateTime?)DateTime.UtcNow : null });
                    }
                }
                catch (DllNotFoundException) { }
                catch (Exception ex) { failure = "显卡温度读取失败：" + ex.Message; }
                finally { if (opened) { try { nvmlShutdown(); } catch { } } }
            }
            if (readings.Count == 0) readings.Add(new TemperatureReading { Kind = "GPU", Name = "显卡", Sensor = "显卡温度", Source = "NVIDIA NVML", Note = failure, IsPlaceholder = true });
            return readings;
        }
    }
}
