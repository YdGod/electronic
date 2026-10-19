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
    internal sealed class CpuServiceTemperature : ITemperatureProvider
    {
        public string Id { get { return "cpu-service"; } }
        public string Kind { get { return "CPU"; } }
        public IEnumerable<TemperatureReading> Read(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return new[] { ReadCpu() };
        }
        private static TemperatureReading ReadCpu()
        {
            string name = "CPU";
            try { using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) if (key != null) name = Convert.ToString(key.GetValue("ProcessorNameString")).Trim(); } catch { }
            var unknown = new TemperatureReading { Kind = "CPU", Name = name, Sensor = "CPU 温度", Source = "LibreHardwareMonitor / PawnIO", Note = "请运行 Enable-Cpu-Temperature.cmd 启用 CPU 温度采集。" };
            try
            {
                ThermalSnapshot cached;
                // Ask for a new sample every time. An idle service performs no sensor updates.
                using (var pipe = new NamedPipeClientStream(".", CpuTemperatureProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(1500);
                    var request = new byte[] { 82, 69, 65, 68 }; pipe.Write(request, 0, request.Length);
                    var size = new byte[4]; ReadExactly(pipe, size);
                    int length = BitConverter.ToInt32(size, 0);
                    if (length <= 0 || length > CpuTemperatureProtocol.MaximumResponseBytes) throw new IOException("无效的 CPU 温度响应长度。");
                    var response = new byte[length]; ReadExactly(pipe, response);
                    using (var stream = new MemoryStream(response)) cached = (ThermalSnapshot)new DataContractJsonSerializer(typeof(ThermalSnapshot)).ReadObject(stream);
                }
                if (cached == null || cached.Readings == null) return unknown;
                foreach (var reading in cached.Readings)
                {
                    if (reading.Kind != "CPU") continue;
                    if (reading.Celsius.HasValue && (!TemperatureRules.Fresh(cached.ReadAt, DateTime.UtcNow) || !TemperatureRules.Fresh(reading.ReadAt, DateTime.UtcNow)))
                    { unknown.Note = "CPU 温度采集已停止或数据已过期。"; return unknown; }
                    reading.Celsius = TemperatureRules.Valid(reading.Celsius);
                    return reading;
                }
            }
            catch (TimeoutException) { unknown.Note = "CPU 采集服务未响应；请运行 Enable-Cpu-Temperature.cmd 更新或启用服务。"; }
            catch (Exception ex) { unknown.Note = "CPU 温度读取失败：" + ex.Message; }
            return unknown;
        }
        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            int offset = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
            while (offset < buffer.Length)
            {
                var read = stream.ReadAsync(buffer, offset, buffer.Length - offset);
                int remaining = Math.Max(1, 5000 - (int)clock.ElapsedMilliseconds);
                if (!read.Wait(remaining)) throw new TimeoutException("CPU 采样超时。");
                int count = read.GetAwaiter().GetResult(); if (count == 0) throw new IOException("CPU 服务中断了响应。");
                offset += count;
            }
        }
    }
}
