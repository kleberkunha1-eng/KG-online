using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TOP.Diagnostics;
using UnityEditor;
using UnityEngine;

// Tools > Trace: view and delete the development trace (Logs/Trace, Build/*/Trace and API/logs).
public sealed class GameTraceWindow : EditorWindow
{
    const int MaxViewLines = 2000;
    static readonly string[] Filters = { "Problemas", "Tudo", "Resumo", "Rede/HTTP", "Logs" };

    string[] files = Array.Empty<string>();
    int fileIndex, filter;
    string search = "";
    Vector2 scroll;
    readonly List<string> lines = new List<string>();
    string report = "";
    long lastSize = -1;
    GUIStyle mono;

    [MenuItem("Tools/Trace/Ver Trace")]
    static void Open() => GetWindow<GameTraceWindow>("Trace").Reload();

    [MenuItem("Tools/Trace/Apagar Trace")]
    static void ClearMenu()
    {
        if (EditorUtility.DisplayDialog("Apagar trace", "Apagar todos os arquivos de trace (Unity, servidor e API)?", "Apagar", "Cancelar"))
        {
            int n = DeleteAll();
            Debug.Log($"[GameTrace] {n} arquivo(s) de trace apagado(s).");
            foreach (var w in Resources.FindObjectsOfTypeAll<GameTraceWindow>()) w.Reload();
        }
    }

    [MenuItem("Tools/Trace/Abrir Pasta")]
    static void OpenFolder()
    {
        string dir = Path.Combine(ProjectRoot, "Logs", "Trace");
        Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(Path.Combine(dir, "."));
    }

    static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    static IEnumerable<string> TraceFolders()
    {
        yield return Path.Combine(ProjectRoot, "Logs", "Trace");
        yield return Path.Combine(ProjectRoot, "API", "logs");
        string build = Path.Combine(ProjectRoot, "Build");
        if (Directory.Exists(build))
            foreach (var d in Directory.GetDirectories(build)) yield return Path.Combine(d, "Trace");
    }

    static string[] FindFiles() => TraceFolders().Where(Directory.Exists)
        .SelectMany(d => Directory.GetFiles(d, "trace-*.log"))
        .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();

    // Files in use are emptied instead of deleted (the running game keeps writing to them).
    static int DeleteAll()
    {
        int n = 0;
        foreach (var f in FindFiles())
        {
            try { File.Delete(f); n++; }
            catch { try { using (new FileStream(f, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { } n++; } catch { } }
        }
        GameTrace.Clear();
        return n;
    }

    void Reload()
    {
        string current = files.Length > fileIndex ? files[fileIndex] : null;
        files = FindFiles();
        fileIndex = Math.Max(0, Array.IndexOf(files, current));
        lastSize = -1;
        Load();
    }

    void OnEnable() => Reload();
    void OnInspectorUpdate()
    {
        if (files.Length == 0) return;
        long size = 0;
        try { size = new FileInfo(files[fileIndex]).Length; } catch { }
        if (size != lastSize) { Load(); Repaint(); }
    }

    void Load()
    {
        lines.Clear(); report = "";
        if (files.Length == 0) return;
        string path = files[fileIndex];
        string[] all;
        try
        {
            lastSize = new FileInfo(path).Length;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            all = reader.ReadToEnd().Split('\n');
        }
        catch (Exception e) { report = "Erro lendo: " + e.Message; return; }

        report = BuildReport(all);
        foreach (var raw in all)
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || !Matches(line)) continue;
            lines.Add(line);
        }
        if (lines.Count > MaxViewLines) lines.RemoveRange(0, lines.Count - MaxViewLines);
    }

    static string Cat(string line)
    {
        var p = line.Split('\t');
        return p.Length >= 4 ? p[2] : p.Length >= 3 ? p[1] : "";
    }

    bool Matches(string line)
    {
        if (search.Length > 0 && line.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) return false;
        string c = Cat(line);
        switch (filter)
        {
            case 0: return c is "FREEZE" or "STALL" or "HITCH" or "SLOW" or "HANDLER" or "HTTP-SLOW" or "ERROR" or "EXC" or "WARN" or "MARK" or "SESSION" or "REQ-SLOW" or "SQL-SLOW" or "REQ-ERR";
            case 2: return c.StartsWith("SUMMARY") || c == "SESSION" || c == "MARK";
            case 3: return c.StartsWith("HTTP") || c is "NET" or "HANDLER" or "REQ" or "REQ-SLOW" or "REQ-ERR" or "SQL-SLOW";
            case 4: return c is "LOG" or "WARN" or "ERROR" or "EXC";
            default: return true;
        }
    }

    static string BuildReport(string[] all)
    {
        var counts = new Dictionary<string, int>();
        var worst = new List<(double ms, string line)>();
        foreach (var raw in all)
        {
            string line = raw.TrimEnd('\r');
            string c = Cat(line);
            if (c.Length == 0) continue;
            counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
            if (c is "FREEZE" or "STALL" or "HITCH" or "SLOW" or "HANDLER" or "HTTP-SLOW" or "REQ-SLOW" or "SQL-SLOW")
            {
                int i = line.IndexOf("ms=", StringComparison.Ordinal);
                if (i >= 0 && double.TryParse(new string(line.Skip(i + 3).TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray()).Replace(',', '.'),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ms))
                    worst.Add((ms, line));
            }
        }
        var sb = new StringBuilder();
        sb.Append(string.Join("  ", counts.OrderByDescending(k => k.Value).Select(k => $"{k.Key}:{k.Value}")));
        foreach (var w in worst.OrderByDescending(w => w.ms).Take(5))
            sb.Append("\n• ").Append(w.line.Length > 220 ? w.line.Substring(0, 220) + "..." : w.line);
        return sb.ToString();
    }

    void OnGUI()
    {
        mono ??= new GUIStyle(EditorStyles.label) { font = Font.CreateDynamicFontFromOSFont("Consolas", 11), richText = false, wordWrap = false };

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Atualizar", EditorStyles.toolbarButton, GUILayout.Width(70))) Reload();
            if (GUILayout.Button("Apagar Trace", EditorStyles.toolbarButton, GUILayout.Width(90))) ClearMenu();
            if (GUILayout.Button("Abrir Pasta", EditorStyles.toolbarButton, GUILayout.Width(80)))
                EditorUtility.RevealInFinder(files.Length > 0 ? files[fileIndex] : Path.Combine(ProjectRoot, "Logs", "Trace"));
            GUILayout.FlexibleSpace();
            long total = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
            GUILayout.Label($"{files.Length} arquivo(s), {total / 1048576.0:F1} MB", EditorStyles.miniLabel);
        }

        if (files.Length == 0)
        {
            EditorGUILayout.HelpBox("Nenhum trace ainda. Entre em Play (ou rode servidor/API) e volte aqui.", MessageType.Info);
            return;
        }

        EditorGUI.BeginChangeCheck();
        var names = files.Select(f => $"{Path.GetFileName(f)}  ({new FileInfo(f).Length / 1024} KB)  [{Path.GetFileName(Path.GetDirectoryName(f))}]").ToArray();
        fileIndex = EditorGUILayout.Popup("Arquivo", Mathf.Min(fileIndex, files.Length - 1), names);
        filter = GUILayout.Toolbar(filter, Filters);
        search = EditorGUILayout.TextField("Buscar", search);
        if (EditorGUI.EndChangeCheck()) Load();

        EditorGUILayout.HelpBox(report, MessageType.None);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var line in lines)
        {
            string c = Cat(line);
            var old = GUI.color;
            if (c is "FREEZE" or "STALL" or "EXC" or "ERROR" or "REQ-ERR") GUI.color = new Color(1f, .55f, .55f);
            else if (c is "HITCH" or "SLOW" or "HANDLER" or "HTTP-SLOW" or "WARN" or "REQ-SLOW" or "SQL-SLOW") GUI.color = new Color(1f, .85f, .45f);
            else if (c.StartsWith("SUMMARY") || c == "MARK") GUI.color = new Color(.6f, .85f, 1f);
            EditorGUILayout.SelectableLabel(line, mono, GUILayout.Height(15));
            GUI.color = old;
        }
        EditorGUILayout.EndScrollView();
    }
}
