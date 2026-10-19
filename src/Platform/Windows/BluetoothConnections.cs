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
    internal static class BluetoothConnections
    {
        [StructLayout(LayoutKind.Sequential)] private struct Search { public uint Size; public int Authenticated, Remembered, Unknown, Connected, Inquiry; public byte Timeout; public IntPtr Radio; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Info { public uint Size; public ulong Address; public uint Class; public int Connected, Remembered, Authenticated; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Times; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name; }
        [DllImport("BluetoothApis.dll", SetLastError = true)] private static extern IntPtr BluetoothFindFirstDevice(ref Search search, ref Info info);
        [DllImport("BluetoothApis.dll", SetLastError = true)] private static extern bool BluetoothFindNextDevice(IntPtr find, ref Info info);
        [DllImport("BluetoothApis.dll")] private static extern bool BluetoothFindDeviceClose(IntPtr find);
        internal static Dictionary<string, bool> Read()
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var search = new Search { Size = (uint)Marshal.SizeOf(typeof(Search)), Authenticated = 1, Remembered = 1, Connected = 1, Inquiry = 0 };
            var info = new Info { Size = (uint)Marshal.SizeOf(typeof(Info)), Times = new byte[16] };
            var find = BluetoothFindFirstDevice(ref search, ref info);
            if (find == IntPtr.Zero) return result;
            try
            {
                do { result[info.Address.ToString("X12")] = info.Connected != 0; info.Size = (uint)Marshal.SizeOf(typeof(Info)); } while (BluetoothFindNextDevice(find, ref info));
            }
            finally { BluetoothFindDeviceClose(find); }
            return result;
        }
        internal static bool TryMatch(IEnumerable<string> ids, Dictionary<string, bool> devices, out bool connected)
        {
            foreach (var pair in devices)
                if (ids.Any(id => id.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)) { connected = pair.Value; return true; }
            connected = false; return false;
        }
    }
}
