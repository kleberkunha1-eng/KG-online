using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Builds Assets/Prefabs/Effects/{id}.prefab for every converted .eff JSON.
[InitializeOnLoad]
public static class PKOEffectBuilder
{
    const string JsonDir = "Assets/ImportedClient/Effects";
    const string TexDir = "Assets/ImportedClient/Textures/effect";
    const string ModelDir = "Assets/ImportedClient/Models/model/effect";
    const string OutDir = "Assets/Prefabs/Effects";
    static readonly string Request = Path.GetFullPath("Tools/build-effects.request");
    static readonly string Done = Path.GetFullPath("Tools/build-effects.done.txt");
    static double next;

    static PKOEffectBuilder() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isPlaying) return;
        next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        try { File.WriteAllText(Done, BuildAll(), new UTF8Encoding(true)); }
        catch (System.Exception e) { File.WriteAllText(Done, e.ToString()); }
    }

    [MenuItem("Tools/PKO/Build Effect Prefabs")]
    public static void Menu() { Debug.Log(BuildAll()); }

    static Material Mat(string path, string shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        m = new Material(Shader.Find(shader));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    public static string BuildAll()
    {
        Directory.CreateDirectory(OutDir);
        var fxMat = Mat("Assets/Materials/Effects/PKOEffect.mat", "PKO/Effect");
        var texCache = new Dictionary<string, Texture2D>();
        var meshCache = new Dictionary<string, Mesh>();
        int ok = 0, missingTex = 0, missingModel = 0;
        var missing = new HashSet<string>();
        var misModels = new HashSet<string>();

        AssetDatabase.StartAssetEditing();
        try { /* texture import is batched by the editor on stop */ } finally { AssetDatabase.StopAssetEditing(); }

        foreach (string jsonPath in Directory.GetFiles(JsonDir, "*.json"))
        {
            string id = Path.GetFileNameWithoutExtension(jsonPath);
            string assetPath = JsonDir + "/" + id + ".json";
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (ta == null) continue;
            var data = JsonUtility.FromJson<PKOEffectPlayer.Data>(ta.text);
            var texs = new List<Texture2D>(); var meshNames = new List<string>(); var meshes = new List<Mesh>();
            foreach (var s in data.effects)
            {
                var names = new List<string> { s.texture };
                if (s.frameTex != null) names.AddRange(s.frameTex);
                foreach (string n in names)
                {
                    if (string.IsNullOrEmpty(n)) continue;
                    if (!texCache.TryGetValue(n, out var t))
                    {
                        t = null;
                        foreach (string ext in new[] { ".png", ".tga" })
                        {
                            t = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + n + ext);
                            if (t != null) break;
                        }
                        texCache[n] = t;
                        if (t == null && missing.Add(n)) missingTex++;
                    }
                    if (t != null && !texs.Contains(t)) texs.Add(t);
                }
                if (s.model != null && s.model.EndsWith(".lgo", System.StringComparison.OrdinalIgnoreCase))
                {
                    string stem = Path.GetFileNameWithoutExtension(s.model);
                    if (!meshCache.TryGetValue(stem, out var mesh))
                    {
                        mesh = null;
                        foreach (string p in Directory.GetFiles(ModelDir, stem + ".obj"))
                            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(ModelDir + "/" + Path.GetFileName(p)))
                                if (a is Mesh mm) { mesh = mm; break; }
                        meshCache[stem] = mesh;
                        if (mesh == null && misModels.Add(stem)) missingModel++;
                    }
                    if (mesh != null && !meshNames.Contains(s.model)) { meshNames.Add(s.model); meshes.Add(mesh); }
                }
            }
            var go = new GameObject(id);
            var p2 = go.AddComponent<PKOEffectPlayer>();
            p2.json = ta; p2.textures = texs.ToArray(); p2.effectMaterial = fxMat;
            p2.modelMeshNames = meshNames.ToArray(); p2.modelMeshes = meshes.ToArray();
            PrefabUtility.SaveAsPrefabAsset(go, OutDir + "/" + id + ".prefab");
            Object.DestroyImmediate(go);
            ok++;
        }
        AssetDatabase.SaveAssets();
        return "built=" + ok + " missingTextures=" + missingTex + " missingModels=" + missingModel + "\n" + string.Join(",", missing) + "\n" + string.Join(",", misModels);
    }
}
