using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TOP.Core;
using TOP.Data;
using TOP.Inventory;
using TOP.Player;
using TOP.Systems;

namespace TOP.UI.Pko
{
    public class PkoClickBar : MonoBehaviour, IPointerClickHandler, IDragHandler
    {
        public PkoProgress Bar; public System.Action<float> Changed; public float Value;
        public void OnPointerClick(PointerEventData e) { Apply(e); }
        public void OnDrag(PointerEventData e) { Apply(e); }
        void Apply(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, null, out var p)) return;
            Value = Mathf.Clamp01((p.x - rt.rect.xMin) / Mathf.Max(1f, rt.rect.width));
            Bar.Set(Value); Changed?.Invoke(Value);
        }
    }

    // Liga as janelas originais ao jogador local.
    public class PkoUiGame : MonoBehaviour
    {
        static readonly string[] Startup = { "frmDetail", "frmMainFun", "frmFast", "frmMain800", "frmMinimap" };
        static readonly Dictionary<string, EquipmentSlot> EquipMap = new Dictionary<string, EquipmentSlot>
        {
            { "cmdArmet", EquipmentSlot.Helmet }, { "cmdBody", EquipmentSlot.Armor }, { "cmdRightHand", EquipmentSlot.Weapon },
            { "cmdLeftHand", EquipmentSlot.Shield }, { "cmdGlove", EquipmentSlot.Gloves }, { "cmdShoes", EquipmentSlot.Boots },
            { "cmdNecklace", EquipmentSlot.Necklace }, { "cmdJewelry1", EquipmentSlot.Ring1 }, { "cmdJewelry2", EquipmentSlot.Ring2 },
            { "cmdJewelry3", EquipmentSlot.Earring }, { "cmdJewelry4", EquipmentSlot.Belt }, { "cmdCirclet1", EquipmentSlot.Tattoo },
            { "cmdCloak", EquipmentSlot.Cape }, { "cmdWing", EquipmentSlot.Wing }, { "cmdPet", EquipmentSlot.Pet },
            { "cmdMount", EquipmentSlot.Mount }, { "cmdBodyApp", EquipmentSlot.ApparelBody }, { "cmdArmetApp", EquipmentSlot.ApparelHelmet },
            { "cmdGloveApp", EquipmentSlot.ApparelGloves }, { "cmdShoesApp", EquipmentSlot.ApparelBoots }, { "cmdShieldApp", EquipmentSlot.ApparelShield },
            { "cmdSword1App", EquipmentSlot.ApparelSword }, { "cmdGreatSwordApp", EquipmentSlot.ApparelGreatSword }, { "cmdGunApp", EquipmentSlot.ApparelGun },
            { "cmdDaggerApp", EquipmentSlot.ApparelDagger }, { "cmdStaffApp", EquipmentSlot.ApparelStaff }, { "cmdBowApp", EquipmentSlot.ApparelBow },
            { "cmdPetApp", EquipmentSlot.ApparelPet }, { "cmdGlowApp", EquipmentSlot.ApparelGlow }
        };

        PkoUi ui; PlayerController pc; PlayerInventory inv; PlayerEquipment eq; PlayerSkills sk; PlayerHotbar hb; PlayerStats st; PlayerClass cls;
        PkoWindow detail, fun, fast, chat, mini, invW, stateW, skillW;
        float nextRefresh; int skillTab; bool browser; Vector2 browserScroll; RawImage minimapView; MinimapRenderer minimapSrc;
        readonly List<string> allNames = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, _) =>
            {
                if (scene.name == "GameScene") Create();
                else DestroyExisting(); // logout/char-select/etc: nao deixar o HUD do jogo vazar para outras telas
            };
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "GameScene") Create();
        }

        public static PkoUiGame Create()
        {
            var existing = FindAnyObjectByType<PkoUiGame>(); if (existing != null) return existing;
            var go = new GameObject("PkoUiGame"); DontDestroyOnLoad(go); return go.AddComponent<PkoUiGame>();
        }

        static void DestroyExisting()
        {
            var existing = FindAnyObjectByType<PkoUiGame>();
            if (existing == null) return;
            existing.CloseAllWindows(); // fechar na hora: Destroy() so remove o GameObject no fim do frame
            Destroy(existing.gameObject);
        }

        // Ao sair da GameScene (logout, troca de personagem, etc.) as janelas do HUD (status,
        // minimapa, hotbar, chat, inventario...) ficam na mesma PkoCanvas persistente usada pelas
        // telas de login/selecao - por isso precisam ser fechadas explicitamente aqui, e nao apenas
        // destruir este GameObject (que so contem a logica, nao as janelas em si).
        void CloseAllWindows()
        {
            if (ui == null) return;
            foreach (var w in new List<PkoWindow>(ui.Windows.Values))
                if (w != null && w.IsOpen) w.Close();
        }

        void OnDestroy() => CloseAllWindows();

        void Start()
        {
            ui = PkoUi.Ensure();
            foreach (var n in Startup) ui.Open(n);
            detail = ui.Get("frmDetail"); fun = ui.Get("frmMainFun"); fast = ui.Get("frmFast"); chat = ui.Get("frmMain800"); mini = ui.Get("frmMinimap");
            allNames.AddRange(ui.Defs.Keys); allNames.Sort();
            HideLegacyHud();
            SetupHud();
            SetupSystem();
            foreach (var kv in ui.Defs) ui.Get(kv.Key); // todas as janelas existem (fechadas) e navegaveis pelo F10
            SetupInventory(); SetupState(); SetupSkills();
            foreach (var n in Startup) ui.Open(n);
            ApplySavedGameSettings();
        }

        // Aplica, ao entrar no jogo, as preferencias de video/jogo salvas pelo jogador em sessoes
        // anteriores (sem precisar reabrir frmVideo/frmGame e clicar em "Yes" de novo).
        void ApplySavedGameSettings()
        {
            GameSettings.ApplyVideo();
            GameSettings.ApplyGame();
            detail?.SetVisible("proMainHP1", GameSettings.ShowHudBars);
            detail?.SetVisible("proMainSP", GameSettings.ShowHudBars);
            detail?.SetVisible("labMainID", GameSettings.ShowPlayerInfo);
            detail?.SetVisible("labMainLv", GameSettings.ShowPlayerInfo);
            FpsCounterUI.SetVisible(true);
        }

        static void HideLegacyHud()
        {
            var hud = GameObject.Find("HUDPanel");
            var targets = new List<GameObject>();
            if (hud != null) { var canvas = hud.GetComponentInParent<Canvas>(); targets.Add(canvas != null ? canvas.gameObject : hud); }
            var mm = GameObject.Find("MiniMap_BG"); if (mm != null) targets.Add(mm);
            foreach (var go in targets)
            {
                if (!go.TryGetComponent<CanvasGroup>(out var cg)) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = 0; cg.blocksRaycasts = false; cg.interactable = false;
            }
        }
        // ---------- HUD ----------
        void SetupHud()
        {
            var rt = fast.Rect; rt.anchorMin = rt.anchorMax = new Vector2(.5f, 0); rt.pivot = new Vector2(.5f, 0); rt.anchoredPosition = new Vector2(0, 34);
            for (int i = 0; i < 12; i++)
            {
                var slot = fast.Get<PkoSlot>("fscMainF" + i); if (slot == null) continue;
                slot.Index = i; slot.Group = "hotbar"; slot.Clicked = HotbarClicked; slot.Dropped = HotbarDropped; slot.Tip = () => HotbarTip(slot.Index);
            }
            fun.SetVisible("btnOpenSociliaty", false);
            var map = new Dictionary<string, string>
            {
                { "btnState", "frmState" }, { "btnOpenBag", "frmInv" }, { "btnSkill", "frmSkill" }, { "btnMission", "frmMission" },
                { "btnGuild", "frmManage" }, { "btnShip", "frmShipBiuld" }, { "btnSystem", "frmSystem" }, { "btnQQ", "frmQQ" }
            };
            foreach (var kv in map) { string target = kv.Value; fun.OnClick(kv.Key, () => ui.Toggle(target)); }
            fun.SetVisible("btnLevelUpHelp", false); fun.SetVisible("btnInfoCenter", false);
            mini.OnClick("btnOpen", () => ui.Toggle("frmBigmap")); mini.OnClick("btnSearch", () => ui.Toggle("frmSearch"));
            mini.OnClick("btnteam", () => ui.Toggle("frmTeamMenber1"));
            mini.OnClick("btnOpenStore", () => ui.Toggle("frmStore")); mini.OnClick("btnMarketGlobal", () => ui.Toggle("frmMarketGlobal"));
            for (int i = 1; i < 10; i++) mini.SetVisible("labMapPos" + i, false);
            if (mini.Named.TryGetValue("imgMinimapRect", out var rect))
            {
                var go = new GameObject("MinimapView", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(mini.transform, false); go.transform.SetSiblingIndex(0);
                var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
                r.anchoredPosition = rect.anchoredPosition; r.sizeDelta = rect.sizeDelta;
                minimapView = go.GetComponent<RawImage>(); minimapView.raycastTarget = false;
            }
            var edit = chat.Get<InputField>("edtSay");
            if (edit != null) edit.onEndEdit.AddListener(SubmitChat);
            var combo = chat.Get<PkoCombo>("cboChannel"); if (combo != null) { combo.Items = new[] { "All", "Local", "Team", "Guild" }; combo.Set(0); }
            chat.Get<PkoLog>("lstOnSay")?.Add("Welcome to Tales of Pirates.");
            if (pc == null) return;
        }

        static string HpSpText(int cur, int max)
        {
            if (!GameSettings.HpAsPercent) return cur + "/" + max;
            return max > 0 ? Mathf.RoundToInt(100f * cur / max) + "%" : "0%";
        }

        void SubmitChat(string text)
        {
            if (!(Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) || string.IsNullOrWhiteSpace(text)) return;
            var edit = chat.Get<InputField>("edtSay"); edit.text = "";
            string who = pc != null ? pc.CharacterName : "Me";
            chat.Get<PkoLog>("lstOnSay")?.Add($"[{who}] {text}");
            EventSystem.current.SetSelectedGameObject(null);
        }

        // ---------- sistema ----------
        void SetupSystem()
        {
            var sys = ui.Get("frmSystem");
            var map = new Dictionary<string, string>
            {
                { "btnVideo", "frmVideo" }, { "btnAudio", "frmAudio" }, { "btnGame", "frmGame" }, { "btnChange", "frmAskChange" },
                { "btnRelogin", "frmAskRelogin" }, { "btnExit", "frmAskExit" }, { "btnOfflineMode", "frmAskOfflineMode" }
            };
            foreach (var kv in map) { string target = kv.Value; sys.OnClick(kv.Key, () => { sys.Close(); ui.Open(target); }); }

            var exit = ui.Get("frmAskExit");
            exit.OnClick("btnYes", () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });

            // Logout e troca de personagem: este projeto so suporta reautenticacao completa (nao
            // existe fluxo de "trocar personagem sem desconectar" no servidor), entao ambos usam
            // o mesmo caminho seguro de desconectar e voltar para a tela de login.
            var relogin = ui.Get("frmAskRelogin");
            relogin.OnClick("btnYes", () => { relogin.Close(); DisconnectToLogin(); });

            var change = ui.Get("frmAskChange");
            change.OnClick("btnYes", () => { change.Close(); DisconnectToLogin(); });

            // Jogo 100% online (Mirror): nao existe um "modo offline" real. Avisamos o jogador
            // explicitamente em vez de deixar o botao sem fazer nada.
            var offline = ui.Get("frmAskOfflineMode");
            offline.OnClick("btnYes", () =>
            {
                chat?.Get<PkoLog>("lstOnSay")?.Add("Offline mode is not available on this server - an online connection is required.");
                offline.Close();
            });

            var audio = ui.Get("frmAudio");
            Bar(audio, "proAudioMusic", PlayerPrefs.GetFloat("pko_music", 1f), v => PlayerPrefs.SetFloat("pko_music", v));
            float sfx = PlayerPrefs.GetFloat("pko_sfx", AudioListener.volume);
            AudioListener.volume = sfx;
            Bar(audio, "proAudioMidi", sfx, v => { AudioListener.volume = v; PlayerPrefs.SetFloat("pko_sfx", v); });
            audio.OnClick("btnYes", audio.Close);

            SetupVideoSettings();
            SetupGameSettings();
        }

        static void DisconnectToLogin()
        {
            if (GameFlowManager.Instance != null) { GameFlowManager.Instance.Logout(); return; }
            if (NetworkClient.isConnected) NetworkClient.Disconnect();
            SceneManager.LoadScene("LoginScene");
        }

        // ---------- frmVideo ----------
        void SetupVideoSettings()
        {
            var w = ui.Get("frmVideo");

            var combo = w.Get<PkoCombo>("cboResolution");
            if (combo != null)
            {
                var res = GameSettings.Resolutions;
                var items = new string[res.Length];
                for (int i = 0; i < items.Length; i++) items[i] = res[i].width + "x" + res[i].height;
                combo.Items = items;
                combo.Set(Mathf.Max(0, GameSettings.ResolutionIndex));
            }

            SetCheck(w, "chkFull", GameSettings.Fullscreen); SetCheck(w, "chkWindow", !GameSettings.Fullscreen);
            int q = Mathf.Clamp(GameSettings.QualityLevel, 0, 2);
            SetCheck(w, "chkHigh", q == 2); SetCheck(w, "chkNormal", q == 1); SetCheck(w, "chkLow", q == 0);
            SetCheck(w, "chkViewFar", GameSettings.ViewFar); SetCheck(w, "chkViewNear", !GameSettings.ViewFar);

            // Texturas/filme/tremor de camera/trilha/profundidade de cor: preferencia e salva e
            // restaurada, mas este projeto ainda nao tem um sistema grafico separado para aplica-las.
            PersistCheckSet(w, "pko_pref_texture", 1, "chkTextureHigh", "chkTextureNormal", "chkTextureLow");
            PersistCheckSet(w, "pko_pref_movie", 0, "chkMovieOn", "chkMovieOff");
            PersistCheckSet(w, "pko_pref_camshake", 0, "chkCameraOn", "chkCameraOff");
            PersistCheckSet(w, "pko_pref_trail", 0, "chkTrailOn", "chkTrailOff");
            PersistCheckSet(w, "pko_pref_color", 0, "chkColor32", "chkColor16");

            w.OnClick("btnYes", () =>
            {
                if (combo != null) GameSettings.ResolutionIndex = combo.Index;
                GameSettings.Fullscreen = IsOn(w, "chkFull");
                GameSettings.QualityLevel = IsOn(w, "chkHigh") ? 2 : IsOn(w, "chkLow") ? 0 : 1;
                GameSettings.ViewFar = !IsOn(w, "chkViewNear");
                GameSettings.ApplyVideo();
                w.Close();
            });
        }

        // ---------- frmGame ----------
        void SetupGameSettings()
        {
            var w = ui.Get("frmGame");

            // Sem sistema de jogo equivalente ainda (nao ha Nameplate/AutoLock/MountController no
            // projeto): a preferencia do jogador e salva e restaurada, mas nao tem efeito visual.
            PersistGroup(w, 68, "pko_pref_run", 0);
            PersistGroup(w, 74, "pko_pref_autolock", 0);
            PersistGroup(w, 80, "pko_pref_help", 1);
            PersistGroup(w, 92, "pko_pref_apparel", 1);
            PersistGroup(w, 104, "pko_pref_mounts", 1);
            PersistGroup(w, 110, "pko_pref_state", 1);
            PersistGroup(w, 116, "pko_pref_names", 1);

            // Com efeito real no jogo.
            SetGroup(w, 86, GameSettings.CameraMode);
            SetGroup(w, 98, GameSettings.ShowEffects ? 1 : 0);
            SetGroup(w, 122, GameSettings.ShowHudBars ? 1 : 0);
            SetGroup(w, 128, GameSettings.HpAsPercent ? 1 : 0);
            SetGroup(w, 134, GameSettings.ShowPlayerInfo ? 1 : 0);
            SetGroup(w, 140, GameSettings.TargetFps >= 60 ? 1 : 0);

            w.OnClick("btnYes", () =>
            {
                GameSettings.CameraMode = GroupIndex(w, 86, GameSettings.CameraMode);
                GameSettings.ShowEffects = GroupIndex(w, 98, 1) == 1;
                GameSettings.ShowHudBars = GroupIndex(w, 122, 1) == 1;
                GameSettings.HpAsPercent = GroupIndex(w, 128, 0) == 1;
                GameSettings.ShowPlayerInfo = GroupIndex(w, 134, 1) == 1;
                GameSettings.TargetFps = GroupIndex(w, 140, 1) == 1 ? 60 : 30;

                GameSettings.ApplyGame();
                detail?.SetVisible("proMainHP1", GameSettings.ShowHudBars);
                detail?.SetVisible("proMainSP", GameSettings.ShowHudBars);
                detail?.SetVisible("labMainID", GameSettings.ShowPlayerInfo);
                detail?.SetVisible("labMainLv", GameSettings.ShowPlayerInfo);
                w.Close();
            });
        }

        static bool IsOn(PkoWindow w, string name) { var t = w.Get<Toggle>(name); return t != null && t.isOn; }
        static void SetCheck(PkoWindow w, string name, bool on) { var t = w.Get<Toggle>(name); if (t != null) t.isOn = on; }
        static void SetGroup(PkoWindow w, int group, int idx) { var t = w.GroupToggle(group, idx); if (t != null) t.isOn = true; }

        static int GroupIndex(PkoWindow w, int group, int fallback)
        {
            if (!w.Groups.TryGetValue(group, out var list)) return fallback;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].isOn) return i;
            return fallback;
        }

        // Grupo de checkboxes sem sistema de jogo correspondente: so lembra a escolha do jogador
        // entre sessoes (salva ao marcar, sem precisar esperar o botao "Yes").
        static void PersistGroup(PkoWindow w, int group, string prefKey, int defaultIndex)
        {
            SetGroup(w, group, PlayerPrefs.GetInt(prefKey, defaultIndex));
            if (!w.Groups.TryGetValue(group, out var list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                var tg = list[i]; if (tg == null) continue;
                int captured = i;
                tg.onValueChanged.AddListener(v => { if (v) PlayerPrefs.SetInt(prefKey, captured); });
            }
        }

        // Variante por nome (quando o formulario nao tem colisao de nomes, ex.: frmVideo).
        static void PersistCheckSet(PkoWindow w, string prefKey, int defaultIndex, params string[] names)
        {
            int idx = PlayerPrefs.GetInt(prefKey, defaultIndex);
            for (int i = 0; i < names.Length; i++)
            {
                var t = w.Get<Toggle>(names[i]); if (t == null) continue;
                t.isOn = i == idx;
                int captured = i;
                t.onValueChanged.AddListener(v => { if (v) PlayerPrefs.SetInt(prefKey, captured); });
            }
        }

        static void Bar(PkoWindow w, string name, float value, System.Action<float> onChange)
        {
            var pr = w.Get<PkoProgress>(name); if (pr == null) return;
            var cb = pr.gameObject.AddComponent<PkoClickBar>(); cb.Bar = pr; cb.Changed = onChange; pr.Set(value);
            pr.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .15f);
        }

        // ---------- inventario ----------
        void SetupInventory()
        {
            invW = ui.Get("frmInv");
            if (invW.Grids.TryGetValue("grdItem", out var grid))
                foreach (var s in grid)
                {
                    s.Group = "inv"; s.Clicked = InvClicked; s.Dropped = SlotDropped; s.Tip = () => ItemTip(SlotItemId(s), SlotItem(s));
                }
            foreach (var kv in EquipMap)
            {
                var s = invW.Get<PkoSlot>(kv.Key); if (s == null) continue;
                s.Group = "equip"; s.Index = (int)kv.Value; s.Clicked = EquipClicked; s.Dropped = SlotDropped; s.Tip = () => ItemTip(SlotItemId(s), SlotItem(s));
            }
            invW.SetVisible("ui3dCha", false);
            invW.SetVisible("cmdFaceApp", false); invW.SetVisible("cmdSword2App", false);
            var trash = ui.AddTrashSlot(invW, 372, 352);
            trash.Dropped = (from, to) => { if (from.Group == "inv" && from.Filled) AskDelete(from.Index); };
            invW.OnClick("btnOpenTempBag", () => ui.Toggle("frmTempBag"));
            invW.Opened += _ => RefreshInventory();
        }

        TOP.Inventory.InventoryItem SlotItem(PkoSlot s)
        {
            if (s.Group == "inv") return inv != null ? inv.GetSlot(s.Index) : null;
            if (s.Group == "equip") return eq != null ? eq.GetEquippedItem((EquipmentSlot)s.Index) : null;
            return null;
        }

        int SlotItemId(PkoSlot s)
        {
            if (s.Group == "inv") { var it = inv != null ? inv.GetSlot(s.Index) : null; return it != null && !it.IsEquipped ? it.ItemId : 0; }
            if (s.Group == "equip") return eq != null ? eq.GetEquippedItem((EquipmentSlot)s.Index)?.ItemId ?? 0 : 0;
            return 0;
        }

        Texture2D ItemIcon(int id)
        {
            if (id <= 0) return null;
            if (PkoTables.Items.TryGetValue(id, out var it)) return ui.Icon(it.Icon);
            return null;
        }

        const string Red = "#ff5a4a", Blue = "#7fc4ff", Gold = "#ffd24a", Orange = "#ff9a3a", Grey = "#c8c8c8";

        // Tooltip no formato do cliente original (linhas centralizadas); requisitos nao cumpridos em vermelho.
        string ItemTip(int id, TOP.Inventory.InventoryItem inst = null)
        {
            if (id <= 0 || !PkoTables.Items.TryGetValue(id, out var it)) return null;
            var sb = new StringBuilder();
            void Line(string text, string color = null) { if (color != null) sb.Append("<color=").Append(color).Append('>').Append(text).Append("</color>"); else sb.Append(text); sb.Append('\n'); }
            void Stat(string label, int v) { if (v != 0) Line(label + " Bonus:" + (v > 0 ? "+" : "") + v, Blue); }
            void Pct(string label, int tenths) { if (tenths > 0) Line(label + " Bonus:+" + (tenths / 10f).ToString("0.#") + "%", Blue); }

            int refine = inst != null ? inst.RefineLevel : 0;
            Line((it.Level > 0 ? "Lv" + it.Level + " " : "") + it.Name + (refine > 0 ? "+" + refine : ""), Gold);
            if (PkoTables.ItemTypes.TryGetValue(it.Type, out var typeName)) Line("(" + typeName + ")", Orange);

            var add = inst != null && PkoGems.CanSocket(it) ? PkoGems.InstanceBonus(inst) : default;
            if (it.MaxAtk > 0) Line("Attack (" + (it.MinAtk + add.Atk) + " - " + (it.MaxAtk + add.Atk) + ")");
            if (it.Def > 0) Line("Defense (+" + (it.Def + add.Def) + ")");
            if (it.Durability > 0)
            {
                int cur = inst != null && inst.Durability > 0 ? Mathf.Min(inst.Durability, it.Durability) : it.Durability;
                Line("Durability (" + cur + "/" + it.Durability + ")");
            }
            if (it.Resist > 0) Line("Physical Resist (+" + it.Resist + ")");

            int lvl = pc != null ? pc.Level : 0;
            bool equipable = it.EquipSlots.Length > 0;
            if (it.Level > 1) Line("Level Requirement: " + it.Level, lvl >= it.Level ? null : Red);
            if (equipable)
            {
                if (it.Races.Length > 0)
                    Line("Character Requirement: " + string.Join(" ", System.Array.ConvertAll(it.Races, PkoClasses.RaceName)), pc == null || PkoClasses.RaceOk(it, pc.Job) ? null : Red);
                if (it.Classes.Length > 0)
                    Line("Class Requirement: " + PkoClasses.NameList(it.Classes), cls == null || PkoClasses.Allows(it.Classes, cls.CurrentClass) ? null : Red);
            }

            Pct("Defense", it.PctDef); Pct("Maximum HP", it.PctHp); Pct("Critical", it.PctCrit);
            Stat("Strength", it.Str); Stat("Agility", it.Agi); Stat("Accuracy", it.Acc);
            Stat("Constitution", it.Con); Stat("Spirit", it.Spr);
            Stat("Maximum HP", it.Hp); Stat("Maximum SP", it.Sp);
            Stat("Hit", it.Hit); Stat("Flee", it.Flee); Stat("Critical", it.Crit); Stat("Movement Speed", it.MoveSpeed);
            Stat("HP recovery speed", it.HpRec); Stat("SP Recovery rate", it.SpRec);

            if (inst != null && PkoGems.CanSocket(it) && inst.SocketCount > 0)
            {
                Line("Socket: " + inst.SocketCount);
                for (int i = 0; i < PkoGems.MaxSockets; i++)
                {
                    int g = inst.Gems[i];
                    if (g <= 0) continue;
                    Line(PkoTables.Stones.TryGetValue(g, out var st2) ? st2.Name : "?", Gold);
                }
                string gb = add.ToString();
                if (!add.IsZero) foreach (var part in gb.Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries)) Line("Gem Bonus " + part.Trim(), Blue);
            }
            if (it.Price > 0) Line("Trade Value: " + it.Price.ToString("N0"), Gold);
            if (!string.IsNullOrEmpty(it.Description)) Line(it.Description, Grey);
            return sb.ToString().TrimEnd('\n');
        }
        void AskDelete(int index)
        {
            var item = inv != null ? inv.GetSlot(index) : null; if (item == null || item.IsEmpty || item.IsEquipped) return;
            string name = PkoTables.Items.TryGetValue(item.ItemId, out var it) ? it.Name : "item";
            ui.Confirm("Delete " + name + (item.Quantity > 1 ? " x" + item.Quantity : "") + "?\nThis cannot be undone.", () => inv.CmdDeleteItem((ushort)index));
        }

        void InvClicked(PkoSlot s, int button, int clicks)
        {
            if (inv == null || !s.Filled) return;
            if (button == 1 || clicks >= 2) UseInventoryItem(s.Index);
        }

        void UseInventoryItem(int index)
        {
            var item = inv.GetSlot(index); if (item == null || item.IsEmpty) return;
            var data = ItemDatabase.Instance != null ? ItemDatabase.Instance.GetItem(item.ItemId) : null;
            if (data is EquipmentData e) inv.CmdEquipItem((ushort)index, e.slot);
            else inv.CmdUseItem((ushort)index);
        }

        void EquipClicked(PkoSlot s, int button, int clicks)
        {
            if (inv == null || !s.Filled) return;
            if (button == 1 || clicks >= 2) inv.CmdUnequipItem((EquipmentSlot)s.Index);
        }

        void SlotDropped(PkoSlot from, PkoSlot to)
        {
            if (inv == null) return;
            if (from.Group == "inv" && to.Group == "inv") inv.CmdMoveItem((ushort)from.Index, (ushort)to.Index);
            else if (from.Group == "inv" && to.Group == "equip") inv.CmdEquipItem((ushort)from.Index, (EquipmentSlot)to.Index);
            else if (from.Group == "equip" && to.Group == "inv") inv.CmdUnequipItemTo((EquipmentSlot)from.Index, (ushort)to.Index);
        }

        void RefreshInventory()
        {
            if (invW == null || !invW.IsOpen || inv == null) return;
            if (invW.Grids.TryGetValue("grdItem", out var grid))
                foreach (var s in grid)
                {
                    var item = s.Index < inv.totalSlots ? inv.GetSlot(s.Index) : null;
                    int id = item != null && !item.IsEmpty && !item.IsEquipped ? item.ItemId : 0;
                    s.SetIcon(ItemIcon(id), item != null && item.Quantity > 1 ? item.Quantity.ToString() : null);
                }
            foreach (var kv in EquipMap)
            {
                var s = invW.Get<PkoSlot>(kv.Key); if (s == null) continue;
                var it = eq != null ? eq.GetEquippedItem(kv.Value) : null;
                s.SetIcon(it != null && !it.IsEmpty ? ItemIcon(it.ItemId) : null);
            }
            invW.SetText("labItemgoldnumber", pc != null ? pc.Gold.ToString("N0") : "0");
            invW.SetText("labItemIMPnumber", "0");
        }

        // ---------- status ----------
        void SetupState()
        {
            stateW = ui.Get("frmState");
            var map = new Dictionary<string, int> { { "btnStr", 0 }, { "btnAgi", 1 }, { "btnCon", 2 }, { "btnSta", 3 }, { "btnDex", 4 } };
            foreach (var kv in map) { int stat = kv.Value; stateW.OnClick(kv.Key, () => pc?.CmdSpendStatPoint(stat)); }
        }

        void RefreshState()
        {
            if (stateW == null || !stateW.IsOpen || pc == null) return;
            var w = stateW;
            w.SetText("labStateName", pc.CharacterName); w.SetText("labStateJob", cls != null ? cls.CurrentClass.ToString() : "");
            w.SetText("labStateGuid", "-");
            w.SetText("labStateLevel", pc.Level.ToString()); w.SetText("labStateEXP", pc.Exp + "/" + pc.ExperienceToNextLevel);
            w.SetText("labSailLevel", "1"); w.SetText("labSailEXP", "0");
            w.SetText("labStateHP", pc.CurrentHp + "/" + pc.MaxHp); w.SetText("labStateSP", pc.CurrentMp + "/" + pc.MaxMp);
            w.SetText("labStatePoint", pc.StatPoints.ToString()); w.SetText("labSkillPoint", pc.SkillPoints.ToString());
            if (st != null)
            {
                w.SetText("labStrshow", st.Strength.ToString()); w.SetText("labAgishow", st.Agility.ToString());
                w.SetText("labConshow", st.Constitution.ToString()); w.SetText("labStashow", st.Spirit.ToString());
                w.SetText("labDexshow", st.StaminaStat.ToString());
                w.SetText("labMinAtackShow", st.PhysicalAttack.ToString()); w.SetText("labMaxAtackShow", st.PhysicalAttack.ToString());
                w.SetText("labDefenceShow", st.PhysicalDefense.ToString()); w.SetText("labPhysDefineShow", st.PhysicalDefense.ToString());
                w.SetText("labAspeedShow", st.AttackSpeed.ToString("0.00")); w.SetText("labMspeedShow", st.MoveSpeed.ToString("0.0"));
                w.SetText("labHitShow", "-"); w.SetText("labFleeShow", "-");
            }
            w.SetText("labFameShow", pc.Reputation.ToString()); w.SetText("labBattlepoints", pc.PkPoints.ToString());
            bool pts = pc.StatPoints > 0;
            foreach (var b in new[] { "btnStr", "btnAgi", "btnCon", "btnSta", "btnDex" }) w.SetVisible(b, pts);
        }

        // ---------- skills ----------
        void SetupSkills()
        {
            skillW = ui.Get("frmSkill");
            skillW.TabChanged += (_, page, key) => { skillTab = skillW.Tabs.FindIndex(t => t.Key == key); BuildSkillList(); };
            skillW.Opened += _ => BuildSkillList();
            skillTab = 0;
        }

        static int PkoClassId(CharacterClass c)
        {
            switch (c) { case CharacterClass.Swordsman: return 1; case CharacterClass.Hunter: return 2; case CharacterClass.Explorer: return 4; case CharacterClass.Champion: return 8;
                case CharacterClass.Crusader: return 10; case CharacterClass.Sharpshooter: return 12; case CharacterClass.Cleric: return 13; case CharacterClass.Voyager: return 16; default: return 0; }
        }

        bool SkillVisible(PkoSkill s, int tab)
        {
            bool sail = s.ClassMaxLevel.ContainsKey(16);
            if (tab == 1) return s.IsLife;
            if (tab == 2) return sail && !s.IsLife;
            if (s.IsLife || sail) return false;
            int cid = cls != null ? PkoClassId(cls.CurrentClass) : 0;
            return cid == 0 || s.ClassMaxLevel.Count == 0 || s.ClassMaxLevel.ContainsKey(cid) || s.ClassMaxLevel.ContainsKey(1) && cid == 1;
        }

        void BuildSkillList()
        {
            if (skillW == null) return;
            foreach (var n in new[] { "lstSkill", "lstSkillW", "lstSkillS" }) skillW.SetVisible(n, false);
            string listName = skillTab == 1 ? "lstSkillW" : skillTab == 2 ? "lstSkillS" : "lstSkill";
            skillW.SetVisible(listName, true);
            var content = ui.ContentOf(skillW, listName); if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
            var rows = new List<PkoSkill>();
            foreach (var s in PkoTables.Skills.Values) if (SkillVisible(s, skillTab)) rows.Add(s);
            rows.Sort((a, b) => a.LearnLevel != b.LearnLevel ? a.LearnLevel.CompareTo(b.LearnLevel) : a.Id.CompareTo(b.Id));
            foreach (var s in rows) BuildSkillRow(content, s);
            RefreshSkills();
        }

        readonly Dictionary<int, Text> skillLevelLabels = new Dictionary<int, Text>();
        readonly Dictionary<int, GameObject> skillLearnButtons = new Dictionary<int, GameObject>();

        void BuildSkillRow(Transform parent, PkoSkill s)
        {
            var row = new GameObject("skill" + s.Id, typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            row.transform.SetParent(parent, false); row.GetComponent<LayoutElement>().preferredHeight = 38; row.GetComponent<Image>().color = new Color(0, 0, 0, .18f);
            var slotGo = new GameObject("Slot", typeof(RectTransform)); slotGo.transform.SetParent(row.transform, false);
            var srt = (RectTransform)slotGo.transform; srt.anchorMin = srt.anchorMax = new Vector2(0, .5f); srt.pivot = new Vector2(0, .5f); srt.anchoredPosition = new Vector2(3, 0); srt.sizeDelta = new Vector2(32, 32);
            var bg = slotGo.AddComponent<Image>(); bg.color = new Color(0, 0, 0, 0);
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(RawImage)); icon.transform.SetParent(slotGo.transform, false);
            var irt = (RectTransform)icon.transform; irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one; irt.offsetMin = irt.offsetMax = Vector2.zero;
            var raw = icon.GetComponent<RawImage>(); raw.raycastTarget = false; raw.texture = SkillIcon(s); raw.enabled = raw.texture != null;
            var cnt = ui.MakeLabel(slotGo.transform, "", 9, TextAnchor.LowerRight); cnt.rectTransform.anchorMin = Vector2.zero; cnt.rectTransform.anchorMax = Vector2.one; cnt.rectTransform.offsetMin = cnt.rectTransform.offsetMax = Vector2.zero;
            var slot = slotGo.AddComponent<PkoSlot>(); slot.Group = "skill"; slot.Index = s.Id; slot.Icon = raw; slot.Count = cnt; slot.Window = skillW; slot.Filled = raw.enabled;
            slot.Tip = () => SkillTip(s);
            slot.Clicked = (sl, b, c) => { if (c >= 2 && sk != null && sk.GetSkillLevel(s.Id) > 0) sk.TryUseSkill(s.Id); };

            var name = ui.MakeLabel(row.transform, s.Name, 11); var nrt = name.rectTransform;
            nrt.anchorMin = new Vector2(0, .5f); nrt.anchorMax = new Vector2(1, .5f); nrt.offsetMin = new Vector2(42, 0); nrt.offsetMax = new Vector2(-30, 14); name.color = Color.white;
            var lv = ui.MakeLabel(row.transform, "", 10); var lrt = lv.rectTransform;
            lrt.anchorMin = new Vector2(0, .5f); lrt.anchorMax = new Vector2(1, .5f); lrt.offsetMin = new Vector2(42, -14); lrt.offsetMax = new Vector2(-30, 0); lv.color = new Color(.9f, .9f, .6f);
            skillLevelLabels[s.Id] = lv;

            var btn = new GameObject("Learn", typeof(RectTransform), typeof(Image), typeof(Button)); btn.transform.SetParent(row.transform, false);
            var brt = (RectTransform)btn.transform; brt.anchorMin = brt.anchorMax = new Vector2(1, .5f); brt.pivot = new Vector2(1, .5f); brt.anchoredPosition = new Vector2(-4, 0); brt.sizeDelta = new Vector2(20, 20);
            btn.GetComponent<Image>().color = new Color(.2f, .55f, .2f, .9f);
            var plus = ui.MakeLabel(btn.transform, "+", 14, TextAnchor.MiddleCenter); plus.rectTransform.anchorMin = Vector2.zero; plus.rectTransform.anchorMax = Vector2.one; plus.rectTransform.offsetMin = plus.rectTransform.offsetMax = Vector2.zero;
            btn.GetComponent<Button>().onClick.AddListener(() => sk?.CmdLearnSkill(s.Id));
            skillLearnButtons[s.Id] = btn;
        }

        Texture2D SkillIcon(PkoSkill s)
        {
            return ui.Icon(s.Icon) ?? ui.Icon("s" + s.Id.ToString("D4"));
        }

        static string SkillTip(PkoSkill s)
        {
            var sb = new StringBuilder(s.Name);
            if (s.LearnLevel > 0) sb.Append("\nRequired level ").Append(s.LearnLevel);
            if (s.SpCost > 0) sb.Append("\nSP ").Append(s.SpCost);
            if (s.CooldownMs > 0) sb.Append("\nCooldown ").Append(s.CooldownMs / 1000f).Append("s");
            if (!string.IsNullOrEmpty(s.Description)) sb.Append('\n').Append(s.Description);
            return sb.ToString();
        }

        void RefreshSkills()
        {
            if (skillW == null || !skillW.IsOpen || pc == null) return;
            skillW.SetText("labPoint", pc.SkillPoints.ToString()); skillW.SetText("labPoint1", "");
            foreach (var kv in skillLevelLabels)
            {
                if (kv.Value == null) continue;
                var s = PkoTables.Skills[kv.Key]; int lv = sk != null ? sk.GetSkillLevel(kv.Key) : 0;
                int max = 1; foreach (var v in s.ClassMaxLevel.Values) max = Mathf.Max(max, v);
                kv.Value.text = $"Lv {lv}/{max}   (Req. {s.LearnLevel})";
                if (skillLearnButtons.TryGetValue(kv.Key, out var b) && b != null)
                    b.SetActive(lv < max && pc.Level >= s.LearnLevel && pc.SkillPoints >= Mathf.Max(1, s.Points));
                var slot = kv.Value.transform.parent.Find("Slot")?.GetComponent<PkoSlot>();
                if (slot != null) { slot.Icon.color = lv > 0 ? Color.white : new Color(.5f, .5f, .5f, 1f); slot.Draggable = lv > 0; }
            }
        }

        // ---------- hotbar ----------
        void HotbarClicked(PkoSlot s, int button, int clicks)
        {
            if (hb == null) return;
            if (button == 1) { hb.CmdClearSlot(s.Index); return; }
            var slot = hb.GetSlot(s.Index);
            if (slot.type == HotbarSlotType.Skill) sk?.TryUseSkill(slot.id);
            else if (slot.type == HotbarSlotType.Item) { int i = inv != null ? inv.FindItemSlot(slot.id) : -1; if (i >= 0) UseInventoryItem(i); }
        }

        void HotbarDropped(PkoSlot from, PkoSlot to)
        {
            if (hb == null) return;
            if (from.Group == "skill") hb.CmdSetSlot(to.Index, new HotbarSlot { type = HotbarSlotType.Skill, id = from.Index });
            else if (from.Group == "inv") { int id = SlotItemId(from); if (id > 0) hb.CmdSetSlot(to.Index, new HotbarSlot { type = HotbarSlotType.Item, id = id }); }
            else if (from.Group == "hotbar") { var a = hb.GetSlot(from.Index); var b = hb.GetSlot(to.Index); hb.CmdSetSlot(to.Index, a); hb.CmdSetSlot(from.Index, b); }
        }

        string HotbarTip(int i)
        {
            if (hb == null) return null; var s = hb.GetSlot(i);
            if (s.type == HotbarSlotType.Skill && PkoTables.Skills.TryGetValue(s.id, out var sk2)) return SkillTip(sk2);
            if (s.type == HotbarSlotType.Item) return ItemTip(s.id);
            return null;
        }

        void RefreshHotbar()
        {
            if (fast == null || hb == null) return;
            for (int i = 0; i < 12; i++)
            {
                var slot = fast.Get<PkoSlot>("fscMainF" + i); if (slot == null) continue;
                var h = hb.GetSlot(i); Texture tex = null;
                if (h.type == HotbarSlotType.Skill && PkoTables.Skills.TryGetValue(h.id, out var ps)) tex = SkillIcon(ps);
                else if (h.type == HotbarSlotType.Item) tex = ItemIcon(h.id);
                slot.SetIcon(tex, i < 9 ? "" : "");
            }
        }

        // ---------- loop ----------
        void FindPlayer()
        {
            var lp = NetworkClient.localPlayer; if (lp == null) return;
            pc = lp.GetComponent<PlayerController>(); inv = lp.GetComponent<PlayerInventory>(); eq = lp.GetComponent<PlayerEquipment>();
            sk = lp.GetComponent<PlayerSkills>(); hb = lp.GetComponent<PlayerHotbar>(); st = lp.GetComponent<PlayerStats>(); cls = lp.GetComponent<PlayerClass>();
            if (pc != null) { detail?.SetText("labMainID", pc.CharacterName); chat?.Get<PkoLog>("lstOnSay")?.Add("Entered " + pc.MapName + "."); }
        }

        void Update()
        {
            if (pc == null) { FindPlayer(); if (pc == null) return; }
            if (Input.GetKeyDown(KeyCode.F10)) browser = !browser;
            if (Input.GetKeyDown(KeyCode.Return) && !(EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null))
            {
                var e = chat.Get<InputField>("edtSay"); if (e != null) { EventSystem.current.SetSelectedGameObject(e.gameObject); e.ActivateInputField(); }
            }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .1f;

            detail.SetProgress("proMainHP1", pc.MaxHp > 0 ? (float)pc.CurrentHp / pc.MaxHp : 0, HpSpText(pc.CurrentHp, pc.MaxHp));
            detail.SetProgress("proMainSP", pc.MaxMp > 0 ? (float)pc.CurrentMp / pc.MaxMp : 0, HpSpText(pc.CurrentMp, pc.MaxMp));
            ulong need = pc.ExperienceToNextLevel;
            detail.SetProgress("proMainEXP", need > 0 ? Mathf.Clamp01((float)pc.Exp / need) : 0, need > 0 ? (pc.Exp * 100 / need) + "%" : "");
            detail.SetText("labMainLv", pc.Level.ToString()); detail.SetText("labMainID", pc.CharacterName);
            if (mini != null)
            {
                mini.SetText("labMapName", pc.MapName);
                var p = pc.transform.position; mini.SetText("labMapPos0", Mathf.RoundToInt(p.x) + ", " + Mathf.RoundToInt(p.z));
                mini.SetText("labClock", System.DateTime.Now.ToString("HH:mm:ss"));
                if (minimapView != null)
                {
                    if (minimapSrc == null) minimapSrc = FindAnyObjectByType<MinimapRenderer>();
                    if (minimapSrc != null && minimapSrc.Texture != null) { minimapView.texture = minimapSrc.Texture; minimapView.color = Color.white; }
                }
            }
            RefreshHotbar(); RefreshInventory(); RefreshState(); RefreshSkills();
        }

        void OnGUI()
        {
            if (!browser) return;
            GUILayout.BeginArea(new Rect(10, 10, 230, Screen.height - 20), GUI.skin.box);
            GUILayout.Label("Janelas PKO (F10 fecha)");
            browserScroll = GUILayout.BeginScrollView(browserScroll);
            foreach (var n in allNames)
            {
                var w = ui.Get(n);
                if (GUILayout.Button((w != null && w.IsOpen ? "* " : "") + n + "  (" + ui.Defs[n].file.Replace(".clu", "") + ")")) ui.Toggle(n);
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
    }
}
