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
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct DeviceInfo { public int Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct InterfaceInfo { public int Size; public Guid Guid; public uint Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Guid; public uint Id; public PropertyKey(string guid, uint id) { Guid = new Guid(guid); Id = id; } }
        internal static readonly PropertyKey Description = new PropertyKey("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
        internal static readonly PropertyKey FriendlyName = new PropertyKey("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
        internal static readonly PropertyKey Class = new PropertyKey("a45c254e-df1c-4efd-8020-67d146a850e0", 9);
        internal static readonly PropertyKey BusDescription = new PropertyKey("540b947e-8b40-45bc-a8a2-6a0b894cbda2", 4);
        internal static readonly PropertyKey Parent = new PropertyKey("4340a6c5-93fa-4706-972c-7b648008a5a7", 8);
        internal static readonly PropertyKey Container = new PropertyKey("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);
        internal static readonly PropertyKey Battery = new PropertyKey("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 2);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr SetupDiGetClassDevs(IntPtr guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr SetupDiGetClassDevsGuid(ref Guid guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] internal static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DeviceInfo info, StringBuilder id, uint size, out uint required);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetupDiGetDeviceProperty(IntPtr set, ref DeviceInfo info, ref PropertyKey key, out uint type, byte[] value, uint size, out uint required, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref InterfaceInfo detail);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceInfo detail, IntPtr data, uint size, out uint required, ref DeviceInfo info);
        [DllImport("setupapi.dll")] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        internal static byte[] Property(IntPtr set, ref DeviceInfo info, PropertyKey key)
        {
            uint type, length;
            var bytes = new byte[4096];
            if (!SetupDiGetDeviceProperty(set, ref info, ref key, out type, bytes, (uint)bytes.Length, out length, 0) || length > bytes.Length) return null;
            return bytes.Take((int)length).ToArray();
        }
        internal static string StringProperty(IntPtr set, ref DeviceInfo info, PropertyKey key)
        {
            var bytes = Property(set, ref info, key);
            return bytes == null ? "" : Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        internal static List<PnpNode> Nodes()
        {
            var result = new List<PnpNode>();
            var set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 6); // PRESENT | ALLCLASSES
            if (set == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                for (uint index = 0; ; index++)
                {
                    var info = new DeviceInfo { Size = Marshal.SizeOf(typeof(DeviceInfo)) };
                    if (!SetupDiEnumDeviceInfo(set, index, ref info))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != 259) throw new System.ComponentModel.Win32Exception(error);
                        break;
                    }
                    uint length; var id = new StringBuilder(1024);
                    if (!SetupDiGetDeviceInstanceId(set, ref info, id, (uint)id.Capacity, out length)) continue;
                    var container = Property(set, ref info, Container);
                    var name = StringProperty(set, ref info, BusDescription);
                    if (String.IsNullOrWhiteSpace(name)) name = StringProperty(set, ref info, FriendlyName);
                    if (String.IsNullOrWhiteSpace(name)) name = StringProperty(set, ref info, Description);
                    var battery = Property(set, ref info, Battery);
                    int? percent = null;
                    if (battery != null && battery.Length == 1) percent = BatteryRules.Percentage(battery[0]);
                    if (battery != null && battery.Length == 4) percent = BatteryRules.Percentage(BitConverter.ToUInt32(battery, 0));
                    result.Add(new PnpNode { Id = id.ToString(), Name = name, Class = StringProperty(set, ref info, Class), Parent = StringProperty(set, ref info, Parent), Container = container != null && container.Length == 16 ? new Guid(container).ToString() : "", Battery = percent });
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }
        internal static List<DeviceInterface> Interfaces(Guid guid)
        {
            var result = new List<DeviceInterface>();
            var set = SetupDiGetClassDevsGuid(ref guid, null, IntPtr.Zero, 18); // PRESENT | DEVICEINTERFACE
            if (set == new IntPtr(-1)) return result;
            try
            {
                for (uint index = 0; ; index++)
                {
                    var item = new InterfaceInfo { Size = Marshal.SizeOf(typeof(InterfaceInfo)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref item)) break;
                    var info = new DeviceInfo { Size = Marshal.SizeOf(typeof(DeviceInfo)) };
                    uint required;
                    SetupDiGetDeviceInterfaceDetail(set, ref item, IntPtr.Zero, 0, out required, ref info);
                    if (required < 8 || required > 65536) continue;
                    var pointer = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        Marshal.WriteInt32(pointer, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref item, pointer, required, out required, ref info)) continue;
                        var id = new StringBuilder(1024); uint idLength;
                        if (SetupDiGetDeviceInstanceId(set, ref info, id, (uint)id.Capacity, out idLength)) result.Add(new DeviceInterface { Path = Marshal.PtrToStringUni(IntPtr.Add(pointer, 4)), InstanceId = id.ToString() });
                    }
                    finally { Marshal.FreeHGlobal(pointer); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }
    }
}
