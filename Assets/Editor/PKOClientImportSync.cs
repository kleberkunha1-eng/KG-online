using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TOP.EditorTools
{
    public static class PKOClientImportSync
    {
        const string SourceRoot = "Assets/PKO_Data/ClientImport";
        const string TargetRoot = "Assets/ImportedClient";

        [MenuItem("TOP/Sync PKO Client Import")]
        public static void Sync()
        {
            if (!Directory.Exists(SourceRoot))
            {
                Debug.LogError($"PKO client import folder not found: {SourceRoot}");
                return;
            }

            foreach (string relativeFolder in new[]
                     {
                         "Models",
                         "Textures",
                         "Source",
                         "Audio",
                         "animations",
                         "effects",
                         "maps",
                         "shaders"
                     })
            {
                string sourceDir = Path.Combine(SourceRoot, relativeFolder).Replace('\\', '/');
                string targetDir = Path.Combine(TargetRoot, relativeFolder).Replace('\\', '/');

                if (!Directory.Exists(sourceDir))
                    continue;

                CopyDirectory(sourceDir, targetDir);
            }

            AssetDatabase.Refresh();
            Debug.Log("PKO client import synced into Assets/ImportedClient.");
        }

        static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);
                string destinationPath = Path.Combine(targetDir, fileName).Replace('\\', '/');
                if (!File.Exists(destinationPath))
                    File.Copy(filePath, destinationPath, false);
            }

            foreach (string directoryPath in Directory.GetDirectories(sourceDir))
            {
                string childName = Path.GetFileName(directoryPath);
                CopyDirectory(directoryPath, Path.Combine(targetDir, childName).Replace('\\', '/'));
            }
        }
    }
}
