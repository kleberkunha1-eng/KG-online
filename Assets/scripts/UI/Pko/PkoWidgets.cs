using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TOP.UI.Pko
{
    public class PkoTab : MonoBehaviour, IPointerClickHandler
    {
        public PkoWindow Window; public int Page, Key; public RawImage Image; public Rect Active, Normal;
        public void OnPointerClick(PointerEventData e) { Window.ShowTab(Page, Key); }
        public void SetActive(bool on) { Image.uvRect = on ? Active : Normal; }
    }

    /// <summary>Shrinks a label's font until the text fits its original width (Latin glyphs are wider than the original bitmap font).</summary>
    [RequireComponent(typeof(Text))]
    public class PkoFitText : MonoBehaviour
    {
        public int BaseSize = 11, MinSize = 7; public float MaxWidth;
        Text t; string last; int lastSize;
        void Awake() { t = GetComponent<Text>(); }
        void LateUpdate()
        {
            if (t == null || MaxWidth <= 0) return;
            if (t.text == last && t.fontSize == lastSize) return;
            int size = BaseSize; t.fontSize = size;
            while (size > MinSize && t.preferredWidth > MaxWidth) { size--; t.fontSize = size; }
            last = t.text; lastSize = t.fontSize;
        }
    }
    public class PkoClick : MonoBehaviour, IPointerClickHandler, IDragHandler
    {
        public Action<int> Clicked;
        public Action<float> Dragged; // deslocamento horizontal em pixels
        public void OnPointerClick(PointerEventData e) { Clicked?.Invoke(e.clickCount); }
        public void OnDrag(PointerEventData e) { Dragged?.Invoke(e.delta.x); }
    }

    public class PkoProgress : MonoBehaviour
    {
        public RectTransform Clip; public float Width, Height; public Text Label;
        public void Set(float v, string text = null)
        {
            Clip.sizeDelta = new Vector2(Width * Mathf.Clamp01(v), Height);
            if (Label != null) Label.text = text ?? "";
        }
    }

    public class PkoLog : MonoBehaviour
    {
        public Text Body; public ScrollRect Scroll; readonly List<string> lines = new List<string>(); public int MaxLines = 200;
        public void Clear() { lines.Clear(); Body.text = ""; }
        public void Add(string line)
        {
            lines.Add(line); if (lines.Count > MaxLines) lines.RemoveAt(0);
            Body.text = string.Join("\n", lines);
            Canvas.ForceUpdateCanvases(); if (Scroll != null) Scroll.verticalNormalizedPosition = 0f;
        }
    }

    public class PkoCombo : MonoBehaviour, IPointerClickHandler
    {
        public Text Label; public string[] Items = new string[0]; public int Index; public Action<int> Changed;
        public void Set(int i) { Index = Items.Length == 0 ? 0 : (i % Items.Length + Items.Length) % Items.Length; if (Label != null && Items.Length > 0) Label.text = Items[Index]; }
        public void OnPointerClick(PointerEventData e) { Set(Index + 1); Changed?.Invoke(Index); }
    }

    public class PkoSlot : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public string Group; public int Index; public RawImage Icon; public Text Count;
        public Func<string> Tip; public Action<PkoSlot, int, int> Clicked; public Action<PkoSlot, PkoSlot> Dropped;
        public bool Filled; public PkoWindow Window; public bool Draggable = true;
        GameObject ghost;

        public void SetIcon(Texture tex, string count = null, bool filled = false)
        {
            Filled = filled || tex != null; Icon.texture = tex; Icon.enabled = tex != null;
            Count.text = Filled && !string.IsNullOrEmpty(count) ? count : "";
        }

        public void OnPointerClick(PointerEventData e) { Clicked?.Invoke(this, (int)e.button, e.clickCount); }
        public void OnPointerEnter(PointerEventData e) { var t = Tip?.Invoke(); if (!string.IsNullOrEmpty(t)) PkoUi.Instance.ShowTip(t); }
        public void OnPointerExit(PointerEventData e) { PkoUi.Instance.HideTip(); }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!Filled || !Draggable) { Window?.OnBeginDrag(e); return; }
            ghost = new GameObject("Ghost", typeof(RectTransform), typeof(RawImage));
            ghost.transform.SetParent(PkoUi.Instance.Canvas.transform, false);
            var r = ghost.GetComponent<RawImage>(); r.texture = Icon.texture; r.raycastTarget = false;
            ((RectTransform)ghost.transform).sizeDelta = new Vector2(32, 32);
            ghost.transform.position = e.position;
        }

        public void OnDrag(PointerEventData e)
        {
            if (ghost != null) ghost.transform.position = e.position; else if (!Filled || !Draggable) Window?.OnDrag(e);
        }

        public void OnEndDrag(PointerEventData e) { if (ghost != null) Destroy(ghost); ghost = null; }

        public void OnDrop(PointerEventData e)
        {
            var from = e.pointerDrag != null ? e.pointerDrag.GetComponent<PkoSlot>() : null;
            if (from != null && from != this) Dropped?.Invoke(from, this);
        }
    }

    public class PkoWindow : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        public PkoForm Def; public RectTransform Rect;
        public readonly Dictionary<string, RectTransform> Named = new Dictionary<string, RectTransform>();
        public readonly HashSet<string> BoundButtons = new HashSet<string>();
        public readonly Dictionary<string, List<PkoSlot>> Grids = new Dictionary<string, List<PkoSlot>>();
        // Alguns formularios (ex.: frmGame) reutilizam o mesmo nome de componente em varios grupos
        // de checkbox (ex.: "chkHelpmodel1"/"chkHelpmodel2" repetidos 9x). Named so guarda a 1a
        // ocorrencia de cada nome, entao os demais toggles so sao endercaveis pelo id do grupo.
        public readonly Dictionary<int, List<Toggle>> Groups = new Dictionary<int, List<Toggle>>();
        public readonly List<PkoTab> Tabs = new List<PkoTab>();
        public readonly Dictionary<long, List<GameObject>> PageContent = new Dictionary<long, List<GameObject>>();
        public event Action<PkoWindow> Opened;
        public event Action<PkoWindow, int, int> TabChanged;
        public bool Draggable;

        public bool IsOpen => gameObject.activeSelf;

        public void Open() { gameObject.SetActive(true); transform.SetAsLastSibling(); Opened?.Invoke(this); }
        public void Close() { gameObject.SetActive(false); PkoUi.Instance.HideTip(); }
        public void Toggle() { if (IsOpen) Close(); else Open(); }
        public void CenterOnScreen() { Rect.anchorMin = Rect.anchorMax = Rect.pivot = new Vector2(.5f, .5f); Rect.anchoredPosition = Vector2.zero; }

        public T Get<T>(string name) where T : Component { return Named.TryGetValue(name, out var rt) ? rt.GetComponent<T>() : null; }
        public Text Label(string name) { return Get<Text>(name); }
        public void SetText(string name, string text) { var t = Label(name); if (t != null) t.text = text; }
        public void SetProgress(string name, float v, string text = null) { Get<PkoProgress>(name)?.Set(v, text); }
        public void OnClick(string name, UnityEngine.Events.UnityAction a)
        {
            var b = Get<Button>(name);
            if (b == null) return;
            BoundButtons.Add(name);
            b.onClick.AddListener(a);
        }
        public void SetVisible(string name, bool on) { if (Named.TryGetValue(name, out var rt)) rt.gameObject.SetActive(on); }
        /// <summary>Retorna o toggle de indice "idx" (0, 1, ...) dentro de um grupo CHECK_GROUP_TYPE.</summary>
        public Toggle GroupToggle(int group, int idx) { return Groups.TryGetValue(group, out var list) && idx >= 0 && idx < list.Count ? list[idx] : null; }

        public void ShowTab(int page, int key)
        {
            foreach (var kv in PageContent) if ((int)(kv.Key >> 32) == page) foreach (var go in kv.Value) go.SetActive((int)(kv.Key & 0xffffffff) == key);
            foreach (var t in Tabs) if (t.Page == page) t.SetActive(t.Key == key);
            TabChanged?.Invoke(this, page, key);
        }

        public void OnPointerDown(PointerEventData e) { transform.SetAsLastSibling(); }
        public void OnBeginDrag(PointerEventData e) { }
        public void OnDrag(PointerEventData e)
        {
            if (!Draggable) return;
            Rect.anchoredPosition += e.delta / PkoUi.Instance.Canvas.scaleFactor;
        }
    }
}
