using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace BatteryPet
{
    internal sealed class ScyroxV12Battery : IHidBatteryAdapter
    {
        string IHidBatteryAdapter.Id { get { return "ScyroxV12Battery"; } }
        bool IHidBatteryAdapter.Supports(HidDescriptor descriptor) { return Supports(descriptor); }
        DeviceBattery IHidBatteryAdapter.Read(HidDescriptor descriptor, System.Threading.CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return Read(descriptor);
        }
        // Official V12 driver: commands 0x2A=connection, 0x0E=battery, 0x20=charging.
        // WebHID strips the report ID; the native 64-byte report includes it.
        internal static byte[] Packet(byte reportId, byte command)
        {
            if (command != 0x2A && command != 0x0E && command != 0x20) throw new ArgumentException("Only battery queries are allowed.");
            var report = new byte[64]; report[0] = reportId; report[1] = command; report[63] = (byte)((reportId + command) & 255); return report;
        }
        internal static int? Value(byte[] report, byte reportId, byte command)
        {
            return report != null && report.Length == 64 && report[0] == reportId && report[1] == command && report[62] == 0 ? (int?)report[4] : null;
        }
        internal static bool Supports(HidDescriptor descriptor)
        {
            if (descriptor == null || descriptor.UsagePage != 0xFF00 || descriptor.InputLength != 64 || descriptor.OutputLength != 64 || !descriptor.InputIds.SequenceEqual(new byte[] { 8 }) || !descriptor.OutputIds.SequenceEqual(new byte[] { 8 })) return false;
            return descriptor.InstanceId.IndexOf("VID_395A&PID_0006", StringComparison.OrdinalIgnoreCase) >= 0 && descriptor.Product == "V12M RECEIVER" && descriptor.Usage == 2
                || descriptor.InstanceId.IndexOf("VID_395A&PID_0005", StringComparison.OrdinalIgnoreCase) >= 0 && descriptor.Product == "V12M" && descriptor.Usage == 0 && descriptor.FeatureLength == 64;
        }
        internal static DeviceBattery Read(HidDescriptor descriptor)
        {
            if (!Supports(descriptor)) return null;
            var reading = new DeviceBattery { Name = descriptor.Product, Source = "Scyrox 官方 HID 协议", Note = "接收器暂未响应；请唤醒鼠标后刷新。" };
            byte reportId = descriptor.OutputIds[0];
            try
            {
                using (var channel = new HidQueryChannel(descriptor))
                {
                    Func<byte, byte[]> query = delegate(byte command) { return channel.Query(Packet(reportId, command), r => Value(r, descriptor.InputIds[0], command).HasValue, 1800).GetAwaiter().GetResult(); };
                    var connection = Value(query(0x2A), descriptor.InputIds[0], 0x2A);
                    if (connection == 0) { reading.Connected = false; reading.Note = "鼠标当前未连接接收器或已休眠。"; return reading; }
                    if (connection != 1) { reading.Note = "接收器返回了未识别的连接状态。"; return reading; }
                    reading.Connected = true;
                    var percent = Value(query(0x0E), descriptor.InputIds[0], 0x0E);
                    reading.Percent = percent.HasValue ? BatteryRules.Percentage(percent.Value) : null;
                    if (reading.Percent.HasValue) reading.ReadAt = DateTime.Now;
                    var charge = Value(query(0x20), descriptor.InputIds[0], 0x20);
                    reading.Charging = charge == 1 ? (bool?)true : charge == 0 ? (bool?)false : null;
                    reading.Note = reading.Percent.HasValue ? "通过赛洛斯 V12 官方协议实时读取。" : "鼠标返回的电量无效。";
                }
            }
            catch (Exception ex) { reading.Note = reading.Percent.HasValue ? "电量已读取，充电状态查询失败。" : "接收器未响应电量查询；请唤醒鼠标后刷新。"; AppLog.Write("V12 电量：" + ex.Message); }
            return reading;
        }
    }
}
