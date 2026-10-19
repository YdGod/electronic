using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BatteryPet
{
    [DataContract]
    public sealed class Settings
    {
        [DataMember] public double Left = -1;
        [DataMember] public double Top = -1;
        [DataMember] public bool PositionSaved;
        [DataMember] public double Size = 210;
        [DataMember] public bool Topmost = true;
        [DataMember] public string Skin = PetSkins.DefaultId;
        // USB does not expose whether a peripheral is wireless. Include candidates, clearly labelled.
        [DataMember] public bool IncludeUsb = true;
        public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryPet");
        public static Settings Load()
        {
            try
            {
                var settings = JsonFile.Read<Settings>(Path.Combine(DirectoryPath, "settings.json"));
                if (Double.IsNaN(settings.Left) || Double.IsInfinity(settings.Left)) { settings.Left = -1; settings.PositionSaved = false; }
                if (Double.IsNaN(settings.Top) || Double.IsInfinity(settings.Top)) { settings.Top = -1; settings.PositionSaved = false; }
                if (settings.Size < 140 || settings.Size > 320 || Double.IsNaN(settings.Size)) settings.Size = 210;
                settings.Skin = PetSkins.Find(settings.Skin).Id;
                return settings;
            }
            catch { return new Settings(); }
        }
        public void Save()
        {
            try { Directory.CreateDirectory(DirectoryPath); JsonFile.Write(Path.Combine(DirectoryPath, "settings.json"), this); }
            catch (Exception ex) { AppLog.Write("保存设置失败：" + ex.Message); }
        }
    }
}
