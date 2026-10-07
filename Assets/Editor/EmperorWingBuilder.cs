using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EmperorWingBuilder
{
    const string Request = "Tools/replace-mage-wings.request";
    const string Source = "Assets/Resources/Wings/Emperor/EmperorWings.fbx";
    const string Folder = "Assets/Resources/Wings/Animated/MageWings";
    const string Prefab = "Assets/Resources/Wings/Animated/MageWings.prefab";

    static EmperorWingBuilder() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { File.WriteAllText("Tools/replace-mage-wings-results.txt", Build()); }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText("Tools/replace-mage-wings-results.txt", "FAILED\n" + e);
        }
    }

    [MenuItem("Tools/PKO/Replace Mage Wings with Emperor")]
    public static void Menu() { Debug.Log(Build()); }

    public static string Build()
    {
        AssetDatabase.ImportAsset(Source, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(Source) as ModelImporter;
        if (importer == null) throw new FileNotFoundException("Emperor Wings FBX is missing.");
        importer.animationType = ModelImporterAnimationType.Legacy;
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.preserveHierarchy = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        var sourceClip = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Wings/Emperor/EmperorWings.png");
        var previous = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        if (source == null || sourceClip == null || texture == null || previous == null)
            throw new InvalidDataException("Emperor model, animation, texture or current Mage prefab is missing.");
        if (sourceClip.length < 7.8f || sourceClip.length > 8.1f)
            throw new InvalidDataException($"Unexpected Emperor animation duration: {sourceClip.length}.");

        Directory.CreateDirectory(Folder);
        var old = UnityEngine.Object.Instantiate(previous);
        var root = new GameObject("MageWings");
        try
        {
            old.GetComponent<Animation>().clip.SampleAnimation(old, 0f);
            Bounds target = PosedBounds(old);
            var model = new GameObject("EmperorModel");
            model.transform.SetParent(root.transform, false);
            var imported = UnityEngine.Object.Instantiate(source, model.transform);
            imported.name = "Imported";
            foreach (var animation in model.GetComponentsInChildren<Animation>())
                UnityEngine.Object.DestroyImmediate(animation);
            var clip = UnityEngine.Object.Instantiate(sourceClip);
            clip.name = "Asas_Penas_Vento_Loop";
            clip.legacy = true;
            clip.wrapMode = WrapMode.Loop;
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var prefixed = binding;
                prefixed.path = "EmperorModel/Imported" + (binding.path.Length == 0 ? "" : "/" + binding.path);
                AnimationUtility.SetEditorCurve(clip, prefixed, curve);
            }
            clip = SkinnedCharacterBuilder.SaveAsset(clip, Folder + "/Asas_Penas_Vento_Loop.anim");
            var playback = root.AddComponent<Animation>();
            playback.AddClip(clip, clip.name);
            playback.clip = clip;
            playback.wrapMode = WrapMode.Loop;
            playback.playAutomatically = true;
            clip.SampleAnimation(root, 0f);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidDataException("URP Lit shader is missing.");
            var body = new Material(shader) { mainTexture = texture };
            body.SetTexture("_BaseMap", texture);
            body.SetFloat("_Cull", 0f);
            body = SkinnedCharacterBuilder.SaveAsset(body, Folder + "/EmperorBody.mat");
            var wind = new Material(shader);
            wind.SetColor("_BaseColor", new Color(.28f, .65f, .85f, 1f));
            wind.SetColor("_EmissionColor", new Color(.048f, .156f, .27f));
            wind.EnableKeyword("_EMISSION");
            wind.SetFloat("_Cull", 0f);
            wind = SkinnedCharacterBuilder.SaveAsset(wind, Folder + "/EmperorWind.mat");
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = Enumerable.Repeat(
                    renderer.name.StartsWith("Rastro_Vento_", StringComparison.Ordinal) ? wind : body,
                    renderer.sharedMaterials.Length).ToArray();
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            Bounds incoming = PosedBounds(root);
            if (target.size.x <= 0f || incoming.size.x <= 0f)
                throw new InvalidDataException("Wing bounds are degenerate.");
            float factor = target.size.x / incoming.size.x;
            model.transform.localScale *= factor;
            model.transform.localPosition += target.center - incoming.center * factor;
            Bounds fitted = PosedBounds(root);
            if (Mathf.Abs(fitted.size.x - target.size.x) > .001f
                || Vector3.Distance(fitted.center, target.center) > .001f)
                throw new InvalidDataException("Emperor Wings did not preserve the previous size and center.");
            float motion = VerifyMotion(root, clip);
            clip.SampleAnimation(root, 0f);
            Bounds final = PosedBounds(root);
            if (Mathf.Abs(final.size.x - target.size.x) > .001f
                || Vector3.Distance(final.center, target.center) > .001f)
                throw new InvalidDataException("Animation changed the fitted wing size or attachment center.");
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            AssetDatabase.SaveAssets();
            return $"Succeeded\nAnimation={clip.name} Duration={clip.length:F3}s\n"
                + $"Renderers={root.GetComponentsInChildren<Renderer>().Length}\n"
                + $"PreviousWidth={target.size.x:F6} NewWidth={fitted.size.x:F6}\n"
                + $"CenterError={Vector3.Distance(fitted.center, target.center):F6}\n"
                + $"AnimatedVertexDisplacement={motion:F6}\n";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(old);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static Bounds PosedBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidDataException("Wing has no renderers.");
        foreach (var skin in renderers.OfType<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh);
                mesh.RecalculateBounds();
                skin.localBounds = mesh.bounds;
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    static float VerifyMotion(GameObject root, AnimationClip clip)
    {
        var skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin == null || skin.sharedMesh == null || skin.bones.Length < 6)
            throw new InvalidDataException("Emperor wing rig is missing.");
        var first = new Mesh();
        var second = new Mesh();
        try
        {
            clip.SampleAnimation(root, 0f);
            skin.BakeMesh(first);
            clip.SampleAnimation(root, clip.length * .4f);
            skin.BakeMesh(second);
            var a = first.vertices;
            var b = second.vertices;
            float motion = 0f;
            for (int i = 0; i < a.Length; i++) motion = Mathf.Max(motion, Vector3.Distance(a[i], b[i]));
            if (!float.IsFinite(motion) || motion < .001f)
                throw new InvalidDataException("Emperor wing animation does not deform its mesh.");
            return motion;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }
    }
}
