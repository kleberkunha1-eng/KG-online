using UnityEngine;
using Mirror;
using TOP.Core;
using TOP.Inventory;
using System.Collections;
using TOP.Player;

namespace TOP.Systems
{
    public class WorldItem : NetworkBehaviour
    {
        [SyncVar] private int itemId;
        [SyncVar] private int quantity;
        [SyncVar] private float despawnTime;

        private ItemData itemData;
        private GameObject visualModel;
        private float spawnTime;
        private bool isPickedUp;
        private bool visualCreated = false;

        public int ItemId => itemId;
        public int Quantity => quantity;

        [Server]
        public void Initialize(ItemData item, int qty, float despawnDuration = 120f)
        {
            itemId = item.itemId;
            quantity = qty;
            despawnTime = Time.time + despawnDuration;
            spawnTime = Time.time;

            RpcCreateVisual(item.itemId);
        }

        [ClientRpc]
        private void RpcCreateVisual(int id)
        {
            if (ItemDatabase.Instance == null)
            {
                StartCoroutine(WaitForDatabase(id));
                return;
            }

            CreateVisual(id);
        }

        private IEnumerator WaitForDatabase(int id)
        {
            float timeout = 5f;
            float elapsed = 0f;

            while (ItemDatabase.Instance == null && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (ItemDatabase.Instance != null)
            {
                CreateVisual(id);
            }
        }

        private void CreateVisual(int id)
        {
            if (visualCreated) return;

            itemData = ItemDatabase.Instance?.GetItem(id);
            if (itemData == null)
            {
                Debug.LogError($"[WorldItem] Item ID {id} não encontrado!");
                return;
            }

            if (itemData.worldModelPrefab != null)
            {
                visualModel = Instantiate(itemData.worldModelPrefab, transform);
                visualModel.transform.localRotation = Quaternion.Euler(itemData.dropRotation);
                visualModel.transform.localScale = itemData.dropScale;
            }
            else
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(transform);
                cube.transform.localPosition = Vector3.zero;
                cube.transform.localScale = Vector3.one * 0.3f;
                Destroy(cube.GetComponent<Collider>());
                visualModel = cube;
            }

            if (GetComponent<Collider>() == null)
            {
                SphereCollider col = gameObject.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 0.5f;
            }

            if (GetComponent<Rigidbody>() == null)
            {
                Rigidbody rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
            }

            visualCreated = true;
            StartCoroutine(FloatAnimation());
        }

        private IEnumerator FloatAnimation()
        {
                float offset = UnityEngine.Random.Range(0f, 2f);
                while (visualModel != null)
                {
                    float y = Mathf.Sin((Time.time - spawnTime) * 2f + offset) * 0.2f;
                    visualModel.transform.localPosition = new Vector3(0, y, 0);
                    visualModel.transform.Rotate(Vector3.up, 50f * Time.deltaTime);
                    yield return null;
                }
        }

        void Update()
        {
            if (!isServer) return;

            if (Time.time >= despawnTime && !isPickedUp)
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        [Server]
        public void Pickup(PlayerInventory inventory)
        {
            if (isPickedUp || inventory == null) return;

            isPickedUp = true;

            bool success = inventory.AddItem(itemId, quantity);
            if (success)
            {
                RpcPickupSuccess();
                WorldItemManager.Instance?.RemoveWorldItem(netId);
            }
            else
            {
                isPickedUp = false;
                TargetInventoryFull(inventory.connectionToClient);
            }
        }

        [ClientRpc]
        private void RpcPickupSuccess()
        {
            if (itemData != null)
            {
                Debug.Log($"<color=yellow>+{quantity} {itemData.itemName}</color>");
            }
        }

        [TargetRpc]
        private void TargetInventoryFull(NetworkConnectionToClient target)
        {
            Debug.Log("<color=red>Inventário Cheio!</color>");
        }

        void OnTriggerEnter(Collider other)
        {
            if (!isServer) return;

            if (other.TryGetComponent(out PlayerInventory inventory))
            {
                Pickup(inventory);
            }
        }

        [Command]
        public void CmdPickup()
        {
            PlayerInventory inv = connectionToClient.identity.GetComponent<PlayerInventory>();
            if (inv != null)
                Pickup(inv);
        }
    }
}