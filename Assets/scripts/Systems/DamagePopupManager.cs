using UnityEngine;
using TMPro;
using System.Collections.Generic;

namespace TOP.Systems
{

    public class DamagePopupManager : MonoBehaviour
    {
        public static DamagePopupManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private GameObject damagePopupPrefab;
        [SerializeField] private Transform canvasTransform;
        [SerializeField] private int poolSize = 50;
        [SerializeField] private float popupDuration = 1.5f;
        [SerializeField] private float popupSpeed = 2f;
        [SerializeField] private float popupSpread = 0.5f;

        private Queue<GameObject> popupPool = new Queue<GameObject>();
        private List<ActivePopup> activePopups = new List<ActivePopup>();
        private Camera mainCamera;

        [System.Serializable]
        private class ActivePopup
        {
            public GameObject gameObject;
            public TextMeshProUGUI textMesh;
            public float startTime;
            public Vector3 startPosition;
            public Vector3 velocity;
            public Color color;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;


            mainCamera = Camera.main;

            if (canvasTransform == null)
            {
                Canvas canvas = FindAnyObjectByType<Canvas>();
                if (canvas != null)
                    canvasTransform = canvas.transform;
            }

            InitializePool();
        }

        private void InitializePool()
        {
            if (damagePopupPrefab == null)
            {
                damagePopupPrefab = CreateDefaultPopupPrefab();
            }

            for (int i = 0; i < poolSize; i++)
            {
                GameObject popup = Instantiate(damagePopupPrefab, canvasTransform);
                popup.SetActive(false);
                popupPool.Enqueue(popup);
            }
        }

        // 🔧 CORREÇÃO: Criar prefab com TextMeshProUGUI para Canvas
        private GameObject CreateDefaultPopupPrefab()
        {
            GameObject go = new GameObject("DamagePopup");
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200, 50);

            TextMeshProUGUI textMesh = go.AddComponent<TextMeshProUGUI>();
            textMesh.fontSize = 36;
            textMesh.alignment = TextAlignmentOptions.Center;
            textMesh.color = Color.white;

            go.SetActive(false);
            return go;
        }

        void Update()
        {
            for (int i = activePopups.Count - 1; i >= 0; i--)
            {
                var popup = activePopups[i];
                float elapsed = Time.time - popup.startTime;

                if (elapsed >= popupDuration)
                {
                    ReturnPopup(popup);
                    activePopups.RemoveAt(i);
                    continue;
                }

                float t = elapsed / popupDuration;

                // 🔧 CORREÇÃO: Mover em screen space (Canvas)
                popup.gameObject.transform.position += popup.velocity * Time.deltaTime;

                float alpha = 1f - Mathf.Pow(t, 2f);
                popup.textMesh.color = new Color(popup.color.r, popup.color.g, popup.color.b, alpha);

                float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.5f;
                popup.gameObject.transform.localScale = Vector3.one * scale;
            }
        }

        // 🔧 CORREÇÃO: Todos os métodos agora usam TextMeshProUGUI
        public void ShowDamage(Vector3 worldPosition, int damage, bool isCritical = false)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;
            GameObject popup = GetPopup();
            if (popup == null) return;

            TextMeshProUGUI textMesh = popup.GetComponent<TextMeshProUGUI>();
            textMesh.text = damage.ToString();

            Color color = isCritical ? new Color(1f, 0.3f, 0f) : Color.white;
            if (damage <= 0) color = Color.gray;
            textMesh.color = color;

            // Converter world position para screen position
            Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPosition);
            Vector3 offset = new Vector3(
                Random.Range(-popupSpread * 100, popupSpread * 100),
                Random.Range(-popupSpread * 50, popupSpread * 50),
                0
            );

            popup.transform.position = screenPos + offset;
            popup.SetActive(true);

            activePopups.Add(new ActivePopup
            {
                gameObject = popup,
                textMesh = textMesh,
                startTime = Time.time,
                startPosition = screenPos + offset,
                velocity = new Vector3(Random.Range(-0.5f, 0.5f), popupSpeed * 50f, 0),
                color = color
            });
        }

        public void ShowHeal(Vector3 worldPosition, int amount)
        {
            GameObject popup = GetPopup();
            if (popup == null) return;

            TextMeshProUGUI textMesh = popup.GetComponent<TextMeshProUGUI>();
            textMesh.text = "+" + amount;
            textMesh.color = Color.green;

            Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPosition);
            Vector3 offset = new Vector3(
                Random.Range(-popupSpread * 100, popupSpread * 100),
                0,
                0
            );

            popup.transform.position = screenPos + offset;
            popup.SetActive(true);

            activePopups.Add(new ActivePopup
            {
                gameObject = popup,
                textMesh = textMesh,
                startTime = Time.time,
                startPosition = screenPos + offset,
                velocity = new Vector3(0, popupSpeed * 40f, 0),
                color = Color.green
            });
        }

        public void ShowText(Vector3 worldPosition, string text, Color color)
        {
            GameObject popup = GetPopup();
            if (popup == null) return;

            TextMeshProUGUI textMesh = popup.GetComponent<TextMeshProUGUI>();
            textMesh.text = text;
            textMesh.color = color;

            Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPosition);
            popup.transform.position = screenPos;
            popup.SetActive(true);

            activePopups.Add(new ActivePopup
            {
                gameObject = popup,
                textMesh = textMesh,
                startTime = Time.time,
                startPosition = screenPos,
                velocity = new Vector3(0, popupSpeed * 25f, 0),
                color = color
            });
        }

        // 🔧 NOVO: Métodos que PlayerCombat espera
        public void ShowCritical(Vector3 worldPosition, int damage)
        {
            ShowDamage(worldPosition, damage, true);
        }

        public void ShowMiss(Vector3 worldPosition)
        {
            ShowText(worldPosition, "MISS", new Color(0.9f, 0.9f, 0.9f, 1f));
        }

        private GameObject GetPopup()
        {
            if (popupPool.Count > 0)
                return popupPool.Dequeue();

            if (activePopups.Count > 0)
            {
                var oldest = activePopups[0];
                activePopups.RemoveAt(0);
                return oldest.gameObject;
            }

            return null;
        }

        private void ReturnPopup(ActivePopup popup)
        {
            popup.gameObject.SetActive(false);
            popupPool.Enqueue(popup.gameObject);
        }
    }
}
