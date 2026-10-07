using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TOP.Core;
using TOP.Data;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PkoWingBuilder
{
    const string Request = "Tools/build-wings.request";
    const string Done = "Tools/build-wings-result.txt";
    const string Animated = "Assets/Resources/Wings/Animated";
    const string Items = "Assets/Resources/Wings/Items";
    static readonly string[] RebirthEffects =
    {
        "1chi", "2chi", "3chi", "1chig2", "2chig2", "3chim2",
        "1chim1", "2chim1", "3chim1", "1chim2", "2chim2", "3chim2"
    };

    static PkoWingBuilder() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { File.WriteAllText(Done, Build()); }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText(Done, "FAILED\n" + e); }
    }

    [MenuItem("Tools/PKO/Build Animated Wings")]
    public static void Menu() { Debug.Log(Build()); }

    public static string Build()
    {
        Directory.CreateDirectory(Animated);
        Directory.CreateDirectory(Items);
        AssetDatabase.Refresh();
        var builtModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var item in PkoTables.Items.Values)
        {
            if (item.Id == PkoTables.MeshyMageWingsItemId || item.EquipSlots.Length == 0
                || PkoTables.SlotOf(item) != EquipmentSlot.Wing) continue;
            string effect;
            if (item.VisualEffectId >= 537 && item.VisualEffectId <= 548)
                effect = RebirthEffects[item.VisualEffectId - 537];
            else
            {
                var entry = PkoTables.SceneEffects.FirstOrDefault(e => e.Id == item.VisualEffectId);
                if (entry == null) throw new InvalidDataException($"No scene effect for wing {item.Id}.");
                effect = Path.GetFileNameWithoutExtension(entry.File);
            }
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/ImportedClient/Effects/{effect.ToLowerInvariant()}.json");
            if (json == null) throw new FileNotFoundException($"Missing converted wing effect {effect} for {item.Id}.");
            var data = JsonUtility.FromJson<PKOEffectPlayer.Data>(json.text);
            var root = new GameObject("wing_" + item.Id);
            try
            {
                foreach (var layer in data.effects)
                {
                    if (string.IsNullOrEmpty(layer.model) || !layer.model.EndsWith(".lgo", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Wing {item.Id} has unsupported model {layer.model}.");
                    string model = Path.GetFileNameWithoutExtension(layer.model);
                    if (builtModels.Add(model) && !SkinnedCharacterBuilder.Build(model, true))
                        throw new InvalidDataException($"Export embedded wing animation first: {model}.");
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Animated}/{model}.prefab");
                    if (prefab == null) throw new InvalidDataException($"Missing animated wing {model}.");
                    var instance = UnityEngine.Object.Instantiate(prefab, root.transform);
                    instance.name = model;
                    instance.transform.localPosition = PKOEffectPlayer.ModelPosition(layer.pos);
                    instance.transform.localRotation = PKOEffectPlayer.ModelRotation(layer.angles);
                    if (layer.sizes != null && layer.sizes.Length >= 3)
                        instance.transform.localScale = new Vector3(layer.sizes[0], layer.sizes[1], layer.sizes[2]);
                    var texture = LoadEffectTexture(layer.texture);
                    if (texture == null) throw new FileNotFoundException($"Missing wing texture {layer.texture}.");
                    foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        Color color = layer.colors != null && layer.colors.Length >= 4
                            ? new Color(layer.colors[0], layer.colors[1], layer.colors[2], layer.colors[3]) : Color.white;
                        var mesh = UnityEngine.Object.Instantiate(renderer.sharedMesh);
                        mesh.colors = Enumerable.Repeat(color, mesh.vertexCount).ToArray();
                        renderer.sharedMesh = SkinnedCharacterBuilder.SaveAsset(mesh, $"{Items}/{item.Id}_{instance.transform.GetSiblingIndex()}_mesh.asset");
                        var material = new Material(Shader.Find("PKO/Effect")) { mainTexture = texture };
                        material.SetFloat("_Src", PKOEffectPlayer.UnityBlend(layer.src, true));
                        material.SetFloat("_Dst", PKOEffectPlayer.UnityBlend(layer.dst, false));
                        material = SkinnedCharacterBuilder.SaveAsset(material, $"{Items}/{item.Id}_{instance.transform.GetSiblingIndex()}.mat");
                        renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMesh.subMeshCount).ToArray();
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, $"{Items}/{item.Id}.prefab");
                count++;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        EmperorWingBuilder.Build();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return $"Succeeded | {count} original wing visuals | {builtModels.Count} animated models | Mage Wings: Emperor model and original wind-loop animation";
    }

    static Texture2D LoadEffectTexture(string name)
    {
        foreach (string extension in new[] { ".tga", ".png", ".bmp" })
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/ImportedClient/Textures/effect/{name}{extension}");
            if (texture != null) return texture;
        }
        return null;
    }

}
