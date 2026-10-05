using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace TOP.EditorTools
{
    public static class PlayerModelAudit
    {
        public static void Run()
        {
            var player=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            var lines=new List<string>();
            foreach(var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.updateWhenOffscreen=true;
                var mesh=new Mesh(); renderer.BakeMesh(mesh); mesh.RecalculateBounds();
                lines.Add(renderer.name+" baked="+mesh.bounds.center.ToString("F6")+" size="+mesh.bounds.size.ToString("F6")+" vertices="+mesh.vertexCount+" source="+renderer.sharedMesh.bounds+" bones="+renderer.bones.Length+" root="+renderer.rootBone+" matrix="+renderer.localToWorldMatrix);
                lines.Add("First vertex="+mesh.vertices[0].ToString("F6")+" weights="+renderer.sharedMesh.boneWeights.Length+" bindposes="+renderer.sharedMesh.bindposes.Length);
                if(renderer.sharedMesh.boneWeights.Length>0) { var w=renderer.sharedMesh.boneWeights[0]; lines.Add("Weight="+w.weight0+" bone="+w.boneIndex0+" bindpose="+renderer.sharedMesh.bindposes[w.boneIndex0]); }
                foreach(var bone in renderer.bones) lines.Add(bone==null?"NULL BONE":bone.name+" pos="+bone.position+" scale="+bone.lossyScale);
                Object.DestroyImmediate(mesh);
            }
            File.WriteAllLines("Tools/player-model-audit.txt",lines);
            PrefabUtility.UnloadPrefabContents(player);
        }
    }
}
