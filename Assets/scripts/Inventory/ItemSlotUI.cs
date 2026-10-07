using UnityEngine;
using UnityEngine.UI;
using TOP.Core;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using TOP.Inventory;  // ✅ ItemDatabase

namespace TOP.Inventory
{
    public class ItemSlotUI : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("UI Elements")]
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI quantityText;
        [SerializeField] private Image highlightImage;

        [Header("Cores")]
        [SerializeField] private Color normalColor = new Color(1, 1, 1, 0.8f);
        [SerializeField] private Color hoverColor = new Color(1, 1, 1, 1);
        [SerializeField] private Color selectedColor = new Color(0.3f, 0.7f, 1f, 1f);
        [SerializeField] private Color dragColor = new Color(1, 1, 1, 0.3f);

        private int slotIndex;
        private InventoryUI inventoryUI;
        private bool isDragging = false;
        private ItemData currentItem;
        private Vector3 originalIconPosition;
        private Transform originalIconParent;
        private CanvasGroup iconCanvasGroup;

        // ✅ Double-click detection
        private float lastClickTime = -1f;
        private const float DOUBLE_CLICK_THRESHOLD = 0.3f;

        public int SlotIndex => slotIndex;
        public Image IconImage => iconImage;
        public bool HasItem => currentItem != null;

        public void Initialize(int index, InventoryUI ui)
        {
            slotIndex = index;
            inventoryUI = ui;

            Debug.Log($"[ItemSlotUI] Slot {index} inicializado");

            if (iconImage != null)
            {
                iconCanvasGroup = iconImage.GetComponent<CanvasGroup>();
                if (iconCanvasGroup == null)
                    iconCanvasGroup = iconImage.gameObject.AddComponent<CanvasGroup>();
                originalIconParent = iconImage.transform.parent;
                originalIconPosition = iconImage.transform.localPosition;
            }
        }

        // ✅ CORRIGIDO: Recebe ItemData (não só EquipmentData)
        public void SetItem(ItemData item, int quantity, int durability)
        {
            if (item == null || iconImage == null)
            {
                Clear();
                return;
            }

            currentItem = item;
            Debug.Log($"[ItemSlotUI] Slot {slotIndex}: SetItem({item.itemName}, qty:{quantity})");

            iconImage.sprite = item.icon;
            iconImage.color = item.icon != null ? Color.white : Color.clear;
            iconImage.enabled = item.icon != null;
            iconImage.raycastTarget = true;

            if (quantityText != null)
            {
                if (quantity > 1)
                {
                    quantityText.text = quantity.ToString();
                    quantityText.gameObject.SetActive(true);
                }
                else
                {
                    quantityText.gameObject.SetActive(false);
                }
            }
        }

        public void Clear()
        {
            currentItem = null;
            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.color = Color.clear;
                iconImage.enabled = false;
                iconImage.raycastTarget = false;
            }
            if (quantityText != null)
                quantityText.gameObject.SetActive(false);
        }

        public void SetSelected(bool selected)
        {
            if (backgroundImage != null)
                backgroundImage.color = selected ? selectedColor : normalColor;
        }

        public void SetDragging(bool dragging)
        {
            if (backgroundImage != null)
                backgroundImage.color = dragging ? dragColor : normalColor;
            if (iconCanvasGroup != null)
                iconCanvasGroup.alpha = dragging ? 0.5f : 1f;
        }

        #region Pointer Events

        public void OnPointerClick(PointerEventData eventData)
        {
            float timeSinceLastClick = Time.time - lastClickTime;
            lastClickTime = Time.time;

            bool isDoubleClick = (timeSinceLastClick <= DOUBLE_CLICK_THRESHOLD) && HasItem;

            Debug.Log($"[ItemSlotUI] Click slot {slotIndex}, double: {isDoubleClick}");

            if (isDoubleClick)
            {
                inventoryUI?.OnSlotDoubleClick(slotIndex);
            }
            else
            {
                inventoryUI?.OnSlotSelected(slotIndex);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            Debug.Log($"[ItemSlotUI] OnBeginDrag slot {slotIndex}");

            if (!HasItem || inventoryUI == null) return;

            isDragging = true;
            originalIconParent = iconImage.transform.parent;
            originalIconPosition = iconImage.transform.localPosition;

            if (inventoryUI.DragCanvas != null)
            {
                iconImage.transform.SetParent(inventoryUI.DragCanvas.transform);
            }
            else
            {
                iconImage.transform.SetParent(transform.root);
            }

            iconImage.transform.SetAsLastSibling();
            iconImage.raycastTarget = false;
            SetDragging(true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDragging) return;
            iconImage.transform.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!isDragging) return;
            isDragging = false;

            Debug.Log($"[ItemSlotUI] OnEndDrag slot {slotIndex}");

            iconImage.transform.SetParent(originalIconParent);
            iconImage.transform.localPosition = originalIconPosition;
            iconImage.raycastTarget = true;
            SetDragging(false);

            // ✅ Detecta EquipmentSlot ou outro ItemSlot
            var equipSlot = GetEquipmentSlotUnderMouse(eventData);
            if (equipSlot != null)
            {
                inventoryUI?.OnSlotDraggedToEquipment(slotIndex, equipSlot.SlotType);
                return;
            }

            var otherSlot = GetSlotUnderMouse(eventData);
            if (otherSlot != null && otherSlot != this)
            {
                inventoryUI?.OnSlotDropped(slotIndex, otherSlot.slotIndex);
                return;
            }
        }

        public void OnDrop(PointerEventData eventData)
        {
            var draggedEquip = eventData.pointerDrag?.GetComponent<EquipmentSlotUI>();
            if (draggedEquip != null)
            {
                inventoryUI?.OnEquipmentDraggedToInventory(draggedEquip.SlotType, slotIndex);
            }
        }

        // ✅ TOOLTIP ADICIONADO
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!HasItem || inventoryUI == null) return;

            if (backgroundImage != null)
                backgroundImage.color = hoverColor;

            // ✅ Mostra tooltip
            Vector3 tooltipPos = iconImage.transform.position;
            ItemData itemData = ItemDatabase.Instance?.GetItem(GetCurrentItemId());
            inventoryUI.ShowTooltip(itemData, tooltipPos);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!isDragging && backgroundImage != null)
                backgroundImage.color = normalColor;

            // ✅ Esconde tooltip
            inventoryUI?.HideTooltip();
        }

        #endregion

        #region Helpers

        EquipmentSlotUI GetEquipmentSlotUnderMouse(PointerEventData eventData)
        {
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            foreach (var result in results)
            {
                var equipSlot = result.gameObject.GetComponent<EquipmentSlotUI>();
                if (equipSlot != null) return equipSlot;
            }
            return null;
        }

        ItemSlotUI GetSlotUnderMouse(PointerEventData eventData)
        {
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            foreach (var result in results)
            {
                var slot = result.gameObject.GetComponent<ItemSlotUI>();
                if (slot != null) return slot;
            }
            return null;
        }

        // ✅ Helper para tooltip
        private int GetCurrentItemId()
        {
            // Recupera do inventário atual
            return inventoryUI != null ? 
                inventoryUI.playerInventory?.GetSlot((ushort)slotIndex)?.ItemId ?? 0 : 0;
        }

        #endregion
    }
}
