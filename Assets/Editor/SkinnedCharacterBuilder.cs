using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Builds animated prefabs from Skinned/{id}.json + {id}.frames.bin produced by the exporter's --skinned mode.
[InitializeOnLoad]
public static class SkinnedCharacterBuilder
{
    const string SourceDir = "Assets/ImportedClient/Skinned";
    const string OutDir = "Assets/Prefabs/Skinned";
    const string Request = "Tools/build-skinned.request";
    const float FrameRate = 30f;

    [Serializable] class BoneInfo { public string name; public int parent; }
    [Serializable] class SubsetInfo { public string texture; public int[] triangles; }
    [Serializable] class ActionInfo { public int id; public int start; public int end; }
    [Serializable] class DummyInfo { public int id; public int bone; public float[] mat; }
    [Serializable]
    class Data
    {
        public BoneInfo[] bones; public float[] invMats; public int frameCount;
        public float[] positions, normals, uvs, boneWeights; public int[] boneIndices;
        public SubsetInfo[] subsets; public ActionInfo[] actions; public DummyInfo[] dummies;
    }

    static SkinnedCharacterBuilder() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string[] ids = File.ReadAllText(Request).Split(new[] { ',', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        File.Delete(Request);
        var log = new List<string>();
        foreach (string id in ids)
        {
            try { log.Add(id + ": " + (Build(id) ? "ok" : "missing")); }
            catch (Exception e) { log.Add(id + ": FAILED " + e); }
        }
        File.WriteAllLines("Tools/build-skinned.done.txt", log);
    }

    // (x,y,z) -> (-x,z,y) and row-vector D3D matrices -> column-vector Unity matrices.
    static Matrix4x4 Convert(float[] m, int offset)
    {
        var d3d = new Matrix4x4();
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                d3d[c, r] = m[offset + r * 4 + c]; // transpose
        var s = new Matrix4x4();
        s.SetRow(0, new Vector4(-1, 0, 0, 0));
        s.SetRow(1, new Vector4(0, 0, 1, 0));
        s.SetRow(2, new Vector4(0, 1, 0, 0));
        s.SetRow(3, new Vector4(0, 0, 0, 1));
        return s * d3d * s.inverse;
    }

    static void Decompose(Matrix4x4 m, out Vector3 pos, out Quaternion rot)
    {
        pos = m.GetColumn(3);
        Vector3 fwd = m.GetColumn(2), up = m.GetColumn(1);
        rot = fwd.sqrMagnitude < 1e-8f || up.sqrMagnitude < 1e-8f ? Quaternion.identity : Quaternion.LookRotation(fwd, up);
    }

    public static bool Build(string id)
    {
        string jsonPath = $"{SourceDir}/{id}.json", binPath = $"{SourceDir}/{id}.frames.bin";
        if (!File.Exists(jsonPath) || !File.Exists(binPath)) return false;
        var data = JsonUtility.FromJson<Data>(File.ReadAllText(jsonPath));
        Directory.CreateDirectory(OutDir + "/" + id);
        string baseDir = $"{OutDir}/{id}";

        int boneCount = data.bones.Length;
        var root = new GameObject(id);
        var bones = new Transform[boneCount];
        var paths = new string[boneCount];
        var worldBind = new Matrix4x4[boneCount];
        var bindPoses = new Matrix4x4[boneCount];
        for (int i = 0; i < boneCount; i++)
        {
            bindPoses[i] = Convert(data.invMats, i * 16);
            worldBind[i] = bindPoses[i].inverse;
            bones[i] = new GameObject($"b{i}_{data.bones[i].name}".Replace('/', '_')).transform;
        }
        for (int i = 0; i < boneCount; i++)
        {
            int p = data.bones[i].parent;
            bones[i].SetParent(p >= 0 && p != i ? bones[p] : root.transform, false);
            Matrix4x4 local = p >= 0 && p != i ? worldBind[p].inverse * worldBind[i] : worldBind[i];
            Decompose(local, out Vector3 lp, out Quaternion lr);
            bones[i].localPosition = lp; bones[i].localRotation = lr;
        }
        // Dummies are stored in world bind space; weapon/effect links hang from them.
        if (data.dummies != null)
            foreach (var d in data.dummies)
            {
                if (d.mat == null || d.mat.Length < 16 || d.bone < 0 || d.bone >= boneCount) continue;
                var dt = new GameObject("dummy_" + d.id).transform;
                dt.SetParent(bones[d.bone], false);
                Decompose(worldBind[d.bone].inverse * Convert(d.mat, 0), out Vector3 dp, out Quaternion dr);
                dt.localPosition = dp; dt.localRotation = dr;
            }
        for (int i = 0; i < boneCount; i++)
        {
            string path = bones[i].name; var t = bones[i].parent;
            while (t != null && t != root.transform) { path = t.name + "/" + path; t = t.parent; }
            paths[i] = path;
        }

        int vc = data.positions.Length / 3;
        var verts = new Vector3[vc]; var norms = new Vector3[vc]; var uvs = new Vector2[vc]; var weights = new BoneWeight[vc];
        for (int i = 0; i < vc; i++)
        {
            verts[i] = new Vector3(-data.positions[i * 3], data.positions[i * 3 + 2], data.positions[i * 3 + 1]);
            if (data.normals.Length == vc * 3) norms[i] = new Vector3(-data.normals[i * 3], data.normals[i * 3 + 2], data.normals[i * 3 + 1]);
            if (data.uvs.Length == vc * 2) uvs[i] = new Vector2(data.uvs[i * 2], data.uvs[i * 2 + 1]);
            if (data.boneIndices.Length == vc * 4)
            {
                var w = new BoneWeight();
                w.boneIndex0 = data.boneIndices[i * 4]; w.weight0 = data.boneWeights[i * 4];
                w.boneIndex1 = data.boneIndices[i * 4 + 1]; w.weight1 = data.boneWeights[i * 4 + 1];
                w.boneIndex2 = data.boneIndices[i * 4 + 2]; w.weight2 = data.boneWeights[i * 4 + 2];
                w.boneIndex3 = data.boneIndices[i * 4 + 3]; w.weight3 = data.boneWeights[i * 4 + 3];
                weights[i] = w;
            }
            else weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
        }
        var mesh = new Mesh { name = id + "_mesh", indexFormat = vc > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.vertices = verts; mesh.uv = uvs;
        if (data.normals.Length == vc * 3) mesh.normals = norms;
        mesh.subMeshCount = data.subsets.Length;
        for (int s = 0; s < data.subsets.Length; s++) mesh.SetTriangles(data.subsets[s].triangles, s);
        if (data.normals.Length != vc * 3) mesh.RecalculateNormals();
        mesh.boneWeights = weights; mesh.bindposes = bindPoses;
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{baseDir}/{id}_mesh.asset");

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mats = new Material[data.subsets.Length];
        for (int s = 0; s < mats.Length; s++)
        {
            var mat = new Material(shader) { name = $"{id}_mat{s}" };
            var tex = string.IsNullOrEmpty(data.subsets[s].texture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ImportedClient/" + data.subsets[s].texture);
            if (tex != null) { mat.mainTexture = tex; if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex); }
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0);
            AssetDatabase.CreateAsset(mat, $"{baseDir}/{id}_mat{s}.asset".Replace(".asset", ".mat"));
            mats[s] = mat;
        }

        var smr = new GameObject("Mesh").AddComponent<SkinnedMeshRenderer>();
        smr.transform.SetParent(root.transform, false);
        smr.sharedMesh = mesh; smr.sharedMaterials = mats; smr.bones = bones; smr.rootBone = root.transform;
        smr.localBounds = mesh.bounds; smr.updateWhenOffscreen = true;

        var anim = root.AddComponent<Animation>();
        byte[] bin = File.ReadAllBytes(binPath);
        int frames = BitConverter.ToInt32(bin, 4);
        var seen = new Dictionary<long, int>(); var aliases = new List<string>();
        AnimationClip first = null;
        foreach (var act in data.actions)
        {
            if (act.start < 0 || act.end >= frames || act.end <= act.start || act.end - act.start > 600) continue;
            long key = ((long)act.start << 32) | (uint)act.end;
            if (seen.TryGetValue(key, out int canonical)) { aliases.Add(act.id + " " + canonical); continue; }
            seen[key] = act.id;
            var clip = BuildClip(bin, boneCount, paths, act, $"action_{act.id}");
            AssetDatabase.CreateAsset(clip, $"{baseDir}/{id}_action{act.id}.anim");
            anim.AddClip(clip, clip.name);
            if (first == null || act.id == 1) first = clip;
        }
        if (first != null) anim.clip = first;
        // Several poses share the same frame range (e.g. sword wait = fist wait); the runtime resolves them through this table.
        Directory.CreateDirectory("Assets/Resources/PkoChar");
        File.WriteAllText($"Assets/Resources/PkoChar/Alias_{id}.txt", string.Join("\n", aliases));
        anim.playAutomatically = true;
        root.AddComponent<LegacyAnimDriver>();

        PrefabUtility.SaveAsPrefabAsset(root, $"{OutDir}/{id}.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return true;
    }

    static AnimationClip BuildClip(byte[] bin, int boneCount, string[] paths, ActionInfo act, string name)
    {
        var clip = new AnimationClip { name = name, legacy = true, frameRate = FrameRate, wrapMode = WrapMode.Loop };
        int count = act.end - act.start + 1;
        var rawBuf = new float[16];
        for (int b = 0; b < boneCount; b++)
        {
            var px = new Keyframe[count]; var py = new Keyframe[count]; var pz = new Keyframe[count];
            var qx = new Keyframe[count]; var qy = new Keyframe[count]; var qz = new Keyframe[count]; var qw = new Keyframe[count];
            Quaternion prev = Quaternion.identity;
            for (int k = 0; k < count; k++)
            {
                int offset = 8 + ((act.start + k) * boneCount + b) * 64;
                Buffer.BlockCopy(bin, offset, rawBuf, 0, 64);
                Decompose(Convert(rawBuf, 0), out Vector3 p, out Quaternion q);
                if (k > 0 && Quaternion.Dot(prev, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                prev = q;
                float t = k / FrameRate;
                px[k] = new Keyframe(t, p.x); py[k] = new Keyframe(t, p.y); pz[k] = new Keyframe(t, p.z);
                qx[k] = new Keyframe(t, q.x); qy[k] = new Keyframe(t, q.y); qz[k] = new Keyframe(t, q.z); qw[k] = new Keyframe(t, q.w);
            }
            Set(clip, paths[b], "localPosition.x", px); Set(clip, paths[b], "localPosition.y", py); Set(clip, paths[b], "localPosition.z", pz);
            Set(clip, paths[b], "localRotation.x", qx); Set(clip, paths[b], "localRotation.y", qy);
            Set(clip, paths[b], "localRotation.z", qz); Set(clip, paths[b], "localRotation.w", qw);
        }
        return clip;
    }

    static void Set(AnimationClip clip, string path, string prop, Keyframe[] keys)
    {
        clip.SetCurve(path, typeof(Transform), prop, new AnimationCurve(keys));
    }

    [MenuItem("TOP/Build Skinned Sample (0087)")]
    static void Sample() { Debug.Log("Skinned 0087: " + Build("0087")); }
}

