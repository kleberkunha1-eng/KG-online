using UnityEngine;

namespace TOP.World
{
    /// <summary>
    /// TerrainBuilder - Cria e configura o terreno do mapa
    /// </summary>
    public class TerrainBuilder : MonoBehaviour
    {
        [Header("Terrain Settings")]
        [SerializeField] private float terrainScale = 100f;
        [SerializeField] private float terrainHeight = 1f;
        [SerializeField] private bool createPhysicsCollider = true;

        [Header("Material")]
        [SerializeField] private Material terrainMaterial;

        [Header("Spawn Point")]
        [SerializeField] private Transform playerSpawnPoint;

        /// <summary>
        /// Cria um terreno simples (plane)
        /// </summary>
        public GameObject CreateSimpleTerrain()
        {
            // Cria um plano como base
            GameObject terrainObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
            terrainObj.name = "Ground";
            terrainObj.transform.position = Vector3.zero;
            terrainObj.transform.localScale = new Vector3(terrainScale, 1, terrainScale);

            // Remove o collider padrão
            Collider defaultCollider = terrainObj.GetComponent<Collider>();
            if (defaultCollider != null)
            {
                Destroy(defaultCollider);
            }

            // Adiciona um collider próprio
            if (createPhysicsCollider)
            {
                BoxCollider collider = terrainObj.AddComponent<BoxCollider>();
                collider.center = Vector3.zero;
                collider.size = new Vector3(1, 0.2f, 1);
            }

            // Aplica material
            if (terrainMaterial != null)
            {
                terrainObj.GetComponent<Renderer>().material = terrainMaterial;
            }
            else
            {
                // Cria material padrão
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0.2f, 0.6f, 0.2f); // Verde grama
                terrainObj.GetComponent<Renderer>().material = mat;
            }

            // Layer
            terrainObj.layer = LayerMask.NameToLayer("Terrain");
            terrainObj.tag = "Ground";

            Debug.Log("[TerrainBuilder] Terreno simples criado");
            return terrainObj;
        }

        /// <summary>
        /// Cria terreno usando Unity Terrain
        /// </summary>
        public GameObject CreateUnityTerrain()
        {
            // Cria dados do terreno
            TerrainData terrainData = new TerrainData();
            terrainData.name = "GameTerrain";
            terrainData.size = new Vector3(terrainScale, terrainHeight, terrainScale);

            // Cria objeto do terreno
            GameObject terrainObj = Terrain.CreateTerrainGameObject(terrainData);
            terrainObj.name = "Terrain";
            terrainObj.transform.position = Vector3.zero;

            Terrain terrain = terrainObj.GetComponent<Terrain>();
            terrain.basemapDistance = 1000;
            terrain.castShadows = true;

            Debug.Log("[TerrainBuilder] Unity Terrain criado");
            return terrainObj;
        }

        /// <summary>
        /// Adiciona paredes invisíveis nas bordas do mapa
        /// </summary>
        public void CreateMapBoundaries()
        {
            float boundaryHeight = 10f;
            float boundaryThickness = 0.5f;

            // Parede Norte
            CreateBoundaryWall(
                new Vector3(0, boundaryHeight / 2, terrainScale / 2),
                new Vector3(terrainScale, boundaryHeight, boundaryThickness),
                "Boundary_North"
            );

            // Parede Sul
            CreateBoundaryWall(
                new Vector3(0, boundaryHeight / 2, -terrainScale / 2),
                new Vector3(terrainScale, boundaryHeight, boundaryThickness),
                "Boundary_South"
            );

            // Parede Leste
            CreateBoundaryWall(
                new Vector3(terrainScale / 2, boundaryHeight / 2, 0),
                new Vector3(boundaryThickness, boundaryHeight, terrainScale),
                "Boundary_East"
            );

            // Parede Oeste
            CreateBoundaryWall(
                new Vector3(-terrainScale / 2, boundaryHeight / 2, 0),
                new Vector3(boundaryThickness, boundaryHeight, terrainScale),
                "Boundary_West"
            );

            Debug.Log("[TerrainBuilder] Paredes de limite criadas");
        }

        private void CreateBoundaryWall(Vector3 position, Vector3 scale, string name)
        {
            GameObject wall = new GameObject(name);
            wall.transform.position = position;
            BoxCollider collider = wall.AddComponent<BoxCollider>();
            collider.size = scale;
            wall.layer = LayerMask.NameToLayer("Terrain");
        }

        /// <summary>
        /// Cria um ponto de spawn do player
        /// </summary>
        public Transform CreateSpawnPoint(Vector3 position = default)
        {
            if (position == default)
            {
                position = new Vector3(0, 1, 0);
            }

            GameObject spawnObj = new GameObject("PlayerSpawn");
            spawnObj.transform.position = position;

            // Adiciona visual (gizmo)
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(position, 0.5f);

            Debug.Log($"[TerrainBuilder] Ponto de spawn criado em {position}");
            return spawnObj.transform;
        }

        /// <summary>
        /// Aplica textura ao terreno
        /// </summary>
        public void ApplyTerrainTexture(Texture2D texture, Material material = null)
        {
            Terrain terrain = GetComponent<Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[TerrainBuilder] Nenhum Terrain encontrado!");
                return;
            }

            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
            }

            material.mainTexture = texture;
            terrain.GetComponent<Renderer>().material = material;

            Debug.Log("[TerrainBuilder] Textura aplicada");
        }

        /// <summary>
        /// Levanta pontos do terreno para criar colinas
        /// </summary>
        public void RaiseTerrain(Vector3 center, float radius, float strength, int samples = 10)
        {
            Terrain terrain = GetComponent<Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[TerrainBuilder] Nenhum Terrain encontrado!");
                return;
            }

            TerrainData terrainData = terrain.terrainData;
            if (terrainData == null) return;

            // Algoritmo simples de elevação
            for (int i = 0; i < samples; i++)
            {
                // Implementar elevação do terreno
            }

            Debug.Log("[TerrainBuilder] Terreno elevado");
        }
    }
}
