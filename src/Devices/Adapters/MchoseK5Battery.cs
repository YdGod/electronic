using System;
using System.Linq;

namespace BatteryPet
{
    internal sealed class MchoseK5Battery : IHidBatteryAdapter
    {
        string IHidBatteryAdapter.Id { get { return "MchoseK5Battery"; } }
        bool IHidBatteryAdapter.Supports(HidDescriptor descriptor) { return Supports(descriptor); }
        DeviceBattery IHidBatteryAdapter.Read(HidDescriptor descriptor, System.Threading.CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return Read(descriptor);
        }
        private const byte ReportId = 0x4D;
        private const string Source = "MCHOSE 官方 HID 协议";

        internal static bool Supports(HidDescriptor descriptor)
        {
            return descriptor != null && descriptor.InstanceId != null
                && descriptor.InstanceId.IndexOf("VID_3837&PID_1018&", StringComparison.OrdinalIgnoreCase) >= 0
                && descriptor.Product == "MCHOSE K5 Ultra"
                && descriptor.UsagePage == 0xFF01 && descriptor.Usage == 1
                && descriptor.InputLength == 64 && descriptor.OutputLength == 64 && descriptor.FeatureLength == 64
                && descriptor.InputIds.Contains(ReportId) && descriptor.OutputIds.Contains(ReportId);
        }

        internal static byte[] Packet()
        {
            // Official a1e/ugt: device information 0x0900, no payload.
            // Only this fixed read request can be emitted; settings and firmware commands are absent.
            var report = new byte[64];
            report[0] = ReportId; report[1] = 1; report[2] = 1; report[5] = 9;
            report[8] = 8; // XOR of bytes 2 through 7.
            return report;
        }

        internal static DeviceBattery Parse(byte[] report)
        {
            if (report == null || report.Length != 64 || report[0] != ReportId || report[1] != 1 || report[2] != 1
                || report[3] < 14 || report[3] > 55 || report[4] != 0 || report[5] != 9 || report[6] != 0 || report[7] != 0) return null;
            int checksumIndex = 8 + report[3]; byte checksum = 0;
            for (int i = 2; i < checksumIndex; i++) checksum ^= report[i];
            if (report[checksumIndex] != checksum) return null;
            // The shared receiver must report the K5 Ultra's own VID/PID (3837:4038).
            if (report[8] != 0x37 || report[9] != 0x38 || report[10] != 0x38 || report[11] != 0x40) return null;
            var reading = new DeviceBattery { Name = "MCHOSE K5 Ultra", Kind = "鼠标", Connection = "2.4 GHz 接收器", Source = Source };
            // 2 is the live wireless state observed on this model. Other states are unverified.
            if (report[18] != 2) { reading.Note = "接收器未确认 K5 Ultra 在线；请唤醒鼠标后刷新。"; return reading; }
            reading.Connected = true;
            reading.Percent = BatteryRules.Percentage(report[20]);
            reading.Charging = report[19] == 1 ? (bool?)true : report[19] == 0 ? (bool?)false : null;
            if (reading.Percent.HasValue) reading.ReadAt = DateTime.Now;
            reading.Note = reading.Percent.HasValue ? "通过迈从 K5 Ultra 官方设备信息指令即时读取。" : "鼠标返回的电量无效。";
            return reading;
        }

        internal static DeviceBattery Read(HidDescriptor descriptor)
        {
            if (!Supports(descriptor)) return null;
            var unknown = new DeviceBattery { Name = descriptor.Product, Kind = "鼠标", Source = Source, Note = "接收器暂未响应；请唤醒鼠标后刷新。" };
            try
            {
                using (var channel = new HidQueryChannel(descriptor))
                {
                    var response = channel.Query(Packet(), r => r.Length == 64 && r[0] == ReportId && r[4] == 0 && r[5] == 9, 2500).GetAwaiter().GetResult();
                    var reading = Parse(response);
                    if (reading != null) return reading;
                    unknown.Note = "接收器返回的 K5 Ultra 响应未通过校验；请稍后刷新。";
                }
            }
            catch (Exception ex) { AppLog.Write("K5 Ultra 电量：" + ex.Message); }
            return unknown;
        }
    }
}
