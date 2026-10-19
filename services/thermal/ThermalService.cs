using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.ServiceProcess;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BatteryPet;
using LibreHardwareMonitor.Hardware;

internal sealed class ThermalService : ServiceBase
{
    private readonly ManualResetEvent stop = new ManualResetEvent(false);
    private CancellationTokenSource stopCancellation;
    private Thread worker;
    private readonly object pipeGate = new object();
    private NamedPipeServerStream currentPipe;
    private static readonly string CachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BatteryPet", "temperatures.json");
    internal ThermalService() { ServiceName = "BatteryPetThermal"; CanStop = true; AutoLog = true; }
    protected override void OnStart(string[] args)
    {
        stop.Reset(); stopCancellation = new CancellationTokenSource();
        worker = new Thread(Serve) { IsBackground = true, Name = "CPU temperature requests" }; worker.Start();
    }
    protected override void OnStop()
    {
        stop.Set(); stopCancellation.Cancel();
        lock (pipeGate) { if (currentPipe != null) currentPipe.Dispose(); }
        if (worker != null) worker.Join(10000);
        try { if (File.Exists(CachePath)) File.Delete(CachePath); } catch { }
    }
    private static ThermalSnapshot Sample(Computer computer)
    {
        var snapshot = new ThermalSnapshot();
        foreach (var hardware in computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
        {
            hardware.Update();
            snapshot.Readings.Add(TemperatureRules.SelectCpu(hardware.Name, hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature).Select(s => new TemperatureReading { Sensor = s.Name, Celsius = s.Value.HasValue ? (double?)s.Value.Value : null })));
        }
        return snapshot;
    }
    private void Serve()
    {
        while (!stop.WaitOne(0))
        {
            try
            {
                var security = new PipeSecurity();
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                using (var pipe = new NamedPipeServerStream(CpuTemperatureProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 512, 4096, security))
                {
                    lock (pipeGate) { currentPipe = pipe; }
                    if (stop.WaitOne(0)) break;
                    // Let the pipe own its completion handle. Closing an APM wait handle
                    // can race the .NET Framework IO callback, including during service stop.
                    pipe.WaitForConnectionAsync(stopCancellation.Token).GetAwaiter().GetResult();
                    var request = new byte[4]; int offset = 0;
                    while (offset < request.Length)
                    {
                        var read = pipe.ReadAsync(request, offset, request.Length - offset);
                        if (!read.Wait(2000) || read.Result == 0) throw new IOException("Incomplete CPU request.");
                        offset += read.Result;
                    }
                    if (!CpuTemperatureProtocol.IsReadRequest(request) || stop.WaitOne(0)) continue;
                    ThermalSnapshot snapshot;
                    var computer = new Computer { IsCpuEnabled = true };
                    try { computer.Open(); snapshot = Sample(computer); }
                    catch (Exception ex) { snapshot = new ThermalSnapshot { Readings = new List<TemperatureReading> { new TemperatureReading { Kind = "CPU", Name = "CPU", Note = "采集失败：" + ex.Message, Source = "LibreHardwareMonitor / PawnIO" } } }; }
                    finally { try { computer.Close(); } catch { } }
                    WriteAtomic(CachePath, snapshot);
                    using (var memory = new MemoryStream())
                    {
                        new DataContractJsonSerializer(typeof(ThermalSnapshot)).WriteObject(memory, snapshot);
                        var json = memory.ToArray(); var length = BitConverter.GetBytes(json.Length);
                        if (!pipe.WriteAsync(length, 0, length.Length).Wait(2000) || !pipe.WriteAsync(json, 0, json.Length).Wait(2000)) throw new IOException("CPU response timed out.");
                    }
                    try { File.Delete(CachePath + ".error.txt"); } catch { }
                }
            }
            catch (Exception ex)
            {
                if (!stop.WaitOne(0))
                {
                    try { File.WriteAllText(CachePath + ".error.txt", DateTime.UtcNow.ToString("o") + Environment.NewLine + ex.ToString()); } catch { }
                    stop.WaitOne(250);
                }
            }
            finally { lock (pipeGate) { currentPipe = null; } }
        }
    }
    private static void WriteAtomic(string path, ThermalSnapshot value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(ThermalSnapshot)).WriteObject(stream, value);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--probe")
        {
            var computer = new Computer { IsCpuEnabled = true };
            try { computer.Open(); var reading = Sample(computer); WriteAtomic(args[1], reading); return reading.Readings.Any(s => s.Celsius.HasValue) ? 0 : 2; }
            finally { computer.Close(); }
        }
        ServiceBase.Run(new ThermalService()); return 0;
    }
}
