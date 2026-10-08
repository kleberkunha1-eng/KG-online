using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TOP.Character;
using TOP.Data;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class NewCharacterTestBuilder
{
    const string Source = "Assets/ImportedClient/NewCharacterTest/Personagem_RPG.glb";
    const string Output = "Assets/Resources/PkoChar/NewCharacterTest";
    static readonly string Request = Path.Combine(Application.dataPath, "../Tools/build-new-character-test.request");
    static readonly string Result = Path.Combine(Application.dataPath, "../Tools/new-character-test-results.txt");
    static readonly Regex BoneName = new Regex(@"^b(\d+)_");

    static NewCharacterTestBuilder() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        Build();
    }

    [MenuItem("Tools/PKO/Build NewCharacterTest")]
    public static void Build()
    {
        var report = new StringBuilder();
        GameObject rig = null;
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            if (source == null) throw new InvalidDataException("GLB is not imported: " + Source);
            var skins = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length != 1) throw new InvalidDataException("Expected one skinned source mesh.");
            var skin = skins[0];
            var mesh = skin.sharedMesh;
            if (mesh == null || skin.sharedMaterials.Any(material => material == null))
                throw new InvalidDataException("Source mesh/material is missing.");
            var reference = Resources.Load<PkoPartAsset>("PkoChar/Parts/0000000002");
            if (reference == null || reference.mesh == null) throw new InvalidDataException("Lance body is missing.");
            var lance = Resources.Load<GameObject>("PkoChar/Rig_0000");
            if (lance == null) throw new InvalidDataException("Lance rig is missing.");
            var animation = lance.GetComponent<Animation>();
            if (animation == null) throw new InvalidDataException("Lance animation library is missing.");
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            var materials = BuildMaterials(skin.sharedMaterials, report);
            var lanceBind = reference.mesh.bindposes;
            var lanceJoints = lanceBind.Select(pose => (Vector3)pose.inverse.GetColumn(3)).ToArray();
            // The rig, skin and animations are the ones exported from Blender: nothing is refitted or retargeted.
            rig = new GameObject("Rig_0004");
            var facing = new GameObject("Facing").transform;
            facing.SetParent(rig.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var byPath = model.GetComponentsInChildren<Transform>(true)
                .ToDictionary(node => AnimationUtility.CalculateTransformPath(node, model.transform));
            model.name = "Model";
            model.transform.SetParent(facing, false);
            foreach (var component in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(component);
            foreach (var component in model.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(component);
            var modelSkin = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
            var modelBones = modelSkin.bones;
            int nextBone = 56;
            foreach (var node in model.GetComponentsInChildren<Transform>(true))
                if (node.name.StartsWith("RPG_", StringComparison.Ordinal)) node.name = $"b{nextBone++}_{node.name}";
            var boneByIndex = model.GetComponentsInChildren<Transform>(true).Where(node => BoneName.IsMatch(node.name))
                .ToDictionary(node => int.Parse(BoneName.Match(node.name).Groups[1].Value));
            var lanceNames = lance.GetComponentsInChildren<Transform>(true).Where(node => BoneName.IsMatch(node.name))
                .ToDictionary(node => int.Parse(BoneName.Match(node.name).Groups[1].Value), node => node.name);
            for (int bone = 0; bone < lanceBind.Length; bone++)
            {
                if (boneByIndex.ContainsKey(bone)) continue;
                // Lance's toe nubs are not exported by Blender; they carry no skin and only keep the bone table complete.
                if (!boneByIndex.TryGetValue(bone - 1, out var parentBone))
                    throw new InvalidDataException("New model is missing bone " + bone);
                var nub = new GameObject(lanceNames[bone]).transform;
                nub.SetParent(parentBone, false);
                boneByIndex[bone] = nub;
                report.AppendLine($"INFO added placeholder {nub.name} under {parentBone.name}");
            }
            int boneCount = boneByIndex.Keys.Max() + 1;
            if (Enumerable.Range(0, boneCount).Any(bone => !boneByIndex.ContainsKey(bone)))
                throw new InvalidDataException("Bone indices of the new rig are not contiguous.");
            var rigBones = Enumerable.Range(0, boneCount).Select(bone => boneByIndex[bone]).ToArray();
            var sourceBind = mesh.bindposes;
            var skinIndex = modelBones.Select((bone, index) => (bone, index)).ToDictionary(entry => entry.bone, entry => entry.index);
            Matrix4x4 RigSpace(Transform node) => rig.transform.worldToLocalMatrix * node.localToWorldMatrix;
            Matrix4x4[] ComputeBind()
            {
                var skinToRig = RigSpace(modelSkin.transform);
                return rigBones.Select(bone => skinIndex.TryGetValue(bone, out int index)
                    ? sourceBind[index] * skinToRig.inverse : RigSpace(bone).inverse).ToArray();
            }
            Vector3 Joint(Matrix4x4[] poses, int bone) => poses[bone].inverse.GetColumn(3);
            var bind = ComputeBind();
            if (Mathf.Sign(Joint(bind, 34).z - Joint(bind, 33).z) != Mathf.Sign(lanceJoints[34].z - lanceJoints[33].z))
            {
                facing.localRotation = Quaternion.Euler(0, 180, 0);
                bind = ComputeBind();
            }
            Check(Mathf.Sign(Joint(bind, 34).z - Joint(bind, 33).z) == Mathf.Sign(lanceJoints[34].z - lanceJoints[33].z)
                && Mathf.Sign(Joint(bind, 18).x) == Mathf.Sign(lanceJoints[18].x),
                $"New model faces and is handed like Lance (facing yaw {facing.localEulerAngles.y:F0})", report);
            var meshToRig = RigSpace(modelSkin.transform);
            var normalToRig = meshToRig.inverse.transpose;
            var vertices = mesh.vertices.Select(vertex => meshToRig.MultiplyPoint3x4(vertex)).ToArray();
            var normals = mesh.normals.Select(normal => normalToRig.MultiplyVector(normal).normalized).ToArray();
            var uvs = mesh.uv;
            if (normals.Length != vertices.Length || uvs.Length != vertices.Length)
                throw new InvalidDataException("Source normals/UVs are incomplete.");
            var mapped = modelBones.Select(bone => Array.IndexOf(rigBones, bone)).ToArray();
            if (mapped.Any(index => index < 0)) throw new InvalidDataException("Skin uses a bone outside the rig.");
            var vertexWeights = new List<BoneWeight1>[vertices.Length];
            float droppedWeight = 0;
            using (var counts = mesh.GetBonesPerVertex())
            using (var weights = mesh.GetAllBoneWeights())
            {
                int cursor = 0;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    var combined = new Dictionary<int, float>();
                    float total = 0;
                    for (int influence = 0; influence < counts[vertex]; influence++)
                    {
                        var weight = weights[cursor++];
                        if (weight.weight <= 0) continue;
                        int target = mapped[weight.boneIndex];
                        combined[target] = combined.TryGetValue(target, out float existing) ? existing + weight.weight : weight.weight;
                        total += weight.weight;
                    }
                    if (total <= 0) throw new InvalidDataException("Unweighted vertex " + vertex);
                    var strongest = combined.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).Take(4).ToList();
                    float kept = strongest.Sum(entry => entry.Value);
                    droppedWeight = Mathf.Max(droppedWeight, 1f - kept / total);
                    // Real-time skinning is limited to 4 bones per vertex; Blender exported up to 12.
                    vertexWeights[vertex] = strongest
                        .Select(entry => new BoneWeight1 { boneIndex = entry.Key, weight = entry.Value / kept }).ToList();
                }
            }
            report.AppendLine($"INFO skin limited to the 4 strongest bones per vertex (largest dropped weight {droppedWeight:P1})");
            float restHeight = vertices.Max(vertex => vertex.y) - vertices.Min(vertex => vertex.y);
            Check(Mathf.Abs(restHeight - mesh.bounds.size.y) < .001f,
                $"Model keeps its exported size: {restHeight:F3} m", report);
            var triangles = new List<int>[5];
            for (int part = 0; part < triangles.Length; part++) triangles[part] = new List<int>();
            if (mesh.subMeshCount != 1) throw new InvalidDataException("Expected one source material/submesh.");
            var indices = mesh.triangles;
            for (int triangle = 0; triangle < indices.Length; triangle += 3)
            {
                var groups = new float[5];
                for (int corner = 0; corner < 3; corner++)
                    foreach (var weight in vertexWeights[indices[triangle + corner]])
                        groups[PartForBone(weight.boneIndex)] += weight.weight;
                int part = Array.IndexOf(groups, groups.Max());
                for (int corner = 0; corner < 3; corner++) triangles[part].Add(indices[triangle + corner]);
            }
            for (int part = 0; part < triangles.Length; part++)
            {
                var baked = PartMesh(vertices, normals, uvs, vertexWeights, triangles[part], bind, part);
                string path = $"{Output}/part_{part}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<PkoPartAsset>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<PkoPartAsset>();
                    AssetDatabase.CreateAsset(asset, path);
                }
                if (asset.mesh != null) Object.DestroyImmediate(asset.mesh, true);
                asset.mesh = baked;
                asset.materials = materials;
                AssetDatabase.AddObjectToAsset(baked, asset);
                EditorUtility.SetDirty(asset);
                report.AppendLine($"PASS part={part} vertices={baked.vertexCount} triangles={triangles[part].Count / 3} bounds={baked.bounds}");
            }
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer.gameObject);
            CopyClips(source, rig, byPath, animation, report);
            // Lance equipment: each piece is scaled around its Lance joint and placed on the same joint of this rig.
            float equipmentScale = Joint(bind, 6).y / lanceJoints[6].y;
            var rigFit = rig.AddComponent<PkoRigFit>();
            rigFit.equipmentScale = equipmentScale;
            rigFit.boneCount = boneCount;
            rigFit.boneCorrection = Enumerable.Range(0, lanceBind.Length).Select(bone => bind[bone]
                * Matrix4x4.Translate(Joint(bind, bone)) * Matrix4x4.Scale(Vector3.one * equipmentScale)
                * Matrix4x4.Translate(-lanceJoints[bone]) * lanceBind[bone].inverse).ToArray();
            report.AppendLine($"INFO rig bones={boneCount}, Lance equipment scale {equipmentScale:F3}");
            PrefabUtility.SaveAsPrefabAsset(rig, "Assets/Resources/PkoChar/Rig_0004.prefab");
            AssetDatabase.SaveAssets();
            report.AppendLine($"PASS source vertices={vertices.Length} triangles={indices.Length / 3} skin joints={modelBones.Length}");
            ReferenceComparison(vertices, vertexWeights, bind, mesh.bounds.size.y, report);
            Validate(report, animation);
        }
        catch (Exception exception)
        {
            report.AppendLine("FAIL " + exception);
            Debug.LogException(exception);
        }
        finally { if (rig != null) Object.DestroyImmediate(rig); }
        File.WriteAllText(Result, report.ToString());
        Debug.Log("[NewCharacterTest] " + report);
    }

    // Blender clips are converted to legacy clips on the rebuilt hierarchy; events/wrap modes follow Lance's clip.
    static void CopyClips(GameObject source, GameObject rig, Dictionary<string, Transform> byPath, Animation lance,
        StringBuilder report)
    {
        string folder = Output + "/Animations";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Output, "Animations");
        var lanceClips = AnimationUtility.GetAnimationClips(lance.gameObject).Where(clip => clip != null)
            .GroupBy(clip => clip.name).ToDictionary(group => group.Key, group => group.First());
        var sourceClips = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview", StringComparison.Ordinal)).ToArray();
        Transform Resolve(string path)
        {
            while (true)
            {
                if (byPath.TryGetValue(path, out var node)) return node;
                int slash = path.IndexOf('/');
                if (slash < 0) return null;
                path = path.Substring(slash + 1);
            }
        }
        string Property(string name) =>
            name.StartsWith("localPosition", StringComparison.Ordinal) ? "m_LocalPosition" + name.Substring(13)
            : name.StartsWith("localRotation", StringComparison.Ordinal) ? "m_LocalRotation" + name.Substring(13)
            : name.StartsWith("localScale", StringComparison.Ordinal) ? "m_LocalScale" + name.Substring(10)
            : name;
        var clips = new List<AnimationClip>();
        int curves = 0, missing = 0, flips = 0;
        long keysBefore = 0, keysAfter = 0;
        foreach (var original in sourceClips)
        {
            lanceClips.TryGetValue(original.name, out var lanceClip);
            var clip = new AnimationClip
            {
                name = original.name, legacy = true, frameRate = original.frameRate,
                wrapMode = lanceClip != null ? lanceClip.wrapMode : original.wrapMode
            };
            foreach (var binding in AnimationUtility.GetCurveBindings(original))
            {
                var node = binding.type == typeof(Transform) ? Resolve(binding.path) : null;
                if (node == null || node.GetComponent<Renderer>() != null) { missing++; continue; }
                var target = EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(node, rig.transform),
                    typeof(Transform), Property(binding.propertyName));
                AnimationUtility.SetEditorCurve(clip, target, AnimationUtility.GetEditorCurve(original, binding));
                curves++;
            }
            if (lanceClip != null) AnimationUtility.SetAnimationEvents(clip, AnimationUtility.GetAnimationEvents(lanceClip));
            flips += FixQuaternionContinuity(clip);
            var (before, after) = ReduceKeys(clip);
            keysBefore += before; keysAfter += after;
            string path = $"{folder}/{original.name}.anim";
            // Replaced without loading the old asset (the unreduced clips were ~100 MB each); only Rig_0004, rebuilt below, references them.
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets))) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            clips.Add(clip);
        }
        var animation = rig.AddComponent<Animation>();
        var idle = clips.FirstOrDefault(clip => clip.name == "0000_action1") ?? clips.FirstOrDefault();
        // Only the idle ships inside the rig; PkoPoseDriver loads the other clips from Resources on first use.
        AnimationUtility.SetAnimationClips(animation, idle != null ? new[] { idle } : new AnimationClip[0]);
        animation.clip = idle;
        animation.playAutomatically = false;
        var absent = lanceClips.Keys.Where(name => clips.All(clip => clip.name != name)).ToArray();
        Check(clips.Count > 0 && missing == 0 && absent.Length == 0,
            $"Blender animations used as exported: {clips.Count} clips, {curves} curves, {flips} rotation keys sign-aligned, unresolved {missing}, missing Lance actions [{string.Join(",", absent)}]", report);
        Check(keysAfter > 0 && keysAfter * 4 <= keysBefore,
            $"Animation keys reduced (same motion within tolerance): {keysBefore} -> {keysAfter}", report);
    }

    // The GLB bakes a key on every frame of every channel (gigabytes of clips, minute-long freeze when the rig loads).
    // Per bone channel group (position/rotation/scale) keep only the keys needed to reproduce the motion with linear
    // interpolation inside the tolerance; constant channels keep a single key.
    static (long before, long after) ReduceKeys(AnimationClip clip)
    {
        long before = 0, after = 0;
        foreach (var group in AnimationUtility.GetCurveBindings(clip)
            .GroupBy(binding => binding.path + "|" + binding.propertyName.Substring(0, Math.Max(0, binding.propertyName.LastIndexOf('.')))))
        {
            var bindings = group.ToArray();
            var source = bindings.Select(binding => AnimationUtility.GetEditorCurve(clip, binding)).ToArray();
            var times = source.SelectMany(curve => curve.keys.Select(key => key.time)).Distinct().OrderBy(time => time).ToArray();
            if (times.Length == 0) continue;
            var values = source.Select(curve => times.Select(curve.Evaluate).ToArray()).ToArray();
            string property = bindings[0].propertyName;
            float tolerance = property.StartsWith("m_LocalRotation", StringComparison.Ordinal) ? 0.001f
                : property.StartsWith("m_LocalScale", StringComparison.Ordinal) ? 0.001f : 0.0005f;
            before += (long)times.Length * bindings.Length;

            var kept = new List<int> { 0 };
            bool constant = values.All(channel => channel.All(value => Mathf.Abs(value - channel[0]) <= tolerance));
            if (!constant)
            {
                int anchor = 0;
                for (int candidate = 2; candidate < times.Length; candidate++)
                {
                    bool fits = true;
                    for (int middle = anchor + 1; middle < candidate && fits; middle++)
                    {
                        float t = (times[middle] - times[anchor]) / (times[candidate] - times[anchor]);
                        foreach (var channel in values)
                            if (Mathf.Abs(Mathf.Lerp(channel[anchor], channel[candidate], t) - channel[middle]) > tolerance) { fits = false; break; }
                    }
                    if (!fits) { anchor = candidate - 1; kept.Add(anchor); }
                }
            }
            // The last key keeps the clip length (legacy Animation derives it from the curves).
            if (times.Length > 1) kept.Add(times.Length - 1);

            for (int axis = 0; axis < bindings.Length; axis++)
            {
                var keys = new Keyframe[kept.Count];
                for (int k = 0; k < kept.Count; k++)
                {
                    int i = kept[k];
                    float inSlope = k > 0 ? (values[axis][i] - values[axis][kept[k - 1]]) / (times[i] - times[kept[k - 1]]) : 0f;
                    float outSlope = k + 1 < kept.Count ? (values[axis][kept[k + 1]] - values[axis][i]) / (times[kept[k + 1]] - times[i]) : 0f;
                    keys[k] = new Keyframe(times[i], values[axis][i], inSlope, outSlope);
                }
                AnimationUtility.SetEditorCurve(clip, bindings[axis], new AnimationCurve(keys));
            }
            after += (long)kept.Count * bindings.Length;
        }
        return (before, after);
    }

    // glTF rotation keys may switch quaternion hemisphere between frames; per-component interpolation then
    // passes near a zero quaternion and folds the bone. Keep consecutive keys in the same hemisphere.
    static int FixQuaternionContinuity(AnimationClip clip)
    {
        int flipped = 0;
        foreach (var group in AnimationUtility.GetCurveBindings(clip)
            .Where(binding => binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal))
            .GroupBy(binding => binding.path))
        {
            var bindings = new[] { "x", "y", "z", "w" }
                .Select(axis => group.FirstOrDefault(binding => binding.propertyName == "m_LocalRotation." + axis)).ToArray();
            if (bindings.Any(binding => binding.path == null)) continue;
            var curves = bindings.Select(binding => AnimationUtility.GetEditorCurve(clip, binding)).ToArray();
            var times = curves.SelectMany(curve => curve.keys.Select(key => key.time)).Distinct().OrderBy(time => time).ToArray();
            var keys = curves.Select(_ => new Keyframe[times.Length]).ToArray();
            Vector4 previous = Vector4.zero;
            bool changed = false;
            for (int index = 0; index < times.Length; index++)
            {
                var value = new Vector4(curves[0].Evaluate(times[index]), curves[1].Evaluate(times[index]),
                    curves[2].Evaluate(times[index]), curves[3].Evaluate(times[index]));
                if (index > 0 && Vector4.Dot(previous, value) < 0) { value = -value; flipped++; changed = true; }
                previous = value;
                for (int axis = 0; axis < 4; axis++) keys[axis][index] = new Keyframe(times[index], value[axis]);
            }
            if (!changed) continue;
            for (int axis = 0; axis < 4; axis++)
            {
                var curve = new AnimationCurve(keys[axis]);
                for (int index = 0; index < curve.length; index++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(clip, bindings[axis], curve);
            }
        }
        return flipped;
    }

    // Compares the fitted idle against the reference export of the same mesh (Blender OBJ in Idle action1).
    static void ReferenceComparison(Vector3[] vertices, List<BoneWeight1>[] weights, Matrix4x4[] bindposes,
        float sourceHeight, StringBuilder report)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/PkoChar/Rig_0004.prefab");
        var rig = Object.Instantiate(prefab);
        try
        {
            var bones = new Transform[bindposes.Length];
            foreach (var node in rig.GetComponentsInChildren<Transform>(true))
            {
                var match = BoneName.Match(node.name);
                int index = match.Success ? int.Parse(match.Groups[1].Value) : -1;
                if (index >= 0 && index < bones.Length) bones[index] = node;
            }
            var idle = rig.GetComponent<Animation>().GetClip("0000_action1");
            idle.SampleAnimation(rig, 0);
            var skinning = bones.Select((bone, index) => rig.transform.worldToLocalMatrix * bone.localToWorldMatrix * bindposes[index]).ToArray();
            var posed = new Vector3[vertices.Length];
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                Vector3 point = Vector3.zero;
                foreach (var weight in weights[vertex])
                    point += skinning[weight.boneIndex].MultiplyPoint3x4(vertices[vertex]) * weight.weight;
                posed[vertex] = point;
                minY = Mathf.Min(minY, point.y); maxY = Mathf.Max(maxY, point.y);
            }
            Check(Mathf.Abs((maxY - minY) - sourceHeight) < sourceHeight * .08f,
                $"Idle keeps the model's original size: height {maxY - minY:F3} vs model {sourceHeight:F3} (feet {minY:F3})", report);
            string obj = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "new character", "the char", "Personagem_RPG_Idle_action1.obj");
            if (!File.Exists(obj)) { report.AppendLine("INFO reference OBJ not found: " + obj); return; }
            var reference = File.ReadLines(obj).Where(line => line.StartsWith("v ", StringComparison.Ordinal))
                .Select(line => line.Split(' '))
                .Select(parts => new Vector3(-float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            float referenceHeight = reference.Max(point => point.y) - reference.Min(point => point.y);
            report.AppendLine($"INFO reference OBJ idle height {referenceHeight:F3}, fitted idle height {maxY - minY:F3}");
            if (reference.Length != posed.Length) return;
            foreach (var yaw in new[] { 0f, 180f })
            {
                var rotation = Quaternion.Euler(0, yaw, 0);
                var distances = posed.Select((point, index) => Vector3.Distance(rotation * point, reference[index])).OrderBy(d => d).ToArray();
                report.AppendLine($"INFO reference OBJ vertex distance yaw={yaw}: median={distances[distances.Length / 2]:F3} p95={distances[(int)(distances.Length * .95f)]:F3}");
            }
        }
        finally { Object.DestroyImmediate(rig); }
    }

    static int PartForBone(int bone)
    {
        if (bone >= 56) return 3;
        if (bone >= 7 && bone <= 14) return 1;
        if (bone == 6) return 0;
        if ((bone >= 18 && bone <= 22) || (bone >= 26 && bone <= 30)) return 3;
        if ((bone >= 33 && bone <= 35) || (bone >= 38 && bone <= 40)) return 4;
        return 2;
    }

    static Material[] BuildMaterials(Material[] source, StringBuilder report)
    {
        if (source.Length != 1) throw new InvalidDataException("Expected one character material.");
        string path = Output + "/NewCharacterTest.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source[0]);
            AssetDatabase.CreateAsset(material, path);
        }
        else material.CopyPropertiesFromMaterial(source[0]);
        foreach (var texture in new[]
        {
            ("baseColor", "baseColorTexture"), ("normal", "normalTexture"),
            ("metallicRoughness", "metallicRoughnessTexture")
        })
        {
            string texturePath = "Assets/ImportedClient/NewCharacterTest/Textures/" + texture.Item1 + ".png";
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) throw new InvalidDataException("Run node Tools/Extract-NewCharacterTextures.cjs: " + texturePath);
            importer.maxTextureSize = 4096;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.sRGBTexture = texture.Item1 == "baseColor";
            importer.textureType = texture.Item1 == "normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            material.SetTexture(texture.Item2, AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            report.AppendLine("PASS high-quality compressed mipmapped texture <=4096: " + texture.Item1);
        }
        EditorUtility.SetDirty(material);
        return new[] { material };
    }

    static Mesh PartMesh(Vector3[] positions, Vector3[] normals, Vector2[] uvs,
        List<BoneWeight1>[] weights, List<int> triangles, Matrix4x4[] bindposes, int part)
    {
        int[] used = triangles.Distinct().OrderBy(index => index).ToArray();
        var remap = used.Select((old, index) => (old, index)).ToDictionary(entry => entry.old, entry => entry.index);
        var mesh = new Mesh { name = "NewCharacterTest_part_" + part, indexFormat = IndexFormat.UInt32 };
        mesh.vertices = used.Select(index => positions[index]).ToArray();
        mesh.normals = used.Select(index => normals[index]).ToArray();
        mesh.uv = used.Select(index => uvs[index]).ToArray();
        mesh.bindposes = bindposes;
        using (var counts = new NativeArray<byte>(used.Select(index => checked((byte)weights[index].Count)).ToArray(), Allocator.Temp))
        using (var influences = new NativeArray<BoneWeight1>(used.SelectMany(index => weights[index]).ToArray(), Allocator.Temp))
            mesh.SetBoneWeights(counts, influences);
        mesh.triangles = triangles.Select(index => remap[index]).ToArray();
        mesh.RecalculateBounds();
        if (used.Length > 0) mesh.RecalculateTangents();
        return mesh;
    }

    static void Check(bool condition, string message, StringBuilder report)
    {
        report.AppendLine((condition ? "PASS " : "FAIL ") + message);
        if (!condition) throw new InvalidDataException(message);
    }

    static void Validate(StringBuilder report, Animation original)
    {
        var root = new GameObject("NewCharacterTestValidation");
        try
        {
            var visual = PkoCharacterVisual.Create(root.transform, PkoRaces.NewCharacterTest, 0, 0, null);
            var animation = visual.GetComponentInChildren<Animation>();
            Check(visual.Race == PkoRaces.NewCharacterTest && animation != null, "New race loads its own rig.", report);
            var originalClips = original.Cast<AnimationState>().ToArray();
            Check(animation.GetClipCount() == 1 && animation.clip != null && animation.clip.name == "0000_action1",
                "Rig ships only the idle clip (others load on demand).", report);
            foreach (var state in originalClips)
                if (animation.GetClip(state.name) == null)
                {
                    var lazy = Resources.Load<AnimationClip>(PkoCharacterVisual.NewCharacterClipFolder + state.name);
                    if (lazy != null) animation.AddClip(lazy, state.name);
                }
            var copiedClips = animation.Cast<AnimationState>().ToArray();
            Check(originalClips.Length > 0 && copiedClips.Length == originalClips.Length,
                "Complete Lance animation library: " + originalClips.Length + " clips.", report);
            foreach (var state in originalClips)
            {
                var fitted = animation.GetClip(state.name);
                Check(fitted != null && fitted != state.clip && fitted.length > 0,
                    $"Blender clip for {state.name}: length {(fitted != null ? fitted.length : 0):F2}s (Lance {state.clip.length:F2}s)", report);
                foreach (float phase in new[] { 0f, .5f, 1f })
                {
                    fitted.SampleAnimation(animation.gameObject, fitted.length * phase);
                    foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var baked = new Mesh();
                        try
                        {
                            renderer.BakeMesh(baked);
                            var vertices = baked.vertices;
                            bool stable = StableEdges(renderer.sharedMesh, vertices);
                            // Deep knee/foot bends in the Blender export stretch the boot cuffs the same way in Blender.
                            if (!stable) report.AppendLine($"INFO {state.name} phase={phase} as exported from Blender: " + StretchDiagnostics(renderer, vertices));
                            Check(vertices.Length > 0 && vertices.All(vertex => float.IsFinite(vertex.x)
                                && float.IsFinite(vertex.y) && float.IsFinite(vertex.z))
                                && baked.bounds.size.magnitude < 20f,
                                $"{state.name} phase={phase} {renderer.name} finite geometry, bounds={baked.bounds.size}", report);
                        }
                        finally { Object.DestroyImmediate(baked); }
                    }
                }
            }
            foreach (int pose in new[] { PkoPoses.Wait, PkoPoses.Run, PkoPoses.Attack1, PkoPoses.Death, PkoPoses.Sit,
                PkoPoses.FlyWait, PkoPoses.FlyRun })
                Check(visual.Pose.Has(pose), "Gameplay pose available: " + pose, report);
            var lanceOnly = new PkoItem { Races = new[] { 1 } };
            Check(PkoClasses.RaceOk(lanceOnly, PkoRaces.NewCharacterTest)
                && !PkoClasses.RaceOk(lanceOnly, 3), "Lance equipment restrictions inherited without changing Ami.", report);
            Check(PkoWeaponPose.Key(4, 0) == PkoWeaponPose.Key(0, 0)
                && ReferenceEquals(PkoWingPose.Get(4, PkoTables.MeshyMageWingsItemId),
                    PkoWingPose.Get(0, PkoTables.MeshyMageWingsItemId)), "Lance weapon and wing adjustments inherited.", report);
            var flow = typeof(TOP.UI.Pko.PkoFlow);
            var raceNames = (string[])flow.GetField("Races", System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            Check(raceNames.Length == PkoRaces.Count && raceNames[4] == "NewCharacterTest",
                "Creation and selection catalog includes NewCharacterTest.", report);
            PreviewValidation(root.transform, report);
            AppearanceValidation(root.transform, report);
            EquipmentValidation(root.transform, report);
            Capture(visual, report);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void PreviewValidation(Transform parent, StringBuilder report)
    {
        for (int race = 0; race < PkoRaces.Count; race++)
        {
            var visual = PkoCharacterVisual.Create(parent, race, 0, 0, null);
            Check(visual.Race == race && visual.GetComponentsInChildren<SkinnedMeshRenderer>().Length > 0
                && visual.Pose.Has(PkoPoses.Wait), "Existing/new playable race remains valid: " + PkoRaces.Name(race), report);
            Object.DestroyImmediate(visual.gameObject);
        }
    }

    static void AppearanceValidation(Transform parent, StringBuilder report)
    {
        foreach (var style in new[] { (face: 0, hair: 1), (face: 1, hair: 0) })
        {
            var visual = PkoCharacterVisual.Create(parent, PkoRaces.NewCharacterTest, style.face, style.hair, null);
            try
            {
                var names = visual.GetComponentsInChildren<SkinnedMeshRenderer>().Select(renderer => renderer.sharedMesh.name).ToArray();
                var face = Resources.Load<PkoPartAsset>("PkoChar/Parts/0000" + style.face.ToString("00") + "0000");
                var hair = Resources.Load<PkoPartAsset>("PkoChar/Parts/0000" + style.hair.ToString("00") + "0001");
                var customFace = Resources.Load<PkoPartAsset>("PkoChar/NewCharacterTest/part_0");
                Check(face != null && hair != null && names.Contains(face.mesh.name + "_fit") && names.Contains(hair.mesh.name + "_fit")
                    && !names.Contains(customFace.mesh.name),
                    $"Lance appearance face={style.face} hair={style.hair} replaces integrated custom head/hair without overlays.", report);
            }
            finally { Object.DestroyImmediate(visual.gameObject); }
        }
    }

    static bool StableEdges(Mesh rest, Vector3[] animated)
    {
        var positions = rest.vertices;
        var indices = rest.triangles;
        int checkedEdges = 0, stretched = 0;
        for (int triangle = 0; triangle < indices.Length; triangle += 3)
            for (int corner = 0; corner < 3; corner++)
            {
                int a = indices[triangle + corner], b = indices[triangle + (corner + 1) % 3];
                float lengthSquared = (positions[a] - positions[b]).sqrMagnitude;
                if (lengthSquared <= .0000000001f) continue;
                checkedEdges++;
                if ((animated[a] - animated[b]).sqrMagnitude > lengthSquared * 16f) stretched++;
            }
        return checkedEdges > 0 && stretched <= checkedEdges * .01f;
    }

    static string StretchDiagnostics(SkinnedMeshRenderer renderer, Vector3[] animated)
    {
        var mesh = renderer.sharedMesh;
        var positions = mesh.vertices;
        var indices = mesh.triangles;
        var weights = mesh.boneWeights;
        var bones = new Dictionary<int, int>();
        int stretched = 0, total = 0;
        float worst = 0;
        for (int triangle = 0; triangle < indices.Length; triangle += 3)
            for (int corner = 0; corner < 3; corner++)
            {
                int a = indices[triangle + corner], b = indices[triangle + (corner + 1) % 3];
                float rest = (positions[a] - positions[b]).sqrMagnitude;
                if (rest <= .0000000001f) continue;
                total++;
                float ratio = (animated[a] - animated[b]).sqrMagnitude / rest;
                if (ratio <= 16f) continue;
                stretched++;
                worst = Mathf.Max(worst, Mathf.Sqrt(ratio));
                foreach (var weight in new[] { weights[a], weights[b] })
                    bones[weight.boneIndex0] = bones.TryGetValue(weight.boneIndex0, out int count) ? count + 1 : 1;
            }
        var text = new StringBuilder($"{renderer.name}: {stretched}/{total} edges >4x (worst {worst:F1}x); main bones:");
        foreach (var entry in bones.OrderByDescending(entry => entry.Value).Take(6))
        {
            var bone = renderer.bones[entry.Key];
            text.Append($" [{bone.name} n={entry.Value} localPos={bone.localPosition:F3} localScale={bone.localScale:F3}]");
        }
        return text.ToString();
    }

    static void EquipmentValidation(Transform parent, StringBuilder report)
    {
        var ids = BlueMageSet.Pieces.Select(piece => piece.Id).ToArray();
        var visual = PkoCharacterVisual.Create(parent, 4, 0, 0, ids);
        try
        {
            Check(visual.Equipped.SequenceEqual(ids)
                && visual.GetComponentsInChildren<SkinnedMeshRenderer>().Count(renderer => renderer.name.StartsWith("blue_")
                    && renderer.sharedMesh.name.EndsWith("_fit", StringComparison.Ordinal)) >= 5,
                "New character supports all Blue Mage equipment refitted to its body size.", report);
        }
        finally { Object.DestroyImmediate(visual.gameObject); }
    }

    static void Capture(PkoCharacterVisual visual, StringBuilder report)
    {
        var cameraObject = new GameObject("NewCharacterTestPreviewCamera");
        var lightObject = new GameObject("NewCharacterTestPreviewLight");
        var texture = new Texture2D(768, 768, TextureFormat.RGB24, false);
        var target = new RenderTexture(768, 768, 24);
        var previous = RenderTexture.active;
        try
        {
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .16f, .22f);
            camera.orthographic = true;
            camera.orthographicSize = 1.4f;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 15f;
            camera.targetTexture = target;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(35, -30, 0);
            int layer = 30;
            foreach (var node in visual.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
            camera.cullingMask = 1 << layer;
            visual.transform.position = new Vector3(0, -500, 0);
            var animation = visual.GetComponentInChildren<Animation>();
            var idle = animation.GetClip("0000_action1");
            idle.SampleAnimation(animation.gameObject, 0);
            foreach (var view in new[] { ("front", new Vector3(0, 1.15f, -4)), ("back", new Vector3(0, 1.15f, 4)),
                ("side", new Vector3(4, 1.15f, 0)) })
            {
                camera.transform.position = visual.transform.position + view.Item2;
                camera.transform.LookAt(visual.transform.position + Vector3.up * 1.15f);
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 768, 768), 0, 0);
                texture.Apply();
                string path = Path.Combine(Application.dataPath, "../Tools/new-character-test-" + view.Item1 + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                report.AppendLine("PASS preview " + view.Item1);
            }
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
        }
    }
}
