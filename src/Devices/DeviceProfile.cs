using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BatteryPet
{
    [DataContract]
    public sealed class DeviceProfile
    {
        [DataMember] public string Product;
        [DataMember] public string Vid;
        [DataMember] public string Pid;
        [DataMember] public string Name;
        [DataMember] public string Kind;
        [DataMember] public string Connection;
        public bool Matches(string product, IEnumerable<string> instanceIds)
        {
            if (!ValidIdentity(this) || product == null || instanceIds == null || product.IndexOf(Product, StringComparison.OrdinalIgnoreCase) < 0) return false;
            string pattern = "VID_" + Vid + "&PID_" + Pid + "(?=&|\\\\|$)";
            return instanceIds.Any(id => id != null && System.Text.RegularExpressions.Regex.IsMatch(id, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }
        public static List<DeviceProfile> Load()
        {
            return LoadFrom(AppDomain.CurrentDomain.BaseDirectory);
        }
        internal static List<DeviceProfile> LoadFrom(string root)
        {
            var result = new List<DeviceProfile>();
            // Existing users may retain their portable profiles.json overrides.
            string legacy = Path.Combine(root, "profiles.json");
            if (File.Exists(legacy))
            {
                try { result.AddRange((JsonFile.Read<List<DeviceProfile>>(legacy) ?? new List<DeviceProfile>()).Where(Valid)); }
                catch (Exception ex) { AppLog.Write("设备名称配置 " + legacy + "：" + ex.Message); }
            }
            string directory = Path.Combine(root, "devices", "profiles");
            if (!Directory.Exists(directory)) return result;
            foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var profile = JsonFile.Read<DeviceProfile>(file);
                    if (!Valid(profile)) throw new InvalidDataException("设备配置字段不完整或 VID/PID 格式无效。");
                    result.Add(profile);
                }
                catch (Exception ex) { AppLog.Write("设备名称配置 " + file + "：" + ex.Message); }
            }
            return result;
        }
        private static bool Valid(DeviceProfile profile)
        { return ValidIdentity(profile) && !String.IsNullOrWhiteSpace(profile.Name); }
        private static bool ValidIdentity(DeviceProfile profile)
        {
            return profile != null && !String.IsNullOrWhiteSpace(profile.Product)
                && System.Text.RegularExpressions.Regex.IsMatch(profile.Vid ?? "", "\\A[0-9a-fA-F]{4}\\z")
                && System.Text.RegularExpressions.Regex.IsMatch(profile.Pid ?? "", "\\A[0-9a-fA-F]{4}\\z");
        }
    }
}
