using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BatteryPet
{
    [DataContract] internal sealed class TestReport { [DataMember] public int Passed; [DataMember] public List<string> Checks = new List<string>(); [DataMember] public List<string> Failures = new List<string>(); }
    internal static class Verification
    {
        internal static int Run(string path)
        {
            var report = new TestReport();
            Action<bool, string> check = delegate(bool pass, string name) { if (pass) { report.Passed++; report.Checks.Add(name); } else report.Failures.Add(name); };
            check(CpuTemperatureProtocol.IsReadRequest(new byte[] { 82, 69, 65, 68 }) && !CpuTemperatureProtocol.IsReadRequest(new byte[] { 83, 69, 84, 33 }) && !CpuTemperatureProtocol.IsReadRequest(new byte[0]), "CPU service accepts only the fixed read-only sample request");
            bool cancelledBattery = false, cancelledTemperature = false;
            var cancelled = new CancellationToken(true);
            try { new WindowsBatteryProvider().Scan(true, cancelled); } catch (OperationCanceledException) { cancelledBattery = true; }
            try { ThermalProvider.Read(cancelled); } catch (OperationCanceledException) { cancelledTemperature = true; }
            check(cancelledBattery && cancelledTemperature, "Cancelled popup queries exit before accessing peripherals or temperature sensors");
            check(!TemperatureRules.Valid(Double.NaN).HasValue && !TemperatureRules.Valid(Double.PositiveInfinity).HasValue && !TemperatureRules.Valid(-1).HasValue && !TemperatureRules.Valid(255).HasValue && !TemperatureRules.Valid(null).HasValue, "Invalid temperature and missing sensor values stay unknown");
            var now = DateTime.UtcNow;
            check(TemperatureRules.Fresh(now.AddSeconds(-2), now) && !TemperatureRules.Fresh(now.AddSeconds(-16), now) && !TemperatureRules.Fresh(now.AddMinutes(1), now) && !TemperatureRules.Fresh(null, now), "Stopped collectors and invalid future timestamps cannot show cached temperatures");
            var cpuSensors = new[] { new TemperatureReading { Sensor = "CPU Package", Celsius = 40 }, new TemperatureReading { Sensor = "Core Max", Celsius = 55 }, new TemperatureReading { Sensor = "P-Core #1 Distance to TjMax", Celsius = 99 } };
            check(TemperatureRules.SelectCpu("CPU", cpuSensors).Celsius == 40 && TemperatureRules.SelectCpu("CPU", cpuSensors.Skip(1)).Celsius == 55 && !TemperatureRules.SelectCpu("CPU", cpuSensors.Skip(2)).Celsius.HasValue, "CPU package wins; only real core temperatures are a fallback, never TjMax distance");
            check(BatteryRules.Percentage(0) == 0 && BatteryRules.Percentage(100) == 100, "Valid percentage boundaries: 0 and 100");
            check(!BatteryRules.Percentage(-1).HasValue && !BatteryRules.Percentage(101).HasValue && !BatteryRules.Percentage(255).HasValue && !BatteryRules.Percentage(UInt32.MaxValue).HasValue, "Unknown/sentinel readings never become fake percentages");
            check(BatteryRules.BleCharging(0x20) == true && BatteryRules.BleCharging(0x10) == false && !BatteryRules.BleCharging(0x00).HasValue && !BatteryRules.BleCharging(0x30).HasValue, "BLE charging bitfield preserves true, false and unknown");
            var mchoseDescriptor = new HidDescriptor { InstanceId = "HID\\VID_3837&PID_1018&MI_02&COL02", Product = "MCHOSE K5 Ultra", UsagePage = 0xFF01, Usage = 1, InputLength = 64, OutputLength = 64, FeatureLength = 64, InputIds = new List<byte> { 17, 77, 19 }, OutputIds = new List<byte> { 77, 19 } };
            check(MchoseK5Battery.Supports(mchoseDescriptor), "K5 Ultra query binds the observed vendor collection, product and VID/PID");
            mchoseDescriptor.UsagePage = 1;
            bool refusesMchoseInput = !MchoseK5Battery.Supports(mchoseDescriptor);
            mchoseDescriptor.UsagePage = 0xFF01; mchoseDescriptor.InstanceId = "HID\\VID_3837&PID_10180&MI_02&COL02";
            check(refusesMchoseInput && !MchoseK5Battery.Supports(mchoseDescriptor), "K5 Ultra refuses standard input collections and nonmatching hardware identities");
            var mchosePacket = MchoseK5Battery.Packet();
            check(mchosePacket.Length == 64 && mchosePacket.Take(9).SequenceEqual(new byte[] { 0x4D, 1, 1, 0, 0, 9, 0, 0, 8 }) && mchosePacket.Skip(9).All(b => b == 0), "K5 Ultra emits only the official fixed 0x0900 read request with its XOR checksum");
            var mchoseFrame = new byte[64];
            new byte[] { 0x4D, 1, 1, 0x10, 0, 9, 0, 0, 0x37, 0x38, 0x38, 0x40, 4, 0, 0, 0, 0, 0x10, 2, 0, 0x46, 0, 8, 0xE4, 0xD3 }.CopyTo(mchoseFrame, 0);
            var mchoseReading = MchoseK5Battery.Parse(mchoseFrame);
            check(mchoseReading != null && mchoseReading.Percent == 70 && mchoseReading.Charging == false && mchoseReading.Connected == true, "K5 Ultra decodes the captured real 70-percent wireless response");
            var corruptMchose = (byte[])mchoseFrame.Clone(); corruptMchose[20] ^= 1;
            var wrongMchoseCommand = (byte[])mchoseFrame.Clone(); wrongMchoseCommand[5] = 1;
            check(MchoseK5Battery.Parse(null) == null && MchoseK5Battery.Parse(mchoseFrame.Take(63).ToArray()) == null && MchoseK5Battery.Parse(corruptMchose) == null && MchoseK5Battery.Parse(wrongMchoseCommand) == null, "K5 Ultra rejects truncated, corrupt and unrelated responses");
            Action<byte[]> sealMchose = frame => { byte checksum = 0; for (int i = 2; i < 8 + frame[3]; i++) checksum ^= frame[i]; frame[8 + frame[3]] = checksum; };
            var otherMchose = (byte[])mchoseFrame.Clone(); otherMchose[10] = 0x37; sealMchose(otherMchose);
            check(MchoseK5Battery.Parse(otherMchose) == null, "K5 Ultra shared receiver rejects other mouse model identities even with a valid checksum");
            var valueMchose = (byte[])mchoseFrame.Clone(); valueMchose[20] = 0; sealMchose(valueMchose);
            bool mchoseZero = MchoseK5Battery.Parse(valueMchose).Percent == 0;
            valueMchose[20] = 100; sealMchose(valueMchose); bool mchoseFull = MchoseK5Battery.Parse(valueMchose).Percent == 100;
            valueMchose[20] = 255; sealMchose(valueMchose);
            check(mchoseZero && mchoseFull && !MchoseK5Battery.Parse(valueMchose).Percent.HasValue, "K5 Ultra preserves zero and full batteries while invalid values stay unknown");
            valueMchose[19] = 1; sealMchose(valueMchose); bool mchoseCharging = MchoseK5Battery.Parse(valueMchose).Charging == true;
            valueMchose[19] = 2; sealMchose(valueMchose);
            check(mchoseCharging && !MchoseK5Battery.Parse(valueMchose).Charging.HasValue, "K5 Ultra charging uses only explicit official 0/1 values");
            valueMchose[18] = 0; valueMchose[20] = 70; sealMchose(valueMchose); var unconfirmedMchose = MchoseK5Battery.Parse(valueMchose);
            check(!unconfirmedMchose.Connected.HasValue && !unconfirmedMchose.Percent.HasValue && !unconfirmedMchose.Charging.HasValue && !unconfirmedMchose.ReadAt.HasValue, "K5 Ultra unconfirmed connection states cannot display retained battery data");
            var packet = ScyroxV12Battery.Packet(8, 14);
            check(packet.Length == 64 && packet[0] == 8 && packet[1] == 14 && packet[63] == 22 && packet.Skip(2).Take(61).All(b => b == 0), "V12 query matches official 64-byte packet and report-ID-inclusive checksum");
            var wiredMouse = new HidDescriptor { InstanceId = "HID\\VID_395A&PID_0005&MI_02&COL03", Product = "V12M", UsagePage = 0xFF00, Usage = 0, InputLength = 64, OutputLength = 64, FeatureLength = 64, InputIds = new List<byte> { 8 }, OutputIds = new List<byte> { 8 } };
            check(ScyroxV12Battery.Supports(wiredMouse), "V12 wired charging mode binds the observed exact VID/PID, product and vendor report collection");
            wiredMouse.UsagePage = 1; check(!ScyroxV12Battery.Supports(wiredMouse), "V12 battery query cannot target the standard mouse or keyboard input collections");
            bool blockedCommand = false; try { ScyroxV12Battery.Packet(8, 16); } catch (ArgumentException) { blockedCommand = true; }
            check(blockedCommand, "V12 factory-reset/settings commands cannot be generated");
            var response = new byte[64]; response[0] = 8; response[1] = 14; response[4] = 20;
            check(ScyroxV12Battery.Value(response, 8, 14) == 20 && !ScyroxV12Battery.Value(response, 7, 14).HasValue && !ScyroxV12Battery.Value(response, 8, 32).HasValue && !ScyroxV12Battery.Value(new byte[63], 8, 14).HasValue, "V12 responses require the right report ID, command and complete frame");
            response[62] = 1; check(!ScyroxV12Battery.Value(response, 8, 14).HasValue, "V12 error replies never become battery readings");
            check(EposGsp370Battery.UiPercentage("87 %") == 87 && EposGsp370Battery.UiPercentage("0%") == 0 && !EposGsp370Battery.UiPercentage("255 %").HasValue && !EposGsp370Battery.UiPercentage("GSP 370").HasValue, "EPOS parses only a valid explicit percentage, never device-name digits");
            var headsetFrame = new byte[] { 4, 14, 5, 1, 0x9C, 0xFC, 0, 87 };
            check(EposGsp370Battery.SerialValue(headsetFrame, 0x9C) == 87 && !EposGsp370Battery.SerialValue(headsetFrame, 0x9A).HasValue && !EposGsp370Battery.SerialValue(headsetFrame.Take(7).ToArray(), 0x9C).HasValue, "NXP HCI frame validates opcode and complete payload");
            headsetFrame[6] = 1; check(!EposGsp370Battery.SerialValue(headsetFrame, 0x9C).HasValue, "NXP error status is rejected");
            check(EposGsp370Battery.SerialPacket(0x9A).SequenceEqual(new byte[] { 1, 0x9A, 0xFC, 0 }) && EposGsp370Battery.SerialPacket(0x9C).SequenceEqual(new byte[] { 1, 0x9C, 0xFC, 0 }), "GSP 370 direct query packets match the captured read-only commands");
            bool blockedHeadsetCommand = false; try { EposGsp370Battery.SerialPacket(0x01); } catch (ArgumentException) { blockedHeadsetCommand = true; }
            check(blockedHeadsetCommand, "GSP 370 direct transport refuses firmware and settings commands");
            check(EposGsp370Battery.SerialValue(new byte[] { 4, 14, 5, 1, 0x9A, 0xFC, 0, 1 }, 0x9A) == 1 && EposGsp370Battery.SerialValue(new byte[] { 4, 14, 5, 1, 0x9C, 0xFC, 0, 0x57 }, 0x9C) == 87, "GSP 370 parser accepts the actual receiver state and 87-percent frames");
            var keyboardFrame = new byte[33]; keyboardFrame[1] = 0xB2; keyboardFrame[2] = 1; keyboardFrame[3] = 0x2D; keyboardFrame[4] = 0x36; keyboardFrame[5] = 0x2B; keyboardFrame[6] = 0x13; keyboardFrame[7] = 1;
            check(SknReceiverInfo.QingLongConnected(keyboardFrame) == true && !SknReceiverInfo.QingLongConnected(new byte[33]).HasValue, "SKN receiver state matches the actual QingLong VID/PID");
            keyboardFrame[7] = 0; check(SknReceiverInfo.QingLongConnected(keyboardFrame) == false, "SKN offline state does not retain a made-up battery");
            check(BatteryRules.Kind("Mouse", "HID") == "鼠标" && BatteryRules.Kind("Keyboard", "HID") == "键盘" && BatteryRules.Kind("AudioEndpoint", "GSP 370") == "耳机", "Windows peripheral categories");
            var profile = new DeviceProfile { Product = "V12M RECEIVER", Vid = "395A", Pid = "0006" };
            check(profile.Matches("V12M RECEIVER", new[] { "HID\\VID_395A&PID_0006&MI_00" }) && !profile.Matches("V12M RECEIVER", new[] { "HID\\VID_395A&PID_0007" }) && !profile.Matches("OTHER RECEIVER", new[] { "HID\\VID_395A&PID_0006" }), "Model mapping requires both product string and exact hardware identity");
            var merged = BatteryRules.Merge(new[] { new DeviceBattery { Id = "a", Kind = "鼠标", Name = "HID-compliant mouse", Percent = 70, Source = "Windows 电池属性" }, new DeviceBattery { Id = "a", Kind = "鼠标", Name = "Wireless Mouse", Percent = 65, Charging = true, Source = "Bluetooth GATT" }, new DeviceBattery { Id = "a", Kind = "键盘", Name = "Keyboard" } });
            check(merged.Count == 2 && merged[0].Name == "Wireless Mouse" && merged[0].Percent == 65 && merged[0].Charging == true && merged[0].Source == "Bluetooth GATT", "Duplicate interfaces preserve name and prefer live battery data");
            var offline = BatteryRules.Merge(new[] { new DeviceBattery { Id = "x", Kind = "耳机", Name = "Headset", Percent = 80, Connected = false } }).Single();
            check(!offline.Percent.HasValue && !offline.Charging.HasValue, "Known disconnected devices never show stale cached percentages");
            check(new DeviceBattery { Percent = 60, Charging = null }.StatusText == "充电状态未知" && new DeviceBattery { Percent = 60, Charging = false }.StatusText == "未充电" && new DeviceBattery { Percent = null }.StatusText == "暂未读取电量", "Missing battery and charging readings stay explicit");
            check(Marshal.SizeOf(typeof(Native.DeviceInfo)) == 32 && Marshal.SizeOf(typeof(Native.InterfaceInfo)) == 32 && Marshal.SizeOf(typeof(Native.PropertyKey)) == 20, "SetupAPI x64 structure layout");
            check(Marshal.SizeOf(typeof(BleBattery.Uuid)) == 20 && Marshal.SizeOf(typeof(BleBattery.Service)) == 24 && Marshal.SizeOf(typeof(BleBattery.Characteristic)) == 36, "Bluetooth GATT structure layout");
            var first = new PnpNode { Id = "first", Parent = "second" }; var second = new PnpNode { Id = "second", Parent = "first" };
            check(WindowsBatteryProvider.Ancestors(first, new Dictionary<string, PnpNode> { { "first", first }, { "second", second } }).Count == 2, "Malformed device ancestry terminates safely");
            var image = Theme.Pet(); var pixels = new byte[4]; image.CopyPixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
            var embeddedSkinIds = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .Where(r => r.StartsWith("BatteryPet.Skins.", StringComparison.Ordinal) && r.EndsWith(".skin.json", StringComparison.Ordinal))
                .Select(r => r.Substring("BatteryPet.Skins.".Length, r.Length - "BatteryPet.Skins.".Length - ".skin.json".Length)).ToList();
            check(embeddedSkinIds.Count > 0 && embeddedSkinIds.All(id => PetSkins.All.Any(s => s.Id == id)), "Every contributed embedded skin decodes successfully and appears in the catalog");
            var whalePopup = PetSkins.Find(PetSkins.DefaultId).Frame(true);
            check(image.PixelWidth == 1254 && image.PixelHeight == 1254 && pixels[3] == 0 && whalePopup.PixelWidth == 1254 && whalePopup.PixelHeight == 1254 && !ReferenceEquals(image, whalePopup) && TransparentCorners(image) && TransparentCorners(whalePopup), "Default whale embeds the supplied transparent idle and popup frames on equal canvases");
            var phoebe = PetSkins.Find("phoebe"); var idleFrame = phoebe.Frame(false); var saluteFrame = phoebe.Frame(true);
            check(PetSkins.Find(null).Id == "whale" && PetSkins.Find("old-or-unknown-skin").Name == "大肥鱼" && phoebe.Name == "菲比啾比", "Old or invalid skin choices fall back to the original whale character");
            check(idleFrame.PixelWidth == 610 && idleFrame.PixelHeight == 610 && saluteFrame.PixelWidth == 610 && saluteFrame.PixelHeight == 610 && !ReferenceEquals(idleFrame, saluteFrame), "Phoebe has distinct idle and salute frames on identical canvases");
            var idleBounds = SpriteBounds(idleFrame); var saluteBounds = SpriteBounds(saluteFrame);
            // Retouched outlines may differ by a few antialias pixels on the
            // same 610px canvas; four source pixels are less than one screen
            // pixel at the normal 160px pet size.
            check(Math.Abs(idleBounds.Top - saluteBounds.Top) <= 1 && Math.Abs(idleBounds.Bottom - saluteBounds.Bottom) <= 1 && Math.Abs(idleBounds.Width - saluteBounds.Width) <= 4, "Phoebe poses keep the same visible height, width and hat position");
            check(TransparentCorners(idleFrame) && TransparentCorners(saluteFrame) && ContainsColor(idleFrame, false) && ContainsColor(saluteFrame, false), "Phoebe backgrounds are transparent while white hat and sleeves remain opaque");
            var animatedPet = new PetView("phoebe");
            check(!animatedPet.IsSaluting && animatedPet.Width == 210 && animatedPet.Height == 236, "Phoebe starts in the idle pose with the standard pet bounds");
            animatedPet.SetPopupVisible(true, false); animatedPet.SetPopupVisible(true, false);
            check(animatedPet.IsSaluting, "Phoebe holds the salute throughout popup visibility and repeated refresh state");
            animatedPet.SetSkin("whale", false);
            check(animatedPet.SkinId == "whale" && !animatedPet.IsSaluting, "Switching away while the popup is open restores the whale skin");
            animatedPet.SetSkin("phoebe", false);
            check(animatedPet.IsSaluting, "Switching to Phoebe while the popup is open immediately uses the salute pose");
            animatedPet.SetPopupVisible(false, false);
            check(!animatedPet.IsSaluting && animatedPet.Width == 210 && animatedPet.Height == 236, "Closing or hiding the popup restores idle without changing pet bounds");
            var white = Render(new BatteryGlyph(75, false), 76, 34); var green = Render(new BatteryGlyph(75, true), 76, 34);
            check(ContainsColor(white, false) && ContainsColor(green, true), "Battery renders white without charging and green when charging");
            var jsonPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "test-roundtrip.json");
            try
            {
                JsonFile.Write(jsonPath, new ScanResult { Devices = new List<DeviceBattery> { new DeviceBattery { Name = "无线耳机", Percent = null, Charging = null } } });
                var read = JsonFile.Read<ScanResult>(jsonPath);
                check(read.Devices[0].Name == "无线耳机" && read.Devices[0].Percent == null && read.Devices[0].Charging == null, "Diagnostic JSON retains Chinese names and unknown readings");
                JsonFile.Write(jsonPath, new Settings { Skin = "phoebe" });
                check(JsonFile.Read<Settings>(jsonPath).Skin == "phoebe", "Skin selection survives settings serialization");
                File.WriteAllText(jsonPath, "{\"Left\":10,\"Top\":20,\"Size\":210,\"Topmost\":true,\"IncludeUsb\":true}");
                check(PetSkins.Find(JsonFile.Read<Settings>(jsonPath).Skin).Id == "whale", "Settings from before skin support remain compatible");
            }
            finally { if (File.Exists(jsonPath)) File.Delete(jsonPath); }
            ExtensionVerification.Run(check, Path.GetDirectoryName(Path.GetFullPath(path)));
            JsonFile.Write(path, report); return report.Failures.Count == 0 ? 0 : 1;
        }
        private static bool TransparentCorners(BitmapSource image)
        {
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            return pixels[3] == 0 && pixels[(image.PixelWidth - 1) * 4 + 3] == 0 && pixels[(image.PixelHeight - 1) * image.PixelWidth * 4 + 3] == 0 && pixels[pixels.Length - 1] == 0;
        }
        private static Rect SpriteBounds(BitmapSource image)
        {
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            int left = image.PixelWidth, top = image.PixelHeight, right = 0, bottom = 0;
            for (int y = 0; y < image.PixelHeight; y++) for (int x = 0; x < image.PixelWidth; x++)
                if (pixels[(y * image.PixelWidth + x) * 4 + 3] > 200) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            return new Rect(left, top, right - left + 1, bottom - top + 1);
        }
        private static bool ContainsColor(BitmapSource image, bool green)
        {
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            for (int i = 0; i < pixels.Length; i += 4) { int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2]; if (pixels[i + 3] > 200 && (green ? g > 190 && g > r + 50 : r > 230 && g > 230 && b > 230)) return true; } return false;
        }
        private static RenderTargetBitmap Render(FrameworkElement element, int width, int height)
        {
            element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(element); return image;
        }
        internal static void Preview(string path, ScanResult live)
        {
            var canvas = new Canvas { Width = 900, Height = 860, Background = new LinearGradientBrush(Color.FromRgb(27, 33, 57), Color.FromRgb(49, 60, 82), 35) };
            var header = Theme.Label("小电量管家", 24, Theme.Text); header.FontWeight = FontWeights.SemiBold; Canvas.SetLeft(header, 42); Canvas.SetTop(header, 30); canvas.Children.Add(header);
            var note = Theme.Label(live == null ? "界面预览 · 以下为示例设备与电量，非本机实测" : "本机实测 · 未开放电量接口的设备显示为未知", 12, Theme.Muted); Canvas.SetLeft(note, 42); Canvas.SetTop(note, 69); canvas.Children.Add(note);
            var bubble = new BubbleView();
            bubble.SetTemperatures(live == null ? new ThermalSnapshot { Readings = new List<TemperatureReading> { new TemperatureReading { Kind = "CPU", Name = "示例 CPU", Celsius = 48, Sensor = "CPU Package", Source = "示例", Note = "CPU 封装温度" }, new TemperatureReading { Kind = "GPU", Name = "示例显卡", Celsius = 42, Sensor = "GPU Core", Source = "示例", Note = "显卡核心温度" } } } : ThermalProvider.Read());
            bubble.SetState(live ?? new ScanResult { ScannedAt = DateTime.Today.AddHours(16).AddMinutes(28), Devices = new List<DeviceBattery> {
                new DeviceBattery { Id = "demo1", Name = "Wireless Mouse", Kind = "鼠标", Connection = "蓝牙", Percent = 76, Charging = false, Source = "示例" },
                new DeviceBattery { Id = "demo2", Name = "Wireless Keyboard", Kind = "键盘", Connection = "蓝牙", Percent = 42, Charging = true, Source = "示例" },
                new DeviceBattery { Id = "demo3", Name = "无线耳机", Kind = "耳机", Connection = "USB / 接收器", Percent = null, Charging = null, Source = "示例" }
            } }, false, null);
            Canvas.SetLeft(bubble, 130); Canvas.SetTop(bubble, 118); canvas.Children.Add(bubble);
            var pet = new PetView(); pet.Caption.Text = (live == null ? 3 : live.Devices.Count) + " 个设备 · 点击查看"; Canvas.SetLeft(pet, 570); Canvas.SetTop(pet, 260); canvas.Children.Add(pet);
            var connector = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 510,365 L 525,376 L 510,387 Z"), Fill = Theme.Brush("#151928"), Stroke = Theme.Brush("#42445E"), StrokeThickness = 1 }; canvas.Children.Add(connector);
            var image = Render(canvas, 900, 860); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
