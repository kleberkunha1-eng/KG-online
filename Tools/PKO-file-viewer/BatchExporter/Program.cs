using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FreeImageAPI;
using Mindpower;

namespace PKOAssetBatchExporter
{
    internal static partial class Program
    {
        private static string clientRoot;
        private static string outputRoot;
        private static int converted;
        private static int failed;
        private static int rawCopied;

        private static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length < 3 || (args[2] != "--all" && args[2] != "--file" && args[2] != "--raw" && args[2] != "--missing-models" && args[2] != "--scene-models" && args[2] != "--skinned" && args[2] != "--charparts" && args[2] != "--charextra" && args[2] != "--effects"))
            {
                Console.Error.WriteLine("Usage: PKOAssetBatchExporter <clientRoot> <outputRoot> --all|--file <modelPath>");
                return 2;
            }

            clientRoot = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            outputRoot = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(outputRoot);

            if (args[2] == "--all")
            {
                ImportModels(false);
                ImportTextures();
                ImportRawFolders();
            }
            else if (args[2] == "--raw")
            {
                ImportRawFolders();
            }
            else if (args[2] == "--missing-models")
            {
                ImportModels(true);
            }
            else if (args[2] == "--effects")
            {
                ExportEffects();
            }
            else if (args[2] == "--charparts")
            {
                for (int i = 3; i < args.Length; i++)
                {
                    try { ExportSkinned(args[i]); ExportCharParts(args[i]); }
                    catch (Exception ex) { failed++; Console.Error.WriteLine("CHARPARTS_FAILED {0}: {1}", args[i], ex.Message); }
                }
            }
            else if (args[2] == "--charextra")
            {
                // args: <raceId> <text file with one model name per line>
                try { ExportExtraParts(args[3], File.ReadAllLines(args[4])); }
                catch (Exception ex) { failed++; Console.Error.WriteLine("CHAREXTRA_FAILED {0}: {1}", args[3], ex.Message); }
            }
            else if (args[2] == "--skinned")
            {
                for (int i = 3; i < args.Length; i++)
                {
                    try { ExportSkinned(args[i]); }
                    catch (Exception ex) { failed++; Console.Error.WriteLine("SKINNED_FAILED {0}: {1}", args[i], ex.Message); }
                }
            }
            else if (args[2] == "--scene-models")
            {
                ImportSceneModels();
            }
            else
            {
                if (args.Length < 4)
                {
                    Console.Error.WriteLine("--file requires a modelPath");
                    return 2;
                }
                Convert(Path.GetFullPath(args[3]));
            }

            Console.WriteLine("Converted: {0}; raw files copied: {1}; failed: {2}", converted, rawCopied, failed);
            return args[2] == "--raw" ? (rawCopied > 0 ? 0 : 1) : (converted > 0 ? 0 : 1);
        }

        private static void ImportSceneModels()
        {
            string sceneRoot = Path.Combine(clientRoot, "model", "scene");
            foreach (string file in Directory.GetFiles(sceneRoot, "*.lmo", SearchOption.AllDirectories))
                Convert(file);
        }

        private static Bitmap DecodeTexture(byte[] bytes)
        {
            Bitmap Decode(byte[] imageBytes)
            {
                try
                {
                    using (var stream = new MemoryStream(imageBytes))
                    using (var decoded = FreeImageBitmap.FromStream(stream))
                        return decoded.ToBitmap();
                }
                catch
                {
                    return null;
                }
            }

            Bitmap bitmap = Decode(bytes);
            if (bitmap != null || bytes.Length < 88)
                return bitmap;

            var repaired = new byte[bytes.Length];
            Array.Copy(bytes, bytes.Length - 48, repaired, 0, 44);
            Array.Copy(bytes, 44, repaired, 44, bytes.Length - 88);
            Array.Copy(bytes, 0, repaired, bytes.Length - 44, 44);
            return Decode(repaired);
        }

        private static void ImportModels(bool missingOnly)
        {
            string modelRoot = Path.Combine(clientRoot, "model");
            foreach (string file in Directory.GetFiles(modelRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".lgo", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".lmo", StringComparison.OrdinalIgnoreCase)))
            {
                if (missingOnly)
                {
                    string relative = file.Substring(clientRoot.Length);
                    string outputDirectory = Path.Combine(outputRoot, "Models", Path.GetDirectoryName(relative) ?? string.Empty);
                    string outputPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(file) + ".obj");
                    string rawFailurePath = Path.Combine(outputRoot, "Source", "UnconvertedModels", relative + ".bytes");
                    if (HasRenderableObj(outputPath) && !File.Exists(rawFailurePath))
                        continue;
                }

                Convert(file);
            }
        }

        private static bool HasRenderableObj(string path)
        {
            if (!File.Exists(path))
                return false;

            using (var stream = File.OpenRead(path))
            {
                int tailLength = (int)Math.Min(4096, stream.Length);
                stream.Seek(-tailLength, SeekOrigin.End);
                byte[] tail = new byte[tailLength];
                int bytesRead = stream.Read(tail, 0, tail.Length);
                string text = Encoding.ASCII.GetString(tail, 0, bytesRead);
                return text.StartsWith("f ", StringComparison.Ordinal)
                    || text.IndexOf("\nf ", StringComparison.Ordinal) >= 0
                    || text.IndexOf("\rf ", StringComparison.Ordinal) >= 0;
            }
        }

        private static void CopyWithRetry(string sourcePath, string destinationPath, bool overwrite = false)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Copy(sourcePath, destinationPath, overwrite);
                    return;
                }
                catch (IOException) when (attempt < 10)
                {
                    System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(250 * attempt));
                }
                catch (UnauthorizedAccessException) when (attempt < 10)
                {
                    System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(250 * attempt));
                }
            }
        }

        private static void Convert(string sourcePath)
        {
            try
            {
                string relative = sourcePath.Substring(clientRoot.Length);
                string outputDirectory = Path.Combine(outputRoot, "Models", Path.GetDirectoryName(relative) ?? string.Empty);
                Directory.CreateDirectory(outputDirectory);
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                string objPath = Path.Combine(outputDirectory, baseName + ".obj");
                string mtlPath = Path.Combine(outputDirectory, baseName + ".mtl");

                using (var obj = new StreamWriter(objPath, false, new UTF8Encoding(false)))
                using (var mtl = new StreamWriter(mtlPath, false, new UTF8Encoding(false)))
                {
                    obj.WriteLine("mtllib {0}.mtl", baseName);
                    uint vertexOffset = 1;
                    int geometryCount = 0;
                    if (sourcePath.EndsWith(".lgo", StringComparison.OrdinalIgnoreCase))
                    {
                        var geometry = new lwGeomObjInfo();
                        if (geometry.Load(sourcePath) != 0)
                            throw new InvalidDataException("Unable to parse LGO");
                        WriteGeometry(geometry, sourcePath, outputDirectory, obj, mtl, ref vertexOffset, ref geometryCount);
                    }
                    else
                    {
                        var model = new lwModelObjInfo();
                        if (model.Load(sourcePath) != 0)
                            throw new InvalidDataException("Unable to parse LMO");
                        for (int i = 0; i < model.geom_obj_num; i++)
                            WriteGeometry(model.geom_obj_seq[i], sourcePath, outputDirectory, obj, mtl, ref vertexOffset, ref geometryCount);
                    }

                    if (geometryCount == 0)
                        throw new InvalidDataException("Model contains no geometry");
                }

                converted++;
                if (converted % 100 == 0)
                    Console.WriteLine("Converted {0}: {1}", converted, relative);
            }
            catch (Exception exception)
            {
                failed++;
                string relative = sourcePath.Substring(clientRoot.Length);
                string outputDirectory = Path.Combine(outputRoot, "Models", Path.GetDirectoryName(relative) ?? string.Empty);
                string modelName = Path.GetFileNameWithoutExtension(sourcePath);
                string objPath = Path.Combine(outputDirectory, modelName + ".obj");
                string mtlPath = Path.Combine(outputDirectory, modelName + ".mtl");
                string rawPath = Path.Combine(outputRoot, "Source", "UnconvertedModels", relative + ".bytes");
                Console.Error.WriteLine("MODEL_SKIPPED {0}: {1}", relative, exception.Message);
                if (!HasRenderableObj(objPath))
                {
                    if (File.Exists(objPath)) File.Delete(objPath);
                    if (File.Exists(mtlPath)) File.Delete(mtlPath);
                }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(rawPath));
                    CopyWithRetry(sourcePath, rawPath, true);
                }
                catch (Exception copyException)
                {
                    Console.Error.WriteLine("RAW_MODEL_COPY_SKIPPED {0}: {1}", relative, copyException.Message);
                }
            }
        }

        private static void WriteGeometry(lwGeomObjInfo geometry, string sourcePath, string outputDirectory,
            StreamWriter obj, StreamWriter mtl, ref uint vertexOffset, ref int geometryCount)
        {
            lwMeshInfo mesh = geometry.mesh;
            if (mesh.vertex_seq == null || mesh.index_seq == null || mesh.subset_seq == null)
                return;

            string objectName = Path.GetFileNameWithoutExtension(sourcePath) + "_part_" + geometryCount++;
            obj.WriteLine("o {0}", objectName);
            foreach (D3DXVECTOR3 vertex in mesh.vertex_seq)
            {
                D3DXVECTOR3 position = vertex * geometry.header.mat_local;
                obj.WriteLine("v {0:R} {1:R} {2:R}", position.x, position.z, position.y);
            }

            bool hasUv = mesh.texcoord0_seq != null && mesh.texcoord0_seq.Length == mesh.vertex_seq.Length;
            bool hasNormals = mesh.normal_seq != null && mesh.normal_seq.Length == mesh.vertex_seq.Length;
            if (hasUv)
            {
                foreach (D3DXVECTOR2 uv in mesh.texcoord0_seq)
                    obj.WriteLine("vt {0:R} {1:R}", uv.x, 1f - uv.y);
            }
            if (hasNormals)
            {
                foreach (D3DXVECTOR3 normal in mesh.normal_seq)
                    obj.WriteLine("vn {0:R} {1:R} {2:R}", normal.x, normal.z, normal.y);
            }

            for (int subsetIndex = 0; subsetIndex < mesh.subset_seq.Length; subsetIndex++)
            {
                string materialName = objectName + "_material_" + subsetIndex;
                obj.WriteLine("usemtl {0}", materialName);
                mtl.WriteLine("newmtl {0}", materialName);
                if (geometry.mtl_seq != null && subsetIndex < geometry.mtl_seq.Length)
                {
                    lwMtlTexInfo material = geometry.mtl_seq[subsetIndex];
                    mtl.WriteLine("Ka {0:R} {1:R} {2:R}", material.mtl.amb.r, material.mtl.amb.g, material.mtl.amb.b);
                    mtl.WriteLine("Kd {0:R} {1:R} {2:R}", material.mtl.dif.r, material.mtl.dif.g, material.mtl.dif.b);
                    mtl.WriteLine("Ks {0:R} {1:R} {2:R}", material.mtl.spe.r, material.mtl.spe.g, material.mtl.spe.b);
                    if (material.tex_seq != null && material.tex_seq.Length > 0)
                    {
                        string texturePath = ExportTexture(sourcePath, outputDirectory, material.tex_seq[0].file_name);
                        if (texturePath != null)
                            mtl.WriteLine("map_Kd {0}", texturePath.Replace('\\', '/'));
                    }
                }

                lwSubsetInfo subset = mesh.subset_seq[subsetIndex];
                uint end = Math.Min((uint)mesh.index_seq.Length, subset.start_index + subset.primitive_num * 3);
                for (uint index = subset.start_index; index + 2 < end; index += 3)
                {
                    uint a = vertexOffset + mesh.index_seq[index];
                    uint b = vertexOffset + mesh.index_seq[index + 2];
                    uint c = vertexOffset + mesh.index_seq[index + 1];
                    WriteFace(obj, a, b, c, hasUv, hasNormals);
                }
            }

            vertexOffset += (uint)mesh.vertex_seq.Length;
        }

        private static void WriteFace(StreamWriter writer, uint a, uint b, uint c, bool hasUv, bool hasNormals)
        {
            if (hasUv && hasNormals)
                writer.WriteLine("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", a, b, c);
            else if (hasUv)
                writer.WriteLine("f {0}/{0} {1}/{1} {2}/{2}", a, b, c);
            else if (hasNormals)
                writer.WriteLine("f {0}//{0} {1}//{1} {2}//{2}", a, b, c);
            else
                writer.WriteLine("f {0} {1} {2}", a, b, c);
        }

        private static string ExportTexture(string sourcePath, string outputDirectory, char[] textureName)
        {
            string name = new string(textureName);
            int terminator = name.IndexOf('\0');
            if (terminator >= 0)
                name = name.Substring(0, terminator);
            name = name.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(name))
                return null;

            string relativeModel = sourcePath.Substring(Path.Combine(clientRoot, "model").Length).TrimStart(Path.DirectorySeparatorChar);
            string textureRoot = Path.Combine(clientRoot, "texture", Path.GetDirectoryName(relativeModel) ?? string.Empty);
            string sourceTexture = Path.Combine(textureRoot, name);
            if (!File.Exists(sourceTexture))
            {
                if (!Directory.Exists(textureRoot))
                    return null;
                string stem = Path.GetFileNameWithoutExtension(sourceTexture);
                sourceTexture = Directory.GetFiles(textureRoot, stem + ".*", SearchOption.TopDirectoryOnly).FirstOrDefault();
            }
            if (sourceTexture == null || !File.Exists(sourceTexture))
                return null;

            string textureRelative = sourceTexture.Substring(Path.Combine(clientRoot, "texture").Length).TrimStart(Path.DirectorySeparatorChar);
            bool convertToPng = string.Equals(Path.GetExtension(sourceTexture), ".dds", StringComparison.OrdinalIgnoreCase);
            string targetTexture = Path.Combine(outputRoot, "Textures", convertToPng ? Path.ChangeExtension(textureRelative, ".png") : textureRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetTexture));
            if (!File.Exists(targetTexture))
            {
                if (convertToPng)
                {
                    try
                    {
                        using (Bitmap bitmap = DecodeTexture(File.ReadAllBytes(sourceTexture)))
                        {
                            if (bitmap == null)
                                throw new InvalidDataException("Unable to decode DDS texture");
                            bitmap.Save(targetTexture, ImageFormat.Png);
                        }
                    }
                    catch (Exception exception)
                    {
                        targetTexture = Path.ChangeExtension(targetTexture, ".dds");
                        try
                        {
                            if (!File.Exists(targetTexture))
                                CopyWithRetry(sourceTexture, targetTexture);
                            Console.Error.WriteLine("DDS_NATIVE_FALLBACK {0}: {1}", name, exception.Message);
                        }
                        catch (Exception copyException)
                        {
                            Console.Error.WriteLine("TEXTURE_SKIPPED {0}: {1}", name, copyException.Message);
                            return null;
                        }
                    }
                }
                else
                {
                    CopyWithRetry(sourceTexture, targetTexture);
                }
            }

            Uri modelUri = new Uri(Path.GetFullPath(outputDirectory) + Path.DirectorySeparatorChar);
            Uri textureUri = new Uri(Path.GetFullPath(targetTexture));
            return Uri.UnescapeDataString(modelUri.MakeRelativeUri(textureUri).ToString());
        }

        private static void ImportTextures()
        {
            string textureRoot = Path.Combine(clientRoot, "texture");
            string[] supportedExtensions = { ".bmp", ".png", ".tga", ".dds", ".jpg", ".jpeg", ".psd" };
            foreach (string sourcePath in Directory.GetFiles(textureRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => supportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
            {
                string relative = sourcePath.Substring(textureRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                bool convertToPng = string.Equals(Path.GetExtension(sourcePath), ".dds", StringComparison.OrdinalIgnoreCase);
                string targetPath = Path.Combine(outputRoot, "Textures", convertToPng ? Path.ChangeExtension(relative, ".png") : relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                if (File.Exists(targetPath))
                    continue;

                try
                {
                    if (convertToPng)
                    {
                        using (Bitmap bitmap = DecodeTexture(File.ReadAllBytes(sourcePath)))
                        {
                            if (bitmap == null)
                                throw new InvalidDataException("Unable to decode DDS texture");
                            bitmap.Save(targetPath, ImageFormat.Png);
                        }
                    }
                    else
                    {
                        CopyWithRetry(sourcePath, targetPath);
                    }
                }
                catch (Exception exception)
                {
                    failed++;
                    string rawPath = Path.Combine(outputRoot, "Source", "UnconvertedTextures", relative + ".bytes");
                    Directory.CreateDirectory(Path.GetDirectoryName(rawPath));
                    CopyWithRetry(sourcePath, rawPath, true);
                }
            }
        }

        private static void ImportRawFolder(string sourceFolder, string destinationFolder)
        {
            string sourceRoot = Path.Combine(clientRoot, sourceFolder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(sourceRoot))
                return;

            foreach (string sourcePath in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                string relative = sourcePath.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                string extension = Path.GetExtension(sourcePath);
                bool nativeText = extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase);
                string targetName = nativeText ? relative : relative + ".bytes";
                string targetPath = Path.Combine(outputRoot, destinationFolder, targetName);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                if (!File.Exists(targetPath))
                {
                    CopyWithRetry(sourcePath, targetPath);
                    rawCopied++;
                }
            }
        }

        private static void ImportRawFolders()
        {
            ImportRawFolder("animation", "Source/Animation");
            ImportRawFolder("effect", "Source/Effects");
            ImportRawFolder("map", "Source/Maps");
            ImportRawFolder("shader", "Source/Shaders");
            ImportRawFolder("scripts/table", "Source/Tables");
            ImportRawFolder("music", "Audio");
        }
    }
}
