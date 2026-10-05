using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Mindpower;

namespace PKOAssetBatchExporter
{
    internal static partial class Program
    {
        // Exports every equipment part mesh (model\character\<type>*.lgo) of a character type as Skinned\parts\<name>.json,
        // with bone indices expressed in the skeleton of animation\<type>.lab.
        private static void ExportCharParts(string typeId)
        {
            string animPath = Path.Combine(clientRoot, "animation", typeId + ".lab");
            var bone = new lwAnimDataBone();
            if (bone.Load(animPath) != 0) throw new InvalidDataException("Unable to parse LAB " + typeId);
            var ids = new Dictionary<uint, int>();
            for (int i = 0; i < (int)bone._header.bone_num; i++) ids[bone._base_seq[i].id] = i;

            string dir = Path.Combine(outputRoot, "Skinned", "parts");
            Directory.CreateDirectory(dir);
            foreach (string file in Directory.GetFiles(Path.Combine(clientRoot, "model", "character"), typeId + "??????.lgo"))
            {
                try
                {
                    var geometry = new lwGeomObjInfo();
                    if (geometry.Load(file) != 0 || geometry.mesh.vertex_seq == null) { failed++; continue; }
                    string json = PartJson(geometry, file, ids);
                    File.WriteAllText(Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + ".json"), json, new UTF8Encoding(false));
                    converted++;
                }
                catch (Exception ex) { failed++; Console.Error.WriteLine("PART_FAILED {0}: {1}", file, ex.Message); }
            }
        }

        // Exports specific part models (special skins such as 9011xxxxxx) skinned to the skeleton of the given race.
        private static void ExportExtraParts(string typeId, IEnumerable<string> names)
        {
            var bone = new lwAnimDataBone();
            if (bone.Load(Path.Combine(clientRoot, "animation", typeId + ".lab")) != 0) throw new InvalidDataException("Unable to parse LAB " + typeId);
            var ids = new Dictionary<uint, int>();
            for (int i = 0; i < (int)bone._header.bone_num; i++) ids[bone._base_seq[i].id] = i;
            string dir = Path.Combine(outputRoot, "Skinned", "parts");
            Directory.CreateDirectory(dir);
            foreach (string name in names)
            {
                string file = Path.Combine(clientRoot, "model", "character", name + ".lgo");
                try
                {
                    var geometry = new lwGeomObjInfo();
                    if (!File.Exists(file) || geometry.Load(file) != 0 || geometry.mesh.vertex_seq == null) { failed++; continue; }
                    string json = PartJson(geometry, file, ids);
                    json = json.Substring(0, json.Length - 1) + ",\"race\":" + int.Parse(typeId) + "}";
                    File.WriteAllText(Path.Combine(dir, name + ".json"), json, new UTF8Encoding(false));
                    converted++;
                }
                catch (Exception ex) { failed++; Console.Error.WriteLine("PART_FAILED {0}: {1}", file, ex.Message); }
            }
        }

        private static string PartJson(lwGeomObjInfo geometry, string modelPath, Dictionary<uint, int> ids)
        {
            lwMeshInfo mesh = geometry.mesh;
            var pos = mesh.vertex_seq.Select(v => v * geometry.header.mat_local).ToArray();
            var sb = new StringBuilder();
            sb.Append("{\"positions\":["); AppendFloats(sb, pos.SelectMany(p => new[] { p.x, p.y, p.z }));
            sb.Append("],\"normals\":[");
            if (mesh.normal_seq != null && mesh.normal_seq.Length == pos.Length)
                AppendFloats(sb, mesh.normal_seq.SelectMany(n => new[] { n.x, n.y, n.z }));
            sb.Append("],\"uvs\":[");
            if (mesh.texcoord0_seq != null && mesh.texcoord0_seq.Length == pos.Length)
                AppendFloats(sb, mesh.texcoord0_seq.SelectMany(t => new[] { t.x, 1f - t.y }));
            var weights = new List<float>(); var indices = new List<int>();
            if (mesh.blend_seq != null && mesh.blend_seq.Length == pos.Length)
            {
                foreach (var blend in mesh.blend_seq)
                {
                    float sum = blend.weight.Sum(); if (sum <= 0f) sum = 1f;
                    for (int k = 0; k < 4; k++)
                    {
                        int local = blend.index[k], boneIndex = 0;
                        if (mesh.bone_index_seq != null && local < mesh.bone_index_seq.Length)
                            ids.TryGetValue(mesh.bone_index_seq[local], out boneIndex);
                        indices.Add(boneIndex);
                        weights.Add(blend.weight[k] / sum);
                    }
                }
            }
            sb.Append("],\"boneIndices\":[").Append(string.Join(",", indices.Select(i => i.ToString(CultureInfo.InvariantCulture))));
            sb.Append("],\"boneWeights\":["); AppendFloats(sb, weights);
            sb.Append("],\"subsets\":[");
            for (int s = 0; s < mesh.subset_seq.Length; s++)
            {
                var subset = mesh.subset_seq[s];
                uint end = Math.Min((uint)mesh.index_seq.Length, subset.start_index + subset.primitive_num * 3);
                var tri = new List<uint>();
                for (uint i = subset.start_index; i + 2 < end; i += 3) { tri.Add(mesh.index_seq[i]); tri.Add(mesh.index_seq[i + 1]); tri.Add(mesh.index_seq[i + 2]); }
                string texture = null;
                if (geometry.mtl_seq != null && s < geometry.mtl_seq.Length && geometry.mtl_seq[s].tex_seq != null && geometry.mtl_seq[s].tex_seq.Length > 0)
                    texture = ExportTexture(modelPath, outputRoot, geometry.mtl_seq[s].tex_seq[0].file_name);
                if (s > 0) sb.Append(',');
                sb.Append("{\"texture\":\"").Append((texture ?? "").Replace('\\', '/')).Append("\",\"triangles\":[")
                  .Append(string.Join(",", tri.Select(i => i.ToString(CultureInfo.InvariantCulture)))).Append("]}");
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
