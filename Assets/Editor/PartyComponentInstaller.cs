using System.IO;
using UnityEditor;
using UnityEngine;
using TOP.Player;

// One-shot utility: ensures the Player prefab has a PlayerParty component.
// Triggered by creating Tools/addparty.request; writes Tools/addparty.done.txt when finished.
[InitializeOnLoad]
public static class PartyComponentInstaller
{
    static readonly string Request = Path.GetFullPath("Tools/addparty.request");
    static readonly string Done = Path.GetFullPath("Tools/addparty.done.txt");
    static double next;

    static PartyComponentInstaller() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isPlaying) return;
        next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        File.Delete(Request);

        string path = "Assets/Prefabs/Player.prefab";
        var contents = PrefabUtility.LoadPrefabContents(path);
        string result;
        try
        {
            var existing = contents.GetComponent<PlayerParty>();
            if (existing == null)
            {
                contents.AddComponent<PlayerParty>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                result = "PlayerParty component added to " + path;
            }
            else
            {
                result = "PlayerParty component already present on " + path;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
        File.WriteAllText(Done, result);
    }
}
