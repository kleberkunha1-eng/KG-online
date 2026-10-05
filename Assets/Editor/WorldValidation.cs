using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

namespace TOP.EditorTools
{
    [InitializeOnLoad]
    public static class WorldValidation
    {
        static WorldValidation() { EditorApplication.update += Poll; }
        static void Poll() { if(EditorApplication.isPlaying) { EditorApplication.QueuePlayerLoopUpdate(); } if(EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Tools/validate-world.request") || File.Exists("Tools/configure-world.request")) return; File.Delete("Tools/validate-world.request"); try { Run(); } catch(Exception ex) { File.WriteAllText("Tools/validation-error.txt",ex.ToString()); } }
        [MenuItem("TOP/Validate gameplay (local test)")]
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            var issues=new List<string>();
            foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                    if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0) issues.Add("Missing script: "+t.name);
            File.WriteAllLines("Tools/scene-validation.txt",issues);
            if(issues.Count>0) throw new Exception(string.Join("\n",issues));
            SessionState.SetBool("TOP.RunSmokeTest",true);
            EditorApplication.isPaused=false;
            EditorApplication.EnterPlaymode();
        }
    }
}
