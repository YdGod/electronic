using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace BatteryPet
{
    [DataContract]
    internal sealed class PacketFixture
    {
        [DataMember] public string ResponseHex { get; set; }
        [DataMember] public int ExpectedPercent { get; set; }
        [DataMember] public bool ExpectedCharging { get; set; }
        [DataMember] public bool ExpectedConnected { get; set; }
    }

    internal static class ExtensionVerification
    {
        internal static void Run(Action<bool, string> check, string outputDirectory)
        {
            var ignored = new FakeHid { Match = false };
            var selected = new FakeHid { Match = true, Result = new DeviceBattery { Percent = 0, Charging = false } };
            var registry = new DeviceProviderRegistry(new IHidBatteryAdapter[] { ignored, selected }, new IPhysicalBatteryAdapter[0]);
            var reading = registry.ReadHid(new HidDescriptor(), "unused", CancellationToken.None);
            check(ignored.Reads == 0 && selected.Reads == 1 && reading.Percent == 0, "Registry queries only a matching adapter and preserves zero percent");
            ignored.MatchFailure = new IOException("expected matching failure");
            check(registry.ReadHid(new HidDescriptor(), "unused", CancellationToken.None).Percent == 0, "A broken match predicate does not block another adapter");
            selected.Failure = new IOException("expected test failure");
            reading = registry.ReadHid(new HidDescriptor(), "unused", CancellationToken.None);
            check(!reading.Percent.HasValue && reading.Source == "fake-hid", "A failing device extension returns unknown instead of breaking the scan");
            int previous = selected.Reads;
            bool cancelled = false;
            try { registry.ReadHid(new HidDescriptor(), "unused", new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled && selected.Reads == previous, "Registry cancellation prevents any adapter query");
            reading = DeviceProviderRegistry.Normalize(new DeviceBattery { Percent = 70, Charging = true, Connected = false, ReadAt = DateTime.Now });
            check(!reading.Percent.HasValue && !reading.Charging.HasValue && !reading.ReadAt.HasValue, "Extensions cannot retain battery data after confirming disconnection");
            reading = DeviceProviderRegistry.Normalize(new DeviceBattery { Percent = 255, ReadAt = DateTime.Now });
            check(!reading.Percent.HasValue && !reading.ReadAt.HasValue, "Registry rejects invalid contributed battery values");
            var gsp = (IPhysicalBatteryAdapter)new EposGsp370Battery();
            check(gsp.Supports(new PnpNode { Id = @"USB\VID_1395&PID_00AB\test", Name = "GSP 370" })
                && !gsp.Supports(new PnpNode { Id = @"USB\VID_1395&PID_00AB0\test", Name = "GSP 370" }), "Serial adapters match the physical device without sending requests");

            var failedCpu = new FakeTemperature("CPU", () => { throw new IOException("expected"); });
            var gpu = new FakeTemperature("GPU", () => new[] { Value("GPU", 44), Value("GPU", 48) });
            var temperatures = ThermalProvider.Collect(new ITemperatureProvider[] { failedCpu, gpu }, CancellationToken.None);
            check(temperatures.Readings.Count == 3 && !temperatures.Readings[0].Celsius.HasValue && temperatures.Readings.Count(r => r.Kind == "GPU" && r.Celsius.HasValue) == 2, "A failing CPU extension does not block multiple GPU readings");
            var stale = Value("CPU", 90); stale.ReadAt = DateTime.UtcNow.AddMinutes(-1);
            temperatures = ThermalProvider.Collect(new[] { new FakeTemperature("CPU", () => new[] { stale, Value("CPU", Double.NaN) }) }, CancellationToken.None);
            check(temperatures.Readings.All(r => !r.Celsius.HasValue && !r.ReadAt.HasValue), "Temperature extensions cannot display stale or nonnumeric readings");
            temperatures = ThermalProvider.Collect(new[] { new FakeTemperature("GPU", () => PartialFailure()) }, CancellationToken.None);
            check(temperatures.Readings.Count == 1 && !temperatures.Readings[0].Celsius.HasValue, "A provider throwing during enumeration discards its incomplete batch");
            temperatures = ThermalProvider.Collect(new ITemperatureProvider[] { new FakeTemperature("GPU", () => new TemperatureReading[0]), gpu }, CancellationToken.None);
            check(temperatures.Readings.Count == 2 && temperatures.Readings.All(r => r.Celsius.HasValue), "A supported GPU backend replaces an unsupported backend's placeholder");
            temperatures = ThermalProvider.Collect(new[] { new FakeTemperature("GPU", () => new[] { Value("GPU", 40), new TemperatureReading { Kind = "GPU", Name = "second GPU", Sensor = "GPU Core" } }) }, CancellationToken.None);
            check(temperatures.Readings.Count == 2 && temperatures.Readings.Any(r => r.Name == "second GPU" && !r.Celsius.HasValue), "A failed sensor on another physical GPU remains visible beside working GPUs");
            temperatures = ThermalProvider.Collect(new[] { new FakeTemperature("CPU", () => new[] { Value("GPU", 50) }) }, CancellationToken.None);
            check(temperatures.Readings.Count == 1 && temperatures.Readings[0].Kind == "CPU" && !temperatures.Readings[0].Celsius.HasValue, "Providers cannot mislabel GPU sensors as CPU temperatures");

            string temporary = Path.Combine(outputDirectory, "extension-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                var profiles = Path.Combine(temporary, "devices", "profiles"); Directory.CreateDirectory(profiles);
                var profile = new DeviceProfile { Product = "Example", Vid = "1234", Pid = "5678", Name = "例子设备" };
                JsonFile.Write(Path.Combine(profiles, "valid.json"), profile);
                File.WriteAllText(Path.Combine(profiles, "broken.json"), "{invalid");
                check(DeviceProfile.LoadFrom(temporary).Count == 1, "One malformed device profile does not prevent loading other profiles");
                JsonFile.Write(Path.Combine(temporary, "profiles.json"), new[] { new DeviceProfile { Product = "Example", Vid = "1234", Pid = "5678", Name = "旧版自定义名称" } });
                check(DeviceProfile.LoadFrom(temporary)[0].Name == "旧版自定义名称", "Legacy portable profile overrides retain priority");
                check(!profile.Matches("Example", new[] { @"HID\VID_1234&PID_56780&COL01" }), "Device profile matching requires the exact PID boundary");

                string skinRoot = Path.Combine(temporary, "skins"); Directory.CreateDirectory(skinRoot);
                string valid = Path.Combine(skinRoot, "test-skin"); Directory.CreateDirectory(valid);
                JsonFile.Write(Path.Combine(valid, "skin.json"), Manifest("test-skin"));
                using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("BatteryPet.Skins.whale.idle.png"))
                using (var destination = File.Create(Path.Combine(valid, "idle.png"))) source.CopyTo(destination);
                string invalid = Path.Combine(skinRoot, "bad-skin"); Directory.CreateDirectory(invalid);
                var bad = Manifest("bad-skin"); bad.Frames.Idle = "../idle.png";
                JsonFile.Write(Path.Combine(invalid, "skin.json"), bad);
                var skins = new List<PetSkin>(); PetSkins.LoadDirectory(skinRoot, skins);
                check(skins.Count == 1 && skins[0].Name == "测试皮肤" && ReferenceEquals(skins[0].Frame(false), skins[0].Frame(true)), "A PNG and manifest install a skin; an omitted popup frame reuses idle");
                PetSkins.LoadDirectory(skinRoot, skins);
                check(skins.Count == 1, "Duplicate external skin IDs do not replace a loaded skin");
                bool rejectedPath = false;
                try { new PetSkin(bad, name => { throw new Exception("must not open"); }); } catch (InvalidDataException) { rejectedPath = true; }
                check(rejectedPath, "Skin paths are validated before opening files outside a pack");
                var oversized = new byte[24]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 32, 0, 0, 0, 0, 1 }.CopyTo(oversized, 0);
                bool rejectedSize = false;
                try { new PetSkin(Manifest("large-skin"), name => new MemoryStream(oversized)); } catch (InvalidDataException) { rejectedSize = true; }
                check(rejectedSize, "Oversized PNG dimensions are rejected before image decoding");
                var noAuthor = Manifest("no-author"); noAuthor.Author = null;
                bool rejectedAuthor = false; try { noAuthor.Validate(); } catch (InvalidDataException) { rejectedAuthor = true; }
                check(rejectedAuthor, "Contributed skin metadata requires attribution and a license");
            }
            finally { Directory.Delete(temporary, true); }

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BatteryPet.Fixtures.mchose-k5-ultra.json"))
            {
                var fixture = (PacketFixture)new DataContractJsonSerializer(typeof(PacketFixture)).ReadObject(stream);
                var bytes = fixture.ResponseHex.Split('-').Select(s => Convert.ToByte(s, 16)).ToArray();
                var observed = MchoseK5Battery.Parse(bytes);
                check(observed != null && observed.Percent == fixture.ExpectedPercent && observed.Charging == fixture.ExpectedCharging && observed.Connected == fixture.ExpectedConnected, "Sanitized contributed packet fixture reproduces the observed hardware reading");
            }
        }

        private static SkinManifest Manifest(string id)
        { return new SkinManifest { SchemaVersion = 1, Id = id, Name = "测试皮肤", Author = "BatteryPet tests", License = "MIT", Frames = new SkinFrames { Idle = "idle.png" } }; }
        private static TemperatureReading Value(string kind, double value)
        { return new TemperatureReading { Kind = kind, Name = "test", Sensor = "test", Source = "test", Celsius = value, ReadAt = DateTime.UtcNow }; }
        private static IEnumerable<TemperatureReading> PartialFailure()
        { yield return Value("GPU", 60); throw new IOException("expected enumeration failure"); }

        private sealed class FakeHid : IHidBatteryAdapter
        {
            internal bool Match;
            internal int Reads;
            internal DeviceBattery Result;
            internal Exception Failure;
            internal Exception MatchFailure;
            public string Id { get { return "fake-hid"; } }
            public bool Supports(HidDescriptor descriptor) { if (MatchFailure != null) throw MatchFailure; return Match; }
            public DeviceBattery Read(HidDescriptor descriptor, CancellationToken cancellation)
            { Reads++; if (Failure != null) throw Failure; return Result; }
        }
        private sealed class FakeTemperature : ITemperatureProvider
        {
            private readonly string kind;
            private readonly Func<IEnumerable<TemperatureReading>> read;
            internal FakeTemperature(string kind, Func<IEnumerable<TemperatureReading>> read) { this.kind = kind; this.read = read; }
            public string Id { get { return "fake-" + kind; } }
            public string Kind { get { return kind; } }
            public IEnumerable<TemperatureReading> Read(CancellationToken cancellation) { return read(); }
        }
    }
}
