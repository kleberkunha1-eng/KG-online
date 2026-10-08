using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TOP.Character;
using TOP.Core;
using TOP.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class BlueMageSetBuilder
{
    const string Request = "Tools/build-blue-mage-set.request";
    const string Source = "Assets/ImportedClient/BlueMageSet";
    const string Output = "Assets/Resources/PkoChar/BlueMageSet";
    [Serializable] sealed class Geometry { public float[] positions, normals, uvs; public int[] triangles; }
    [Serializable] sealed class BoneInfo { public string name; }
    [Serializable] sealed class Skeleton { public BoneInfo[] bones; }

    static BlueMageSetBuilder() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        Build();
    }

    [MenuItem("Tools/PKO/Build Blue Mage Set")]
    public static void Build()
    {
        var report = new StringBuilder();
        try
        {
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            BuildUndersuits(shader, report);
            foreach (var piece in BlueMageSet.Pieces)
            {
                var geometry = JsonUtility.FromJson<Geometry>(File.ReadAllText($"{Source}/{piece.Key}.json"));
                if (geometry == null || geometry.positions == null || geometry.normals == null
                    || geometry.uvs == null || geometry.triangles == null)
                    throw new InvalidDataException("Incomplete source geometry: " + piece.Key);
                string texturePath = $"{Source}/{piece.Key}.png";
                var textureImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                textureImporter.maxTextureSize = 2048;
                textureImporter.textureCompression = TextureImporterCompression.Compressed;
                textureImporter.mipmapEnabled = true;
                textureImporter.SaveAndReimport();
                string iconPath = "Assets/Resources/" + BlueMageSet.IconPath(piece) + ".png";
                var iconImporter = (TextureImporter)AssetImporter.GetAtPath(iconPath);
                iconImporter.textureType = TextureImporterType.Sprite;
                iconImporter.spriteImportMode = SpriteImportMode.Single;
                iconImporter.mipmapEnabled = false;
                iconImporter.alphaIsTransparency = true;
                iconImporter.maxTextureSize = 256;
                iconImporter.textureCompression = TextureImporterCompression.Uncompressed;
                iconImporter.SaveAndReimport();
                string materialPath = $"{Output}/{piece.Key}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                material.SetFloat("_Metallic", .25f);
                material.SetFloat("_Smoothness", .45f);
                EditorUtility.SetDirty(material);
                for (int race = 0; race < PkoCharacterVisual.Races; race++)
                {
                    var body = Part(race, 2);
                    var reference = piece.Slot == EquipmentSlot.Gloves ? Part(race, 3)
                        : piece.Slot == EquipmentSlot.Boots ? Part(race, 4)
                        : piece.Slot == EquipmentSlot.Helmet ? Part(race, 0) : body;
                    var skeleton = JsonUtility.FromJson<Skeleton>(File.ReadAllText(
                        $"Assets/ImportedClient/Skinned/{race:0000}.json"));
                    var mesh = Fit(geometry, reference.mesh, body.mesh, skeleton, piece, race);
                    string assetPath = $"{Output}/{piece.Id}_{race}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<PkoPartAsset>(assetPath);
                    if (asset == null)
                    {
                        asset = ScriptableObject.CreateInstance<PkoPartAsset>();
                        AssetDatabase.CreateAsset(asset, assetPath);
                    }
                    if (asset.mesh != null) UnityEngine.Object.DestroyImmediate(asset.mesh, true);
                    asset.mesh = mesh;
                    asset.materials = new[] { material };
                    AssetDatabase.AddObjectToAsset(mesh, asset);
                    EditorUtility.SetDirty(asset);
                    report.AppendLine($"PASS {piece.Name} race={race} vertices={mesh.vertexCount} triangles={mesh.triangles.Length / 3} bounds={mesh.bounds}");
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Validate(report);
        }
        catch (Exception e) { report.AppendLine("FAIL " + e); Debug.LogException(e); }
        File.WriteAllText("Tools/blue-mage-set-results.txt", report.ToString());
        Debug.Log(report.ToString());
    }

    static PkoPartAsset Part(int race, int part)
    {
        var asset = Resources.Load<PkoPartAsset>($"PkoChar/Parts/{race:0000}00{part:0000}");
        if (asset == null || asset.mesh == null) throw new InvalidOperationException($"Missing reference race {race} part {part}");
        return asset;
    }

    static void BuildUndersuits(Shader shader, StringBuilder report)
    {
        string path = Output + "/undersuit.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.color = new Color(.12f, .23f, .50f);
        material.SetFloat("_Metallic", .15f);
        material.SetFloat("_Smoothness", .30f);
        EditorUtility.SetDirty(material);
        for (int race = 0; race < PkoCharacterVisual.Races; race++)
        {
            var source = Part(race, 2).mesh;
            var vertices = source.vertices;
            var weights = source.boneWeights;
            float pelvis = source.bindposes[0].inverse.GetColumn(3).y;
            Vector3 neck = source.bindposes[5].inverse.GetColumn(3);
            Vector3 head = source.bindposes[6].inverse.GetColumn(3);
            var face = Part(race, 0).mesh.bounds;
            float neckRadius = face.size.x * .20f;
            float neckTop = head.y + source.bounds.size.y * .015f;
            var skeleton = JsonUtility.FromJson<Skeleton>(File.ReadAllText($"Assets/ImportedClient/Skinned/{race:0000}.json"));
            int Bone(string name)
            {
                int bone = Array.FindIndex(skeleton.bones, b => b.name == "Bip01 " + name);
                if (bone < 0) throw new InvalidDataException("Missing undersuit joint: " + name);
                return bone;
            }
            Vector3 Joint(string name) => source.bindposes[Bone(name)].inverse.GetColumn(3);
            var hips = new[] { Joint("L Thigh"), Joint("R Thigh") };
            var knees = new[] { Joint("L Calf"), Joint("R Calf") };
            var ankles = new[] { Joint("L Foot"), Joint("R Foot") };
            var fitted = (Vector3[])vertices.Clone();
            for (int i = 0; i < fitted.Length; i++)
            {
                var w = weights[i];
                Vector3 center = (Vector3)source.bindposes[w.boneIndex0].inverse.GetColumn(3) * w.weight0
                    + (Vector3)source.bindposes[w.boneIndex1].inverse.GetColumn(3) * w.weight1
                    + (Vector3)source.bindposes[w.boneIndex2].inverse.GetColumn(3) * w.weight2
                    + (Vector3)source.bindposes[w.boneIndex3].inverse.GetColumn(3) * w.weight3;
                float inset = vertices[i].y > source.bounds.max.y - source.bounds.size.y * .16f ? .95f : .72f;
                fitted[i].x = Mathf.Lerp(center.x, fitted[i].x, inset);
                fitted[i].z = Mathf.Lerp(center.z, fitted[i].z, inset);
                // The original body includes raised clothing collars, not just skin.
                if (vertices[i].y > neck.y)
                {
                    fitted[i].y = Mathf.Lerp(neck.y, neckTop,
                        Mathf.InverseLerp(neck.y, source.bounds.max.y, vertices[i].y));
                    var neckCenter = Vector3.Lerp(neck, head, Mathf.InverseLerp(neck.y, head.y, fitted[i].y));
                    fitted[i].x = neckCenter.x + Mathf.Clamp(fitted[i].x - neckCenter.x, -neckRadius, neckRadius);
                    fitted[i].z = neckCenter.z + Mathf.Clamp(fitted[i].z - neckCenter.z, -neckRadius, neckRadius);
                    weights[i] = new BoneWeight { boneIndex0 = 6, weight0 = 1 };
                }
                else if (vertices[i].y < pelvis - source.bounds.size.y * .03f)
                {
                    int side = vertices[i].x < 0 ? 0 : 1;
                    bool thigh = vertices[i].y >= knees[side].y;
                    var lower = thigh ? knees[side] : ankles[side];
                    var upper = thigh ? hips[side] : knees[side];
                    var legCenter = Vector3.Lerp(lower, upper, Mathf.InverseLerp(lower.y, upper.y, vertices[i].y));
                    float radius = Vector3.Distance(lower, upper) * (thigh ? .18f : .16f);
                    var radial = Vector2.ClampMagnitude(new Vector2(fitted[i].x - legCenter.x, fitted[i].z - legCenter.z), radius);
                    fitted[i].x = legCenter.x + radial.x;
                    fitted[i].z = legCenter.z + radial.y;
                    string limb = side == 0 ? "L " : "R ";
                    weights[i] = LegWeights(fitted[i], hips[side], knees[side], ankles[side],
                        Bone(limb + "Thigh"), Bone(limb + "Calf"), Bone(limb + "Foot"), Bone("Pelvis"));
                }
            }
            for (int coverage = 1; coverage <= 3; coverage++)
            {
                var mesh = new Mesh { name = $"blue_undersuit_{race}_{coverage}" };
                var coveredVertices = new List<Vector3>(fitted);
                var coveredWeights = new List<BoneWeight>(weights);
                var triangles = new List<int>();
                var original = source.triangles;
                for (int i = 0; i < original.Length; i += 3)
                {
                    float y = (vertices[original[i]].y + vertices[original[i + 1]].y + vertices[original[i + 2]].y) / 3f;
                    if (BlueMageSet.Covers(y, pelvis, coverage))
                    {
                        // Original clothing can bridge both legs; the closed sleeves replace it below the hips.
                        if (y < pelvis && Enumerable.Range(0, 3).Any(corner =>
                            vertices[original[i + corner]].y < pelvis - source.bounds.size.y * .03f)) continue;
                        triangles.Add(original[i]); triangles.Add(original[i + 1]); triangles.Add(original[i + 2]);
                    }
                }
                if ((coverage & 1) != 0)
                    AddCollar(coveredVertices, coveredWeights, triangles, neck, head, neckRadius, source.bounds.size.y);
                for (int side = 0; side < 2; side++)
                {
                    string limb = side == 0 ? "L " : "R ";
                    void Sleeve(Vector3 lower, Vector3 upper, float lowerRadius, float upperRadius, int bone)
                    {
                        AddTube(coveredVertices, coveredWeights, triangles, lower, upper, lowerRadius, upperRadius,
                            source.bounds.size.y * .005f, _ => new BoneWeight { boneIndex0 = bone, weight0 = 1 });
                    }
                    if ((coverage & 2) != 0)
                    {
                        float thighRadius = Vector3.Distance(hips[side], knees[side]) * .18f;
                        float calfRadius = Vector3.Distance(knees[side], ankles[side]) * .16f;
                        Sleeve(knees[side], hips[side], calfRadius, thighRadius, Bone(limb + "Thigh"));
                        Sleeve(ankles[side], knees[side], calfRadius * .72f, calfRadius, Bone(limb + "Calf"));
                    }
                    if ((coverage & 1) != 0)
                    {
                        var wrist = Joint(limb + "Hand"); var elbow = Joint(limb + "ForeArm");
                        float armRadius = Vector3.Distance(wrist, elbow) * .18f;
                        Sleeve(wrist, elbow, armRadius * .65f, armRadius, Bone(limb + "ForeArm"));
                    }
                }
                var used = triangles.Distinct().OrderBy(i => i).ToArray();
                var remap = new int[coveredVertices.Count];
                for (int i = 0; i < used.Length; i++) remap[used[i]] = i;
                mesh.vertices = used.Select(i => coveredVertices[i]).ToArray();
                mesh.boneWeights = used.Select(i => coveredWeights[i]).ToArray();
                mesh.bindposes = source.bindposes;
                mesh.triangles = triangles.Select(i => remap[i]).ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                string assetPath = $"{Output}/underlay_{race}_{coverage}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<PkoPartAsset>(assetPath);
                if (asset == null) { asset = ScriptableObject.CreateInstance<PkoPartAsset>(); AssetDatabase.CreateAsset(asset, assetPath); }
                if (asset.mesh != null) UnityEngine.Object.DestroyImmediate(asset.mesh, true);
                asset.mesh = mesh; asset.materials = new[] { material };
                AssetDatabase.AddObjectToAsset(mesh, asset); EditorUtility.SetDirty(asset);
            }
            report.AppendLine($"PASS Race {race}: fitted undersuits preserve coverage between separate armor pieces");
        }
        AssetDatabase.SaveAssets();
    }

    static void AddCollar(List<Vector3> vertices, List<BoneWeight> weights, List<int> triangles,
        Vector3 neck, Vector3 head, float radius, float bodyHeight)
    {
        var axis = (head - neck).normalized;
        var bottom = neck - axis * bodyHeight * .02f;
        var top = head + axis * bodyHeight * .015f;
        AddTube(vertices, weights, triangles, bottom, top, radius, radius, 0, center =>
        {
            float headWeight = Mathf.Clamp01(Vector3.Dot(center - neck, axis) / Vector3.Distance(neck, head));
            return new BoneWeight { boneIndex0 = 5, boneIndex1 = 6, weight0 = 1f - headWeight, weight1 = headWeight };
        });
    }

    static void AddTube(List<Vector3> vertices, List<BoneWeight> weights, List<int> triangles,
        Vector3 lower, Vector3 upper, float lowerRadius, float upperRadius, float overlap,
        Func<Vector3, BoneWeight> skin)
    {
        const int sides = 24, rings = 6;
        int start = vertices.Count;
        var axis = (upper - lower).normalized;
        var forward = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
        var right = Vector3.Cross(axis, forward).normalized;
        var bottom = lower - axis * overlap;
        var top = upper + axis * overlap;
        for (int ring = 0; ring <= rings; ring++)
        {
            float t = (float)ring / rings;
            var center = Vector3.Lerp(bottom, top, t);
            for (int side = 0; side < sides; side++)
            {
                float angle = side * Mathf.PI * 2f / sides;
                vertices.Add(center + (forward * Mathf.Cos(angle) + right * Mathf.Sin(angle))
                    * Mathf.Lerp(lowerRadius, upperRadius, t));
                weights.Add(skin(center));
                if (ring == rings) continue;
                int a = start + ring * sides + side, b = start + ring * sides + (side + 1) % sides;
                triangles.Add(a); triangles.Add(b); triangles.Add(b + sides);
                triangles.Add(a); triangles.Add(b + sides); triangles.Add(a + sides);
            }
        }
        int bottomCenter = vertices.Count;
        vertices.Add(bottom); weights.Add(skin(bottom));
        int topCenter = vertices.Count;
        vertices.Add(top); weights.Add(skin(top));
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            triangles.Add(bottomCenter); triangles.Add(start + next); triangles.Add(start + side);
            triangles.Add(topCenter); triangles.Add(start + rings * sides + side); triangles.Add(start + rings * sides + next);
        }
    }

    static Mesh Fit(Geometry source, Mesh reference, Mesh body, Skeleton skeleton, BlueMageSet.Piece piece, int race)
    {
        var vertices = new Vector3[source.positions.Length / 3];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            // Meshy faces +Z, like the playable rig. Reflect X for Unity winding, not Z.
            vertices[i] = new Vector3(-source.positions[i * 3], source.positions[i * 3 + 1], source.positions[i * 3 + 2]);
            normals[i] = new Vector3(-source.normals[i * 3], source.normals[i * 3 + 1], source.normals[i * 3 + 2]);
            uvs[i] = new Vector2(source.uvs[i * 2], source.uvs[i * 2 + 1]);
        }
        bool paired = piece.Slot == EquipmentSlot.Gloves || piece.Slot == EquipmentSlot.Boots;
        var referenceVertices = paired ? reference.vertices.Concat(body.vertices).ToArray() : reference.vertices;
        var referenceWeights = paired ? reference.boneWeights.Concat(body.boneWeights).ToArray() : reference.boneWeights;
        var candidates = Enumerable.Range(0, referenceVertices.Length).ToArray();
        float height = body.bounds.size.y;
        int Bone(string name)
        {
            int index = Array.FindIndex(skeleton.bones, b => b.name == "Bip01 " + name);
            if (index < 0) throw new InvalidDataException($"Missing {name} bone for race {race}.");
            return index;
        }
        Vector3 Joint(string name) => body.bindposes[Bone(name)].inverse.GetColumn(3);
        float pelvis = Joint("Pelvis").y;
        if (piece.Slot == EquipmentSlot.Armor)
        {
            var torsoBones = new[] { "Pelvis", "Spine", "Spine1", "L Clavicle", "R Clavicle", "L UpperArm", "R UpperArm" }
                .Select(Bone).ToArray();
            candidates = candidates.Where(i => referenceVertices[i].y >= pelvis + height * .025f
                && referenceVertices[i].y <= Joint("Neck").y + height * .035f
                && torsoBones.Any(bone => HasBone(referenceWeights[i], bone))).ToArray();
        }
        else if (piece.Slot == EquipmentSlot.Belt)
        {
            var legBones = new[] { "Pelvis", "L Thigh", "R Thigh", "L Calf", "R Calf" }.Select(Bone).ToArray();
            candidates = candidates.Where(i => referenceVertices[i].y <= pelvis + height * .075f
                && legBones.Any(bone => HasBone(referenceWeights[i], bone))).ToArray();
        }
        if (candidates.Length == 0) throw new InvalidOperationException($"No fitting region for {piece.Name}, race {race}");
        var weights = new BoneWeight[vertices.Length];
        for (int side = 0; side < (paired ? 2 : 1); side++)
        {
            var sourceIndices = Enumerable.Range(0, vertices.Length)
                .Where(i => !paired || (vertices[i].x < 0 ? 0 : 1) == side).ToArray();
            var targetIndices = candidates.Where(i => !paired || (referenceVertices[i].x < 0 ? 0 : 1) == side).ToArray();
            if (sourceIndices.Length == 0 || targetIndices.Length == 0)
                throw new InvalidOperationException($"Paired fitting region is missing for {piece.Name}, race {race}");
            var sourceBounds = BoundsOf(sourceIndices.Select(i => vertices[i]));
            var targetBounds = BoundsOf(targetIndices.Select(i => referenceVertices[i]));
            if (paired)
            {
                string limb = side == 0 ? "L " : "R ";
                bool glove = piece.Slot == EquipmentSlot.Gloves;
                int proximal = Bone(limb + (glove ? "ForeArm" : "Calf"));
                int distal = Bone(limb + (glove ? "Hand" : "Foot"));
                int end = Bone(limb + (glove ? "Finger1" : "Toe0"));
                targetIndices = targetIndices.Where(i =>
                    HasBone(referenceWeights[i], proximal) || HasBone(referenceWeights[i], distal)
                    || HasBone(referenceWeights[i], end)
                    || (glove && HasBone(referenceWeights[i], Bone(limb + "Finger0")))).ToArray();
                if (targetIndices.Length == 0)
                    throw new InvalidDataException($"Missing limb surface for {piece.Name}, race {race}, side {side}.");
                var wrist = Joint(limb + (glove ? "Hand" : "Foot"));
                var upper = Joint(limb + (glove ? "ForeArm" : "Calf"));
                var axis = (upper - wrist).normalized;
                var forward = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
                var right = Vector3.Cross(axis, forward).normalized;
                var rotation = Quaternion.LookRotation(forward, axis);
                var localReference = targetIndices.Select(i =>
                {
                    var delta = referenceVertices[i] - wrist;
                    return new Vector3(Vector3.Dot(delta, right), Vector3.Dot(delta, axis), Vector3.Dot(delta, forward));
                }).ToArray();
                var localBounds = BoundsOf(localReference);
                // The supplied gauntlets include forearms, and boots include shins.
                // Fit to those joints, not only to the original hand/foot part bounding box.
                float bottom = localBounds.min.y;
                float top = (upper - wrist).magnitude * (glove ? .92f : 1.03f);
                var extremity = BoundsOf(targetIndices.Where(i => i < reference.vertexCount
                    && (HasBone(referenceWeights[i], distal) || HasBone(referenceWeights[i], end)))
                    .Select(i => Quaternion.Inverse(rotation) * (referenceVertices[i] - wrist)));
                float halfWidth = extremity.size.x * (glove ? .51f : .53f);
                float depth = extremity.size.z * (glove ? 1.02f : 1.04f);
                float sourceJointY = glove ? 0f : -.55f;
                var jointSurface = sourceIndices.Where(i => Mathf.Abs(vertices[i].y - sourceJointY) < .10f).ToArray();
                if (jointSurface.Length == 0)
                    throw new InvalidDataException($"Missing source wrist/ankle surface for {piece.Name}, side {side}.");
                var sourceJoint = BoundsOf(jointSurface.Select(i => vertices[i])).center;
                sourceJoint.y = sourceJointY;
                var limbScale = new Vector3(halfWidth * 2 / sourceBounds.size.x,
                    1f, depth / sourceBounds.size.z);
                foreach (int i in sourceIndices)
                {
                    float longitudinalScale = vertices[i].y >= sourceJointY
                        ? top / (sourceBounds.max.y - sourceJointY)
                        : -bottom / (sourceJointY - sourceBounds.min.y);
                    var local = Vector3.Scale(vertices[i] - sourceJoint, limbScale);
                    local.y *= longitudinalScale;
                    vertices[i] = wrist + rotation * local;
                    normals[i] = rotation * new Vector3(normals[i].x / limbScale.x,
                        normals[i].y / longitudinalScale, normals[i].z / limbScale.z).normalized;
                    weights[i] = TransferWeights(vertices[i], targetIndices, referenceVertices, referenceWeights);
                }
                continue;
            }
            if (piece.Slot == EquipmentSlot.Helmet)
            {
                targetBounds.SetMinMax(new Vector3(targetBounds.min.x, Mathf.Min(targetBounds.min.y,
                    Joint("Neck").y + height * .02f), targetBounds.min.z), targetBounds.max);
            }
            else if (piece.Slot == EquipmentSlot.Armor)
            {
                targetBounds.SetMinMax(new Vector3(targetBounds.min.x, pelvis + height * .025f, targetBounds.min.z),
                    new Vector3(targetBounds.max.x, Joint("Neck").y + height * .035f, targetBounds.max.z));
            }
            else if (piece.Slot == EquipmentSlot.Belt)
            {
                targetBounds.SetMinMax(new Vector3(targetBounds.min.x, Mathf.Min(Joint("L Foot").y, Joint("R Foot").y), targetBounds.min.z),
                    new Vector3(targetBounds.max.x, pelvis + height * .075f, targetBounds.max.z));
            }
            float padding = piece.Slot == EquipmentSlot.Helmet ? 1.15f : 1.06f;
            Vector3 size = targetBounds.size * padding;
            var scale = new Vector3(size.x / Mathf.Max(.00001f, sourceBounds.size.x),
                size.y / Mathf.Max(.00001f, sourceBounds.size.y), size.z / Mathf.Max(.00001f, sourceBounds.size.z));
            foreach (int i in sourceIndices)
            {
                var original = vertices[i];
                vertices[i] = targetBounds.center + Vector3.Scale(original - sourceBounds.center, scale);
                if (piece.Slot == EquipmentSlot.Belt)
                {
                    string limb = original.x < 0 ? "L " : "R ";
                    var hip = Joint(limb + "Thigh");
                    var knee = Joint(limb + "Calf");
                    var ankle = Joint(limb + "Foot");
                    const float sourceHip = .50f, sourceKnee = -.30f, sourceLegCenter = .23f;
                    Vector3 center;
                    if (original.y >= sourceHip)
                    {
                        float t = Mathf.InverseLerp(sourceHip, sourceBounds.max.y, original.y);
                        vertices[i].y = Mathf.Lerp(pelvis, pelvis + height * .075f, t);
                        center = hip;
                    }
                    else if (original.y >= sourceKnee)
                    {
                        float t = Mathf.InverseLerp(sourceKnee, sourceHip, original.y);
                        center = Vector3.Lerp(knee, hip, t);
                        vertices[i].y = center.y;
                    }
                    else
                    {
                        float t = Mathf.InverseLerp(sourceBounds.min.y, sourceKnee, original.y);
                        center = Vector3.Lerp(ankle, knee, t);
                        vertices[i].y = center.y;
                    }
                    float legBlend = 1f - Mathf.InverseLerp(.30f, .65f, original.y);
                    float legX = center.x + (original.x - Mathf.Sign(original.x) * sourceLegCenter) * scale.x * 1.4f;
                    vertices[i].x = Mathf.Lerp(vertices[i].x, legX, legBlend);
                    vertices[i].z = Mathf.Lerp(vertices[i].z, center.z + (original.z + .02f) * scale.z, legBlend);
                }
                normals[i] = new Vector3(normals[i].x / scale.x, normals[i].y / scale.y, normals[i].z / scale.z).normalized;
                if (piece.Slot == EquipmentSlot.Helmet)
                    weights[i] = new BoneWeight { boneIndex0 = Bone("Head"), weight0 = 1 };
                else if (piece.Slot == EquipmentSlot.Belt)
                {
                    string limb = original.x < 0 ? "L " : "R ";
                    weights[i] = LegWeights(vertices[i], Joint(limb + "Thigh"), Joint(limb + "Calf"), Joint(limb + "Foot"),
                        Bone(limb + "Thigh"), Bone(limb + "Calf"), Bone(limb + "Foot"), Bone("Pelvis"));
                    // The connected crotch panel must not stretch between two independently animated thighs.
                    float crotch = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.10f, .30f, original.y))
                        * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.08f, .20f, Mathf.Abs(original.x))));
                    var weight = weights[i];
                    weight.weight0 *= 1 - crotch; weight.weight1 *= 1 - crotch; weight.weight3 *= 1 - crotch;
                    weight.weight2 = weight.weight2 * (1 - crotch) + crotch;
                    weights[i] = weight;
                }
                else weights[i] = TransferWeights(vertices[i], targetIndices, referenceVertices, referenceWeights);
            }
        }
        var underlay = AssetDatabase.LoadAssetAtPath<PkoPartAsset>($"{Output}/underlay_{race}_3.asset");
        if (underlay == null || underlay.mesh == null)
            throw new InvalidDataException($"Missing fitting undersuit for race {race}.");
        var lining = underlay.mesh.vertices;
        var liningWeights = underlay.mesh.boneWeights;
        if (paired || piece.Slot == EquipmentSlot.Belt)
        {
            for (int side = 0; side < 2; side++)
            {
                string limb = side == 0 ? "L " : "R ";
                bool glove = piece.Slot == EquipmentSlot.Gloves;
                var regions = piece.Slot == EquipmentSlot.Belt
                    ? new[] { ("Foot", "Calf"), ("Calf", "Thigh") }
                    : new[] { (glove ? "Hand" : "Foot", glove ? "ForeArm" : "Calf") };
                var indices = Enumerable.Range(0, vertices.Length)
                    .Where(i => (source.positions[i * 3] > 0 ? 0 : 1) == side).ToArray();
                foreach (var (lower, upper) in regions)
                {
                    int bone = Bone(limb + upper);
                    var liningIndices = Enumerable.Range(0, lining.Length)
                        .Where(i => HasBone(liningWeights[i], bone) && (lining[i].x < 0 ? 0 : 1) == side).ToArray();
                    ExpandClearance(vertices, indices, lining, liningIndices, Joint(limb + lower),
                        Joint(limb + upper), height * .008f);
                }
            }
        }
        else if (piece.Slot == EquipmentSlot.Armor)
        {
            var torsoBones = new[] { "Pelvis", "Spine", "Spine1" }.Select(Bone).ToArray();
            float torsoHalfWidth = Vector3.Distance(Joint("L UpperArm"), Joint("R UpperArm")) * .35f;
            var liningIndices = Enumerable.Range(0, lining.Length)
                .Where(i => Mathf.Abs(lining[i].x) < torsoHalfWidth
                    && torsoBones.Any(b => HasBone(liningWeights[i], b))).ToArray();
            ExpandClearance(vertices, Enumerable.Range(0, vertices.Length).ToArray(), lining, liningIndices,
                Joint("Pelvis"), Joint("Neck"), height * .008f);
        }
        var triangles = (int[])source.triangles.Clone();
        for (int i = 0; i < triangles.Length; i += 3)
            (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
        var mesh = new Mesh { name = $"{piece.Id}_{race}", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uvs;
        mesh.triangles = triangles; mesh.bindposes = body.bindposes; mesh.boneWeights = weights;
        if (piece.Slot != EquipmentSlot.Helmet)
        {
            var smooth = new Dictionary<Vector3, Vector3>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
                var normal = Vector3.Cross(b - a, c - a);
                foreach (var point in new[] { a, b, c })
                {
                    smooth.TryGetValue(point, out var sum);
                    smooth[point] = sum + normal;
                }
            }
            for (int i = 0; i < vertices.Length; i++) normals[i] = smooth[vertices[i]].normalized;
            mesh.normals = normals;
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    static void ExpandClearance(Vector3[] vertices, int[] indices, Vector3[] lining, int[] liningIndices,
        Vector3 lower, Vector3 upper, float margin)
    {
        const int bands = 24;
        float length = Vector3.Distance(lower, upper);
        var axis = (upper - lower).normalized;
        var radii = new float[bands + 1];
        for (int band = 0; band <= bands; band++)
        {
            float t = (float)band / bands;
            float liningRadius = 0;
            foreach (int i in liningIndices)
            {
                var delta = lining[i] - lower;
                if (Mathf.Abs(Vector3.Dot(delta, axis) / length - t) > 1.5f / bands) continue;
                liningRadius = Mathf.Max(liningRadius, Vector3.ProjectOnPlane(delta, axis).magnitude);
            }
            if (liningRadius > 0) radii[band] = liningRadius + margin;
        }
        foreach (int i in indices)
        {
            var delta = vertices[i] - lower;
            float t = Vector3.Dot(delta, axis) / length;
            if (t < 0 || t > 1) continue;
            var radial = Vector3.ProjectOnPlane(delta, axis);
            float sample = t * bands;
            int band = Mathf.Min((int)sample, bands - 1);
            float radius = Mathf.Lerp(radii[band], radii[band + 1], sample - band);
            vertices[i] += radial.normalized * Mathf.Max(0, radius - radial.magnitude);
        }
    }

    static BoneWeight LegWeights(Vector3 position, Vector3 hip, Vector3 knee, Vector3 ankle,
        int thighBone, int calfBone, int footBone, int pelvisBone)
    {
        float thighLength = Vector3.Distance(hip, knee), calfLength = Vector3.Distance(knee, ankle);
        float calf = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(knee.y + thighLength * .12f,
            knee.y - calfLength * .12f, position.y));
        float pelvis = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(hip.y - thighLength * .10f,
            hip.y + thighLength * .05f, position.y));
        float foot = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(ankle.y + calfLength * .10f,
            ankle.y - calfLength * .05f, position.y));
        return new BoneWeight
        {
            boneIndex0 = thighBone, weight0 = (1 - calf) * (1 - pelvis),
            boneIndex1 = calfBone, weight1 = calf * (1 - foot),
            boneIndex2 = pelvisBone, weight2 = (1 - calf) * pelvis,
            boneIndex3 = footBone, weight3 = calf * foot
        };
    }

    static Bounds BoundsOf(IEnumerable<Vector3> points)
    {
        bool first = true;
        var bounds = new Bounds();
        foreach (var point in points)
        {
            if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
            else bounds.Encapsulate(point);
        }
        return bounds;
    }

    static bool HasBone(BoneWeight weight, int bone) =>
        (weight.boneIndex0 == bone && weight.weight0 > .05f)
        || (weight.boneIndex1 == bone && weight.weight1 > .05f)
        || (weight.boneIndex2 == bone && weight.weight2 > .05f)
        || (weight.boneIndex3 == bone && weight.weight3 > .05f);

    static BoneWeight TransferWeights(Vector3 position, int[] candidates, Vector3[] vertices, BoneWeight[] weights)
    {
        var indices = new int[3] { -1, -1, -1 };
        var distances = new float[3] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        foreach (int candidate in candidates)
        {
            float distance = (vertices[candidate] - position).sqrMagnitude;
            for (int j = 0; j < 3; j++)
                if (distance < distances[j])
                {
                    for (int k = 2; k > j; k--) { distances[k] = distances[k - 1]; indices[k] = indices[k - 1]; }
                    distances[j] = distance; indices[j] = candidate;
                    break;
                }
        }
        var bones = new Dictionary<int, float>();
        for (int j = 0; j < 3; j++)
        {
            if (indices[j] < 0) continue;
            var w = weights[indices[j]];
            float influence = 1f / Mathf.Max(.000001f, distances[j]);
            Add(w.boneIndex0, w.weight0 * influence); Add(w.boneIndex1, w.weight1 * influence);
            Add(w.boneIndex2, w.weight2 * influence); Add(w.boneIndex3, w.weight3 * influence);
        }
        var strongest = bones.OrderByDescending(b => b.Value).Take(4).ToArray();
        float total = strongest.Sum(b => b.Value);
        if (total <= 0) throw new InvalidDataException("Reference skin weights are empty.");
        var result = new BoneWeight { boneIndex0 = strongest[0].Key, weight0 = strongest[0].Value / total };
        if (strongest.Length > 1) { result.boneIndex1 = strongest[1].Key; result.weight1 = strongest[1].Value / total; }
        if (strongest.Length > 2) { result.boneIndex2 = strongest[2].Key; result.weight2 = strongest[2].Value / total; }
        if (strongest.Length > 3) { result.boneIndex3 = strongest[3].Key; result.weight3 = strongest[3].Value / total; }
        return result;

        void Add(int bone, float weight)
        {
            if (weight <= 0) return;
            bones.TryGetValue(bone, out float old);
            bones[bone] = old + weight;
        }
    }

    static void Validate(StringBuilder report)
    {
        var adminObject = new GameObject("BlueMageAdminValidation");
        try
        {
            var panel = adminObject.AddComponent<TOP.Admin.AdminPanel>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(TOP.Admin.AdminPanel).GetMethod("Build", flags).Invoke(panel, null);
            var catalog = (List<PkoItem>)typeof(TOP.Admin.AdminPanel).GetField("all", flags).GetValue(panel);
            if (BlueMageSet.Pieces.Any(piece => !catalog.Any(item => item.Id == piece.Id)))
                throw new InvalidOperationException("An item is missing from the F10 catalog.");
            report.AppendLine("PASS F10 catalog includes all five Blue Mage pieces, including belt-slot pants");
        }
        finally { UnityEngine.Object.DestroyImmediate(adminObject); }
        foreach (var piece in BlueMageSet.Pieces)
        {
            if (!PkoTables.Items.TryGetValue(piece.Id, out var item) || PkoTables.SlotOf(item) != piece.Slot)
                throw new InvalidOperationException("Item catalog/slot mismatch: " + piece.Name);
            var data = PkoTables.ToItemData(item);
            try
            {
                if (data is not EquipmentData equipment || equipment.slot != piece.Slot || data.icon == null)
                    throw new InvalidOperationException("Equipment data/icon missing: " + piece.Name
                        + "; type=" + data.GetType().Name + "; icon=" + data.icon
                        + "; resourceSprites=" + Resources.LoadAll<Sprite>(BlueMageSet.IconPath(piece)).Length
                        + "; importedAssets=" + string.Join(",", AssetDatabase.LoadAllAssetsAtPath(
                            "Assets/Resources/" + BlueMageSet.IconPath(piece) + ".png").Select(a => a.GetType().Name)));
            }
            finally { UnityEngine.Object.DestroyImmediate(data); }
            for (int race = 0; race < PkoCharacterVisual.Races; race++)
            {
                var part = Resources.Load<PkoPartAsset>(BlueMageSet.PartPath(piece.Id, race));
                if (part == null || part.mesh == null || part.mesh.triangles.Length / 3 > 22000
                    || part.materials.Length != 1 || part.materials[0].mainTexture == null)
                    throw new InvalidOperationException("Runtime asset/budget invalid: " + piece.Name);
                foreach (var w in part.mesh.boneWeights)
                    if (Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1f) > .001f
                        || w.boneIndex0 >= part.mesh.bindposes.Length || w.boneIndex1 >= part.mesh.bindposes.Length
                        || w.boneIndex2 >= part.mesh.bindposes.Length || w.boneIndex3 >= part.mesh.bindposes.Length)
                        throw new InvalidOperationException("Invalid skin weights: " + piece.Name);
                ValidateAlignment(part.mesh, piece, race);
            }
            report.AppendLine("PASS Catalog, equipment slot, icon, texture, triangle budget, weights and anatomical alignment: " + piece.Name);
        }
        var parent = new GameObject("BlueMageSetValidation");
        try
        {
            for (int race = 0; race < PkoCharacterVisual.Races; race++)
            {
                var visual = PkoCharacterVisual.Create(parent.transform, race, 0, 0, BlueMageSet.Pieces.Select(p => p.Id));
                try
                {
                    var renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>();
                    foreach (var piece in BlueMageSet.Pieces)
                    {
                        var renderer = renderers.FirstOrDefault(r => r.sharedMesh != null && r.sharedMesh.name == $"{piece.Id}_{race}");
                        if (renderer == null || renderer.bones.Any(b => b == null))
                            throw new InvalidOperationException($"Equipped visual missing: {piece.Name}, race {race}");
                        var before = new Mesh();
                        var after = new Mesh();
                        try
                        {
                            renderer.BakeMesh(before);
                            int boneIndex = renderer.sharedMesh.boneWeights
                                .GroupBy(w => w.boneIndex0).OrderByDescending(g => g.Count()).First().Key;
                            var bone = renderer.bones[boneIndex];
                            var rotation = bone.localRotation;
                            try
                            {
                                bone.localRotation = rotation * Quaternion.Euler(0, 15, 0);
                                renderer.BakeMesh(after);
                            }
                            finally { bone.localRotation = rotation; }
                            if (!before.vertices.Zip(after.vertices, (a, b) => Vector3.Distance(a, b)).Any(d => d > .0001f))
                                throw new InvalidOperationException("Equipment does not follow its skin bone: " + piece.Name);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(before); UnityEngine.Object.DestroyImmediate(after); }
                    }
                    if (renderers.Any(r => r.gameObject.activeSelf && r.name == $"part_{race:0000}000000"))
                        throw new InvalidOperationException("Closed helmet leaves original face visible.");
                    Preview(visual, race, "bind", 0);
                    Preview(visual, race, "bind-side", 90);
                    Preview(visual, race, "bind-back", 180);
                    Preview(visual, race, "bind-left", 270);
                    ValidateCoverage(visual, race, report);
                    SamplePreviews(visual, race, false, report);
                    visual.Apply(race, 0, 0, BlueMageSet.Pieces.Select(p => p.Id).Append(PkoTables.MeshyMageWingsItemId));
                    visual.Pose.Loop(PkoPoses.Wait);
                    if (!visual.Pose.IsFlying || visual.WingMount == null
                        || visual.GetComponentsInChildren<SkinnedMeshRenderer>().Count(r =>
                            BlueMageSet.Pieces.Any(piece => r.sharedMesh.name == $"{piece.Id}_{race}")) != BlueMageSet.Pieces.Count)
                        throw new InvalidOperationException("Blue Mage armor/flight integration failed.");
                    SamplePreviews(visual, race, true, report);
                    report.AppendLine($"PASS Race {race}: full set remains equipped with synchronized Mage wing flight");
                    visual.Apply(race, 0, 0, Array.Empty<int>());
                    if (visual.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.gameObject.activeSelf
                        && r.sharedMesh != null && r.sharedMesh.name.StartsWith("99001", StringComparison.Ordinal)))
                        throw new InvalidOperationException("Unequipped set leaves active visuals.");
                    if (!visual.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r =>
                        r.gameObject.activeSelf && r.name == $"part_{race:0000}000000"))
                        throw new InvalidOperationException("Unequipping helmet does not restore original face.");
                    report.AppendLine($"PASS Race {race}: closed helmet hides face and unequip restores it");
                    report.AppendLine($"PASS Race {race}: five equipped meshes, valid skeleton and clean unequip");
                }
                finally { UnityEngine.Object.DestroyImmediate(visual.gameObject); }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(parent); }
    }

    static void ValidateAlignment(Mesh mesh, BlueMageSet.Piece piece, int race)
    {
        var skeleton = JsonUtility.FromJson<Skeleton>(File.ReadAllText($"Assets/ImportedClient/Skinned/{race:0000}.json"));
        var body = Part(race, 2).mesh;
        var source = JsonUtility.FromJson<Geometry>(File.ReadAllText($"{Source}/{piece.Key}.json"));
        var vertices = mesh.vertices;
        int Bone(string name) => Array.FindIndex(skeleton.bones, b => b.name == "Bip01 " + name);
        Vector3 Joint(string name) => body.bindposes[Bone(name)].inverse.GetColumn(3);
        if (piece.Slot == EquipmentSlot.Helmet && mesh.boneWeights.Any(w =>
            w.boneIndex0 != Bone("Head") || w.weight0 < .999f))
            throw new InvalidOperationException($"Helmet is not rigidly attached to head, race {race}.");
        for (int side = 0; side < 2; side++)
        {
            string limb = side == 0 ? "L " : "R ";
            var indices = Enumerable.Range(0, vertices.Length)
                .Where(i => (source.positions[i * 3] > 0 ? 0 : 1) == side).ToArray();
            if (piece.Slot == EquipmentSlot.Boots)
            {
                var foot = Joint(limb + "Foot");
                var axis = (Joint(limb + "Calf") - foot).normalized;
                var forward = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
                var toe = indices.Where(i => source.positions[i * 3 + 1] < -.70f
                    && source.positions[i * 3 + 2] > .20f).ToArray();
                if (toe.Length == 0 || toe.Average(i => Vector3.Dot(vertices[i] - foot, forward)) < body.bounds.size.y * .025f)
                    throw new InvalidOperationException($"Boot toe faces backwards, race {race}, side {side}.");
            }
            else if (piece.Slot == EquipmentSlot.Belt)
            {
                var knee = indices.Where(i => Mathf.Abs(source.positions[i * 3 + 1] + .30f) < .025f).ToArray();
                if (knee.Length == 0 || Mathf.Abs(knee.Average(i => vertices[i].y) - Joint(limb + "Calf").y) > body.bounds.size.y * .025f)
                    throw new InvalidOperationException($"Pants knee is misaligned, race {race}, side {side}.");
            }
            else if (piece.Slot == EquipmentSlot.Gloves)
            {
                var wrist = indices.Where(i => Mathf.Abs(source.positions[i * 3 + 1]) < .025f).ToArray();
                if (wrist.Length == 0) throw new InvalidOperationException("Missing wrist landmark.");
                var center = BoundsOf(wrist.Select(i => vertices[i])).center;
                if (Vector3.Distance(center, Joint(limb + "Hand")) > body.bounds.size.y * .08f)
                    throw new InvalidOperationException($"Glove wrist is misaligned, race {race}, side {side}.");
            }
        }
    }

    static void SamplePreviews(PkoCharacterVisual visual, int race, bool flying, StringBuilder report)
    {
        var animation = visual.Pose.GetComponent<Animation>();
        foreach (int pose in new[] { PkoPoses.Wait, PkoPoses.Run, PkoPoses.Attack1 })
        {
            visual.Pose.Loop(pose);
            string clip = visual.Pose.CurrentClip;
            if (clip == null) throw new InvalidOperationException($"Missing preview pose {pose} for race {race}.");
            animation.Stop();
            var state = animation[clip];
            state.enabled = true; state.weight = 1f; state.speed = 0;
            foreach (float phase in new[] { .0f, .5f })
            {
                state.normalizedTime = phase;
                animation.Sample();
                foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!renderer.gameObject.activeInHierarchy || renderer.sharedMesh == null) continue;
                    var baked = new Mesh();
                    try
                    {
                        renderer.BakeMesh(baked);
                        if (baked.vertices.Any(v => float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                            || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)))
                            throw new InvalidOperationException($"Invalid animated vertices: {renderer.name}, race {race}, pose {pose}.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(baked); }
                }
                if (phase == .5f)
                    ValidateCoverage(visual, race, report, $"{(flying ? "flight" : "ground")}-{pose}");
                Preview(visual, race, $"{(flying ? "flight" : "ground")}-{pose}-{phase:0.0}", 0);
                Preview(visual, race, $"{(flying ? "flight" : "ground")}-{pose}-{phase:0.0}-side", 90);
                Preview(visual, race, $"{(flying ? "flight" : "ground")}-{pose}-{phase:0.0}-back", 180);
                Preview(visual, race, $"{(flying ? "flight" : "ground")}-{pose}-{phase:0.0}-left", 270);
            }
            state.enabled = false;
            report.AppendLine($"PASS Race {race}: {(flying ? "flight" : "ground")} pose {pose}, sampled front/back/both sides at phases 0 and 0.5");
        }
    }

    static void ValidateCoverage(PkoCharacterVisual visual, int race, StringBuilder report, string pose = "bind")
    {
        var skeleton = JsonUtility.FromJson<Skeleton>(File.ReadAllText($"Assets/ImportedClient/Skinned/{race:0000}.json"));
        var body = Part(race, 2).mesh;
        int Bone(string name) => Array.FindIndex(skeleton.bones, b => b.name == "Bip01 " + name);
        var renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>()
            .Where(r => r.gameObject.activeInHierarchy && r.sharedMesh != null
                && r.name.StartsWith("blue_", StringComparison.Ordinal)).ToArray();
        Vector3 Joint(string name) => visual.transform.InverseTransformPoint(renderers[0].bones[Bone(name)].position);
        var geometry = renderers.Select(renderer =>
        {
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked);
                var matrix = visual.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                return (vertices: baked.vertices.Select(matrix.MultiplyPoint3x4).ToArray(),
                    triangles: baked.triangles, weights: renderer.sharedMesh.boneWeights);
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }).ToArray();
        var regions = new List<(string name, Vector3 lower, Vector3 upper, int[] bones)>
        {
            ("torso", Joint("Pelvis"), Joint("Neck"), new[] { Bone("Pelvis"), Bone("Spine"), Bone("Spine1"),
                Bone("L Clavicle"), Bone("R Clavicle"), Bone("L UpperArm"), Bone("R UpperArm") }),
            ("neck", Joint("Neck"), Joint("Head"), new[] { Bone("Neck"), Bone("Head") })
        };
        foreach (string side in new[] { "L ", "R " })
        {
            regions.Add((side + "thigh", Joint(side + "Calf"), Joint(side + "Thigh"),
                new[] { Bone(side + "Thigh") }));
            regions.Add((side + "shin", Joint(side + "Foot"), Joint(side + "Calf"),
                new[] { Bone(side + "Calf"), Bone(side + "Foot") }));
            regions.Add((side + "forearm", Joint(side + "Hand"), Joint(side + "ForeArm"),
                new[] { Bone(side + "ForeArm"), Bone(side + "Hand") }));
        }
        foreach (var region in regions)
        {
            var surfaces = geometry.Select(g =>
            {
                var triangles = new List<int>();
                for (int i = 0; i < g.triangles.Length; i += 3)
                    if (region.bones.Any(b => HasBone(g.weights[g.triangles[i]], b)
                        || HasBone(g.weights[g.triangles[i + 1]], b) || HasBone(g.weights[g.triangles[i + 2]], b)))
                    {
                        triangles.Add(g.triangles[i]); triangles.Add(g.triangles[i + 1]); triangles.Add(g.triangles[i + 2]);
                    }
                return (g.vertices, triangles: triangles.ToArray());
            }).ToArray();
            var axis = (region.upper - region.lower).normalized;
            var forward = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
            var right = Vector3.Cross(axis, forward).normalized;
            int hits = 0;
            foreach (float phase in new[] { .25f, .5f, .75f })
            {
                var center = Vector3.Lerp(region.lower, region.upper, phase);
                for (int angle = 0; angle < 24; angle++)
                {
                    float radians = angle * Mathf.PI / 12;
                    var direction = forward * Mathf.Cos(radians) + right * Mathf.Sin(radians);
                    bool covered = surfaces.Any(g => RayHits(center, direction, g.vertices, g.triangles,
                        body.bounds.size.y * .30f));
                    if (!covered)
                    {
                        Preview(visual, race, "coverage-failure-back", 180);
                        Preview(visual, race, "coverage-failure-side", 90);
                        throw new InvalidOperationException($"360-degree coverage gap: race={race}, pose={pose}, region={region.name}, ring={phase}, angle={angle * 15}.");
                    }
                    hits++;
                }
            }
            report.AppendLine($"PASS Race {race}: {pose} {region.name} coverage {hits}/72 rays (three rings, full 360 degrees)");
        }
    }

    static bool RayHits(Vector3 origin, Vector3 direction, Vector3[] vertices, int[] triangles, float limit)
    {
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = vertices[triangles[i]];
            var edge1 = vertices[triangles[i + 1]] - a;
            var edge2 = vertices[triangles[i + 2]] - a;
            var cross = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (Mathf.Abs(determinant) < .00000001f) continue;
            var delta = origin - a;
            float u = Vector3.Dot(delta, cross) / determinant;
            // Adjacent triangles share edges; allow only floating-point roundoff at those edges.
            const float edgeTolerance = .00001f;
            if (u < -edgeTolerance || u > 1f + edgeTolerance) continue;
            var q = Vector3.Cross(delta, edge1);
            float v = Vector3.Dot(direction, q) / determinant;
            if (v < -edgeTolerance || u + v > 1f + edgeTolerance) continue;
            float distance = Vector3.Dot(edge2, q) / determinant;
            if (distance > .00001f && distance < limit) return true;
        }
        return false;
    }

    static void Preview(PkoCharacterVisual visual, int race, string label, float angle)
    {
        var preview = new PreviewRenderUtility();
        try
        {
            var objects = new List<GameObject>();
            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.gameObject.activeInHierarchy
                && (visual.WingMount == null || !r.transform.IsChildOf(visual.WingMount))))
            {
                var baked = new Mesh();
                skin.BakeMesh(baked);
                var go = new GameObject(skin.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = baked;
                go.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                go.transform.position = skin.transform.position;
                go.transform.rotation = skin.transform.rotation;
                go.transform.localScale = skin.transform.lossyScale;
                preview.AddSingleGO(go);
                objects.Add(go);
            }
            var renderers = objects.Select(go => go.GetComponent<Renderer>()).ToArray();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.035f, .045f, .07f);
            preview.camera.orthographic = true;
            preview.camera.orthographicSize = bounds.size.y * .60f;
            preview.camera.nearClipPlane = .001f;
            preview.camera.farClipPlane = 1000f;
            preview.camera.transform.position = bounds.center + Quaternion.Euler(0, angle, 0)
                * new Vector3(bounds.size.y * .08f, 0, bounds.size.y * 3f);
            preview.camera.transform.LookAt(bounds.center);
            preview.lights[0].intensity = 1.5f;
            preview.lights[0].transform.rotation = Quaternion.Euler(30, 20, 0);
            preview.lights[1].intensity = 1f;
            preview.ambientColor = new Color(.5f, .5f, .5f);
            preview.BeginStaticPreview(new Rect(0, 0, 640, 640));
            preview.Render(true);
            var image = preview.EndStaticPreview();
            File.WriteAllBytes($"Tools/blue-mage-set-race-{race}-{label}.png", image.EncodeToPNG());
            if (label == "bind") File.WriteAllBytes($"Tools/blue-mage-set-race-{race}.png", image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            foreach (var go in objects) UnityEngine.Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);
        }
        finally { preview.Cleanup(); }
    }
}
