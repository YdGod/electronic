using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BatteryPet
{
    public static class AppLog
    {
        public static bool TraceEnabled;
        public static void Trace(string message) { if (TraceEnabled) Write("手势诊断：" + message); }
        private static readonly object Gate = new object();
        public static void Write(string message)
        {
            try { lock (Gate) { Directory.CreateDirectory(Settings.DirectoryPath); var path = Path.Combine(Settings.DirectoryPath, "app.log"); if (File.Exists(path) && new FileInfo(path).Length > 512000) File.Delete(path); File.AppendAllText(path, DateTime.Now.ToString("s") + " " + message + Environment.NewLine); } }
            catch { }
        }
    }
}
