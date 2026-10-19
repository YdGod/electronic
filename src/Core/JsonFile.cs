using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BatteryPet
{
    public static class JsonFile
    {
        public static void Write<T>(string path, T value)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(folder);
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        }
        public static T Read<T>(string path)
        {
            using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }
}
