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
    public sealed class WindowsBatteryProvider
    {
        public ScanResult Scan(bool includeUsb, CancellationToken cancellation = default(CancellationToken))
        {
            cancellation.ThrowIfCancellationRequested();
            var result = new ScanResult();
            var nodes = Native.Nodes();
            var map = nodes.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
            var readings = new Dictionary<string, DeviceBattery>(StringComparer.OrdinalIgnoreCase);
            var bluetoothConnections = BluetoothConnections.Read();
            var profiles = DeviceProfile.Load();
            var identities = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Native.Interfaces(new Guid("781aee18-7733-4ce4-add0-91f41c67b592")))
            {
                cancellation.ThrowIfCancellationRequested();
                try { var reading = BleBattery.Read(item.Path); if (reading != null) readings[item.InstanceId] = reading; }
                catch (Exception ex) { AppLog.Write("蓝牙读取：" + ex.Message); }
            }
            if (includeUsb)
            {
                foreach (var item in Native.Interfaces(new Guid("4d1e55b2-f16f-11cf-88cb-001111000030")))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        PnpNode node;
                        string product = HidBattery.ProductName(item.Path);
                        if (map.TryGetValue(item.InstanceId, out node) && BatteryRules.NameScore(product) > BatteryRules.NameScore(node.Name)) node.Name = product;
                        var descriptor = HidDescriptor.Read(item);
                        var reading = DeviceProviderRegistry.Default.ReadHid(descriptor, item.Path, cancellation);
                        if (reading != null)
                        {
                            readings[item.InstanceId] = reading;
                            if (map.TryGetValue(item.InstanceId, out node))
                            {
                                var physical = Ancestors(node, map).LastOrDefault();
                                if (physical != null && physical.Id.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase)) readings[physical.Id] = reading;
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { AppLog.Write("HID 读取：" + ex.Message); }
                }
                var context = new DeviceScanContext(nodes, map);
                foreach (var physical in nodes.Where(n => n.Id.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase) && n.Id.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var reading = DeviceProviderRegistry.Default.ReadPhysical(physical, context, cancellation);
                    if (reading != null) readings[physical.Id] = reading;
                }
            }
            foreach (var node in nodes)
            {
                cancellation.ThrowIfCancellationRequested();
                var chain = Ancestors(node, map);
                bool bluetooth = chain.Any(n => n.Id.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase) || n.Id.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase) || n.Id.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase));
                bool usb = chain.Any(n => n.Id.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase));
                bool peripheral = node.Class == "Mouse" || node.Class == "Keyboard" || node.Class == "AudioEndpoint" || node.Class == "MEDIA";
                bool batteryKnown = chain.Any(n => n.Battery.HasValue || readings.ContainsKey(n.Id));
                if ((!bluetooth && !(includeUsb && usb)) || (!peripheral && !batteryKnown)) continue;
                // Ignore USB Bluetooth radios and generic service nodes unless they expose a battery.
                if (node.Class == "Bluetooth" && !batteryKnown) continue;
                if (!peripheral && nodes.Any(n => n.Container == node.Container && (n.Class == "Mouse" || n.Class == "Keyboard" || n.Class == "AudioEndpoint" || n.Class == "MEDIA"))) continue;
                var named = chain.Where(n => !n.Id.StartsWith("USB\\ROOT", StringComparison.OrdinalIgnoreCase) && !n.Name.ToLowerInvariant().Contains("hub") && n.Class != "Bluetooth" && n.Class != "System").OrderByDescending(n => BatteryRules.NameScore(n.Name)).FirstOrDefault() ?? node;
                var container = chain.Select(n => n.Container).FirstOrDefault(c => !String.IsNullOrWhiteSpace(c) && c != Guid.Empty.ToString());
                var device = new DeviceBattery { Id = String.IsNullOrWhiteSpace(container) ? node.Id : container, Name = named.Name, Kind = BatteryRules.Kind(node.Class, named.Name), Connection = bluetooth ? "蓝牙" : "USB · 无线待确认", Note = bluetooth ? "Windows 尚未提供电量；设备可能未连接或不支持标准电池服务。" : "USB 接口无法判断有线或无线；专用接收器通常需要厂商电量协议。" };
                foreach (var ancestor in chain)
                {
                    DeviceBattery reading;
                    if (readings.TryGetValue(ancestor.Id, out reading))
                    {
                        if (BatteryRules.NameScore(reading.Name) > BatteryRules.NameScore(device.Name)) device.Name = reading.Name;
                        if (!String.IsNullOrWhiteSpace(reading.Kind)) device.Kind = reading.Kind;
                        if (!String.IsNullOrWhiteSpace(reading.Connection) && reading.Connection != "USB / 接收器") device.Connection = reading.Connection;
                        device.Percent = reading.Percent; device.Charging = reading.Charging; device.Source = reading.Source; device.ReadAt = reading.ReadAt; device.Connected = reading.Connected; device.Note = reading.Note;
                        break;
                    }
                    if (!device.Percent.HasValue && ancestor.Battery.HasValue) { device.Percent = ancestor.Battery; device.Source = "Windows 电池属性"; device.ReadAt = DateTime.Now; device.Note = "系统缓存电量；充电状态未提供，电量更新时间取决于设备驱动。"; }
                }
                if (String.IsNullOrWhiteSpace(device.Name)) device.Name = "未命名" + device.Kind;
                HashSet<string> identity;
                if (!identities.TryGetValue(device.Id, out identity)) { identity = new HashSet<string>(StringComparer.OrdinalIgnoreCase); identities[device.Id] = identity; }
                foreach (var ancestor in chain) identity.Add(ancestor.Id);
                if (bluetooth && device.Connected != true)
                {
                    bool connected;
                    if (BluetoothConnections.TryMatch(chain.Select(n => n.Id), bluetoothConnections, out connected))
                    {
                        device.Connected = connected;
                        if (!connected) { device.Percent = null; device.Charging = null; device.ReadAt = null; device.Note = "设备已配对但当前未连接。"; }
                    }
                }
                result.Devices.Add(device);
            }
            result.Devices = BatteryRules.Merge(result.Devices);
            // A single USB peripheral often exposes extra keyboard/mouse collections: one row per physical receiver.
            var usbGroups = result.Devices.Where(d => d.Connection.StartsWith("USB")).GroupBy(d => d.Id).ToList();
            foreach (var group in usbGroups)
            {
                if (group.Count() <= 1) continue;
                var best = group.OrderByDescending(d => d.Percent.HasValue).ThenByDescending(d => BatteryRules.NameScore(d.Name)).First();
                best.Kind = String.Join(" / ", group.Select(d => d.Kind).Distinct());
                result.Devices.RemoveAll(d => d.Id == group.Key && d != best);
            }
            foreach (var device in result.Devices)
            {
                HashSet<string> identity;
                if (!identities.TryGetValue(device.Id, out identity)) continue;
                var profile = profiles.FirstOrDefault(p => p.Matches(device.Name, identity));
                if (profile == null) continue;
                // Human-confirmed model mapping changes identity only, never battery data.
                if (!String.IsNullOrWhiteSpace(profile.Name)) device.Name = profile.Name;
                if (!String.IsNullOrWhiteSpace(profile.Kind)) device.Kind = profile.Kind;
                device.Connection = String.IsNullOrWhiteSpace(profile.Connection) ? "2.4 GHz 接收器" : profile.Connection;
                if (!device.Percent.HasValue && device.Source == "Windows") device.Note = "型号由用户确认；此接收器的专用电量协议尚未适配。";
            }
            result.Devices = result.Devices.OrderBy(d => d.Kind == "鼠标" ? 0 : d.Kind == "键盘" ? 1 : d.Kind == "耳机" ? 2 : 3).ToList();
            result.ScannedAt = DateTime.Now;
            return result;
        }
        internal static List<PnpNode> Ancestors(PnpNode node, Dictionary<string, PnpNode> map)
        {
            var result = new List<PnpNode>(); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (node != null && visited.Add(node.Id) && result.Count < 20)
            {
                result.Add(node); PnpNode parent;
                // Stop at the physical USB peripheral, before host controllers and hubs.
                if (node.Id.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase) && node.Id.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase) < 0) break;
                node = !String.IsNullOrEmpty(node.Parent) && map.TryGetValue(node.Parent, out parent) ? parent : null;
            }
            return result;
        }
    }
}
