#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using Mirror;
using TOP.Player;
using TOP.Data;

namespace TOP.Testing
{
    // Runs only when the editor explicitly requests it. Never included in player builds.
    public class WorldSmokeTest : MonoBehaviour
    {
        readonly List<string> checks=new List<string>();
        readonly List<string> errors=new List<string>();
        NetworkManager manager;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartRequested()
        {
            if(!SessionState.GetBool("TOP.RunSmokeTest",false)) return;
            SessionState.SetBool("TOP.RunSmokeTest",false);
            SessionState.SetBool("TOP.SmokeTestActive",true);
            new GameObject("WorldSmokeTest").AddComponent<WorldSmokeTest>();
        }
        void Check(bool condition,string message) { checks.Add((condition?"PASS ":"FAIL ")+message); File.WriteAllLines("Tools/smoke-progress.txt",checks); if(!condition) errors.Add(message); }
        void Log(string message,string stack,LogType type) { if(type==LogType.Exception || type==LogType.Error) errors.Add(message); }
        IEnumerator Start()
        {
            Application.runInBackground=true; Time.timeScale=1;
            Application.logMessageReceived+=Log;
            File.WriteAllText("Tools/smoke-progress.txt","Starting host");
            var go=new GameObject("TestNetworkManager");
            var transport=go.AddComponent<kcp2k.KcpTransport>(); transport.Port=17891;
            manager=go.AddComponent<NetworkManager>(); manager.transport=transport;
            manager.playerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            manager.spawnPrefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Mob_Slime.prefab"));
            manager.StartHost();
            File.AppendAllText("Tools/smoke-progress.txt","\nHost started");
            for(int frame=0;frame<180 && NetworkClient.localPlayer==null;frame++) { EditorApplication.isPaused=false; yield return null; }
            var identity=NetworkClient.localPlayer;
            Check(identity!=null,"Host spawns a local network player");
            if(identity==null) { Finish(); yield break; }
            var controller=identity.GetComponent<PlayerController>();
            // Prevent physical keyboard/mouse state from overriding scripted test commands.
            identity.GetComponent<PlayerMovement>().InputEnabled=false;
            identity.GetComponent<CameraFollow>().allowRotation=false;
            var stats=identity.GetComponent<PlayerStats>();
            controller.InitializeFromCharacterData(new CharacterData { Id=999999, AccountId=999999, Name="Teste local", Level=1, BaseStr=10, BaseAgi=8, BaseCon=10, BaseSpr=10, BaseSta=10, CurrentHp=220,CurrentMp=150,CurrentSp=96,MaxHp=220,MaxMp=150,MaxSp=96,PosY=.6f });
            identity.transform.position=new Vector3(0,.6f,0);
            yield return null;
            Check(stats.CurrentHp>0 && controller.CurrentHp==stats.CurrentHp,"Database data initializes authoritative HP and HUD values");
            Check(Terrain.activeTerrain!=null,"Original Garner terrain is loaded"); Check(TOP.Data.PkoTables.Items.Count>5000 && TOP.Data.PkoTables.Skills.Count>400 && TOP.Data.PkoTables.ExpToNextLevel(1)==5 && TOP.Data.PkoTables.TotalExpForLevel(100)>4000000000UL,"Original item, skill and level tables load");
            Check(NavMesh.SamplePosition(identity.transform.position,out var nav,3f,NavMesh.AllAreas),"Spawn is on baked navigation mesh");
            var start=identity.transform.position;
            identity.GetComponent<PlayerMovement>().SetDestination(start+Vector3.forward*5);
            yield return new WaitForSeconds(1.5f);
            Check(Vector3.Distance(start,identity.transform.position)>3,"Server movement crosses the textured terrain");
            identity.GetComponent<PlayerMovement>().Stop();
            identity.SendMessage("CmdSetRunning",true);
            yield return null;
            var runStart=identity.transform.position;
            identity.GetComponent<PlayerMovement>().SetDestination(runStart+Vector3.forward*12);
            yield return new WaitForSeconds(.7f);
            Check(Vector3.Distance(runStart,identity.transform.position)>4.5f,"Running is faster than walking");
            identity.GetComponent<PlayerMovement>().Stop(); identity.SendMessage("CmdSetRunning",false);
            yield return new WaitForSeconds(3);
            var enemy=UnityEngine.Object.FindAnyObjectByType<EnemyStats>();
            Check(enemy!=null,"Configured spawners create network monsters");
            if(enemy!=null)
            {
                enemy.GetComponent<EnemyAI>().enabled=false;
                enemy.GetComponent<NavMeshAgent>().enabled=false;
                enemy.transform.position=identity.transform.position+Vector3.forward;
                int hp=enemy.Health;
                identity.GetComponent<PlayerCombat>().AttackTarget(enemy.netId);
                yield return new WaitForSeconds(1.2f);
                Check(enemy.Health<hp,"Auto attack damages the selected monster");
                identity.GetComponent<PlayerCombat>().StopAttack();
                int mp=stats.CurrentMp;
                identity.GetComponent<PlayerSkills>().UseSkill(9002,identity.transform.position);
                Check(stats.CurrentMp<mp,"Healing skill consumes mana");
                ulong xp=controller.Exp;
                enemy.TakeTrueDamage(10000,identity.netId);
                Check(controller.Exp>xp || controller.Level>1,"Monster death grants XP");
            }
            controller.AddExp(controller.ExperienceToNextLevel);
            Check(controller.Level>=2,"XP threshold increases level");
            stats.TakeDamage(100000);
            Check(stats.IsDead,"Lethal damage kills the player");
            yield return new WaitForSeconds(5.5f);
            Check(!stats.IsDead && controller.CurrentHp>0,"Player respawns with restored HP");
            yield return new WaitForSeconds(.5f);
            var minimap=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
            Check(Array.Exists(minimap,c=>c.name=="MinimapCamera" && c.targetTexture!=null && Vector2.Distance(new Vector2(c.transform.position.x,c.transform.position.z),new Vector2(identity.transform.position.x,identity.transform.position.z))<.2f),"Minimap camera follows local player");
            var camera=Camera.main;
            var visual=new List<string> { "Player="+identity.transform.position+" scale="+identity.transform.lossyScale, "Camera="+camera.transform.position+" euler="+camera.transform.eulerAngles+" mask="+camera.cullingMask+" near="+camera.nearClipPlane };
            foreach(var r in identity.GetComponentsInChildren<Renderer>(true)) visual.Add(r.name+" enabled="+r.enabled+" active="+r.gameObject.activeInHierarchy+" bounds="+r.bounds+" scale="+r.transform.lossyScale);
            bool validModel=true;
            foreach(var r in identity.GetComponentsInChildren<SkinnedMeshRenderer>()) { var baked=new Mesh(); r.BakeMesh(baked); baked.RecalculateBounds(); validModel &= r.bones.Length==r.sharedMesh.bindposes.Length && baked.bounds.size.sqrMagnitude>.0001f; visual.Add(r.name+" baked="+baked.bounds+" mesh="+r.sharedMesh.vertexCount+" material="+r.sharedMaterial+" shader="+r.sharedMaterial.shader+" forceOff="+r.forceRenderingOff+" shadows="+r.shadowCastingMode); Destroy(baked); }
            Check(validModel,"Player mesh has valid skinning and nonzero geometry");
            var scenery=GameObject.Find("ArgentScenery");
            if(scenery!=null) foreach(Transform structure in scenery.transform)
            {
                var renderers=structure.GetComponentsInChildren<Renderer>(true);
                if(renderers.Length==0) continue;
                var bounds=renderers[0].bounds; var materials=new HashSet<string>();
                foreach(var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    foreach(var material in renderer.sharedMaterials)
                        if(material!=null) materials.Add(material.name+" shader="+material.shader.name+" texture="+(material.mainTexture!=null?material.mainTexture.name:"<none>"));
                }
                visual.Add("Structure="+structure.name+" position="+structure.position+" bounds="+bounds+" materials="+string.Join(", ",materials));
            }
            int playerLayer=LayerMask.NameToLayer("Player"), enemyLayer=LayerMask.NameToLayer("Enemy");
            foreach(var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if(renderer.gameObject.layer==playerLayer || renderer.gameObject.layer==enemyLayer) continue;
                string materials=string.Join(", ",Array.ConvertAll(renderer.sharedMaterials,material=>material==null?"<null>":material.name+" shader="+material.shader.name+" texture="+(material.mainTexture!=null?material.mainTexture.name:"<none>")));
                visual.Add("EnvironmentRenderer="+renderer.name+" root="+renderer.transform.root.name+" bounds="+renderer.bounds+" materials="+materials);
            }
            var animator=identity.transform.Find("CharacterVisual").GetComponentInChildren<Animator>();
            if(animator.avatar!=null && animator.avatar.isHuman)
            {
                float footY=Mathf.Min(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y);
                Check(Mathf.Abs(footY-identity.transform.position.y-.18f)<.1f,"Animated feet remain above terrain");
            }
            else
            {
                var visualRoot=identity.transform.Find("CharacterVisual");
                Check(animator.enabled && animator.runtimeAnimatorController!=null && visualRoot!=null && Mathf.Abs(visualRoot.position.y-identity.transform.position.y)<.01f,"Generic animated character remains attached to movement root");
            }
            foreach(var t in FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsInactive.Exclude)) if(t.name=="Txt_Name" || t.name=="Txt_HP") visual.Add(t.name+" color="+t.color+" face="+t.faceColor+" canvas="+t.canvasRenderer.GetColor()+" shader="+t.fontSharedMaterial.shader+" materialFace="+t.fontSharedMaterial.GetColor("_FaceColor"));
            File.WriteAllLines("Tools/runtime-visual-audit.txt",visual);
#if UNITY_EDITOR
            var fxPrefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effects/00000002.prefab");
            if(fxPrefab!=null)
            {
                var fx=Instantiate(fxPrefab,identity.transform.position+identity.transform.forward*2f,Quaternion.identity);
                foreach(var extra in new[]{"00000004","00000010","00000020"}){ var ep=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Effects/"+extra+".prefab"); if(ep!=null) Instantiate(ep,identity.transform.position+identity.transform.right*(extra=="00000004"?-2.5f:extra=="00000010"?2.5f:5f),Quaternion.identity); }
                yield return new WaitForSeconds(.6f);
                Check(fx.GetComponentsInChildren<MeshRenderer>(true).Length>0,"Converted .eff effect builds visible layers at runtime");
            }
#endif
            var pko=TOP.UI.Pko.PkoUi.Instance;
            Check(pko!=null && pko.Defs.Count>100 && pko.Windows.Count>100,"Original UI forms are built from client layouts");
            if(pko!=null)
            {
                var testInv=identity.GetComponent<TOP.Player.PlayerInventory>();
                if(testInv!=null){ foreach(var kv in PkoTables.Items){ if(!string.IsNullOrEmpty(kv.Value.Icon) && pko.Icon(kv.Value.Icon)!=null){ testInv.AddItem(kv.Key,1); break; } } }
                pko.Open("frmInv"); yield return new WaitForSeconds(.4f);
                var invW=pko.Get("frmInv"); bool anyIcon=false; foreach(var s in invW.Grids["grdItem"]) anyIcon|=s.Filled;
                Check(invW.IsOpen && invW.Grids["grdItem"].Count==48,"Inventory window opens with 48 original grid slots");
                int dbgFilled=0; foreach(var s in invW.Grids["grdItem"]) if(s.Filled) dbgFilled++;
                Debug.Log($"[SmokeUI] testInv={(testInv!=null)} localPlayer={(NetworkClient.localPlayer!=null)} same={(NetworkClient.localPlayer==identity)} filled={dbgFilled} slot0={(testInv!=null&&testInv.GetSlot(0)!=null?testInv.GetSlot(0).ItemId:-1)}");
                Check(anyIcon,"Inventory window shows item icons "+$"testInv={(testInv!=null)} same={(NetworkClient.localPlayer==identity)} filled={dbgFilled} slot0={(testInv!=null&&testInv.GetSlot(0)!=null?testInv.GetSlot(0).ItemId:-1)} total={(testInv!=null?testInv.totalSlots:-1)}");
            }            var rt=new RenderTexture(1280,720,24); camera.targetTexture=rt;
            foreach(var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)) if(canvas.renderMode==RenderMode.ScreenSpaceOverlay) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1; }
            yield return new WaitForEndOfFrame();
            RenderTexture.active=rt; var capture=new Texture2D(1280,720,TextureFormat.RGB24,false); capture.ReadPixels(new Rect(0,0,1280,720),0,0); capture.Apply(); File.WriteAllBytes("Tools/gameplay-smoke.png",capture.EncodeToPNG());
            if(pko!=null)
            {
                pko.Close("frmInv"); pko.Open("frmState"); pko.Open("frmSkill"); yield return new WaitForSeconds(.5f); yield return new WaitForEndOfFrame();
                RenderTexture.active=rt; var cap2=new Texture2D(1280,720,TextureFormat.RGB24,false); cap2.ReadPixels(new Rect(0,0,1280,720),0,0); cap2.Apply(); File.WriteAllBytes("Tools/gameplay-ui2.png",cap2.EncodeToPNG()); Destroy(cap2);
                var skw=pko.Get("frmSkill"); var st2=pko.Get("frmState");
                Check(st2.Label("labStateName")!=null && st2.Label("labStateName").text==controller.CharacterName,"Status window shows character data");
                var content=pko.ContentOf(skw,"lstSkill"); Check(content!=null && content.childCount>10,"Skill window lists original class skills");
                int beforePts=controller.StatPoints; controller.StatPoints+=1; int str=controller.BaseStr; controller.CmdSpendStatPoint(0);
                Check(controller.BaseStr==str+1,"Status point button logic raises attribute");
            }            camera.targetTexture=null; RenderTexture.active=null; rt.Release(); Destroy(rt); Destroy(capture);
            yield return new WaitForSeconds(1);
            Finish();
        }
        void Finish()
        {
            Application.logMessageReceived-=Log;
            SessionState.SetBool("TOP.SmokeTestActive",false);
            File.WriteAllLines("Tools/smoke-results.txt",checks);
            File.WriteAllLines("Tools/smoke-errors.txt",errors);
            if(manager!=null) manager.StopHost();
            if(Application.isBatchMode) EditorApplication.Exit(errors.Count==0?0:1);
            else EditorApplication.isPlaying=false;
        }
    }
}
#endif
