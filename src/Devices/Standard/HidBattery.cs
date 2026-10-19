using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BatteryPet
{
    internal static class HidBattery
    {
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr pointer);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr pointer);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr pointer, byte[] caps);
        [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int type, IntPtr caps, ref ushort count, IntPtr pointer);
        [DllImport("hid.dll")] private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] report, uint length);
        [DllImport("hid.dll")] private static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] text, uint length);
        [DllImport("hid.dll")] private static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr pointer, byte[] report, uint length);
        private const int Success = 0x00110000;
        internal static string ProductName(string path)
        {
            using (var handle = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) return "";
                var text = new byte[512]; return HidD_GetProductString(handle, text, (uint)text.Length) ? Encoding.Unicode.GetString(text).TrimEnd('\0') : "";
            }
        }
        internal static DeviceBattery Read(string path)
        {
            // Standard feature reports only. No vendor commands, output reports or settings changes.
            using (var handle = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) return null;
                IntPtr parsed;
                if (!HidD_GetPreparsedData(handle, out parsed)) return null;
                try
                {
                    var caps = new byte[64];
                    if (HidP_GetCaps(parsed, caps) != Success) return null;
                    ushort length = BitConverter.ToUInt16(caps, 8), count = BitConverter.ToUInt16(caps, 60);
                    if (length < 2 || length > 4096 || count == 0 || count > 512) return null;
                    var values = Marshal.AllocHGlobal(count * 72);
                    try
                    {
                        if (HidP_GetValueCaps(2, values, ref count, parsed) != Success) return null;
                        int? percent = null; bool? charging = null;
                        var reports = new Dictionary<byte, byte[]>();
                        for (int i = 0; i < count; i++)
                        {
                            var cap = IntPtr.Add(values, i * 72);
                            ushort page = (ushort)Marshal.ReadInt16(cap, 0), link = (ushort)Marshal.ReadInt16(cap, 6), min = (ushort)Marshal.ReadInt16(cap, 56);
                            ushort max = Marshal.ReadByte(cap, 12) != 0 ? (ushort)Marshal.ReadInt16(cap, 58) : min;
                            ushort usage = page == 0x06 && min <= 0x20 && max >= 0x20 ? (ushort)0x20 : page == 0x85 && min <= 0x64 && max >= 0x64 ? (ushort)0x64 : page == 0x85 && min <= 0x44 && max >= 0x44 ? (ushort)0x44 : (ushort)0;
                            if (usage == 0) continue;
                            byte reportId = Marshal.ReadByte(cap, 2); byte[] report;
                            if (!reports.TryGetValue(reportId, out report))
                            {
                                report = new byte[length]; report[0] = reportId;
                                reports[reportId] = HidD_GetFeature(handle, report, length) ? report : null;
                            }
                            if (report == null) continue;
                            uint value;
                            if (HidP_GetUsageValue(2, page, link, usage, out value, parsed, report, length) != Success) continue;
                            if (usage == 0x44 && page == 0x85) { if (value <= 1) charging = value != 0; }
                            else
                            {
                                int logicalMin = Marshal.ReadInt32(cap, 40), logicalMax = Marshal.ReadInt32(cap, 44);
                                // Device Controls Battery Strength is normalized only when its descriptor declares bounds.
                                if (page == 0x06 && logicalMin == 0 && logicalMax > 0 && value <= logicalMax) percent = BatteryRules.Percentage((long)Math.Round(value * 100.0 / logicalMax));
                                else percent = BatteryRules.Percentage(value);
                            }
                        }
                        if (!percent.HasValue) return null;
                        var text = new byte[512]; var name = HidD_GetProductString(handle, text, (uint)text.Length) ? Encoding.Unicode.GetString(text).TrimEnd('\0') : "";
                        return new DeviceBattery { Name = name, Percent = percent, Charging = charging, Source = "USB HID", Connected = true, ReadAt = DateTime.Now, Note = charging.HasValue ? "来自 HID 标准电池特征报告。" : "来自 HID 标准电池特征报告；设备未提供充电状态。" };
                    }
                    finally { Marshal.FreeHGlobal(values); }
                }
                finally { HidD_FreePreparsedData(parsed); }
            }
        }
    }
}
