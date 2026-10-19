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
    internal static class BleBattery
    {
        [StructLayout(LayoutKind.Explicit, Size = 20)] internal struct Uuid { [FieldOffset(0)] public byte Short; [FieldOffset(4)] public ushort Value; [FieldOffset(4)] public Guid Long; public bool Is(ushort value) { return Short != 0 ? Value == value : Long == new Guid("0000" + value.ToString("x4") + "-0000-1000-8000-00805f9b34fb"); } }
        [StructLayout(LayoutKind.Sequential)] internal struct Service { public Uuid Uuid; public ushort Handle; }
        [StructLayout(LayoutKind.Sequential)] internal struct Characteristic { public ushort ServiceHandle; public Uuid Uuid; public ushort Handle, ValueHandle; public byte Broadcast, Read, Write, WriteWithoutResponse, SignedWrite, Notify, Indicate, Extended; }
        [DllImport("BluetoothApis.dll")] private static extern int BluetoothGATTGetServices(SafeFileHandle handle, ushort count, [Out] Service[] services, out ushort actual, uint flags);
        [DllImport("BluetoothApis.dll")] private static extern int BluetoothGATTGetCharacteristics(SafeFileHandle handle, ref Service service, ushort count, [Out] Characteristic[] chars, out ushort actual, uint flags);
        [DllImport("BluetoothApis.dll")] private static extern int BluetoothGATTGetCharacteristicValue(SafeFileHandle handle, ref Characteristic characteristic, uint size, IntPtr value, out ushort actual, uint flags);
        private static byte? Value(SafeFileHandle handle, Characteristic characteristic)
        {
            var pointer = Marshal.AllocHGlobal(512);
            try
            {
                ushort actual;
                int hr = BluetoothGATTGetCharacteristicValue(handle, ref characteristic, 512, pointer, out actual, 4); // FORCE_READ_FROM_DEVICE
                return hr >= 0 && Marshal.ReadInt32(pointer) >= 1 && Marshal.ReadInt32(pointer) <= 508 ? (byte?)Marshal.ReadByte(pointer, 4) : null;
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        internal static DeviceBattery Read(string path)
        {
            using (var handle = Native.CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) return null;
                ushort count; BluetoothGATTGetServices(handle, 0, null, out count, 0);
                if (count == 0 || count > 128) return null;
                var services = new Service[count];
                if (BluetoothGATTGetServices(handle, count, services, out count, 0) < 0) return null;
                foreach (var item in services.Where(s => s.Uuid.Is(0x180f)))
                {
                    var service = item; ushort charCount;
                    BluetoothGATTGetCharacteristics(handle, ref service, 0, null, out charCount, 0);
                    if (charCount == 0 || charCount > 128) continue;
                    var chars = new Characteristic[charCount];
                    if (BluetoothGATTGetCharacteristics(handle, ref service, charCount, chars, out charCount, 0) < 0) continue;
                    int? percent = null; bool? charging = null;
                    foreach (var characteristic in chars)
                    {
                        if (characteristic.Read == 0 || (!characteristic.Uuid.Is(0x2a19) && !characteristic.Uuid.Is(0x2a1a))) continue;
                        var data = Value(handle, characteristic);
                        if (!data.HasValue) continue;
                        if (characteristic.Uuid.Is(0x2a19)) percent = BatteryRules.Percentage(data.Value);
                        if (characteristic.Uuid.Is(0x2a1a)) charging = BatteryRules.BleCharging(data.Value);
                    }
                    if (percent.HasValue) return new DeviceBattery { Percent = percent, Charging = charging, Source = "Bluetooth GATT", Connected = true, ReadAt = DateTime.Now, Note = charging.HasValue ? "来自蓝牙标准电池服务。" : "来自蓝牙标准电池服务；设备未提供充电状态。" };
                }
            }
            return null;
        }
    }
}
