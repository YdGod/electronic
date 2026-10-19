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
    [DataContract] internal sealed class HidDescriptor
    {
        [DataMember] public string Path, InstanceId, Product;
        [DataMember] public ushort UsagePage, Usage, InputLength, OutputLength, FeatureLength;
        [DataMember] public List<byte> InputIds = new List<byte>(), OutputIds = new List<byte>();
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr pointer);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr pointer);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr pointer, byte[] caps);
        [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int type, IntPtr caps, ref ushort count, IntPtr pointer);
        [DllImport("hid.dll")] private static extern int HidP_GetButtonCaps(int type, IntPtr caps, ref ushort count, IntPtr pointer);
        private const int Success = 0x00110000;
        internal static HidDescriptor Read(DeviceInterface item)
        {
            using (var handle = Native.CreateFile(item.Path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) return null;
                IntPtr parsed;
                if (!HidD_GetPreparsedData(handle, out parsed)) return null;
                try
                {
                    var caps = new byte[64];
                    if (HidP_GetCaps(parsed, caps) != Success) return null;
                    var descriptor = new HidDescriptor { Path = item.Path, InstanceId = item.InstanceId, Product = HidBattery.ProductName(item.Path), Usage = BitConverter.ToUInt16(caps, 0), UsagePage = BitConverter.ToUInt16(caps, 2), InputLength = BitConverter.ToUInt16(caps, 4), OutputLength = BitConverter.ToUInt16(caps, 6), FeatureLength = BitConverter.ToUInt16(caps, 8) };
                    for (int type = 0; type <= 1; type++)
                    {
                        var ids = type == 0 ? descriptor.InputIds : descriptor.OutputIds;
                        for (int kind = 0; kind < 2; kind++)
                        {
                            ushort count = BitConverter.ToUInt16(caps, 46 + type * 6 + kind * 2);
                            if (count == 0 || count > 512) continue;
                            var buffer = Marshal.AllocHGlobal(count * 72);
                            try
                            {
                                int status = kind == 0 ? HidP_GetButtonCaps(type, buffer, ref count, parsed) : HidP_GetValueCaps(type, buffer, ref count, parsed);
                                if (status == Success) for (int i = 0; i < count; i++) { var id = Marshal.ReadByte(buffer, i * 72 + 2); if (!ids.Contains(id)) ids.Add(id); }
                            }
                            finally { Marshal.FreeHGlobal(buffer); }
                        }
                    }
                    return descriptor;
                }
                finally { HidD_FreePreparsedData(parsed); }
            }
        }
        internal static List<HidDescriptor> Inventory()
        {
            var result = new List<HidDescriptor>();
            foreach (var item in Native.Interfaces(new Guid("4d1e55b2-f16f-11cf-88cb-001111000030"))) { var descriptor = Read(item); if (descriptor != null) result.Add(descriptor); }
            return result;
        }
    }
}
