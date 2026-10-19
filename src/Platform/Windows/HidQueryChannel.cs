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
    // Uses the vendor collection only. Cancels overlapped I/O when a receiver stops replying.
    internal sealed class HidQueryChannel : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
        private readonly SafeFileHandle handle;
        private readonly FileStream stream;
        private readonly HidDescriptor descriptor;
        internal HidQueryChannel(HidDescriptor descriptor)
        {
            this.descriptor = descriptor;
            handle = Native.CreateFile(descriptor.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
            // Disable FileStream buffering: HID packets must be sent immediately, not held until a flush.
            try { stream = new FileStream(handle, FileAccess.ReadWrite, 1, true); }
            catch { handle.Dispose(); throw; }
        }
        private async Task<T> Bounded<T>(Task<T> operation, int timeout)
        {
            if (await Task.WhenAny(operation, Task.Delay(Math.Max(1, timeout))).ConfigureAwait(false) != operation)
            {
                CancelIoEx(handle, IntPtr.Zero);
                try { await operation.ConfigureAwait(false); } catch { }
                throw new TimeoutException("接收器查询超时。");
            }
            return await operation.ConfigureAwait(false);
        }
        internal async Task<byte[]> Query(byte[] report, Func<byte[], bool> matches, int timeout)
        {
            if (report.Length != descriptor.OutputLength) throw new ArgumentException("输出报告长度不匹配。");
            var clock = Stopwatch.StartNew();
            var input = new byte[descriptor.InputLength];
            var read = stream.ReadAsync(input, 0, input.Length);
            Exception failure = null;
            try
            {
                var write = stream.WriteAsync(report, 0, report.Length);
                if (await Task.WhenAny(write, Task.Delay(timeout)).ConfigureAwait(false) != write) throw new TimeoutException("接收器写入超时。");
                await write.ConfigureAwait(false);
                while (clock.ElapsedMilliseconds < timeout)
                {
                    int length = await Bounded(read, timeout - (int)clock.ElapsedMilliseconds).ConfigureAwait(false);
                    if (length == input.Length && matches(input)) return input;
                    input = new byte[descriptor.InputLength];
                    read = stream.ReadAsync(input, 0, input.Length);
                }
                throw new TimeoutException("未收到有效电量响应。");
            }
            catch (Exception ex) { failure = ex; }
            CancelIoEx(handle, IntPtr.Zero);
            try { await read.ConfigureAwait(false); } catch { }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return null;
        }
        public void Dispose() { CancelIoEx(handle, IntPtr.Zero); stream.Dispose(); }
    }
}
