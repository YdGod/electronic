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
    internal sealed class SknReceiverInfo : IHidBatteryAdapter
    {
        string IHidBatteryAdapter.Id { get { return "SknReceiverInfo"; } }
        bool IHidBatteryAdapter.Supports(HidDescriptor descriptor) { return Supports(descriptor); }
        DeviceBattery IHidBatteryAdapter.Read(HidDescriptor descriptor, System.Threading.CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return ReadStatus(descriptor);
        }
        internal static bool Supports(HidDescriptor d)
        {
            return d != null && d.InstanceId.IndexOf("VID_362D&PID_D028", StringComparison.OrdinalIgnoreCase) >= 0 && d.Product == "Ultra-Link 8K" && d.UsagePage == 0xFF60 && d.Usage == 0x61 && d.InputLength == 33 && d.OutputLength == 33 && d.InputIds.SequenceEqual(new byte[] { 0 }) && d.OutputIds.SequenceEqual(new byte[] { 0 });
        }
        internal static bool? QingLongConnected(byte[] report)
        {
            if (report == null || report.Length != 33 || report[0] != 0 || report[1] != 0xB2) return null;
            for (int i = 0; i < 3; i++)
            {
                int offset = 3 + i * 5;
                if (report[offset] == 0x2D && report[offset + 1] == 0x36 && report[offset + 2] == 0x2B && report[offset + 3] == 0x13) return report[offset + 4] == 1 ? (bool?)true : report[offset + 4] == 0 ? (bool?)false : null;
            }
            return null;
        }
        internal static DeviceBattery ReadStatus(HidDescriptor descriptor)
        {
            if (!Supports(descriptor)) return null;
            var reading = new DeviceBattery { Name = descriptor.Product, Source = "SKN 接收器状态", Note = "该型号通过 FN+B 灯光查看电量，尚未找到向电脑提供百分比的接口。" };
            try
            {
                using (var channel = new HidQueryChannel(descriptor))
                {
                    var request = new byte[33]; request[1] = 0xB2;
                    var response = channel.Query(request, r => r[0] == 0 && r[1] == 0xB2, 1500).GetAwaiter().GetResult();
                    reading.Connected = QingLongConnected(response);
                    reading.Note = reading.Connected == false ? "键盘当前未连接接收器或已休眠。" : "该型号通过 FN+B 灯光查看电量，尚未找到向电脑提供百分比的接口。";
                }
            }
            catch (Exception ex) { AppLog.Write("SKN 接收器：" + ex.Message); }
            return reading;
        }
        internal static Dictionary<string, string> Read()
        {
            var result = new Dictionary<string, string>();
            var descriptor = HidDescriptor.Inventory().FirstOrDefault(Supports);
            if (descriptor == null) { result["error"] = "SKN receiver not found."; return result; }
            using (var channel = new HidQueryChannel(descriptor))
            {
                foreach (byte command in new byte[] { 0xB2, 0x01, 0xA0 })
                {
                    // Confirmed read-only receiver state / VIA version / vendor protocol version.
                    var report = new byte[33]; report[1] = command;
                    try { var response = channel.Query(report, r => r[0] == 0 && r[1] == command, 1500).GetAwaiter().GetResult(); result[command.ToString("X2")] = BitConverter.ToString(response); }
                    catch (Exception ex) { result[command.ToString("X2")] = ex.Message; }
                }
            }
            return result;
        }
    }
}
