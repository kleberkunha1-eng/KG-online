using System;
using System.Collections.Generic;
using System.IO;
using TOP.Character;
using TOP.Data;
using UnityEditor;
using UnityEngine;

namespace TOP.EditorTools
{
    // Bakes the original character data into runtime assets under Resources/PkoChar:
    //   Rig_000N.prefab      skeleton + animations + hand dummies of each race
    //   Parts/<id>.asset     skinned equipment parts (face, hair, body, gloves, boots) from Skinned/parts/*.json
    //   Weapons/<id>.prefab  static weapon/shield models referenced by iteminfo
    // Source JSON comes from BatchExporter --charparts. Trigger: menu or Tools/bake-char.request.
    [InitializeOnLoad]
    public static class PkoCharBaker
    {
        const string Src = "Assets/ImportedClient/Skinned";
        const string Out = "Assets/Resources/PkoChar";
        const string Request = "Tools/bake-char.request";

        [Serializable] class SubsetInfo { public string texture; public int[] triangles; }
        [Serializable]
        class PartData
        {
            public float[] positions, normals, uvs, boneWeights; public int[] boneIndices; public SubsetInfo[] subsets; public int race = -1;
        }

        static PkoCharBaker() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string arg = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            string log;
            try { log = Bake(arg); } catch (Exception e) { log = "FAILED " + e; }
            File.WriteAllText("Tools/bake-char.done.txt", log);
        }

        [MenuItem("TOP/Characters/Bake Character Assets")]
        static void Menu() { Debug.Log(Bake("")); }

        static string Bake(string only)
        {
            var report = new List<string>();
            AssetDatabase.Refresh();
            Directory.CreateDirectory(Out + "/Parts"); Directory.CreateDirectory(Out + "/Weapons"); Directory.CreateDirectory(Out + "/Materials");
            AssetDatabase.Refresh();

            var bindPoses = new Matrix4x4[PkoCharacterVisual.Races][];
            for (int race = 0; race < PkoCharacterVisual.Races; race++)
            {
                string id = race.ToString("0000");
                if (!SkinnedCharacterBuilder.Build(id)) { report.Add("rig " + id + " missing"); continue; }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Skinned/{id}.prefab");
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var faceMesh = inst.transform.Find("Mesh");
                if (faceMesh != null) { bindPoses[race] = faceMesh.GetComponent<SkinnedMeshRenderer>().sharedMesh.bindposes; UnityEngine.Object.DestroyImmediate(faceMesh.gameObject); }
                var unpacked = (GameObject)PrefabUtility.SaveAsPrefabAsset(inst, $"{Out}/Rig_{id}.prefab");
                UnityEngine.Object.DestroyImmediate(inst);
                report.Add("rig " + id + " ok");
            }

            if (only != "rigs") BakeParts(bindPoses, report);
            if (only != "rigs") BakeWeapons(report);
            AssetDatabase.SaveAssets();
            return string.Join("\n", report);
        }

        static void BakeParts(Matrix4x4[][] bindPoses, List<string> report)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var materials = new Dictionary<string, Material>();
            int ok = 0, fail = 0, noTex = 0;
            var files = Directory.GetFiles(Src + "/parts", "*.json");
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string assetPath = $"{Out}/Parts/{name}.asset";
                    if (File.Exists(assetPath)) AssetDatabase.DeleteAsset(assetPath);
                    try
                    {
                        var data = JsonUtility.FromJson<PartData>(File.ReadAllText(file));
                        int race = data.race >= 0 ? data.race : int.Parse(name.Substring(0, 4));
                        if (race >= bindPoses.Length || bindPoses[race] == null) { fail++; continue; }
                        var mesh = BuildMesh(name, data, bindPoses[race]);
                        var mats = new Material[data.subsets.Length];
                        for (int s = 0; s < mats.Length; s++)
                        {
                            mats[s] = GetMaterial(materials, shader, data.subsets[s].texture);
                            if (string.IsNullOrEmpty(data.subsets[s].texture)) noTex++;
                        }
                        var asset = ScriptableObject.CreateInstance<PkoPartAsset>();
                        asset.mesh = mesh; asset.materials = mats;
                        AssetDatabase.CreateAsset(asset, assetPath);
                        AssetDatabase.AddObjectToAsset(mesh, asset);
                        ok++;
                    }
                    catch (Exception e) { fail++; Debug.LogWarning("[PkoCharBaker] " + name + ": " + e.Message); }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            report.Add($"parts ok={ok} failed={fail} subsetsWithoutTexture={noTex}");
        }

        static Mesh BuildMesh(string name, PartData d, Matrix4x4[] bindPoses)
        {
            int vc = d.positions.Length / 3;
            var verts = new Vector3[vc]; var norms = new Vector3[vc]; var uvs = new Vector2[vc]; var weights = new BoneWeight[vc];
            bool hasN = d.normals.Length == vc * 3, hasUv = d.uvs.Length == vc * 2, hasW = d.boneIndices.Length == vc * 4;
            for (int i = 0; i < vc; i++)
            {
                verts[i] = new Vector3(-d.positions[i * 3], d.positions[i * 3 + 2], d.positions[i * 3 + 1]);
                if (hasN) norms[i] = new Vector3(-d.normals[i * 3], d.normals[i * 3 + 2], d.normals[i * 3 + 1]);
                if (hasUv) uvs[i] = new Vector2(d.uvs[i * 2], d.uvs[i * 2 + 1]);
                weights[i] = hasW
                    ? new BoneWeight
                    {
                        boneIndex0 = d.boneIndices[i * 4], weight0 = d.boneWeights[i * 4], boneIndex1 = d.boneIndices[i * 4 + 1], weight1 = d.boneWeights[i * 4 + 1],
                        boneIndex2 = d.boneIndices[i * 4 + 2], weight2 = d.boneWeights[i * 4 + 2], boneIndex3 = d.boneIndices[i * 4 + 3], weight3 = d.boneWeights[i * 4 + 3]
                    }
                    : new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            }
            var mesh = new Mesh { name = name, indexFormat = vc > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = verts; mesh.uv = uvs;
            if (hasN) mesh.normals = norms;
            mesh.subMeshCount = d.subsets.Length;
            for (int s = 0; s < d.subsets.Length; s++) mesh.SetTriangles(d.subsets[s].triangles, s);
            if (!hasN) mesh.RecalculateNormals();
            mesh.boneWeights = weights; mesh.bindposes = bindPoses;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material GetMaterial(Dictionary<string, Material> cache, Shader shader, string texture)
        {
            string key = string.IsNullOrEmpty(texture) ? "_none" : texture;
            if (cache.TryGetValue(key, out var cached)) return cached;
            string matPath = $"{Out}/Materials/{(key == "_none" ? "none" : key.Replace('/', '_').Replace('.', '_'))}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null) { cache[key] = existing; return existing; }
            var mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(matPath) };
            var tex = key == "_none" ? null : AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ImportedClient/" + texture);
            if (tex != null)
            {
                mat.mainTexture = tex; mat.SetTexture("_BaseMap", tex);
                string ext = Path.GetExtension(texture).ToLowerInvariant();
                if (ext == ".tga" || ext == ".png" || ext == ".dds")
                {
                    mat.SetFloat("_AlphaClip", 1); mat.SetFloat("_Cutoff", 0.5f); mat.EnableKeyword("_ALPHATEST_ON");
                    mat.renderQueue = 2450;
                }
            }
            mat.SetFloat("_Cull", 0); mat.SetFloat("_Smoothness", 0.1f); mat.SetFloat("_Metallic", 0);
            AssetDatabase.CreateAsset(mat, matPath);
            cache[key] = mat;
            return mat;
        }

        static void BakeWeapons(List<string> report)
        {
            var wanted = new HashSet<string>();
            foreach (var it in PkoTables.Items.Values)
            {
                bool weapon = it.Type == 1 || it.Type == 2 || it.Type == 3 || it.Type == 4 || it.Type == 6 || it.Type == 7 || it.Type == 9 || it.Type == 11;
                if (!weapon) continue;
                foreach (string m in it.RaceModels)
                {
                    string id = (m ?? "").TrimEnd('_');
                    if (id.Length > 1 && id != "0") wanted.Add(id);
                }
            }
            int ok = 0, missing = 0;
            foreach (string id in wanted)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ImportedClient/Models/model/item/{id}.obj");
                if (model == null) { missing++; continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.SaveAsPrefabAsset(inst, $"{Out}/Weapons/{id}.prefab");
                UnityEngine.Object.DestroyImmediate(inst);
                ok++;
            }
            report.Add($"weapons ok={ok} missing={missing}");
        }
    }
}
