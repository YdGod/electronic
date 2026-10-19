using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace BatteryPet
{
    [DataContract]
    internal sealed class SkinFrames
    {
        [DataMember] public string Idle { get; set; }
        [DataMember] public string Popup { get; set; }
    }

    [DataContract]
    internal sealed class SkinManifest
    {
        [DataMember] public int SchemaVersion { get; set; }
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Description { get; set; }
        [DataMember] public string Author { get; set; }
        [DataMember] public string License { get; set; }
        [DataMember] public SkinFrames Frames { get; set; }

        internal void Validate()
        {
            if (SchemaVersion != 1 || !Regex.IsMatch(Id ?? "", "\\A[a-z][a-z0-9-]{0,63}\\z")
                || String.IsNullOrWhiteSpace(Name) || String.IsNullOrWhiteSpace(Author) || String.IsNullOrWhiteSpace(License)
                || Frames == null || !ValidFrame(Frames.Idle) || (!String.IsNullOrEmpty(Frames.Popup) && !ValidFrame(Frames.Popup)))
                throw new InvalidDataException("皮肤清单无效；请检查版本、标识、作者、授权和 PNG 文件名。");
        }
        private static bool ValidFrame(string name)
        { return Regex.IsMatch(name ?? "", "\\A[a-zA-Z0-9][a-zA-Z0-9_.-]*\\.png\\z"); }
    }

    internal sealed class PetSkin
    {
        internal readonly string Id, Name, Description, Author, License;
        private readonly BitmapImage idle, popup;

        internal PetSkin(SkinManifest manifest, Func<string, Stream> open)
        {
            manifest.Validate();
            Id = manifest.Id; Name = manifest.Name; Description = manifest.Description ?? "";
            Author = manifest.Author; License = manifest.License;
            idle = Load(open, manifest.Frames.Idle);
            string popupName = String.IsNullOrEmpty(manifest.Frames.Popup) ? manifest.Frames.Idle : manifest.Frames.Popup;
            popup = popupName == manifest.Frames.Idle ? idle : Load(open, popupName);
            if (idle.PixelWidth != popup.PixelWidth || idle.PixelHeight != popup.PixelHeight)
                throw new InvalidDataException("皮肤的日常帧和弹窗帧必须使用相同画布尺寸。");
        }

        internal BitmapImage Frame(bool popupVisible) { return popupVisible ? popup : idle; }

        private static BitmapImage Load(Func<string, Stream> open, string name)
        {
            using (var stream = open(name))
            {
                if (stream == null || stream.Length == 0 || stream.Length > 8 * 1024 * 1024)
                    throw new InvalidDataException("皮肤图片缺失或超过 8 MB：" + name);
                // Check dimensions before decoding to avoid allocating an oversized canvas.
                var header = new byte[24]; int count = 0;
                while (count < header.Length) { int read = stream.Read(header, count, header.Length - count); if (read == 0) break; count += read; }
                byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                if (count != 24 || !header.Take(8).SequenceEqual(signature) || header[12] != 73 || header[13] != 72 || header[14] != 68 || header[15] != 82)
                    throw new InvalidDataException("皮肤必须使用 PNG 图片：" + name);
                uint width = Dimension(header, 16), height = Dimension(header, 20);
                if (width == 0 || width > 4096 || height == 0 || height > 4096)
                    throw new InvalidDataException("皮肤画布每边必须为 1～4096 像素。");
                stream.Position = 0;
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
            }
        }
        private static uint Dimension(byte[] bytes, int offset)
        { return (uint)bytes[offset] << 24 | (uint)bytes[offset + 1] << 16 | (uint)bytes[offset + 2] << 8 | bytes[offset + 3]; }
    }

    internal static class PetSkins
    {
        internal const string DefaultId = "whale";
        internal static readonly IList<PetSkin> All = Load();

        internal static PetSkin Find(string id)
        { return All.FirstOrDefault(s => String.Equals(s.Id, id, StringComparison.Ordinal)) ?? All.First(s => s.Id == DefaultId); }

        private static IList<PetSkin> Load()
        {
            var result = new List<PetSkin>();
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var resource in assembly.GetManifestResourceNames().Where(r => r.StartsWith("BatteryPet.Skins.", StringComparison.Ordinal) && r.EndsWith(".skin.json", StringComparison.Ordinal)).OrderBy(r => r, StringComparer.Ordinal))
            {
                string prefix = resource.Substring(0, resource.Length - "skin.json".Length);
                try
                {
                    SkinManifest manifest;
                    using (var stream = assembly.GetManifestResourceStream(resource)) manifest = ReadManifest(stream);
                    if (prefix != "BatteryPet.Skins." + manifest.Id + ".") throw new InvalidDataException("内嵌皮肤 ID 与资源目录不一致。");
                    result.Add(new PetSkin(manifest, name => assembly.GetManifestResourceStream(prefix + name)));
                }
                catch (Exception ex) { AppLog.Write("内嵌皮肤 " + resource + "：" + ex.Message); }
            }
            if (!result.Any(s => s.Id == DefaultId)) throw new InvalidDataException("构建缺少默认 whale 皮肤。");
            LoadDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skins"), result);
            LoadDirectory(Path.Combine(Settings.DirectoryPath, "skins"), result);
            return result.AsReadOnly();
        }

        internal static void LoadDirectory(string root, List<PetSkin> result)
        {
            if (!Directory.Exists(root)) return;
            foreach (var directory in Directory.GetDirectories(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string path = Path.Combine(directory, "skin.json");
                    if (!File.Exists(path)) continue;
                    SkinManifest manifest;
                    using (var stream = File.OpenRead(path)) manifest = ReadManifest(stream);
                    if (!String.Equals(Path.GetFileName(directory), manifest.Id, StringComparison.Ordinal))
                        throw new InvalidDataException("皮肤目录名必须与 Id 相同。");
                    if (result.Any(s => s.Id == manifest.Id)) { AppLog.Write("跳过重复皮肤 ID：" + manifest.Id); continue; }
                    result.Add(new PetSkin(manifest, name => File.OpenRead(Path.Combine(directory, name))));
                }
                catch (Exception ex) { AppLog.Write("外部皮肤 " + directory + "：" + ex.Message); }
            }
        }

        private static SkinManifest ReadManifest(Stream stream)
        {
            if (stream == null || stream.Length > 65536) throw new InvalidDataException("皮肤清单缺失或超过 64 KB。");
            var manifest = (SkinManifest)new DataContractJsonSerializer(typeof(SkinManifest)).ReadObject(stream);
            if (manifest == null) throw new InvalidDataException("皮肤清单为空。");
            manifest.Validate(); return manifest;
        }
    }
}
