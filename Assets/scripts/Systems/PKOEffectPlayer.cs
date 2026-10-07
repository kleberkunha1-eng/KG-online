using System;
using System.Collections.Generic;
using UnityEngine;

// Plays a converted Tales of Pirates .eff effect (JSON exported by BatchExporter --effects).
public class PKOEffectPlayer : MonoBehaviour
{
    [Serializable] public class Sub
    {
        public string name; public int type, src, dst; public float length; public int frames;
        public float[] times, sizes, angles, pos, colors;
        public int verCount; public float coordTime; public float[] coords;
        public int texCount; public float texTime; public string texture; public float[] texUV;
        public string model; public bool billboard; public int segments; public float height, radius, botRadius;
        public string[] frameTex; public float frameTexTime; public float[] cylParams;
        public bool rotaLoop; public float[] rotaVec; public bool alphaOnly, rotaBoard;
    }
    [Serializable] public class Data
    {
        public int version, tech; public string sound, path; public bool rotating; public float[] rotaAxis; public float rotaVel;
        public Sub[] effects;
    }

    public TextAsset json;
    public Texture2D[] textures;
    public Material effectMaterial;
    public Mesh[] modelMeshes;
    public string[] modelMeshNames;
    public bool loop = true;
    public bool destroyWhenDone;

    class Layer
    {
        public Sub s; public Transform t; public Mesh mesh; public Material mat; public Vector3[] baseVerts;
        public Color[] cols; public Vector2[] uvs; public float duration; public float spin;
    }

    readonly List<Layer> layers = new List<Layer>();
    Data data;
    float clock;
    float duration;

    static Vector3 D(float x, float y, float z) { return new Vector3(-x, z, y); }
    static Quaternion DQ(float ax, float ay, float az)
    {
        // D3DXMatrixRotationYawPitchRoll(yaw=ay, pitch=ax, roll=az) in the source space, then mapped to Unity space.
        Quaternion q = Quaternion.AngleAxis(az * Mathf.Rad2Deg, Vector3.forward) * Quaternion.AngleAxis(ax * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(ay * Mathf.Rad2Deg, Vector3.up);
        return new Quaternion(-q.x, q.z, q.y, q.w);
    }

    public static Vector3 ModelPosition(float[] values)
    {
        return values != null && values.Length >= 3 ? D(values[0], values[1], values[2]) : Vector3.zero;
    }

    public static Quaternion ModelRotation(float[] values)
    {
        return values != null && values.Length >= 3 ? DQ(values[0], values[1], values[2]) : Quaternion.identity;
    }

    Texture2D FindTex(string n)
    {
        if (textures == null || string.IsNullOrEmpty(n)) return null;
        foreach (var t in textures) if (t != null && t.name == n) return t;
        return null;
    }

    void Awake()
    {
        if (json == null) return;
        data = JsonUtility.FromJson<Data>(json.text);
        foreach (var s in data.effects)
        {
            var go = new GameObject(string.IsNullOrEmpty(s.name) ? "layer" : s.name);
            go.transform.SetParent(transform, false);
            var l = new Layer { s = s, t = go.transform };
            l.mesh = BuildMesh(s, out l.baseVerts);
            if (l.mesh == null) continue;
            l.mat = new Material(effectMaterial);
            l.mat.SetFloat("_Src", UnityBlend(s.src, true)); l.mat.SetFloat("_Dst", UnityBlend(s.dst, false));
            l.mat.mainTexture = FindTex(s.texture);
            l.cols = new Color[l.baseVerts.Length]; l.uvs = l.mesh.uv;
            go.AddComponent<MeshFilter>().sharedMesh = l.mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = l.mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            float d = 0; if (s.times != null) foreach (var x in s.times) d += x;
            l.duration = d > 0 ? d : Mathf.Max(s.length, 0.1f);
            duration = Mathf.Max(duration, l.duration);
            layers.Add(l);
        }
        if (duration <= 0) duration = 1;
    }

    Mesh BuildMesh(Sub s, out Vector3[] verts)
    {
        var m = new Mesh { name = s.model };
        Vector2[] uv; int[] tris;
        switch (s.model)
        {
            case "Rect": verts = new[] { D(-.5f, 0, 0), D(-.5f, 0, 1), D(.5f, 0, 1), D(.5f, 0, 0) }; uv = Quad(); tris = new[] { 0, 1, 2, 0, 2, 3 }; break;
            case "RectPlane": verts = new[] { D(-.5f, -.5f, 0), D(-.5f, .5f, 0), D(.5f, .5f, 0), D(.5f, -.5f, 0) }; uv = Quad(); tris = new[] { 0, 1, 2, 0, 2, 3 }; break;
            case "RectZ": verts = new[] { D(0, 0, 0), D(0, 0, 1), D(0, 1, 1), D(0, 1, 0) }; uv = Quad(); tris = new[] { 0, 1, 2, 0, 2, 3 }; break;
            case "Triangle": verts = new[] { D(-.5f, 0, 0), D(0, 0, 1), D(.5f, 0, 0) }; uv = new[] { new Vector2(0, 1), new Vector2(.5f, 0), new Vector2(1, 1) }; tris = new[] { 0, 1, 2 }; break;
            case "TrianglePlane": verts = new[] { D(-.5f, -.5f, 0), D(0, .5f, 0), D(.5f, -.5f, 0) }; uv = new[] { new Vector2(0, 1), new Vector2(.5f, 0), new Vector2(1, 1) }; tris = new[] { 0, 1, 2 }; break;
            case "Cylinder":
            case "Cone":
            {
                int n = Mathf.Clamp(s.segments > 2 ? s.segments : 16, 3, 64);
                float h = s.height > 0 ? s.height : 1, top = s.model == "Cone" ? 0 : s.radius, bot = s.model == "Cone" ? s.radius : s.botRadius;
                if (s.cylParams != null && s.cylParams.Length >= 4) { n = Mathf.Clamp((int)s.cylParams[0], 3, 64); h = s.cylParams[1]; top = s.cylParams[2]; bot = s.cylParams[3]; }
                var v = new List<Vector3>(); var u = new List<Vector2>(); var t = new List<int>();
                for (int i = 0; i <= n; i++)
                {
                    float a = i * Mathf.PI * 2 / n, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    v.Add(D(c * bot, sn * bot, 0)); u.Add(new Vector2((float)i / n, 1));
                    v.Add(D(c * top, sn * top, h)); u.Add(new Vector2((float)i / n, 0));
                }
                for (int i = 0; i < n; i++) { int b = i * 2; t.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 }); }
                verts = v.ToArray(); uv = u.ToArray(); tris = t.ToArray(); break;
            }
            default:
            {
                Mesh lib = null;
                if (modelMeshNames != null)
                    for (int i = 0; i < modelMeshNames.Length; i++)
                        if (string.Equals(modelMeshNames[i], s.model, StringComparison.OrdinalIgnoreCase)) lib = modelMeshes[i];
                if (lib == null) { verts = null; return null; }
                verts = lib.vertices; m.vertices = verts; m.uv = lib.uv; m.triangles = lib.triangles; m.RecalculateBounds();
                m.MarkDynamic(); return m;
            }
        }
        m.vertices = verts; m.uv = uv; m.triangles = tris; m.MarkDynamic();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100);
        return m;
    }

    // D3DBLEND -> UnityEngine.Rendering.BlendMode; DESTALPHA is treated as ONE because the framebuffer has no alpha here.
    public static float UnityBlend(int d3d, bool src)
    {
        switch (d3d)
        {
            case 1: return 0; case 2: return 1; case 3: return 3; case 4: return 6; case 5: return 5; case 6: return 10;
            case 7: return 1; case 8: return 10; case 9: return 2; case 10: return 4; case 11: return 9;
        }
        return src ? 5 : 10;
    }

    static Vector2[] Quad() { return new[] { new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1) }; }

    void Update()
    {
        if (data == null) return;
        clock += Time.deltaTime;
        if (clock >= duration)
        {
            if (destroyWhenDone && !loop) { Destroy(gameObject); return; }
            if (loop) clock %= duration; else clock = duration;
        }
        if (data.rotating && data.rotaAxis != null && data.rotaAxis.Length >= 3)
            transform.localRotation = Quaternion.AngleAxis(data.rotaVel * Mathf.Rad2Deg * clock, D(data.rotaAxis[0], data.rotaAxis[1], data.rotaAxis[2]).normalized);

        Camera cam = Camera.main;
        foreach (var l in layers)
        {
            var s = l.s;
            int n = s.frames;
            if (n == 0) { l.t.gameObject.SetActive(false); continue; }
            float lt = Mathf.Min(clock, l.duration), acc = 0; int f = 0; float k = 0;
            for (; f < n; f++)
            {
                float ft = s.times[f];
                if (lt <= acc + ft || f == n - 1) { k = ft > 0 ? Mathf.Clamp01((lt - acc) / ft) : 0; break; }
                acc += ft;
            }
            int g = Mathf.Min(f + 1, n - 1);
            bool visible = clock <= l.duration || loop == false && clock < l.duration + 0.0001f || loop;
            l.t.gameObject.SetActive(visible && (loop || clock < l.duration));
            Vector3 sz = Vector3.Lerp(V3(s.sizes, f), V3(s.sizes, g), k);
            Vector3 ps = Vector3.Lerp(V3(s.pos, f), V3(s.pos, g), k);
            Vector3 an = Vector3.Lerp(V3(s.angles, f), V3(s.angles, g), k);
            l.t.localPosition = D(ps.x, ps.y, ps.z);
            l.t.localScale = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.z), Mathf.Abs(sz.y));
            Quaternion rot = DQ(an.x, an.y, an.z);
            if (s.rotaLoop && s.rotaVec != null && s.rotaVec.Length >= 4)
                rot = rot * Quaternion.AngleAxis(s.rotaVec[3] * Mathf.Rad2Deg * clock, D(s.rotaVec[0], s.rotaVec[1], s.rotaVec[2]).normalized);
            if (s.billboard && cam != null) rot = cam.transform.rotation * rot;
            else rot = transform.rotation * rot;
            l.t.rotation = rot;

            Color c0 = Col(s.colors, f), c1 = Col(s.colors, g), c = Color.Lerp(c0, c1, k);
            for (int i = 0; i < l.cols.Length; i++) l.cols[i] = c;
            l.mesh.colors = l.cols;

            if (s.texCount > 1 && s.texTime > 0 && s.texUV != null && s.verCount == l.baseVerts.Length)
            {
                int fr = (int)(clock / s.texTime) % s.texCount; int o = fr * s.verCount * 2;
                for (int i = 0; i < s.verCount; i++) l.uvs[i] = new Vector2(s.texUV[o + i * 2], s.texUV[o + i * 2 + 1]);
                l.mesh.uv = l.uvs;
            }
            else if (s.coords != null && s.verCount == l.baseVerts.Length && s.coords.Length >= s.verCount * 4 && s.coordTime > 0)
            {
                int cc = s.coords.Length / (s.verCount * 2); int fr = (int)(clock / s.coordTime) % cc; int o = fr * s.verCount * 2;
                for (int i = 0; i < s.verCount; i++) l.uvs[i] = new Vector2(s.coords[o + i * 2], s.coords[o + i * 2 + 1]);
                l.mesh.uv = l.uvs;
            }
            if (s.frameTex != null && s.frameTex.Length > 1 && s.frameTexTime > 0)
                l.mat.mainTexture = FindTex(s.frameTex[(int)(clock / s.frameTexTime) % s.frameTex.Length]) ?? l.mat.mainTexture;
        }
    }

    static Vector3 V3(float[] a, int i) { return a != null && a.Length >= i * 3 + 3 ? new Vector3(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]) : Vector3.one; }
    static Color Col(float[] a, int i) { return a != null && a.Length >= i * 4 + 4 ? new Color(a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3]) : Color.white; }

    public static PKOEffectPlayer Spawn(GameObject prefab, Vector3 position, bool loop = false)
    {
        var go = Instantiate(prefab, position, Quaternion.identity);
        var p = go.GetComponent<PKOEffectPlayer>(); p.loop = loop; p.destroyWhenDone = !loop; return p;
    }
}
