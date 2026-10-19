using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BatteryPet
{
    public static class BatteryRules
    {
        public static int? Percentage(long value) { return value >= 0 && value <= 100 ? (int?)value : null; }
        public static bool? BleCharging(byte state)
        {
            var charging = (state >> 4) & 3;
            return charging == 2 ? (bool?)true : charging == 1 ? (bool?)false : null;
        }
        public static string Kind(string deviceClass, string name)
        {
            var value = (name ?? "").ToLowerInvariant();
            if (deviceClass == "Mouse" || value.Contains("mouse") || value.Contains("鼠标")) return "鼠标";
            if (deviceClass == "Keyboard" || value.Contains("keyboard") || value.Contains("键盘")) return "键盘";
            if (deviceClass == "AudioEndpoint" || deviceClass == "MEDIA" || value.Contains("head") || value.Contains("earbud") || value.Contains("耳机") || value.Contains("gsp")) return "耳机";
            return "设备";
        }
        public static int NameScore(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return -10;
            var text = name.ToLowerInvariant();
            if (text.Contains("controller") || text.Contains("控制器") || text.Contains("composite") || text.Contains("复合设备") || text.Contains("hub") || text.Contains("集线器")) return -1;
            if (text.Contains("hid") || text.Contains("compliant") || text == "usb input device" || text == "usb 输入设备") return 0;
            if (text.Contains("receiver") || text.Contains("接收器")) return 1;
            if (text.Contains("microphone") || text.Contains("麦克风")) return 2;
            return 5;
        }
        public static List<DeviceBattery> Merge(IEnumerable<DeviceBattery> values)
        {
            var result = new List<DeviceBattery>();
            foreach (var group in values.GroupBy(d => d.Id + "|" + d.Kind, StringComparer.OrdinalIgnoreCase))
            {
                var best = group.OrderByDescending(d => NameScore(d.Name)).First();
                var battery = group.Where(d => d.Percent.HasValue).OrderByDescending(d => d.Source == "Bluetooth GATT" || d.Source == "USB HID" ? 2 : 1).FirstOrDefault();
                if (battery != null) { best.Percent = battery.Percent; best.Charging = battery.Charging; best.Source = battery.Source; best.ReadAt = battery.ReadAt; best.Note = battery.Note; }
                if (group.Any(d => d.Connected == false) && !group.Any(d => d.Connected == true)) { best.Connected = false; best.Percent = null; best.Charging = null; best.ReadAt = null; }
                result.Add(best);
            }
            return result.OrderBy(d => d.Kind == "鼠标" ? 0 : d.Kind == "键盘" ? 1 : d.Kind == "耳机" ? 2 : 3).ThenBy(d => d.Name).ToList();
        }
    }
}
