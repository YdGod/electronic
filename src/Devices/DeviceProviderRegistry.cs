using System;
using System.Collections.Generic;
using System.Threading;

namespace BatteryPet
{
    // Supports must only inspect identity/capabilities. It must never send a packet.
    internal interface IHidBatteryAdapter
    {
        string Id { get; }
        bool Supports(HidDescriptor descriptor);
        DeviceBattery Read(HidDescriptor descriptor, CancellationToken cancellation);
    }

    internal interface IPhysicalBatteryAdapter
    {
        string Id { get; }
        bool Supports(PnpNode device);
        DeviceBattery Read(PnpNode device, DeviceScanContext context, CancellationToken cancellation);
    }

    internal sealed class DeviceScanContext
    {
        internal readonly List<PnpNode> Nodes;
        internal readonly Dictionary<string, PnpNode> Map;
        internal DeviceScanContext(List<PnpNode> nodes, Dictionary<string, PnpNode> map)
        { Nodes = nodes; Map = map; }
    }

    internal sealed class DeviceProviderRegistry
    {
        // Register a contributed adapter here, before the standard HID fallback.
        internal static readonly DeviceProviderRegistry Default = new DeviceProviderRegistry(
            new IHidBatteryAdapter[] { new ScyroxV12Battery(), new MchoseK5Battery(), new SknReceiverInfo() },
            new IPhysicalBatteryAdapter[] { new EposGsp370Battery() });

        private readonly IEnumerable<IHidBatteryAdapter> hid;
        private readonly IEnumerable<IPhysicalBatteryAdapter> physical;
        internal DeviceProviderRegistry(IEnumerable<IHidBatteryAdapter> hid, IEnumerable<IPhysicalBatteryAdapter> physical)
        { this.hid = hid; this.physical = physical; }

        internal DeviceBattery ReadHid(HidDescriptor descriptor, string path, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var adapter in hid)
            {
                cancellation.ThrowIfCancellationRequested();
                try { if (!adapter.Supports(descriptor)) continue; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { AppLog.Write("设备匹配 " + adapter.Id + "：" + ex.Message); continue; }
                try
                {
                    var reading = adapter.Read(descriptor, cancellation);
                    if (reading != null) return Normalize(reading);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return Failure(adapter.Id, ex); }
            }
            cancellation.ThrowIfCancellationRequested();
            return Normalize(HidBattery.Read(path));
        }

        internal DeviceBattery ReadPhysical(PnpNode device, DeviceScanContext context, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var adapter in physical)
            {
                cancellation.ThrowIfCancellationRequested();
                try { if (!adapter.Supports(device)) continue; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { AppLog.Write("设备匹配 " + adapter.Id + "：" + ex.Message); continue; }
                try
                {
                    var reading = adapter.Read(device, context, cancellation);
                    if (reading != null) return Normalize(reading);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return Failure(adapter.Id, ex); }
            }
            return null;
        }

        internal static DeviceBattery Normalize(DeviceBattery reading)
        {
            if (reading == null) return null;
            if (reading.Percent.HasValue) reading.Percent = BatteryRules.Percentage(reading.Percent.Value);
            if (reading.Connected == false)
            { reading.Percent = null; reading.Charging = null; reading.ReadAt = null; }
            if (!reading.Percent.HasValue) reading.ReadAt = null;
            return reading;
        }

        private static DeviceBattery Failure(string id, Exception error)
        {
            AppLog.Write("设备适配器 " + id + "：" + error.Message);
            return new DeviceBattery { Source = id, Note = "设备查询失败；请唤醒设备后重试。" };
        }
    }
}
