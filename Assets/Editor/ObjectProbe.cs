using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Writes world position/rotation of objects (by name) from the open scene to Tools/probe.done.txt.
[InitializeOnLoad]
public static class ObjectProbe
{
    static readonly string Request = Path.GetFullPath("Tools/probe.request");
    static readonly string Done = Path.GetFullPath("Tools/probe.done.txt");
    static double next;

    static ObjectProbe() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isPlaying) return;
        next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        string[] names = File.ReadAllText(Request).Split(new[] { '\r', '\n', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        File.Delete(Request);
        var sb = new StringBuilder();
        foreach (string n in names) if (n.StartsWith("shot:"))
        {
            var a = n.Substring(5).Split('|');
            float cx = float.Parse(a[0]), cz = float.Parse(a[1]), half = float.Parse(a[2]);
            var camGo = new GameObject("ProbeCam"); var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = half; cam.transform.position = new Vector3(cx, 400, cz); cam.transform.rotation = Quaternion.Euler(90, 0, 0);
            cam.farClipPlane = 1000; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.magenta;
            var rt = new RenderTexture(1600, 1600, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(1600, 1600); tex.ReadPixels(new Rect(0, 0, 1600, 1600), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(Path.GetFullPath("Tools/probe-shot.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        foreach (string n in names) if (n.StartsWith("mesh:"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ImportedClient/Models/model/scene/" + n.Substring(5).Trim() + ".obj");
            if (prefab == null) continue;
            var mf = prefab.GetComponentsInChildren<MeshFilter>();
            var bb = mf[0].sharedMesh.bounds; foreach (var m in mf) bb.Encapsulate(m.sharedMesh.bounds);
            sb.AppendLine(n + " unityMeshMin=" + bb.min.ToString("F2") + " max=" + bb.max.ToString("F2") + " rootRot=" + prefab.transform.localRotation.eulerAngles + " rootScale=" + prefab.transform.localScale);
        }
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            foreach (string n in names)
                if (t.name.Contains(n.Trim()))
                {
                    var r = t.GetComponentInChildren<Renderer>();
                    sb.AppendLine(t.name + " | parent=" + (t.parent ? t.parent.name : "-") + " | pos=" + t.position.ToString("F3") + " | euler=" + t.eulerAngles.ToString("F2") + " | scale=" + t.lossyScale.ToString("F3") + (r ? " | boundsCenter=" + r.bounds.center.ToString("F3") : ""));
                }
        File.WriteAllText(Done, sb.ToString(), new UTF8Encoding(true));
    }
}
