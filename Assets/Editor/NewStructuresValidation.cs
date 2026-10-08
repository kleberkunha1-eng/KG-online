using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class NewStructuresValidation
{
    const string Root = "Assets/Novas estruturas";
    static readonly string Project = Path.GetDirectoryName(Application.dataPath);
    static readonly string Request = Path.Combine(Project, "Tools", "validate-new-structures.request");
    static readonly string Result = Path.Combine(Project, "Tools", "new-structures-validation-results.txt");
    static string[] paths;
    static int index;
    static int failures;
    static StringBuilder report;
    static AsyncOperation unload;
    static double nextStatus;

    static NewStructuresValidation() { EditorApplication.update += Poll; }

    [MenuItem("Tools/Validate New Structures")]
    public static void Validate()
    {
        if (paths != null || EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (File.Exists(Request)) File.Delete(Request);
        paths = Directory.GetFiles(Path.Combine(Project, Root), "*.glb", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => path.Substring(Project.Length + 1).Replace('\\', '/')).ToArray();
        index = 0;
        failures = 0;
        report = new StringBuilder();
        Debug.Log($"[NewStructuresValidation] Starting validation of {paths.Length} GLB assets.");
        if (paths.Length == 0)
        {
            failures++;
            report.AppendLine("FAIL | No GLB assets found.");
        }
    }

    static void Poll()
    {
        if (File.Exists(Request) && EditorApplication.isPlaying && !EditorApplication.isCompiling)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying)
        {
            if (File.Exists(Request) && EditorApplication.timeSinceStartup >= nextStatus)
            {
                nextStatus = EditorApplication.timeSinceStartup + 60;
                Debug.Log($"[NewStructuresValidation] Waiting: compiling={EditorApplication.isCompiling}, updating={EditorApplication.isUpdating}, playing={EditorApplication.isPlaying}.");
            }
            return;
        }
        if (paths == null)
        {
            if (!File.Exists(Request)) return;
            File.Delete(Request);
            Validate();
        }
        if (unload != null && !unload.isDone) return;
        unload = null;
        if (index < paths.Length)
        {
            ValidateAsset(paths[index++]);
            // Embedded textures are large; do not keep the entire collection resident during validation.
            unload = Resources.UnloadUnusedAssets();
            return;
        }
        report.AppendLine($"TOTAL | {paths.Length} models | {failures} failures");
        File.WriteAllText(Result, report.ToString());
        if (failures == 0) Debug.Log("[NewStructuresValidation] " + report);
        else Debug.LogError("[NewStructuresValidation] " + report);
        paths = null;
        report = null;
    }

    static void ValidateAsset(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        if (model == null)
        {
            failures++;
            report.AppendLine("FAIL | " + path + " | No imported GameObject.");
            return;
        }
        var meshes = model.GetComponentsInChildren<MeshFilter>(true).Select(filter => filter.sharedMesh)
            .Concat(model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(renderer => renderer.sharedMesh)).ToArray();
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        var materials = renderers.SelectMany(renderer => renderer.sharedMaterials).ToArray();
        bool valid = meshes.Length > 0 && meshes.All(mesh => mesh != null && mesh.vertexCount > 0 && mesh.subMeshCount > 0)
            && materials.Length > 0 && materials.All(material => material != null && material.shader != null
                && material.shader.isSupported && material.shader.name != "Hidden/InternalErrorShader");
        int textures = 0;
        foreach (var material in materials.Where(material => material != null).Distinct())
        {
            foreach (var property in material.GetTexturePropertyNames())
            {
                var texture = material.GetTexture(property);
                if (texture == null) continue;
                textures++;
                valid &= texture.width > 0 && texture.height > 0;
            }
        }
        valid &= textures > 0;
        int animations = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count();
        if (path.EndsWith("Personagem_Configuracoes_Referencia_Teste.glb", StringComparison.Ordinal)
            || path.EndsWith("Personagem_Ombros_Bracos_Neutros_Pegada.glb", StringComparison.Ordinal))
            valid &= animations == 72;
        if (!valid) failures++;
        report.AppendLine($"{(valid ? "PASS" : "FAIL")} | {path} | meshes={meshes.Length} | vertices={meshes.Where(mesh => mesh != null).Sum(mesh => (long)mesh.vertexCount)} | materials={materials.Length} | textures={textures} | animations={animations}");
        File.WriteAllText(Result, report.ToString());
    }
}
