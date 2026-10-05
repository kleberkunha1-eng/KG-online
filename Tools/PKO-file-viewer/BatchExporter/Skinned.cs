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
        // Exports skeleton, skinned mesh and per-frame bone matrices for one character type (e.g. 0087).
        private static void ExportSkinned(string typeId)
        {
            string modelPath = Path.Combine(clientRoot, "model", "character", typeId + "000000.lgo");
            string animPath = Path.Combine(clientRoot, "animation", typeId + ".lab");
            if (!File.Exists(modelPath) || !File.Exists(animPath))
            {
                Console.Error.WriteLine("SKINNED_SKIPPED {0}: missing model or animation", typeId);
                failed++;
                return;
            }

            var geometry = new lwGeomObjInfo();
            if (geometry.Load(modelPath) != 0 || geometry.mesh.vertex_seq == null)
                throw new InvalidDataException("Unable to parse LGO " + typeId);
            var bone = new lwAnimDataBone();
            if (bone.Load(animPath) != 0)
                throw new InvalidDataException("Unable to parse LAB " + typeId);

            string dir = Path.Combine(outputRoot, "Skinned");
            Directory.CreateDirectory(dir);
            int boneNum = (int)bone._header.bone_num, frameNum = (int)bone._header.frame_num;

            using (var fs = new FileStream(Path.Combine(dir, typeId + ".frames.bin"), FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(boneNum);
                bw.Write(frameNum);
                for (int f = 0; f < frameNum; f++)
                    for (int b = 0; b < boneNum; b++)
                        foreach (float v in FrameMatrix(bone, b, f)) bw.Write(v);
            }

            var ids = new Dictionary<uint, int>();
            for (int i = 0; i < boneNum; i++) ids[bone._base_seq[i].id] = i;

            var sb = new StringBuilder();
            sb.Append("{\"bones\":[");
            for (int i = 0; i < boneNum; i++)
            {
                string name = new string(bone._base_seq[i].name);
                int z = name.IndexOf('\0'); if (z >= 0) name = name.Substring(0, z);
                uint pid = bone._base_seq[i].parent_id;
                int parent = pid == 0xffffffff || !ids.ContainsKey(pid) ? -1 : ids[pid];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(System.Text.RegularExpressions.Regex.Replace(name.Replace("\"", ""), "[\\x00-\\x1f]", "")).Append("\",\"parent\":").Append(parent).Append('}');
            }
            sb.Append("],\"invMats\":[");
            AppendFloats(sb, bone._invmat_seq.SelectMany(m => m.m));
            sb.Append("],\"frameCount\":").Append(frameNum).Append(',');

            lwMeshInfo mesh = geometry.mesh;
            var pos = mesh.vertex_seq.Select(v => v * geometry.header.mat_local).ToArray();
            sb.Append("\"positions\":["); AppendFloats(sb, pos.SelectMany(p => new[] { p.x, p.y, p.z }));
            sb.Append("],\"normals\":[");
            if (mesh.normal_seq != null && mesh.normal_seq.Length == pos.Length)
                AppendFloats(sb, mesh.normal_seq.SelectMany(n => new[] { n.x, n.y, n.z }));
            sb.Append("],\"uvs\":[");
            if (mesh.texcoord0_seq != null && mesh.texcoord0_seq.Length == pos.Length)
                AppendFloats(sb, mesh.texcoord0_seq.SelectMany(t => new[] { t.x, 1f - t.y }));
            sb.Append("],\"boneIndices\":[");
            var weights = new List<float>(); var indices = new List<int>();
            if (mesh.blend_seq != null && mesh.blend_seq.Length == pos.Length)
            {
                foreach (var blend in mesh.blend_seq)
                {
                    float sum = blend.weight.Sum(); if (sum <= 0f) sum = 1f;
                    for (int k = 0; k < 4; k++)
                    {
                        int local = blend.index[k];
                        int boneIndex = 0;
                        if (mesh.bone_index_seq != null && local < mesh.bone_index_seq.Length)
                        {
                            uint id = mesh.bone_index_seq[local];
                            ids.TryGetValue(id, out boneIndex);
                        }
                        indices.Add(boneIndex);
                        weights.Add(blend.weight[k] / sum);
                    }
                }
            }
            sb.Append(string.Join(",", indices.Select(i => i.ToString(CultureInfo.InvariantCulture))));
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
            sb.Append("],\"dummies\":[");
            if (bone._dummy_seq != null)
                sb.Append(string.Join(",", bone._dummy_seq.Select(d => "{\"id\":" + d.id + ",\"bone\":" + (ids.ContainsKey(d.parent_bone_id) ? ids[d.parent_bone_id] : 0) + ",\"mat\":[" + string.Join(",", d.mat.m.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]}")));
            sb.Append("],\"actions\":[");
            sb.Append(string.Join(",", ReadActions(typeId).Select(a => "{\"id\":" + a[0] + ",\"start\":" + a[1] + ",\"end\":" + a[2] + "}")));
            sb.Append("]}");
            File.WriteAllText(Path.Combine(dir, typeId + ".json"), sb.ToString(), new UTF8Encoding(false));
            converted++;
        }

        private static IEnumerable<int[]> ReadActions(string typeId)
        {
            string path = Path.Combine(clientRoot, "scripts", "txt", "CharacterAction.tx");
            int type = int.Parse(typeId, CultureInfo.InvariantCulture), current = -1;
            if (type <= 3) type++; // playable skeletons 0000-0003 use action types 1-4
            if (!File.Exists(path)) yield break;
            foreach (string line in File.ReadAllLines(path, Encoding.GetEncoding(28591)))
            {
                if (line.StartsWith("//") || string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(new[] { '\t', ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (!line.StartsWith("\t") && !line.StartsWith(" "))
                {
                    int t; current = int.TryParse(parts[0], out t) ? t : -1; continue;
                }
                if (current != type || parts.Length < 3) continue;
                int a, s, e;
                if (int.TryParse(parts[0], out a) && int.TryParse(parts[1], out s) && int.TryParse(parts[2], out e) && e >= s)
                    yield return new[] { a, s, e };
            }
        }

        private static float[] FrameMatrix(lwAnimDataBone bone, int b, int f)
        {
            var key = bone._key_seq[b];
            switch (bone._header.key_type)
            {
                case lwBoneKeyInfoType.BONE_KEY_TYPE_MAT43:
                {
                    float[] m = key.mat43_seq[f].m;
                    return new[] { m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, m[9], m[10], m[11], 1 };
                }
                case lwBoneKeyInfoType.BONE_KEY_TYPE_MAT44:
                    return key.mat44_seq[f].m;
                default:
                {
                    var q = key.quat_seq[Math.Min(f, key.quat_seq.Length - 1)];
                    var p = key.pos_seq[Math.Min(f, key.pos_seq.Length - 1)];
                    float x = q.x, y = q.y, z = q.z, w = q.w;
                    return new[]
                    {
                        1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w), 0,
                        2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w), 0,
                        2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y), 0,
                        p.x, p.y, p.z, 1
                    };
                }
            }
        }

        private static void AppendFloats(StringBuilder sb, IEnumerable<float> values)
        {
            sb.Append(string.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
        }
    }
}
