using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PKOAssetBatchExporter
{
    internal static partial class Program
    {
        // Converts every client/effect/*.eff (CMPModelEff format, versions 1-7) to JSON and its textures to PNG/TGA.
        private static void ExportEffects()
        {
            string dir = Path.Combine(outputRoot, "Effects");
            Directory.CreateDirectory(dir);
            int ok = 0, bad = 0;
            foreach (string file in Directory.GetFiles(Path.Combine(clientRoot, "effect"), "*.eff"))
            {
                try
                {
                    string json = ParseEffect(file);
                    File.WriteAllText(Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + ".json"), json, new UTF8Encoding(false));
                    ok++;
                }
                catch (Exception e)
                {
                    bad++;
                    Console.Error.WriteLine("EFF_FAILED {0}: {1}", Path.GetFileName(file), e.Message);
                }
            }
            converted += ok; failed += bad;
            Console.WriteLine("Effects converted {0}, failed {1}", ok, bad);
        }

        private static string F(float v) { return float.IsNaN(v) || float.IsInfinity(v) ? "0" : v.ToString("R", CultureInfo.InvariantCulture); }
        private static string Q(string s) { return "\"" + (s ?? "").Replace("\\", "/").Replace("\"", "") + "\""; }

        private static string ReadFixed(BinaryReader r, int n)
        {
            byte[] b = r.ReadBytes(n);
            int z = Array.IndexOf(b, (byte)0); if (z < 0) z = b.Length;
            return Encoding.GetEncoding(28591).GetString(b, 0, z);
        }

        private static void Vec3s(BinaryReader r, StringBuilder sb, string key, int n)
        {
            sb.Append('"').Append(key).Append("\":[");
            for (int i = 0; i < n * 3; i++) { if (i > 0) sb.Append(','); sb.Append(F(r.ReadSingle())); }
            sb.Append("],");
        }

        private static string ParseEffect(string path)
        {
            var sb = new StringBuilder();
            using (var fs = File.OpenRead(path))
            using (var r = new BinaryReader(fs))
            {
                uint version = r.ReadUInt32();
                if (version < 1 || version > 7) throw new InvalidDataException("version " + version);
                int tech = r.ReadInt32();
                bool usePath = r.ReadBoolean(); string pathName = ReadFixed(r, 32);
                bool useSound = r.ReadBoolean(); string soundName = ReadFixed(r, 32);
                bool rotating = r.ReadBoolean();
                float rx = r.ReadSingle(), ry = r.ReadSingle(), rz = r.ReadSingle(), rv = r.ReadSingle();
                int count = r.ReadInt32();
                if (count < 0 || count > 256) throw new InvalidDataException("effect count " + count);
                sb.Append("{\"version\":").Append(version).Append(",\"tech\":").Append(tech)
                  .Append(",\"sound\":").Append(Q(useSound ? soundName : "")).Append(",\"path\":").Append(Q(usePath ? pathName : ""))
                  .Append(",\"rotating\":").Append(rotating ? "true" : "false")
                  .Append(",\"rotaAxis\":[").Append(F(rx)).Append(',').Append(F(ry)).Append(',').Append(F(rz)).Append("],\"rotaVel\":").Append(F(rv))
                  .Append(",\"effects\":[");
                for (int e = 0; e < count; e++)
                {
                    if (e > 0) sb.Append(',');
                    ParseSubEffect(r, sb, version);
                }
                sb.Append("]}");
                if (fs.Position != fs.Length) Console.Error.WriteLine("EFF_TRAILING {0}: {1} bytes", Path.GetFileName(path), fs.Length - fs.Position);
            }
            return sb.ToString();
        }

        private static void ParseSubEffect(BinaryReader r, StringBuilder sb, uint version)
        {
            string name = ReadFixed(r, 32);
            int type = r.ReadInt32(), src = r.ReadInt32(), dst = r.ReadInt32();
            float length = r.ReadSingle();
            int frames = r.ReadUInt16();
            sb.Append("{\"name\":").Append(Q(name)).Append(",\"type\":").Append(type).Append(",\"src\":").Append(src).Append(",\"dst\":").Append(dst)
              .Append(",\"length\":").Append(F(length)).Append(",\"frames\":").Append(frames).Append(',');
            sb.Append("\"times\":[");
            for (int i = 0; i < frames; i++) { if (i > 0) sb.Append(','); sb.Append(F(r.ReadSingle())); }
            sb.Append("],");
            Vec3s(r, sb, "sizes", frames); Vec3s(r, sb, "angles", frames); Vec3s(r, sb, "pos", frames);
            sb.Append("\"colors\":[");
            for (int i = 0; i < frames * 4; i++) { if (i > 0) sb.Append(','); sb.Append(F(r.ReadSingle())); }
            sb.Append("],");

            int verCount = r.ReadUInt16(), coordCount = r.ReadUInt16(); float coordTime = r.ReadSingle();
            sb.Append("\"verCount\":").Append(verCount).Append(",\"coordTime\":").Append(F(coordTime)).Append(",\"coords\":[");
            for (int i = 0; i < coordCount * verCount * 2; i++) { if (i > 0) sb.Append(','); sb.Append(F(r.ReadSingle())); }
            sb.Append("],");

            int texCount = r.ReadUInt16(); float texTime = r.ReadSingle();
            string tex = ReadFixed(r, 32).ToLowerInvariant();
            sb.Append("\"texCount\":").Append(texCount).Append(",\"texTime\":").Append(F(texTime)).Append(",\"texture\":").Append(Q(StripTexExt(tex))).Append(",\"texUV\":[");
            for (int i = 0; i < texCount * verCount * 2; i++) { if (i > 0) sb.Append(','); sb.Append(F(r.ReadSingle())); }
            sb.Append("],");

            string model = ReadFixed(r, 32);
            bool billboard = r.ReadBoolean(); int vsIndex = r.ReadInt32();
            int segments = 0; float height = 0, radius = 0, botRadius = 0;
            if (version > 1) { segments = r.ReadInt32(); height = r.ReadSingle(); radius = r.ReadSingle(); botRadius = r.ReadSingle(); }
            var frameTex = new List<string>(); float frameTexTime = 0;
            if (version > 2)
            {
                int n = r.ReadUInt16(); frameTexTime = r.ReadSingle();
                for (int i = 0; i < n; i++) frameTex.Add(StripTexExt(ReadFixed(r, 32).ToLowerInvariant()));
                frameTexTime = r.ReadSingle();
            }
            int useParam = 0; var cyl = new List<float>();
            if (version > 3)
            {
                useParam = r.ReadInt32();
                if (useParam > 0)
                    for (int i = 0; i < frames; i++) { cyl.Add(r.ReadInt32()); cyl.Add(r.ReadSingle()); cyl.Add(r.ReadSingle()); cyl.Add(r.ReadSingle()); }
            }
            bool rotaLoop = false; float[] rotaVec = new float[4]; bool alpha = false, rotaBoard = false;
            if (version > 4) { rotaLoop = r.ReadBoolean(); for (int i = 0; i < 4; i++) rotaVec[i] = r.ReadSingle(); }
            if (version > 5) alpha = r.ReadBoolean();
            if (version > 6) rotaBoard = r.ReadBoolean();

            sb.Append("\"model\":").Append(Q(model)).Append(",\"billboard\":").Append(billboard ? "true" : "false")
              .Append(",\"segments\":").Append(segments).Append(",\"height\":").Append(F(height)).Append(",\"radius\":").Append(F(radius)).Append(",\"botRadius\":").Append(F(botRadius))
              .Append(",\"frameTex\":[").Append(string.Join(",", frameTex.Select(Q))).Append("],\"frameTexTime\":").Append(F(frameTexTime))
              .Append(",\"cylParams\":[").Append(string.Join(",", cyl.Select(F))).Append("],\"rotaLoop\":").Append(rotaLoop ? "true" : "false")
              .Append(",\"rotaVec\":[").Append(string.Join(",", rotaVec.Select(F))).Append("],\"alphaOnly\":").Append(alpha ? "true" : "false")
              .Append(",\"rotaBoard\":").Append(rotaBoard ? "true" : "false").Append('}');

            foreach (string t in new[] { StripTexExt(tex) }.Concat(frameTex)) CopyEffectTexture(t);
        }

        private static string StripTexExt(string t)
        {
            string ext = Path.GetExtension(t);
            return ext == ".dds" || ext == ".tga" || ext == ".bmp" || ext == ".png" ? t.Substring(0, t.Length - ext.Length) : t;
        }

        private static readonly HashSet<string> copiedEffectTextures = new HashSet<string>();
        private static void CopyEffectTexture(string stem)
        {
            if (string.IsNullOrWhiteSpace(stem) || !copiedEffectTextures.Add(stem)) return;
            string root = Path.Combine(clientRoot, "texture", "effect");
            string source = Directory.Exists(root) ? Directory.GetFiles(root, stem + ".*").FirstOrDefault() : null;
            if (source == null) { Console.Error.WriteLine("EFF_TEXTURE_MISSING " + stem); return; }
            string outDir = Path.Combine(outputRoot, "Textures", "effect");
            Directory.CreateDirectory(outDir);
            string ext = Path.GetExtension(source).ToLowerInvariant();
            if (ext == ".dds")
            {
                string target = Path.Combine(outDir, stem + ".png");
                if (File.Exists(target)) return;
                try
                {
                    using (var bmp = DecodeTexture(File.ReadAllBytes(source)))
                    {
                        if (bmp == null) throw new InvalidDataException("decode");
                        bmp.Save(target, ImageFormat.Png);
                    }
                }
                catch (Exception e) { Console.Error.WriteLine("EFF_TEXTURE_FAILED {0}: {1}", stem, e.Message); }
            }
            else
            {
                string target = Path.Combine(outDir, stem + ext);
                if (!File.Exists(target)) CopyWithRetry(source, target);
            }
        }
    }
}
