using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32;

namespace BatteryPet
{
    internal sealed class EposGsp370Battery : IPhysicalBatteryAdapter
    {
        string IPhysicalBatteryAdapter.Id { get { return "gsp370"; } }
        bool IPhysicalBatteryAdapter.Supports(PnpNode node)
        {
            return node != null && node.Id != null && node.Id.StartsWith(@"USB\VID_1395&PID_00AB\", StringComparison.OrdinalIgnoreCase) && node.Name == "GSP 370";
        }
        DeviceBattery IPhysicalBatteryAdapter.Read(PnpNode node, DeviceScanContext context, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            return Read(node, context.Nodes, context.Map);
        }
        internal static int? UiPercentage(string text)
        {
            var match = Regex.Match(text ?? "", @"^\s*(\d{1,3})\s*%\s*$");
            int value; return match.Success && Int32.TryParse(match.Groups[1].Value, out value) ? BatteryRules.Percentage(value) : null;
        }
        internal static int? SerialValue(byte[] frame, byte command)
        {
            // NXP HCI Command Complete: packet 04, event 0E, opcode FCxx, status 00, value.
            return frame != null && frame.Length >= 8 && frame[0] == 4 && frame[1] == 0x0E && frame[2] >= 5 && frame.Length >= frame[2] + 3 && frame[4] == command && frame[5] == 0xFC && frame[6] == 0 ? (int?)frame[7] : null;
        }
        internal static byte[] SerialPacket(byte command)
        {
            if (command != 0x9A && command != 0x9C) throw new ArgumentException("Only headset state and battery queries are allowed.");
            return new byte[] { 1, command, 0xFC, 0 };
        }
        internal static DeviceBattery Read(PnpNode physical, List<PnpNode> nodes, Dictionary<string, PnpNode> map)
        {
            if (physical == null || physical.Id.IndexOf("VID_1395&PID_00AB", StringComparison.OrdinalIgnoreCase) < 0 || physical.Name != "GSP 370") return null;
            var direct = ReadDirect(physical, nodes, map);
            // A live offline response must not be replaced by a cached percentage from the UI.
            if (direct.Percent.HasValue || direct.Connected == false) return direct;
            return ReadOfficialUi() ?? direct;
        }
        internal static DeviceBattery ReadDirect(PnpNode physical, List<PnpNode> nodes, Dictionary<string, PnpNode> map)
        {
            var reading = new DeviceBattery { Name = "GSP 370", Kind = "耳机", Connection = "2.4 GHz 接收器", Source = "GSP 370 直接查询" };
            if (physical == null || physical.Id.IndexOf("VID_1395&PID_00AB", StringComparison.OrdinalIgnoreCase) < 0 || physical.Name != "GSP 370") { reading.Note = "未发现 GSP 370 接收器。"; return reading; }
            reading.Id = String.IsNullOrEmpty(physical.Container) ? physical.Id : physical.Container;
            var portNode = nodes.FirstOrDefault(n => n.Class == "Ports" && WindowsBatteryProvider.Ancestors(n, map).Any(a => a.Id == physical.Id));
            try
            {
                if (portNode != null)
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + portNode.Id + @"\Device Parameters"))
                    {
                        var name = key == null ? null : key.GetValue("PortName") as string;
                        if (name != null && Regex.IsMatch(name, @"^COM\d+$"))
                        {
                            var direct = ReadSerial(name);
                            direct.Id = reading.Id; direct.Kind = reading.Kind; direct.Connection = reading.Connection;
                            return direct;
                        }
                    }
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLog.Write("GSP 370 电量：" + ex.Message);
                reading.Note = "耳机串口被其他程序占用或访问受限；独立查询需要先停止 EPOS 后台服务。";
                return reading;
            }
            catch (Exception ex) { AppLog.Write("GSP 370 电量：" + ex.Message); }
            reading.Note = portNode == null ? "未发现接收器的 USB 串口接口。" : "接收器暂未返回有效电量；请确认耳机已开机，然后刷新。";
            return reading;
        }
        private static AutomationElement Find(AutomationElement root, string id)
        {
            return root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
        }
        private static DeviceBattery ReadOfficialUi()
        {
            foreach (var process in Process.GetProcessesByName("EPOSGamingSuite"))
                using (process)
                {
                    try
                    {
                        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
                        foreach (AutomationElement window in windows)
                            foreach (var pair in new[] { new[] { "miclblBatteryName", "MicrophoneBatteryPercentageLbl" }, new[] { "playbackBatteryName", "PlyabackBatteryPercentageLbl" } })
                            {
                                var label = Find(window, pair[0]);
                                if (label == null || !Regex.IsMatch(label.Current.Name ?? "", @"^GSP\s*370$", RegexOptions.IgnoreCase)) continue;
                                var level = Find(window, pair[1]);
                                int? percent = level == null ? null : UiPercentage(level.Current.Name);
                                if (!percent.HasValue) continue;
                                return new DeviceBattery { Name = "GSP 370", Kind = "耳机", Connection = "2.4 GHz 接收器", Percent = percent, Charging = null, Source = "EPOS Gaming Suite", Note = "读取官方软件显示的电量；更新时间由 EPOS 服务决定，充电状态未提供。" };
                            }
                    }
                    catch (Exception ex) { AppLog.Write("EPOS 界面电量：" + ex.Message); }
                }
            return null;
        }
        private static byte[] Query(SerialPort port, byte command)
        {
            var request = SerialPacket(command);
            port.Write(request, 0, request.Length);
            var received = new List<byte>(); var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 1500)
            {
                int value;
                try { value = port.ReadByte(); } catch (TimeoutException) { continue; }
                received.Add((byte)value);
                while (received.Count >= 3)
                {
                    if (received[0] != 4) { received.RemoveAt(0); continue; }
                    int length = 3 + received[2];
                    if (received.Count < length) break;
                    var frame = received.Take(length).ToArray(); received.RemoveRange(0, length);
                    if (SerialValue(frame, command).HasValue) return frame;
                }
                if (received.Count > 512) received.Clear();
            }
            throw new TimeoutException("耳机未返回有效电量。");
        }
        private static DeviceBattery ReadSerial(string name)
        {
            // Official OpenNXPDevice configures 921600 baud, 8 data bits, no parity, one stop bit.
            using (var port = new SerialPort(name, 921600, Parity.None, 8, StopBits.One) { ReadTimeout = 150, WriteTimeout = 600, DtrEnable = true, RtsEnable = true })
            {
                port.Open();
                Thread.Sleep(300);
                var reading = new DeviceBattery { Name = "GSP 370", Source = "GSP 370 直接查询", Charging = null };
                var state = SerialValue(Query(port, 0x9A), 0x9A);
                if (state == 0) { reading.Connected = false; reading.Note = "耳机当前未连接接收器。"; return reading; }
                if (state != 1 && state != 2) { reading.Note = "耳机返回了未知连接状态。"; return reading; }
                reading.Connected = true;
                var level = SerialValue(Query(port, 0x9C), 0x9C);
                reading.Percent = level.HasValue ? BatteryRules.Percentage(level.Value) : null;
                if (reading.Percent.HasValue) reading.ReadAt = DateTime.Now;
                reading.Note = "直接查询耳机接收器，无需 EPOS 软件运行；此接口未提供充电状态。"; return reading;
            }
        }
    }
}
