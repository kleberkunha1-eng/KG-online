using System.Collections;
using Mirror;
using TOP.Core;
using TOP.Data;
using TOP.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TOP.Bootstrap
{
    public class GameSceneDevBootstrap : MonoBehaviour
    {
        [SerializeField] private bool quickPlayInEditor = true;
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject[] networkSpawnPrefabs;
        [SerializeField] private ushort quickPlayPort = 17891;

        private IEnumerator Start()
        {
            if (!Application.isEditor || !quickPlayInEditor || SceneManager.GetActiveScene().name != "GameScene")
                yield break;

            if (NetworkClient.active || NetworkServer.active)
                yield break;

#if UNITY_EDITOR
            if (IsSmokeTestRunning())
                yield break;
#endif

            GameObject resolvedPlayerPrefab = ResolvePlayerPrefab();
            if (resolvedPlayerPrefab == null)
            {
                Debug.LogError("[GameSceneDevBootstrap] Player prefab missing. Quick Play cannot start.");
                yield break;
            }

            EnsureMainCamera();

            var networkGo = new GameObject("QuickPlayNetworkManager");
            DontDestroyOnLoad(networkGo);

            var transport = networkGo.AddComponent<kcp2k.KcpTransport>();
            transport.Port = quickPlayPort;

            var manager = networkGo.AddComponent<NetworkManager>();
            manager.transport = transport;
            manager.playerPrefab = resolvedPlayerPrefab;

            foreach (GameObject prefab in ResolveSpawnPrefabs())
            {
                if (prefab != null && !manager.spawnPrefabs.Contains(prefab))
                    manager.spawnPrefabs.Add(prefab);
            }

            manager.StartHost();
            Debug.Log("[GameSceneDevBootstrap] Quick Play host started for GameScene.");

            for (int frame = 0; frame < 180 && NetworkClient.localPlayer == null; frame++)
                yield return null;

            if (NetworkClient.localPlayer == null)
            {
                Debug.LogError("[GameSceneDevBootstrap] Host started, but no local player spawned.");
                yield break;
            }

            GameObject player = NetworkClient.localPlayer.gameObject;
            Vector3 spawnPosition = GetSpawnPosition();
            player.transform.SetPositionAndRotation(spawnPosition, Quaternion.Euler(0f, 180f, 0f));

            PlayerController controller = player.GetComponent<PlayerController>();
            if (controller != null && NetworkServer.active)
                controller.InitializeFromCharacterData(CreateLocalCharacter(spawnPosition));

            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            if (movement != null)
                movement.InputEnabled = true;

            Debug.Log("[GameSceneDevBootstrap] Local explorer spawned. Click the terrain to move.");
        }

        private GameObject ResolvePlayerPrefab()
        {
            if (playerPrefab != null)
                return playerPrefab;

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
#else
            return null;
#endif
        }

        private GameObject[] ResolveSpawnPrefabs()
        {
            if (networkSpawnPrefabs != null && networkSpawnPrefabs.Length > 0)
                return networkSpawnPrefabs;

#if UNITY_EDITOR
            return new[]
            {
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Mob_Slime.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemy/Prefabs/Enemy_Slime.prefab")
            };
#else
            return System.Array.Empty<GameObject>();
#endif
        }

        private static Vector3 GetSpawnPosition()
        {
            Vector3 position = SpawnManager.Instance != null
                ? SpawnManager.Instance.GetSafeSpawnPoint()
                : new Vector3(0f, 0.65f, 0f);

            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
                position.y = terrain.SampleHeight(position) + terrain.transform.position.y + 0.05f;

            return position;
        }

        private static CharacterData CreateLocalCharacter(Vector3 spawnPosition)
        {
            return new CharacterData
            {
                Id = 1,
                AccountId = 1,
                Name = "Explorador Local",
                Job = 1,
                Gender = 0,
                Level = 1,
                Exp = 0,
                CurrentHp = 220,
                CurrentMp = 150,
                CurrentSp = 96,
                MaxHp = 220,
                MaxMp = 150,
                MaxSp = 96,
                BaseStr = 10,
                BaseAgi = 8,
                BaseCon = 10,
                BaseSpr = 10,
                BaseSta = 10,
                Gold = 1000,
                MapName = "garner",
                PosX = spawnPosition.x,
                PosY = spawnPosition.y,
                PosZ = spawnPosition.z,
                RotationY = 180f
            };
        }

        private static void EnsureMainCamera()
        {
            if (Camera.main != null)
                return;

            var cameraGo = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetPositionAndRotation(new Vector3(0f, 15f, -12f), Quaternion.Euler(50f, 0f, 0f));
        }

#if UNITY_EDITOR
        private static bool IsSmokeTestRunning()
        {
            if (SessionState.GetBool("TOP.RunSmokeTest", false))
                return true;

            if (SessionState.GetBool("TOP.SmokeTestActive", false))
                return true;

            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (behaviour != null && behaviour.GetType().Name == "WorldSmokeTest")
                    return true;
            }

            return false;
        }
#endif
    }
}
