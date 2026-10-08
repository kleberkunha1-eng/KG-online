using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TOP.UI.Pko
{
    // Constroi em runtime as janelas originais (forms.json extraido dos .clu do cliente).
    public class PkoUi : MonoBehaviour
    {
        public static PkoUi Instance { get; private set; }
        public const float RefW = 800f, RefH = 600f;

        public Canvas Canvas;
        public readonly Dictionary<string, PkoForm> Defs = new Dictionary<string, PkoForm>();
        public readonly Dictionary<string, PkoWindow> Windows = new Dictionary<string, PkoWindow>();
        static readonly Dictionary<KeyCode, Func<bool>> GameplayShortcuts = new Dictionary<KeyCode, Func<bool>>
        {
            { KeyCode.G, TOP.UI.GuildUI.ToggleLocal },
            { KeyCode.N, TOP.UI.FriendsUI.ToggleLocal },
            { KeyCode.M, TOP.UI.MailUI.ToggleLocal },
            { KeyCode.J, TOP.UI.QuestLogUI.ToggleLocal },
            { KeyCode.Y, TOP.UI.TradeUI.ToggleLocal }
        };
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        Font font; GameObject tip; Text tipText; RectTransform tipRect;

        public static PkoUi Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PkoUi"); DontDestroyOnLoad(go);
            return go.AddComponent<PkoUi>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var asset = Resources.Load<TextAsset>("PKOUI/forms");
            if (asset != null)
            {
                var all = JsonUtility.FromJson<PkoFormsFile>(asset.text).forms;
                var ov = Resources.Load<TextAsset>("PKOUI/overrides");
                if (ov != null) ApplyOverrides(all, JsonUtility.FromJson<PkoOverridesFile>(ov.text));
                foreach (var f in all)
                {
                    if (!Defs.ContainsKey(f.name)) Defs[f.name] = f;
                    Defs[f.file + "/" + f.name] = f;
                }
            }
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var cgo = new GameObject("PkoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            Canvas = cgo.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 50;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(RefW, RefH);
            sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; sc.matchWidthOrHeight = 1f;
            BuildTip();
        }

        // True when the mouse is over any open PKO window (even where the window art is not a raycast target).
        public static bool PointerOverWindow()
        {
            if (Instance == null) return false;
            Vector2 m = Input.mousePosition;
            foreach (var w in Instance.Windows.Values)
                if (w != null && w.IsOpen && w.Rect != null && RectTransformUtility.RectangleContainsScreenPoint(w.Rect, m, null)) return true;
            return false;
        }

        static void ApplyOverrides(PkoForm[] forms, PkoOverridesFile file)
        {
            if (file == null) return;
            foreach (var o in file.forms)
            {
                var f = Array.Find(forms, x => x.name == o.name && (string.IsNullOrEmpty(o.file) || x.file == o.file));
                if (f == null) continue;
                if (o.hasPos) { f.x = o.x; f.y = o.y; }
                foreach (var co in o.comps)
                {
                    var c = Array.Find(f.comps, x => x.name == co.name); if (c == null) continue;
                    if (co.hasPos) { c.x = co.x; c.y = co.y; }
                    if (co.hasSize) { c.w = co.w; c.h = co.h; }
                    if (co.hidden) c.show = false;
                    if (co.caption != null) c.caption = co.caption;
                    if (!string.IsNullOrEmpty(co.texture)) foreach (var i in c.imgs) i.t = co.texture;
                }
            }
        }

        void BuildTip()
        {
            tip = new GameObject("Tip", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            tip.transform.SetParent(Canvas.transform, false);
            var img = tip.GetComponent<Image>(); img.color = new Color(.02f, .05f, .12f, .93f); img.raycastTarget = false;
            var edge = tip.AddComponent<Outline>(); edge.effectColor = new Color(.55f, .75f, 1f, .7f); edge.effectDistance = new Vector2(1, -1);
            tipRect = (RectTransform)tip.transform; tipRect.pivot = new Vector2(0, 0); tipRect.anchorMin = tipRect.anchorMax = Vector2.zero;
            var hl = tip.GetComponent<HorizontalLayoutGroup>(); hl.padding = new RectOffset(12, 12, 8, 8); hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            var fit = tip.GetComponent<ContentSizeFitter>(); fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            tipText = NewText(tip.transform, "T", 12, Color.white, TextAnchor.UpperCenter);
            tipText.gameObject.AddComponent<LayoutElement>().minWidth = 200;
            tip.SetActive(false);
        }
        public void ShowTip(string text)
        {
            tipText.text = text; tip.SetActive(true); tip.transform.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tipRect);
        }

        public PkoSlot AddTrashSlot(PkoWindow win, float x, float y)
        {
            var go = new GameObject("slotTrash", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(win.transform, false);
            Place((RectTransform)go.transform, x, y, 34, 34);
            var slot = MakeSlot(win, go, "trash", -1, 34, 34);
            slot.Draggable = false;
            go.GetComponent<Image>().color = new Color(.55f, .1f, .1f, .6f);
            var o = go.AddComponent<Outline>(); o.effectColor = new Color(1, 1, 1, .6f);
            var label = NewText(go.transform, "Label", 11, Color.white, TextAnchor.MiddleCenter); Stretch(label.rectTransform);
            label.text = "Delete"; label.raycastTarget = false;
            return slot;
        }

        GameObject confirm; Text confirmText; Action confirmYes;

        public void Confirm(string msg, Action yes)
        {
            if (confirm == null)
            {
                confirm = new GameObject("Confirm", typeof(RectTransform), typeof(Image));
                confirm.transform.SetParent(Canvas.transform, false);
                var rt = (RectTransform)confirm.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f); rt.sizeDelta = new Vector2(300, 110);
                confirm.GetComponent<Image>().color = new Color(.04f, .09f, .2f, .97f);
                var window = confirm.AddComponent<PkoWindow>();
                window.Rect = rt; window.Draggable = true;
                AddCloseButton(window);
                var ol = confirm.AddComponent<Outline>(); ol.effectColor = new Color(.5f, .8f, 1f, .9f);
                confirmText = NewText(confirm.transform, "Msg", 12, Color.white, TextAnchor.MiddleCenter);
                var tr = confirmText.rectTransform; tr.anchorMin = new Vector2(0, .35f); tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(8, 0); tr.offsetMax = new Vector2(-8, -6);
                confirmText.horizontalOverflow = HorizontalWrapMode.Wrap;
                ConfirmButton("Yes", new Vector2(-50, 20), () => { var y = confirmYes; confirm.SetActive(false); y?.Invoke(); });
                ConfirmButton("No", new Vector2(50, 20), () => confirm.SetActive(false));
            }
            confirmText.text = msg; confirmYes = yes; confirm.SetActive(true); confirm.transform.SetAsLastSibling();
        }

        void ConfirmButton(string label, Vector2 pos, UnityEngine.Events.UnityAction a)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(confirm.transform, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(.5f, 0); rt.pivot = new Vector2(.5f, .5f); rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(80, 24);
            go.GetComponent<Image>().color = new Color(.2f, .45f, .75f, 1);
            go.GetComponent<Button>().onClick.AddListener(a);
            var t = NewText(go.transform, "L", 12, Color.white, TextAnchor.MiddleCenter); Stretch(t.rectTransform); t.text = label; t.raycastTarget = false;
        }

        public bool ConfirmOpen => confirm != null && confirm.activeSelf;

        public void HideTip() { if (tip != null) tip.SetActive(false); }

        void LateUpdate()
        {
            if (tip == null || !tip.activeSelf) return;
            float k = 1f / Canvas.scaleFactor;
            Vector2 p = (Vector2)Input.mousePosition * k + new Vector2(14, -14) + new Vector2(0, 0);
            var size = tipRect.sizeDelta; p.x = Mathf.Min(p.x, Screen.width * k - size.x - 4);
            p.y = Mathf.Max(p.y, size.y + 4);
            tipRect.anchoredPosition = p;
        }

        public Texture2D Tex(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (textures.TryGetValue(path, out var t)) return t;
            string p = path.Replace('\\', '/').ToLowerInvariant(); int dot = p.LastIndexOf('.');
            if (dot > p.LastIndexOf('/')) p = p.Substring(0, dot);
            t = Resources.Load<Texture2D>("PKOUI/tex/" + p);
            if (t == null) Debug.LogWarning("[PkoUi] Missing UI texture: Resources/PKOUI/tex/" + p);
            textures[path] = t; return t;
        }

        public Texture2D Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (icons.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("PKOUI/icon/" + name.ToLowerInvariant());
            icons[name] = t; return t;
        }

        // ---------- janelas ----------
        public PkoWindow Get(string name)
        {
            if (Windows.TryGetValue(name, out var w)) return w;
            if (!Defs.TryGetValue(name, out var def)) return null;
            w = BuildWindow(def); w.name = name; Windows[name] = w; return w;
        }

        public void Open(string name) { Get(name)?.Open(); }
        public void Toggle(string name)
        {
            if (TryFunctionalWindow(name, out bool available))
            {
                if (!available) Confirm("Entre no mundo para usar esta janela.", () => { });
                return;
            }
            Get(name)?.Toggle();
        }

        static bool TryFunctionalWindow(string name, out bool available)
        {
            switch (name)
            {
                case "frmManage": available = TOP.UI.GuildUI.ToggleLocal(); return true;
                case "frmQQ": available = TOP.UI.FriendsUI.ToggleLocal(); return true;
                case "frmMission": available = TOP.UI.QuestLogUI.ToggleLocal(); return true;
                case "frmTeamMenber1": available = TOP.UI.PartyUI.ToggleLocal(); return true;
                default: available = false; return false;
            }
        }
        public void Close(string name) { if (Windows.TryGetValue(name, out var w)) w.Close(); }

        public void ReleaseGameWindows()
        {
            foreach (var entry in new List<KeyValuePair<string, PkoWindow>>(Windows))
            {
                var window = entry.Value;
                if (window != null && (window.Def.file == "login.clu" || window.Def.file == "selectcha.clu")) continue;
                Windows.Remove(entry.Key);
                if (window == null) continue;
                window.Close();
                Destroy(window.gameObject);
            }
        }

        PkoWindow BuildWindow(PkoForm f)
        {
            var go = new GameObject(f.name, typeof(RectTransform));
            go.transform.SetParent(Canvas.transform, false);
            var rt = (RectTransform)go.transform;
            float cx = f.x + f.w * .5f, cy = f.y + f.h * .5f;
            // Janelas centralizadas no layout 800x600 continuam centralizadas em qualquer resolucao.
            int ax = Mathf.Abs(cx - RefW * .5f) < 60 ? 1 : cx > RefW * .5f ? 2 : 0;
            int ay = Mathf.Abs(cy - RefH * .5f) < 45 ? 1 : cy > RefH * .5f ? 2 : 0;
            float px = ax == 1 ? .5f : ax == 2 ? 1 : 0, py = ay == 1 ? .5f : ay == 2 ? 0 : 1;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(px, py);
            rt.sizeDelta = new Vector2(f.w, f.h);
            float ox = ax == 1 ? cx - RefW * .5f : ax == 2 ? -(RefW - (f.x + f.w)) : f.x;
            float oy = ay == 1 ? -(cy - RefH * .5f) : ay == 2 ? (RefH - (f.y + f.h)) : -f.y;
            rt.anchoredPosition = new Vector2(ox, oy);

            var win = go.AddComponent<PkoWindow>(); win.Def = f; win.Rect = rt; win.Draggable = true;
            var hitArea = go.AddComponent<Image>();
            hitArea.color = Color.clear;
            hitArea.raycastTarget = true;
            var groups = new Dictionary<int, ToggleGroup>();
            foreach (var c in f.comps) if (c.type == "PAGE_TAB") continue; else BuildComp(win, c, groups);
            BuildTabs(win, f);
            // Toda janela com um botao "btnClose"/"btnNo" fecha ao ser clicada, mesmo quando o
            // formulario nao tem nenhuma logica propria associada a esses botoes.
            win.OnClick("btnClose", win.Close);
            win.OnClick("btnNo", win.Close);
            AddCloseButton(win);
            foreach (var button in win.GetComponentsInChildren<Button>(true))
            {
                string buttonName = button.gameObject.name;
                if (buttonName == "WindowClose") continue;
                button.onClick.AddListener(() =>
                {
                    if (!win.BoundButtons.Contains(buttonName))
                        Confirm($"A funcao '{buttonName}' de '{f.name}' ainda nao esta implementada neste cliente.",
                            () => { });
                });
            }
            go.SetActive(false);
            return win;
        }

        public void AddCloseButton(PkoWindow window)
        {
            if (window.transform.Find("WindowClose") != null) return;
            var go = new GameObject("WindowClose", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(window.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one;
            rt.sizeDelta = new Vector2(22, 22);
            rt.anchoredPosition = new Vector2(-2, -2);
            go.GetComponent<Image>().color = new Color(.55f, .12f, .12f, .95f);
            go.GetComponent<Button>().onClick.AddListener(window.Close);
            var text = NewText(go.transform, "Label", 14, Color.white, TextAnchor.MiddleCenter);
            text.text = "X"; text.raycastTarget = false; Stretch(text.rectTransform);
        }

        // Componentes de aba cujas coordenadas so cabem dentro da pagina sao relativos a ela (ex.: slots do frmInv).
        static bool IsSlotArt(PkoComp c) => c.type == "IMAGE_TYPE" || c.type == "COMMAND_ONE_TYPE";

        static Vector2Int PageOffset(PkoForm f, PkoComp c)
        {
            if (!IsSlotArt(c) || c.page < 0 || c.tabIndex < 0) return Vector2Int.zero;
            PkoComp pg = null;
            foreach (var p in f.comps) if (p.type == "PAGE_TYPE") { pg = p; break; }
            if (pg == null) return Vector2Int.zero;
            bool fitsRelative = true, outsideAbsolute = false;
            foreach (var k in f.comps)
            {
                if (k.page != c.page || k.tabIndex < 0 || !IsSlotArt(k)) continue;
                if (k.x < 0 || k.y < 0 || k.x + k.w > pg.w || k.y + k.h > pg.h) fitsRelative = false;
                if (k.x < pg.x || k.y < pg.y || k.x + k.w > pg.x + pg.w || k.y + k.h > pg.y + pg.h) outsideAbsolute = true;
            }
            return fitsRelative && outsideAbsolute ? new Vector2Int(pg.x, pg.y) : Vector2Int.zero;
        }
        void BuildTabs(PkoWindow win, PkoForm f)
        {
            var pages = new Dictionary<int, PkoComp>();
            foreach (var c in f.comps) if (c.type == "PAGE_TYPE") pages[c.page >= 0 ? c.page : c.id] = c;
            var first = new Dictionary<int, int>();
            foreach (var c in f.comps)
            {
                if (c.type != "PAGE_TAB") continue;
                int px = 0, py = 0;
                foreach (var pg in f.comps) if (pg.type == "PAGE_TYPE") { px = pg.x; py = pg.y; break; }
                var go = new GameObject("tab" + c.tabKey, typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(win.transform, false);
                var rt = (RectTransform)go.transform; Place(rt, px + c.x, py + c.y, c.w, c.h);
                var raw = go.GetComponent<RawImage>();
                var act = Array.Find(c.imgs, i => i.s == "PAGE_ITEM_TITLE_ACTIVE"); var nor = Array.Find(c.imgs, i => i.s == "PAGE_ITEM_TITLE_NORMAL");
                var tex = Tex((act ?? nor)?.t); raw.texture = tex; raw.enabled = tex != null;
                var tab = go.AddComponent<PkoTab>(); tab.Window = win; tab.Page = c.page; tab.Key = c.tabKey; tab.Image = raw;
                if (tex != null) { tab.Active = Uv(tex, act ?? nor); tab.Normal = Uv(tex, nor ?? act); }
                win.Tabs.Add(tab);
                if (!first.ContainsKey(c.page)) first[c.page] = c.tabKey;
            }
            foreach (var kv in first) win.ShowTab(kv.Key, kv.Value);
        }

        static Rect Uv(Texture2D t, PkoImg i)
        {
            if (t == null || i == null) return new Rect(0, 0, 1, 1);
            float W = t.width, H = t.height;
            return new Rect(i.u / W, 1f - (i.v + i.h) / H, i.w / W, i.h / H);
        }

        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
        }

        static Color Argb(long c)
        {
            return new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f, 1f);
        }

        Text NewText(Transform parent, string name, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>(); t.font = font; t.fontSize = size; t.color = color; t.alignment = anchor; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        RawImage NewRaw(Transform parent, string name, PkoImg img, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var raw = go.GetComponent<RawImage>(); raw.raycastTarget = raycast;
            var tex = img != null ? Tex(img.t) : null;
            if (tex == null) { raw.color = new Color(1, 1, 1, 0); }
            else { raw.texture = tex; raw.uvRect = Uv(tex, img); }
            return raw;
        }

        static PkoImg ImgOf(PkoComp c, params string[] states)
        {
            foreach (var s in states) { var i = Array.Find(c.imgs, x => x.s == s); if (i != null) return i; }
            return c.imgs.Length > 0 ? c.imgs[0] : null;
        }

        static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

        void BuildComp(PkoWindow win, PkoComp c, Dictionary<int, ToggleGroup> groups)
        {
            var go = new GameObject(string.IsNullOrEmpty(c.name) ? c.type : c.name, typeof(RectTransform));
            go.transform.SetParent(win.transform, false);
            var rt = (RectTransform)go.transform; var po = PageOffset(win.Def, c); Place(rt, c.x + po.x, c.y + po.y, c.w, c.h);
            bool ok = true;
            switch (c.type)
            {
                case "IMAGE_TYPE": case "IMAGE_FRAME_TYPE": case "IMAGE_FLASH_TYPE":
                {
                    var r = go.AddComponent<RawImage>(); var i = ImgOf(c, "NORMAL", "COMPENT_BACK"); var t = i != null ? Tex(i.t) : null;
                    r.raycastTarget = false;
                    if (t == null) r.color = new Color(1, 1, 1, 0); else { r.texture = t; r.uvRect = Uv(t, i); }
                    break;
                }
                case "BUTTON_TYPE":
                {
                    var r = go.AddComponent<RawImage>(); var i = ImgOf(c, "BUTTON", "NORMAL"); var t = i != null ? Tex(i.t) : null;
                    if (t == null) r.color = new Color(1, 1, 1, 0); else { r.texture = t; r.uvRect = Uv(t, i); }
                    var b = go.AddComponent<Button>(); b.targetGraphic = r;
                    var cb = b.colors; cb.highlightedColor = new Color(.85f, .85f, 1f); cb.pressedColor = new Color(.65f, .65f, .75f); b.colors = cb;
                    if (!string.IsNullOrEmpty(c.caption)) { var tx = NewText(go.transform, "Caption", 11, Color.white, TextAnchor.MiddleCenter); Stretch(tx.rectTransform); tx.text = c.caption; }
                    break;
                }
                case "LABELEX_TYPE": case "LABEL_TYPE": case "TITLE_TYPE":
                {
                    var tx = go.AddComponent<Text>(); tx.font = font; tx.fontSize = 11; tx.raycastTarget = false;
                    tx.color = Argb(c.textColor); tx.alignment = TextAnchor.UpperLeft; tx.text = c.caption ?? "";
                    tx.horizontalOverflow = HorizontalWrapMode.Overflow; tx.verticalOverflow = VerticalWrapMode.Overflow;
                    if (c.w > 0 && c.w < 300) go.AddComponent<PkoFitText>().MaxWidth = c.w;
                    break;
                }
                case "EDIT_TYPE":
                {
                    var bg = go.AddComponent<Image>(); bg.color = new Color(.04f, .08f, .15f, .96f);
                    var edge = go.AddComponent<Outline>(); edge.effectColor = new Color(.45f, .7f, .95f, .8f); edge.effectDistance = new Vector2(1, -1);
                    var txt = NewText(go.transform, "Text", 11, Color.white, TextAnchor.MiddleLeft); Stretch(txt.rectTransform); txt.rectTransform.offsetMin = new Vector2(3, 0);
                    txt.raycastTarget = false; txt.horizontalOverflow = HorizontalWrapMode.Wrap;
                    var inp = go.AddComponent<InputField>(); inp.textComponent = txt; inp.targetGraphic = bg; inp.text = "";
                    if (c.maxNum > 0) inp.characterLimit = c.maxNum;
                    break;
                }
                case "CHECK_TYPE": case "CHECK_GROUP_TYPE":
                {
                    var un = ImgOf(c, "UNCHECKED", "NORMAL"); var ch = Array.Find(c.imgs, x => x.s == "CHECKED");
                    var bgR = go.AddComponent<RawImage>(); var ut = un != null ? Tex(un.t) : null;
                    if (ut == null) bgR.color = new Color(1, 1, 1, 0); else { bgR.texture = ut; bgR.uvRect = Uv(ut, un); }
                    var tg = go.AddComponent<Toggle>(); tg.targetGraphic = bgR; tg.transition = Selectable.Transition.None;
                    if (ch != null)
                    {
                        var mark = NewRaw(go.transform, "Check", ch, false); Stretch(mark.rectTransform); tg.graphic = mark;
                    }
                    if (c.type == "CHECK_GROUP_TYPE" && c.group >= 0)
                    {
                        if (!groups.TryGetValue(c.group, out var g))
                        {
                            var groupGo = new GameObject("ToggleGroup_" + c.group);
                            groupGo.transform.SetParent(win.transform, false);
                            g = groupGo.AddComponent<ToggleGroup>();
                            groups[c.group] = g;
                        }
                        tg.group = g;
                        if (!win.Groups.TryGetValue(c.group, out var list)) { list = new List<Toggle>(); win.Groups[c.group] = list; }
                        list.Add(tg);
                    }
                    break;
                }
                case "PROGRESS_TYPE":
                {
                    var clip = new GameObject("Clip", typeof(RectTransform), typeof(RectMask2D)); clip.transform.SetParent(go.transform, false);
                    Place((RectTransform)clip.transform, 0, 0, c.w, c.h);
                    var fill = NewRaw(clip.transform, "Fill", ImgOf(c, "PROGRESS_PROGRESS", "NORMAL"), false);
                    Place(fill.rectTransform, 0, 0, c.w, c.h);
                    if (fill.texture == null) fill.color = ProgressColor(c.name);
                    var pr = go.AddComponent<PkoProgress>(); pr.Clip = (RectTransform)clip.transform; pr.Width = c.w; pr.Height = c.h;
                    if (c.w >= 60) { pr.Label = NewText(go.transform, "Value", 8, Color.white, TextAnchor.MiddleCenter); Stretch(pr.Label.rectTransform); }
                    break;
                }
                case "COMMAND_ONE_TYPE": case "FAST_COMMANG_TYPE":
                    MakeSlot(win, go, c.name, win.Named.Count, c.w, c.h); break;
                case "GOODS_GRID_TYPE":
                {
                    var list = new List<PkoSlot>(); win.Grids[c.name] = list;
                    int cols = Mathf.Max(1, c.cols), rows = Mathf.Max(1, c.rows);
                    for (int i = 0; i < cols * rows; i++)
                    {
                        var cell = new GameObject("cell" + i, typeof(RectTransform)); cell.transform.SetParent(go.transform, false);
                        Place((RectTransform)cell.transform, (i % cols) * (c.cellW + c.gapX), (i / cols) * (c.cellH + c.gapY), c.cellW, c.cellH);
                        list.Add(MakeSlot(win, cell, c.name, i, c.cellW, c.cellH));
                    }
                    break;
                }
                case "LIST_TYPE": case "MEMO_TYPE": case "MEMOEX_TYPE": case "RICHMEMO_TYPE": case "SKILL_LIST_TYPE": case "TREE_TYPE": case "GRID_TYPE":
                    MakeScroll(go, c); break;
                case "UI3D_COMPENT":
                {
                    var r = go.AddComponent<RawImage>(); r.color = new Color(1, 1, 1, 0); r.raycastTarget = true;
                    break;
                }
                case "COMBO_TYPE":
                {
                    var bg = go.AddComponent<Image>(); bg.color = new Color(0, 0, 0, .2f);
                    var tx = NewText(go.transform, "Text", 11, Color.white, TextAnchor.MiddleCenter); Stretch(tx.rectTransform);
                    go.AddComponent<PkoCombo>().Label = tx; break;
                }
                default: ok = false; break;
            }
            if (!ok) { go.SetActive(false); }
            else if (!c.show) go.SetActive(false);

            if (c.page >= 0 && c.tabIndex >= 0)
            {
                long key = ((long)c.page << 32) | (uint)c.tabIndex;
                if (!win.PageContent.TryGetValue(key, out var l)) win.PageContent[key] = l = new List<GameObject>();
                l.Add(go);
            }
            if (!string.IsNullOrEmpty(c.name)) { if (!win.Named.ContainsKey(c.name)) win.Named[c.name] = rt; }
        }

        static Color ProgressColor(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("hp")) return new Color(.8f, .1f, .1f);
            if (n.Contains("sp")) return new Color(.15f, .35f, .9f);
            if (n.Contains("exp")) return new Color(.9f, .75f, .1f);
            return new Color(.3f, .7f, .3f);
        }

        PkoSlot MakeSlot(PkoWindow win, GameObject go, string group, int index, float w, float h)
        {
            if (!go.TryGetComponent<Image>(out var bg)) bg = go.AddComponent<Image>(); bg.color = new Color(0, 0, 0, 0);
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(RawImage)); icon.transform.SetParent(go.transform, false);
            Stretch((RectTransform)icon.transform);
            var raw = icon.GetComponent<RawImage>(); raw.raycastTarget = false; raw.enabled = false;
            var count = NewText(go.transform, "Count", 10, Color.white, TextAnchor.LowerRight); Stretch(count.rectTransform);
            count.rectTransform.offsetMax = new Vector2(-2, 0); count.rectTransform.offsetMin = new Vector2(0, 1);
            var outline = count.gameObject.AddComponent<Outline>(); outline.effectColor = Color.black;
            var slot = go.AddComponent<PkoSlot>(); slot.Group = group; slot.Index = index; slot.Icon = raw; slot.Count = count; slot.Window = win;
            return slot;
        }

        void MakeScroll(GameObject go, PkoComp c)
        {
            var bg = go.AddComponent<Image>(); bg.color = c.type == "LIST_TYPE" || c.type == "MEMO_TYPE" ? new Color(0, 0, 0, .25f) : new Color(0, 0, 0, 0);
            var view = new GameObject("View", typeof(RectTransform), typeof(RectMask2D)); view.transform.SetParent(go.transform, false); Stretch((RectTransform)view.transform);
            var content = new GameObject("Content", typeof(RectTransform)); content.transform.SetParent(view.transform, false);
            var crt = (RectTransform)content.transform; crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0, 1); crt.sizeDelta = Vector2.zero;
            var vl = content.AddComponent<VerticalLayoutGroup>(); vl.childForceExpandHeight = false; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandWidth = true;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = go.AddComponent<ScrollRect>(); sr.viewport = (RectTransform)view.transform; sr.content = crt; sr.horizontal = false; sr.scrollSensitivity = 18f;
            var body = NewText(content.transform, "Body", 11, Color.white, TextAnchor.UpperLeft);
            body.supportRichText = false;
            body.horizontalOverflow = HorizontalWrapMode.Wrap; body.verticalOverflow = VerticalWrapMode.Overflow;
            var log = go.AddComponent<PkoLog>(); log.Body = body; log.Scroll = sr;
            body.gameObject.SetActive(c.type != "SKILL_LIST_TYPE");
        }

        public Transform ContentOf(PkoWindow win, string name)
        {
            var rt = win.Named.TryGetValue(name, out var r) ? r : null;
            var sr = rt != null ? rt.GetComponent<ScrollRect>() : null;
            return sr != null ? sr.content : null;
        }

        public Text MakeLabel(Transform parent, string text, int size = 11, TextAnchor a = TextAnchor.MiddleLeft)
        {
            var t = NewText(parent, "Label", size, Color.white, a); t.text = text; return t;
        }

        // ---------- teclas de atalho ----------
        void Update()
        {
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                          EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                PkoWindow top = null;
                foreach (var w in Windows.Values) if (w.IsOpen && w.Def != null && w.Def.esc && (top == null || w.transform.GetSiblingIndex() > top.transform.GetSiblingIndex())) top = w;
                if (top != null) top.Close();
                else if (typing) EventSystem.current.SetSelectedGameObject(null);
                // Nenhuma janela "esc" aberta: ESC abre o menu de configuracoes (frmSystem), como no cliente original.
                else if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "GameScene" && Defs.ContainsKey("frmSystem")) Toggle("frmSystem");
                return;
            }
            if (typing || GUIUtility.keyboardControl != 0
                || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "GameScene") return;
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!ctrl && !shift)
                foreach (var key in GameplayShortcuts.Keys)
                    if (Input.GetKeyDown(key)) { ActivateShortcut(key, alt, ctrl, shift); return; }
            if (alt && !ctrl && !shift && Input.GetKeyDown(KeyCode.B))
            {
                ActivateShortcut(KeyCode.B, alt, ctrl, shift);
                return;
            }
            foreach (var entry in Defs)
            {
                var def = entry.Value;
                if (entry.Key != def.name || !TryShortcutKey(def.hotKey, out var key) || !Input.GetKeyDown(key)) continue;
                if (ActivateShortcut(key, alt, ctrl, shift)) return;
            }
        }

        public bool ActivateShortcut(KeyCode key, bool alt, bool ctrl, bool shift)
        {
            if (!ctrl && !shift && GameplayShortcuts.TryGetValue(key, out var action))
            {
                if (!action()) Confirm("Entre no mundo para usar esta janela.", () => { });
                return true;
            }
            if (key == KeyCode.B && alt && !ctrl && !shift)
            {
                Open("frmInv");
                Get("frmInv")?.Get<Button>("btnLock")?.onClick.Invoke();
                return true;
            }
            foreach (var entry in Defs)
            {
                var def = entry.Value;
                if (entry.Key != def.name || def.name == "frmGM" || def.file == "login.clu" || def.file == "selectcha.clu"
                    || !TryShortcutKey(def.hotKey, out var mapped) || mapped != key) continue;
                bool want = def.hotMod == "ALT_KEY" ? alt && !ctrl && !shift
                    : def.hotMod == "CTRL_KEY" ? ctrl && !alt && !shift
                    : def.hotMod == "SHIFT_KEY" ? shift && !alt && !ctrl : !alt && !ctrl && !shift;
                if (!want) continue;
                Toggle(def.name);
                return true;
            }
            return false;
        }

        static bool TryShortcutKey(string hot, out KeyCode key)
        {
            key = KeyCode.None;
            if (string.IsNullOrEmpty(hot)) return false;
            string n = hot.Replace("HOTKEY_", "");
            if (n.Length == 1 && char.IsDigit(n[0])) n = "Alpha" + n;
            return Enum.TryParse(n, true, out key);
        }
    }
}
