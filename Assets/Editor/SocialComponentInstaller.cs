using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TOP.Player;

// One-shot utility: ensures the Player prefab has PlayerFriends/PlayerMail/PlayerGuild components.
// Triggered by creating Tools/addsocial.request; writes Tools/addsocial.done.txt when finished.
[InitializeOnLoad]
public static class SocialComponentInstaller
{
    static readonly string Request = Path.GetFullPath("Tools/addsocial.request");
    static readonly string Done = Path.GetFullPath("Tools/addsocial.done.txt");
    static double next;

    static SocialComponentInstaller() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next || EditorApplication.isCompiling || EditorApplication.isPlaying) return;
        next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        Install();
    }

    public static void Install()
    {
        string path = "Assets/Prefabs/Player.prefab";
        var contents = PrefabUtility.LoadPrefabContents(path);
        var sb = new StringBuilder();
        try
        {
            AddIfMissing<PlayerFriends>(contents, sb);
            AddIfMissing<PlayerMail>(contents, sb);
            AddIfMissing<PlayerGuild>(contents, sb);
            AddIfMissing<PlayerTrade>(contents, sb);
            AddIfMissing<PlayerQuests>(contents, sb);
            AddIfMissing<PlayerForge>(contents, sb);
            AddIfMissing<PlayerLifeServices>(contents, sb);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
        File.WriteAllText(Done, sb.ToString());
    }

    static void AddIfMissing<T>(GameObject go, StringBuilder sb) where T : Component
    {
        if (go.GetComponent<T>() == null)
        {
            go.AddComponent<T>();
            sb.AppendLine(typeof(T).Name + " added.");
        }
        else
        {
            sb.AppendLine(typeof(T).Name + " already present.");
        }
    }
}
