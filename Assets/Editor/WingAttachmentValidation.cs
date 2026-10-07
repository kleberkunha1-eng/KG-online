using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TOP.Character;
using TOP.Core;
using TOP.Data;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class WingAttachmentValidation
{
    const string Request = "Tools/validate-wing-attachment.request";
    static WingAttachmentValidation() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/PKO/Validate Wing Attachment")]
    public static void Run()
    {
        var report = new StringBuilder();
        int passed = 0, failed = 0;
        Action<bool, string> check = (ok, message) =>
        {
            report.AppendLine((ok ? "PASS " : "FAIL ") + message);
            if (ok) passed++; else failed++;
        };
        var parent = new GameObject("WingAttachmentTest");
        try
        {
            var boundsMethod = typeof(PkoCharacterVisual).GetMethod("WingLocalBounds", BindingFlags.Static | BindingFlags.NonPublic);
            var heightMethod = typeof(PkoCharacterVisual).GetMethod("GetBindHeight", BindingFlags.Static | BindingFlags.NonPublic);
            var updateMethod = typeof(PkoCharacterVisual).GetMethod("ApplyWingPose", BindingFlags.Instance | BindingFlags.NonPublic);
            var items = PkoTables.Items.Values.Where(i => i.EquipSlots.Length > 0
                && PkoTables.SlotOf(i) == EquipmentSlot.Wing).ToArray();
            check(items.Length > 0, "Wing items exist");
            for (int race = 0; race < PkoCharacterVisual.Races; race++)
            {
                foreach (var item in items)
                {
                    var visual = PkoCharacterVisual.Create(parent.transform, race, 0, 0, new[] { item.Id });
                    try
                    {
                        var mount = visual.WingMount;
                        bool original = item.Id != PkoTables.MeshyMageWingsItemId;
                        check(mount != null && (original ? mount.parent.name == "dummy_" + item.VisualEffectDummy
                            : mount.parent.name.EndsWith(" Spine1", StringComparison.Ordinal)),
                            $"Race {race} wing {item.Id} follows {(original ? "original effect dummy" : "chest")}");
                        if (mount == null) continue;
                        var rig = mount.parent;
                        while (rig.parent != visual.transform) rig = rig.parent;
                        var body = rig.Find("part_" + race.ToString("0000") + "000002")
                            .GetComponent<SkinnedMeshRenderer>();
                        float height = (float)heightMethod.Invoke(null, new object[] { body.sharedMesh.bindposes });
                        var pose = PkoWingPose.Get(race, item.Id);
                        var oldPosition = pose.position;
                        var oldEuler = pose.euler;
                        float oldScale = pose.scale;
                        try
                        {
                            pose.position = Vector3.zero; pose.euler = Vector3.zero; pose.scale = 1f;
                            updateMethod.Invoke(visual, null);
                            Vector3 attachment = rig.InverseTransformPoint(mount.position);
                            check(original ? mount.localPosition == Vector3.zero && mount.localRotation == Quaternion.identity
                                : Mathf.Abs(attachment.z - body.sharedMesh.bounds.max.z) < .001f,
                                $"Race {race} wing {item.Id} uses {(original ? "original attachment transform" : "rear torso position")}");
                            var model = mount.GetChild(0).gameObject;
                            var bounds = (Bounds)boundsMethod.Invoke(null, new object[] { model, mount });
                            check(original ? model.transform.localScale == Vector3.one && model.transform.localPosition == Vector3.zero
                                : Mathf.Abs(bounds.size.x - height * .9f) < .003f,
                                $"Race {race} wing {item.Id} retains {(original ? "original effect dimensions" : "custom proportional width")}: width={bounds.size.x:F6}");
                            if (original)
                            {
                                var prefab = Resources.Load<GameObject>("Wings/Items/" + item.Id);
                                bool preserved = prefab != null && prefab.transform.childCount == model.transform.childCount;
                                for (int i = 0; preserved && i < prefab.transform.childCount; i++)
                                {
                                    var source = prefab.transform.GetChild(i);
                                    var actual = model.transform.GetChild(i);
                                    preserved &= source.localPosition == actual.localPosition
                                        && source.localRotation == actual.localRotation && source.localScale == actual.localScale;
                                }
                                check(preserved, $"Race {race} wing {item.Id} preserves every original effect layer transform");
                            }
                            var initial = mount.localPosition;
                            pose.position = new Vector3(.1f, .2f, -.1f);
                            pose.euler = new Vector3(10, 20, 30);
                            updateMethod.Invoke(visual, null);
                            check(Vector3.Distance(initial, mount.localPosition) > height * .2f,
                                $"Race {race} wing {item.Id} responds to live position adjustment");
                            var adjusted = (Bounds)boundsMethod.Invoke(null, new object[] { model, mount });
                            check(Mathf.Abs(adjusted.size.x - bounds.size.x) < .003f,
                                $"Race {race} wing {item.Id} adjustment does not resize model");
                            var attachmentBeforeScale = mount.localPosition;
                            var modelScale = model.transform.localScale;
                            float baseWorldWidth = mount.TransformVector(Vector3.right * bounds.size.x).magnitude;
                            pose.scale = 1.5f;
                            updateMethod.Invoke(visual, null);
                            check(Mathf.Abs(mount.localScale.x - 1.5f) < .0001f
                                && model.transform.localScale == modelScale
                                && Mathf.Abs(mount.TransformVector(Vector3.right * bounds.size.x).magnitude
                                    - baseWorldWidth * 1.5f) < .003f,
                                $"Race {race} wing {item.Id} enlarges without cumulative model scaling");
                            pose.scale = .5f;
                            updateMethod.Invoke(visual, null);
                            check(mount.localScale == Vector3.one * .5f
                                && mount.localPosition == attachmentBeforeScale
                                && Mathf.Abs(mount.TransformVector(Vector3.right * bounds.size.x).magnitude
                                    - baseWorldWidth * .5f) < .003f,
                                $"Race {race} wing {item.Id} shrinks without changing attachment");
                            pose.scale = 1f;
                            updateMethod.Invoke(visual, null);
                            check(mount.localScale == Vector3.one,
                                $"Race {race} wing {item.Id} restores base size");
                            var before = mount.position;
                            var followingBone = original ? mount.parent.parent : mount.parent;
                            var boneRotation = followingBone.localRotation;
                            followingBone.localRotation = boneRotation * Quaternion.Euler(0, 30, 0);
                            check(Vector3.Distance(before, mount.position) > .0001f,
                                $"Race {race} wing {item.Id} follows torso animation");
                            followingBone.localRotation = boneRotation;
                        }
                        finally { pose.position = oldPosition; pose.euler = oldEuler; pose.scale = oldScale; }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(visual.gameObject); }
                }
            }
            check(PkoWingPose.ToJson().Contains("\"itemId\""), "Copied JSON identifies race and wing item");
            check(PkoWingPose.ToJson().Contains("\"scale\""), "Copied JSON includes wing size");
            var parse = typeof(PkoWingPose).GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
            var legacy = parse.Invoke(null, new object[] { "{\"entries\":[{\"race\":0,\"itemId\":990001}]}" });
            var entries = (System.Collections.Generic.List<PkoWingPose.Entry>)legacy.GetType().GetField("entries").GetValue(legacy);
            check(entries[0].scale == 1f, "Existing settings without scale preserve base size");
            var saved = parse.Invoke(null, new object[] { "{\"version\":1,\"entries\":[{\"race\":0,\"itemId\":990001,\"scale\":1.75}]}" });
            entries = (System.Collections.Generic.List<PkoWingPose.Entry>)saved.GetType().GetField("entries").GetValue(saved);
            check(entries[0].scale == 1.75f, "Saved scale survives JSON reload");
            var maximum = parse.Invoke(null, new object[] { "{\"version\":1,\"entries\":[{\"race\":0,\"itemId\":990001,\"scale\":150000}]}" });
            entries = (System.Collections.Generic.List<PkoWingPose.Entry>)maximum.GetType().GetField("entries").GetValue(maximum);
            check(entries[0].scale == 150000f && PkoWingPose.MaxScale == 150000f,
                "Scale up to 150000 is accepted and survives JSON reload");
            var packaged = parse.Invoke(null, new object[] { Resources.Load<TextAsset>("PkoChar/WingPose").text });
            entries = (System.Collections.Generic.List<PkoWingPose.Entry>)packaged.GetType().GetField("entries").GetValue(packaged);
            for (int race = 0; race < PkoCharacterVisual.Races; race++)
            {
                var entry = entries.Find(e => e.race == race && e.itemId == PkoTables.MeshyMageWingsItemId);
                var position = race == 3 ? new Vector3(.05f, -.17f, -12.70f) : new Vector3(.06f, -.18f, -15.21f);
                check(entry != null && Vector3.Distance(entry.position, position) < .0001f
                    && entry.euler == Vector3.zero && entry.scale == (race == 3 ? 50f : 60f),
                    $"Packaged Mage Wings race {race} contains approved position, rotation and scale");
            }
            var merge = typeof(PkoWingPose).GetMethod("Merge", BindingFlags.Static | BindingFlags.NonPublic);
            merge.Invoke(null, new[] { packaged, saved });
            entries = (System.Collections.Generic.List<PkoWingPose.Entry>)packaged.GetType().GetField("entries").GetValue(packaged);
            check(entries.Find(e => e.race == 0 && e.itemId == 990001).scale == 1.75f,
                "Local adjustments override only their matching race and item");
            check(entries.Find(e => e.race == 3 && e.itemId == 990001).scale == 50f
                && entries.Find(e => e.race == 2 && e.itemId == 990001).scale == 60f,
                "Partial local settings retain packaged defaults for other races");
        }
        catch (Exception e) { Debug.LogException(e); check(false, e.ToString()); }
        finally { UnityEngine.Object.DestroyImmediate(parent); }
        report.Insert(0, $"Passed={passed} Failed={failed}\n");
        File.WriteAllText("Tools/wing-attachment-validation-results.txt", report.ToString());
        if (failed > 0) Debug.LogError(report.ToString()); else Debug.Log($"Wing attachment: {passed} passed, {failed} failed.");
    }
}
