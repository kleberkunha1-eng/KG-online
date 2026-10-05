using UnityEngine;
using Mirror;
using TOP.Player;
using System.Collections.Generic;

namespace TOP.Core
{
    /// <summary>
    /// SpawnManager - Gerencia pontos de spawn e instancia jogadores
    /// </summary>
    public class SpawnManager : MonoBehaviour
    {
        public static SpawnManager Instance { get; private set; }

        [Header("Spawn Settings")]
        [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
        [SerializeField] private float minDistanceBetweenPlayers = 2f;

        [Header("Debug")]
        [SerializeField] private bool showSpawnPoints = true;

        private int _lastSpawnIndex = 0;
        private NetworkManager _networkManager;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            _networkManager = FindObjectOfType<NetworkManager>();

            // Se não há spawn points definidos, cria um padrão
            if (spawnPoints.Count == 0)
            {
                Debug.LogWarning("[SpawnManager] Nenhum spawn point definido! Usando posição padrão (0, 1, 0)");
                Transform defaultSpawn = new GameObject("DefaultSpawn").transform;
                defaultSpawn.position = new Vector3(0, 1, 0);
                spawnPoints.Add(defaultSpawn);
            }

            Debug.Log($"[SpawnManager] {spawnPoints.Count} spawn points carregados");
        }

        /// <summary>
        /// Retorna um spawn point seguro para novo jogador
        /// </summary>
        public Vector3 GetSafeSpawnPoint()
        {
            // Tenta encontrar um spawn point que não tenha jogadores muito perto
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                Transform spawn = spawnPoints[_lastSpawnIndex];
                _lastSpawnIndex = (_lastSpawnIndex + 1) % spawnPoints.Count;

                if (IsSpawnPointSafe(spawn.position))
                {
                    return spawn.position;
                }
            }

            // Se nenhum está seguro, retorna o próximo mesmo assim
            Debug.LogWarning("[SpawnManager] Todos os spawn points estão ocupados!");
            return spawnPoints[_lastSpawnIndex].position;
        }

        /// <summary>
        /// Verifica se há jogadores muito perto do ponto de spawn
        /// </summary>
        private bool IsSpawnPointSafe(Vector3 spawnPosition)
        {
            Collider[] nearbyColliders = Physics.OverlapSphere(spawnPosition, minDistanceBetweenPlayers);
            foreach (Collider col in nearbyColliders)
            {
                if (col.GetComponent<PlayerController>() != null)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Adiciona um novo spawn point manualmente
        /// </summary>
        public void AddSpawnPoint(Vector3 position)
        {
            Transform spawn = new GameObject($"Spawn_{spawnPoints.Count}").transform;
            spawn.position = position;
            spawn.parent = transform;
            spawnPoints.Add(spawn);
            Debug.Log($"[SpawnManager] Novo spawn point adicionado em {position}");
        }

        /// <summary>
        /// Adiciona um novo spawn point a partir de um Transform
        /// </summary>
        public void AddSpawnPoint(Transform spawnTransform)
        {
            spawnTransform.parent = transform;
            spawnPoints.Add(spawnTransform);
            Debug.Log($"[SpawnManager] Spawn point '{spawnTransform.name}' adicionado");
        }

        void OnDrawGizmosSelected()
        {
            if (!showSpawnPoints) return;

            Gizmos.color = Color.green;
            foreach (Transform spawn in spawnPoints)
            {
                if (spawn != null)
                {
                    Gizmos.DrawWireSphere(spawn.position, 0.5f);
                    Gizmos.DrawLine(spawn.position, spawn.position + Vector3.up);
                }
            }
        }
    }
}
