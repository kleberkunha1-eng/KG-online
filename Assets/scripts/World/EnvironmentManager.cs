using UnityEngine;
using System.Collections.Generic;

namespace TOP.World
{
    /// <summary>
    /// EnvironmentManager - Gerencia decorações e props do mundo
    /// </summary>
    public class EnvironmentManager : MonoBehaviour
    {
        [System.Serializable]
        public class EnvironmentProp
        {
            public string name;
            public GameObject prefab;
            public int count;
            public float minDistance = 2f;
            public float maxDistance = 50f;
        }

        [SerializeField] private List<EnvironmentProp> props = new List<EnvironmentProp>();
        [SerializeField] private float mapSize = 100f;
        [SerializeField] private int randomSeed = 12345;

        private List<Vector3> _usedPositions = new List<Vector3>();

        void Start()
        {
            Random.InitState(randomSeed);
            SpawnEnvironmentProps();
        }

        /// <summary>
        /// Spawna todos os props do ambiente
        /// </summary>
        public void SpawnEnvironmentProps()
        {
            if (props.Count == 0)
            {
                Debug.LogWarning("[EnvironmentManager] Nenhum prop definido!");
                return;
            }

            foreach (var prop in props)
            {
                if (prop.prefab == null)
                {
                    Debug.LogWarning($"[EnvironmentManager] Prefab não definido para {prop.name}");
                    continue;
                }

                for (int i = 0; i < prop.count; i++)
                {
                    Vector3 position = GetRandomPosition(prop.minDistance, prop.maxDistance);
                    GameObject instance = Instantiate(prop.prefab, position, Quaternion.identity, transform);
                    instance.name = $"{prop.name}_{i}";

                    // Rotação aleatória
                    instance.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                }
            }

            Debug.Log($"[EnvironmentManager] {CalculateTotalProps()} props spawnados");
        }

        /// <summary>
        /// Encontra posição aleatória que não sobrepõe outros objetos
        /// </summary>
        private Vector3 GetRandomPosition(float minDistance, float maxDistance)
        {
            Vector3 position;
            bool validPosition = false;
            int maxAttempts = 10;
            int attempts = 0;

            do
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float distance = Random.Range(minDistance, maxDistance);

                position = transform.position + new Vector3(
                    Mathf.Cos(angle) * distance,
                    0,
                    Mathf.Sin(angle) * distance
                );

                // Checa se é válido (não sobrepõe outros objetos)
                validPosition = IsPositionValid(position, minDistance * 0.8f);
                attempts++;

            } while (!validPosition && attempts < maxAttempts);

            _usedPositions.Add(position);
            return position;
        }

        /// <summary>
        /// Verifica se posição não sobrepõe outros objetos
        /// </summary>
        private bool IsPositionValid(Vector3 position, float checkRadius)
        {
            // Verifica raycast para chão
            if (!Physics.Raycast(position + Vector3.up * 10, Vector3.down, 20f))
            {
                return false; // Não tem chão
            }

            // Verifica se tem espaço livre
            Collider[] colliders = Physics.OverlapSphere(position, checkRadius);
            return colliders.Length == 0;
        }

        /// <summary>
        /// Limpa todos os props
        /// </summary>
        public void ClearAllProps()
        {
            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }
            _usedPositions.Clear();
            Debug.Log("[EnvironmentManager] Todos os props removidos");
        }

        private int CalculateTotalProps()
        {
            int total = 0;
            foreach (var prop in props)
            {
                total += prop.count;
            }
            return total;
        }

        /// <summary>
        /// Adiciona novo tipo de prop
        /// </summary>
        public void AddPropType(string name, GameObject prefab, int count)
        {
            props.Add(new EnvironmentProp
            {
                name = name,
                prefab = prefab,
                count = count
            });
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, Vector3.one * mapSize);

            Gizmos.color = Color.cyan;
            foreach (Vector3 pos in _usedPositions)
            {
                Gizmos.DrawWireSphere(pos, 0.3f);
            }
        }
    }
}
