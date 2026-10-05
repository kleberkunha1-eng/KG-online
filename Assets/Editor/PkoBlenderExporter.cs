using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TOP.Character;
using UnityEditor;
using UnityEngine;

namespace TOP.EditorTools
{
    // Exports the baked character data (rigs, parts, weapons) as glTF binaries for Blender.
    // Trigger: menu or Tools/blender-export.request (file content = output folder; empty = Desktop/TalesOfPirates_Characters).
    [InitializeOnLoad]
    public static class PkoBlenderExporter
    {
        const string Request = "Tools/blender-export.request";
        static readonly string[] RaceNames = { "Lance", "Carsise", "Phyllis", "Ami" };
        static readonly string[] PartDirs = { "Face", "Hair", "Body", "Gloves", "Boots" };
        static readonly Matrix4x4 Flip = Matrix4x4.Scale(new Vector3(1, 1, -1));

        static PkoBlenderExporter() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string dir = File.ReadAllText(Request).Trim(); File.Delete(Request);
            string log;
            try { log = Export(dir); } catch (Exception e) { log = "FAILED " + e; }
            File.WriteAllText("Tools/blender-export.done.txt", log);
        }

        [MenuItem("TOP/Characters/Export for Blender")]
        static void Menu() { Debug.Log(Export("")); }

        // ---------------- glTF writer ----------------
        class Gltf
        {
            public readonly List<byte> Bin = new List<byte>();
            public readonly List<string> Views = new List<string>(), Accessors = new List<string>(), Nodes = new List<string>(),
                Meshes = new List<string>(), Materials = new List<string>(), Textures = new List<string>(), Images = new List<string>(),
                Skins = new List<string>(), Anims = new List<string>();
            public string Up = "../";
            public readonly List<int> SceneNodes = new List<int>();
            public readonly Dictionary<string, int> TexIndex = new Dictionary<string, int>();
            public readonly Dictionary<Material, int> MatIndex = new Dictionary<Material, int>();

            public int View(byte[] data, int target = 0)
            {
                while (Bin.Count % 4 != 0) Bin.Add(0);
                int off = Bin.Count; Bin.AddRange(data);
                Views.Add("{\"buffer\":0,\"byteOffset\":" + off + ",\"byteLength\":" + data.Length + (target != 0 ? ",\"target\":" + target : "") + "}");
                return Views.Count - 1;
            }
            public int Acc(int view, int comp, int count, string type, string minmax = "")
            {
                Accessors.Add("{\"bufferView\":" + view + ",\"componentType\":" + comp + ",\"count\":" + count + ",\"type\":\"" + type + "\"" + minmax + "}");
                return Accessors.Count - 1;
            }
            public int Floats(float[] f, int count, string type, int target = 0, string mm = "")
            {
                var b = new byte[f.Length * 4]; Buffer.BlockCopy(f, 0, b, 0, b.Length);
                return Acc(View(b, target), 5126, count, type, mm);
            }
            public int UInts(int[] i) { var b = new byte[i.Length * 4]; Buffer.BlockCopy(i, 0, b, 0, b.Length); return Acc(View(b, 34963), 5125, i.Length, "SCALAR"); }
            public int UShorts4(ushort[] s) { var b = new byte[s.Length * 2]; Buffer.BlockCopy(s, 0, b, 0, b.Length); return Acc(View(b, 34962), 5123, s.Length / 4, "VEC4"); }
        }

        static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static string Fs(params float[] v) => "[" + string.Join(",", v.Select(F)) + "]";

        static string Trs(Transform t)
        {
            var p = t.localPosition; var q = t.localRotation; var s = t.localScale;
            return "\"translation\":" + Fs(p.x, p.y, -p.z) + ",\"rotation\":" + Fs(-q.x, -q.y, q.z, q.w) + ",\"scale\":" + Fs(s.x, s.y, s.z);
        }

        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        static int Texture(Gltf g, Texture tex, string texDir)
        {
            if (tex == null) return -1;
            string key = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(key)) key = tex.name;
            if (g.TexIndex.TryGetValue(key, out int idx)) return idx;
            string file = key.Replace("Assets/ImportedClient/", "").Replace('/', '_').Replace('\\', '_').Replace(' ', '_');
            file = Path.GetFileNameWithoutExtension(file) + ".png";
            string full = Path.Combine(texDir, file);
            if (!File.Exists(full))
            {
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var rd = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                rd.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); rd.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(full, rd.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(rd);
            }
            g.Images.Add("{\"uri\":\"" + g.Up + "Textures/" + file + "\"}");
            g.Textures.Add("{\"source\":" + (g.Images.Count - 1) + "}");
            g.TexIndex[key] = g.Textures.Count - 1;
            return g.Textures.Count - 1;
        }

        static int Material(Gltf g, Material m, string texDir, int depth)
        {
            if (m == null) m = new Material(Shader.Find("Hidden/InternalErrorShader"));
            if (g.MatIndex.TryGetValue(m, out int mi)) return mi;
            int ti = Texture(g, m.mainTexture, texDir);
            string up = string.Concat(Enumerable.Repeat("../", depth));
            string pbr = "\"pbrMetallicRoughness\":{" + (ti >= 0 ? "\"baseColorTexture\":{\"index\":" + ti + "}," : "") + "\"metallicFactor\":0,\"roughnessFactor\":1}";
            g.Materials.Add("{\"name\":\"" + Esc(m.name) + "\"," + pbr + ",\"alphaMode\":\"MASK\",\"alphaCutoff\":0.5,\"doubleSided\":true}");
            g.MatIndex[m] = g.Materials.Count - 1;
            return g.Materials.Count - 1;
        }

        // Adds a mesh as glTF mesh; xf converts Unity mesh space to the "character" Unity space before the handedness flip.
        static int AddMesh(Gltf g, Mesh mesh, Material[] mats, string texDir, bool skinned, Func<Vector3, Vector3> xf, bool swapWinding)
        {
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv;
            var pos = new float[v.Length * 3]; var nor = new float[v.Length * 3]; var tc = new float[v.Length * 2];
            Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), mx = -mn;
            for (int i = 0; i < v.Length; i++)
            {
                var p = xf(v[i]); p.z = -p.z; pos[i * 3] = p.x; pos[i * 3 + 1] = p.y; pos[i * 3 + 2] = p.z;
                mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
                var nn = n.Length == v.Length ? xf(n[i]) : Vector3.up; nn.z = -nn.z; nor[i * 3] = nn.x; nor[i * 3 + 1] = nn.y; nor[i * 3 + 2] = nn.z;
                if (uv.Length == v.Length) { tc[i * 2] = uv[i].x; tc[i * 2 + 1] = 1f - uv[i].y; }
            }
            int aPos = g.Floats(pos, v.Length, "VEC3", 34962, ",\"min\":" + Fs(mn.x, mn.y, mn.z) + ",\"max\":" + Fs(mx.x, mx.y, mx.z));
            int aNor = g.Floats(nor, v.Length, "VEC3", 34962);
            int aUv = g.Floats(tc, v.Length, "VEC2", 34962);
            string attrs = "\"POSITION\":" + aPos + ",\"NORMAL\":" + aNor + ",\"TEXCOORD_0\":" + aUv;
            if (skinned)
            {
                var bw = mesh.boneWeights; var j = new ushort[v.Length * 4]; var w = new float[v.Length * 4];
                for (int i = 0; i < v.Length; i++)
                {
                    var b = bw[i];
                    j[i * 4] = (ushort)b.boneIndex0; j[i * 4 + 1] = (ushort)b.boneIndex1; j[i * 4 + 2] = (ushort)b.boneIndex2; j[i * 4 + 3] = (ushort)b.boneIndex3;
                    w[i * 4] = b.weight0; w[i * 4 + 1] = b.weight1; w[i * 4 + 2] = b.weight2; w[i * 4 + 3] = b.weight3;
                }
                attrs += ",\"JOINTS_0\":" + g.UShorts4(j) + ",\"WEIGHTS_0\":" + g.Floats(w, v.Length, "VEC4", 34962);
            }
            var prims = new List<string>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tri = mesh.GetTriangles(s); if (tri.Length == 0) continue;
                if (!swapWinding) for (int i = 0; i + 2 < tri.Length; i += 3) { int t = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = t; }
                int mat = Material(g, mats != null && s < mats.Length ? mats[s] : null, texDir, 1);
                prims.Add("{\"attributes\":{" + attrs + "},\"indices\":" + g.UInts(tri) + ",\"material\":" + mat + ",\"mode\":4}");
            }
            g.Meshes.Add("{\"name\":\"" + Esc(mesh.name) + "\",\"primitives\":[" + string.Join(",", prims) + "]}");
            return g.Meshes.Count - 1;
        }

        static void Write(Gltf g, string path)
        {
            var sb = new StringBuilder("{\"asset\":{\"version\":\"2.0\",\"generator\":\"TOP PkoBlenderExporter\"}");
            sb.Append(",\"scene\":0,\"scenes\":[{\"nodes\":[" + string.Join(",", g.SceneNodes) + "]}]");
            sb.Append(",\"nodes\":[" + string.Join(",", g.Nodes) + "]");
            if (g.Meshes.Count > 0) sb.Append(",\"meshes\":[" + string.Join(",", g.Meshes) + "]");
            if (g.Materials.Count > 0) sb.Append(",\"materials\":[" + string.Join(",", g.Materials) + "]");
            if (g.Textures.Count > 0) sb.Append(",\"textures\":[" + string.Join(",", g.Textures) + "],\"images\":[" + string.Join(",", g.Images) + "]");
            if (g.Skins.Count > 0) sb.Append(",\"skins\":[" + string.Join(",", g.Skins) + "]");
            if (g.Anims.Count > 0) sb.Append(",\"animations\":[" + string.Join(",", g.Anims) + "]");
            sb.Append(",\"accessors\":[" + string.Join(",", g.Accessors) + "],\"bufferViews\":[" + string.Join(",", g.Views) + "]");
            while (g.Bin.Count % 4 != 0) g.Bin.Add(0);
            sb.Append(",\"buffers\":[{\"byteLength\":" + g.Bin.Count + "}]}");
            var json = Encoding.UTF8.GetBytes(sb.ToString()).ToList(); while (json.Count % 4 != 0) json.Add(0x20);
            using var fs = File.Create(path); using var bw = new BinaryWriter(fs);
            bw.Write(0x46546C67); bw.Write(2); bw.Write(12 + 8 + json.Count + 8 + g.Bin.Count);
            bw.Write(json.Count); bw.Write(0x4E4F534A); bw.Write(json.ToArray());
            bw.Write(g.Bin.Count); bw.Write(0x004E4942); bw.Write(g.Bin.ToArray());
        }

        // ---------------- skeleton ----------------
        static void AddSkeleton(Gltf g, Transform root, out Dictionary<Transform, int> map)
        {
            var m = new Dictionary<Transform, int>();
            int Add(Transform t)
            {
                int idx = g.Nodes.Count; g.Nodes.Add(null); m[t] = idx;
                var kids = new List<int>();
                foreach (Transform c in t) if (c.GetComponent<Renderer>() == null && !c.name.StartsWith("part_") && !c.name.StartsWith("weapon_")) kids.Add(Add(c));
                g.Nodes[idx] = "{\"name\":\"" + Esc(t.name) + "\"," + Trs(t) + (kids.Count > 0 ? ",\"children\":[" + string.Join(",", kids) + "]" : "") + "}";
                return idx;
            }
            g.SceneNodes.Add(Add(root));
            map = m;
        }

        static void AddSkinnedMesh(Gltf g, string name, Mesh mesh, Material[] mats, Transform[] bones, Transform meshT, Dictionary<Transform, int> map, string texDir)
        {
            int mi = AddMesh(g, mesh, mats, texDir, true, p => p, false);
            var ibm = new float[bones.Length * 16];
            var bp = mesh.bindposes;
            for (int b = 0; b < bones.Length; b++)
            {
                var m = Flip * bp[b] * Flip;
                for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) ibm[b * 16 + c * 4 + r] = m[r, c];
            }
            int acc = g.Floats(ibm, bones.Length, "MAT4");
            g.Skins.Add("{\"inverseBindMatrices\":" + acc + ",\"joints\":[" + string.Join(",", bones.Select(b => map[b])) + "]}");
            g.Nodes.Add("{\"name\":\"" + Esc(name) + "\",\"mesh\":" + mi + ",\"skin\":" + (g.Skins.Count - 1) + "}");
            g.SceneNodes.Add(g.Nodes.Count - 1);
        }

        static void AddAnimations(Gltf g, GameObject rigGo, Dictionary<Transform, int> map)
        {
            var anim = rigGo.GetComponent<Animation>(); if (anim == null) return;
            var joints = map.Keys.Where(t => t != rigGo.transform).ToList();
            var clips = new List<AnimationClip>(); foreach (AnimationState st in anim) clips.Add(st.clip);
            foreach (var clip in clips.GroupBy(c => c.name).Select(x => x.First()))
            {
                int frames = Mathf.Max(2, Mathf.CeilToInt(clip.length * 30f) + 1);
                var times = new float[frames]; for (int f = 0; f < frames; f++) times[f] = Mathf.Min(f / 30f, clip.length);
                var tr = joints.ToDictionary(j => j, j => new float[frames * 3]); var ro = joints.ToDictionary(j => j, j => new float[frames * 4]);
                for (int f = 0; f < frames; f++)
                {
                    clip.SampleAnimation(rigGo, times[f]);
                    foreach (var j in joints)
                    {
                        var p = j.localPosition; var q = j.localRotation;
                        tr[j][f * 3] = p.x; tr[j][f * 3 + 1] = p.y; tr[j][f * 3 + 2] = -p.z;
                        ro[j][f * 4] = -q.x; ro[j][f * 4 + 1] = -q.y; ro[j][f * 4 + 2] = q.z; ro[j][f * 4 + 3] = q.w;
                    }
                }
                int tAcc = g.Floats(times, frames, "SCALAR", 0, ",\"min\":[" + F(times[0]) + "],\"max\":[" + F(times[frames - 1]) + "]");
                var samplers = new List<string>(); var channels = new List<string>();
                foreach (var j in joints)
                {
                    samplers.Add("{\"input\":" + tAcc + ",\"output\":" + g.Floats(tr[j], frames, "VEC3") + ",\"interpolation\":\"LINEAR\"}");
                    channels.Add("{\"sampler\":" + (samplers.Count - 1) + ",\"target\":{\"node\":" + map[j] + ",\"path\":\"translation\"}}");
                    samplers.Add("{\"input\":" + tAcc + ",\"output\":" + g.Floats(ro[j], frames, "VEC4") + ",\"interpolation\":\"LINEAR\"}");
                    channels.Add("{\"sampler\":" + (samplers.Count - 1) + ",\"target\":{\"node\":" + map[j] + ",\"path\":\"rotation\"}}");
                }
                g.Anims.Add("{\"name\":\"" + Esc(clip.name) + "\",\"samplers\":[" + string.Join(",", samplers) + "],\"channels\":[" + string.Join(",", channels) + "]}");
            }
        }

        // ---------------- export ----------------
        static string Export(string outDir)
        {
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TalesOfPirates_Characters");
            string texDir = Path.Combine(outDir, "Textures"); Directory.CreateDirectory(texDir);
            int nChar = 0, nPart = 0, nWeapon = 0, fail = 0; var errors = new List<string>();

            for (int race = 0; race < 4; race++)
            {
                var root = new GameObject("export");
                try
                {
                    var vis = PkoCharacterVisual.Create(root.transform, race, 0, 0, null);
                    var rig = vis.transform.Find("Rig").gameObject;
                    var smrs = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
                    var bones = smrs.Length > 0 ? smrs[0].bones : Array.Empty<Transform>();
                    string raceDir = RaceNames[race];

                    // Default character (face 0, hair 0, base body/gloves/boots) with every animation.
                    Directory.CreateDirectory(Path.Combine(outDir, "Characters"));
                    var g = new Gltf(); AddSkeleton(g, rig.transform, out var map);
                    foreach (var s in smrs) AddSkinnedMesh(g, s.name, s.sharedMesh, s.sharedMaterials, bones, s.transform, map, texDir);
                    AddAnimations(g, rig, map);
                    Write(g, Path.Combine(outDir, "Characters", raceDir + "_default.glb")); nChar++;

                    // Every part of this race, one file each (skeleton + skinned mesh).
                    foreach (var guid in AssetDatabase.FindAssets("t:PkoPartAsset", new[] { "Assets/Resources/PkoChar/Parts" }))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<PkoPartAsset>(AssetDatabase.GUIDToAssetPath(guid));
                        if (asset == null || asset.mesh == null || !asset.name.StartsWith(race.ToString("0000"))) continue;
                        int type = asset.name.Length == 10 && int.TryParse(asset.name.Substring(6), out var pt) ? pt : -1;
                        string pd = Path.Combine(outDir, "Parts", raceDir, type >= 0 && type < PartDirs.Length ? PartDirs[type] : "Other");
                        Directory.CreateDirectory(pd);
                        var pg = new Gltf { Up = "../../../" }; AddSkeleton(pg, rig.transform, out var pmap);
                        AddSkinnedMesh(pg, asset.name, asset.mesh, asset.materials, bones, rig.transform, pmap, texDir);
                        Write(pg, Path.Combine(pd, asset.name + ".glb")); nPart++;
                    }
                }
                catch (Exception e) { fail++; errors.Add("race " + race + ": " + e); }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }

            // Static weapon / shield models (original Z-up data mapped like the runtime does).
            Directory.CreateDirectory(Path.Combine(outDir, "Weapons"));
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/PkoChar/Weapons" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                try
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p); var g = new Gltf();
                    foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mr = mf.GetComponent<MeshRenderer>(); if (mf.sharedMesh == null || mr == null) continue;
                        int mi = AddMesh(g, mf.sharedMesh, mr.sharedMaterials, texDir, false, v => new Vector3(v.x, v.z, v.y), false);
                        g.Nodes.Add("{\"name\":\"" + Esc(mf.name) + "\",\"mesh\":" + mi + "}"); g.SceneNodes.Add(g.Nodes.Count - 1);
                    }
                    if (g.Meshes.Count == 0) continue;
                    Write(g, Path.Combine(outDir, "Weapons", Path.GetFileNameWithoutExtension(p) + ".glb")); nWeapon++;
                }
                catch (Exception e) { fail++; if (errors.Count < 20) errors.Add(p + ": " + e.Message); }
            }

            File.WriteAllText(Path.Combine(outDir, "README.txt"),
@"Tales of Pirates - modelos de personagem (glTF binario .glb, importar no Blender: File > Import > glTF 2.0)

Characters\<Raca>_default.glb   esqueleto + malhas padrao (rosto 0, cabelo 0, corpo, luvas, botas) + TODAS as animacoes (30 fps)
Parts\<Raca>\<Face|Hair|Body|Gloves|Boots>\<id>.glb   cada parte de equipamento/rosto/cabelo, ja skinada ao esqueleto da raca
Weapons\<modelo>.glb            armas, escudos e itens segurados (sem esqueleto)
Textures\*.png                  todas as texturas usadas (os .glb apontam para ../Textures)

Racas: Lance (0), Carsise (1), Phyllis (2), Ami (3). Nome da parte = raca(4) + estilo(2) + tipo(4).
Eixos: os dados originais sao Z-up; o glTF segue a convencao Y-up/destro e o Blender converte sozinho para Z-up.
Materiais usam alphaMode MASK (corte em 0,5) para folhas de cabelo/roupas com transparencia.
");
            return "DONE chars=" + nChar + " parts=" + nPart + " weapons=" + nWeapon + " fail=" + fail + "\n" + string.Join("\n", errors) + "\nout=" + outDir;
        }
    }
}