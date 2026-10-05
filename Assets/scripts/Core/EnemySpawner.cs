using UnityEngine;
using Mirror;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

namespace TOP.Core
{
    /// <summary>
    /// EnemySpawner - Gerencia spawn de inimigos no servidor
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [SerializeField] private GameObject enemyPrefab;
        [SerializeField] private int maxEnemiesPerSpawner = 5;
        [SerializeField] private float spawnInterval = 10f;
        [SerializeField] private float spawnRadius = 5f;

        [Header("Spawn Area")]
        [SerializeField] private Transform spawnCenter;

        [Header("Debug")]
        [SerializeField] private bool showSpawnArea = true;
        [SerializeField] private Color spawnAreaColor = Color.red;

        private List<GameObject> _activeEnemies = new List<GameObject>();
        private float _lastSpawnTime;
        private int _enemyCounter = 0;

        void Start()
        {
            if (!NetworkServer.active) return;

            if (spawnCenter == null)
            {
                spawnCenter = transform;
            }

            _lastSpawnTime = Time.time;
            Debug.Log($"[EnemySpawner] Iniciado em {spawnCenter.position}");
        }

        void Update()
        {
            if (!NetworkServer.active) return;

            // Limpa inimigos mortos da lista
            _activeEnemies.RemoveAll(enemy => enemy == null);

            // Spawna novo inimigo se intervalo passou e há espaço
            if (Time.time >= _lastSpawnTime + spawnInterval && _activeEnemies.Count < maxEnemiesPerSpawner)
            {
                SpawnEnemy();
                _lastSpawnTime = Time.time;
            }
        }

        void SpawnEnemy()
        {
            if (enemyPrefab == null)
            {
                Debug.LogError("[EnemySpawner] Enemy Prefab não definido!");
                return;
            }

            // Posição aleatória dentro do raio de spawn
            Vector3 randomOffset = Random.insideUnitSphere * spawnRadius;
            randomOffset.y = 0; // Mantém no chão
            Vector3 spawnPosition = (spawnCenter != null ? spawnCenter.position : transform.position) + randomOffset;

            // Verifica se há chão nessa posição
            if (Physics.Raycast(spawnPosition + Vector3.up * 10, Vector3.down, out RaycastHit hit, 20f))
            {
                spawnPosition = hit.point;
            }

            if (!NavMesh.SamplePosition(spawnPosition, out var navHit, 5f, NavMesh.AllAreas)) return;
            spawnPosition = navHit.position;

            // Instancia o inimigo
            GameObject enemy = Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
            NetworkServer.Spawn(enemy);
            _activeEnemies.Add(enemy);

            _enemyCounter++;
            Debug.Log($"[EnemySpawner] Inimigo #{_enemyCounter} spawned em {spawnPosition}. Total: {_activeEnemies.Count}/{maxEnemiesPerSpawner}");
        }

        /// <summary>
        /// Remove inimigo da lista ativa (chamado quando morre)
        /// </summary>
        public void RemoveEnemy(GameObject enemy)
        {
            _activeEnemies.Remove(enemy);
            Debug.Log($"[EnemySpawner] Inimigo removido. Restam: {_activeEnemies.Count}/{maxEnemiesPerSpawner}");
        }

        /// <summary>
        /// Retorna quantidade de inimigos ativos
        /// </summary>
        public int GetActiveEnemyCount()
        {
            return _activeEnemies.Count;
        }

        void OnDrawGizmosSelected()
        {
            if (!showSpawnArea) return;

            Transform center = spawnCenter != null ? spawnCenter : transform;

            // Desenha esfera de spawn
            Gizmos.color = spawnAreaColor;
            Gizmos.DrawWireSphere(center.position, spawnRadius);

            // Marca o centro
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(center.position, Vector3.one * 0.5f);
        }
    }
}
