using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mirror;
using TOP.CharacterSelect;
using TOP.Core;
using TOP.Data;
using TOP.Network;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TOP.UI.Pko
{
    /// <summary>
    /// Fluxo de entrada do jogo com as telas originais (login.clu, selectcha.clu):
    /// login/registro na LoginScene e selecao/criacao/delecao de personagem na CharacterSelectScene.
    /// Toda a persistencia passa pela API (login) e pelo servidor Mirror + banco (personagens).
    /// </summary>
    public class PkoFlow : MonoBehaviour
    {
        const string LoginSceneName = "LoginScene", SelectSceneName = "CharacterSelectScene";
        const string PrefUser = "pko.lastUser";
        static readonly Regex NameRegex = new Regex(@"^[a-zA-Z0-9_]{3,16}$");

        // Dados de criacao: raca original -> job/genero usados pelo servidor.
        static readonly string[] Races = { "Lance", "Carsise", "Phyllis", "Ami" };
        static readonly byte[] RaceGender = { 0, 0, 1, 1 };
        static readonly string[] RaceInfo =
        {
            "Lance: robusto e disciplinado, forte no combate corpo a corpo.",
            "Carsise: agil e preciso, especialista em ataques a distancia.",
            "Phyllis: gentil e sabia, mestre em cura e suporte.",
            "Ami: curiosa e habilidosa, ligada a exploracao e ao mar."
        };
        const int HairCount = 4, FaceCount = 4;

        string scene;
        PkoUi ui;
        Canvas bgCanvas, statusCanvas;
        Text status;
        readonly List<PkoWindow> opened = new List<PkoWindow>();

        // login
        PkoWindow account, register;
        LoginNetworkClient loginClient;
        bool busy;

        // selecao
        PkoWindow select, found, pwd;
        readonly NetworkCharacterPreview[] chars = new NetworkCharacterPreview[3];
        readonly bool[] has = new bool[3];
        readonly RawImage[] views = new RawImage[3];
        readonly Camera[] cams = new Camera[3];
        readonly RenderTexture[] rts = new RenderTexture[3];
        int selSlot, createSlot = -1, race, hair, face, cityIdx;
        bool listRequested, handlersOn;
        float nextTry;
        StartCity[] cities;
        Text desc;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded += (s, _) => Begin(s.name);
            Begin(SceneManager.GetActiveScene().name);
        }

        static void Begin(string sceneName)
        {
            if (sceneName != LoginSceneName && sceneName != SelectSceneName) return;
            foreach (var o in FindObjectsByType<PkoFlow>(FindObjectsSortMode.None)) DestroyImmediate(o.gameObject);
            var go = new GameObject("PkoFlow");
            go.AddComponent<PkoFlow>().scene = sceneName;
        }

        // ---------- ciclo de vida ----------
        void Start()
        {
            ui = PkoUi.Ensure();
            DisableLegacyUi();
            BuildBackdrop();
            if (scene == LoginSceneName) SetupLogin(); else SetupSelect();
        }

        void OnDestroy()
        {
            if (loginClient != null) { loginClient.OnLoginSuccess -= OnLoginOk; loginClient.OnRegisterSuccess -= OnRegisterOk; loginClient.OnError -= OnLoginError; }
            if (handlersOn)
            {
                NetworkClient.UnregisterHandler<CharacterListResponse>();
                NetworkClient.UnregisterHandler<CreateCharacterResponse>();
                NetworkClient.UnregisterHandler<DeleteCharacterResponse>();
                NetworkClient.UnregisterHandler<SelectCharacterResponse>();
            }
            foreach (var w in opened) if (w != null) w.Close();
            for (int i = 0; i < 3; i++)
            {
                if (cams[i] != null) Destroy(cams[i].gameObject);
                if (rts[i] != null) rts[i].Release();
            }
            if (bgCanvas != null) Destroy(bgCanvas.gameObject);
            if (statusCanvas != null) Destroy(statusCanvas.gameObject);
        }

        void DisableLegacyUi()
        {
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.gameObject != ui.Canvas.gameObject) c.enabled = false;
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        void BuildBackdrop()
        {
            bgCanvas = MakeCanvas("FlowBackdrop", 40);
            var bg = new GameObject("Bg", typeof(RawImage)); bg.transform.SetParent(bgCanvas.transform, false);
            var r = bg.GetComponent<RawImage>(); r.raycastTarget = false;
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(new Color(.02f, .05f, .12f), new Color(.09f, .22f, .38f), y / 63f));
            tex.Apply(); r.texture = tex;
            var rt = r.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;

            statusCanvas = MakeCanvas("FlowStatus", 60);
            var go = new GameObject("Status", typeof(Text)); go.transform.SetParent(statusCanvas.transform, false);
            status = go.GetComponent<Text>(); status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            status.fontSize = 14; status.alignment = TextAnchor.MiddleCenter; status.color = Color.white; status.raycastTarget = false;
            var sr = status.rectTransform; sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(1, 0); sr.pivot = new Vector2(.5f, 0);
            sr.sizeDelta = new Vector2(0, 28); sr.anchoredPosition = new Vector2(0, 14);
        }

        static Canvas MakeCanvas(string n, int order)
        {
            var go = new GameObject(n, typeof(Canvas), typeof(CanvasScaler));
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var sc = go.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(800, 600); sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; sc.matchWidthOrHeight = 1f;
            return c;
        }

        void Say(string msg, bool error = false)
        {
            if (status != null) { status.text = msg; status.color = error ? new Color(1f, .45f, .4f) : Color.white; }
        }

        PkoWindow Show(string name, bool center = true)
        {
            var w = ui.Get(name); if (w == null) return null;
            if (center) w.CenterOnScreen();
            w.Open(); if (!opened.Contains(w)) opened.Add(w);
            return w;
        }

        // Estas telas nao tem moldura propria: painel escuro translucido + textos claros para leitura sobre o fundo.
        static void Backplate(PkoWindow w)
        {
            var go = new GameObject("Backplate", typeof(Image)); go.transform.SetParent(w.Rect, false); go.transform.SetAsFirstSibling();
            var im = go.GetComponent<Image>(); im.color = new Color(0, 0, 0, .35f); im.raycastTarget = false;
            var r = im.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(-6, -6); r.offsetMax = new Vector2(6, 6);
            foreach (var t in w.GetComponentsInChildren<Text>(true)) if (t.GetComponentInParent<Button>() == null) t.color = Color.white;
        }

        static InputField Field(PkoWindow w, string n, bool password = false)
        {
            var f = w.Get<InputField>(n);
            if (f != null && password) { f.contentType = InputField.ContentType.Password; f.ForceLabelUpdate(); }
            return f;
        }

        // ---------- LOGIN ----------
        void SetupLogin()
        {
            loginClient = LoginNetworkClient.Instance;
            if (loginClient == null) loginClient = new GameObject("LoginNetworkClient").AddComponent<LoginNetworkClient>();
            loginClient.OnLoginSuccess += OnLoginOk; loginClient.OnRegisterSuccess += OnRegisterOk; loginClient.OnError += OnLoginError;

            account = Show("login.clu/frmAccount");
            Field(account, "edtPassword", true);
            var id = Field(account, "edtID"); if (id != null) id.text = PlayerPrefs.GetString(PrefUser, "");
            var chk = account.Get<Toggle>("chkID"); if (chk != null) chk.isOn = id != null && id.text.Length > 0;
            account.OnClick("btnYes", DoLogin);
            account.OnClick("btnNo", Application.Quit);
            account.OnClick("btnKeyboard", OpenRegister);
            account.SetText("labTitle", "Login");
            account.Draggable = false;

            var link = ui.MakeLabel(account.Rect, "[Criar conta]  (F2)", 11, TextAnchor.MiddleCenter);
            var lr = link.rectTransform; lr.anchorMin = lr.anchorMax = new Vector2(.5f, 0); lr.pivot = new Vector2(.5f, 1);
            lr.sizeDelta = new Vector2(185, 16); lr.anchoredPosition = new Vector2(0, -6);
            link.color = new Color(1f, .9f, .5f); link.raycastTarget = true;
            link.gameObject.AddComponent<PkoClick>().Clicked = _ => OpenRegister();

            register = ui.Get("login.clu/frmRegister"); register.CenterOnScreen(); opened.Add(register);
            Field(register, "edtRegPassword", true); Field(register, "edtRegPassword2", true);
            register.OnClick("btnRegYes", DoRegister);
            register.OnClick("btnRegNo", () => { register.Close(); account.Open(); });
            Say("Informe sua conta para entrar.");
        }

        void OpenRegister() { if (register == null) return; account.Close(); register.Open(); Say("Preencha os dados para criar sua conta."); }

        void DoLogin()
        {
            if (busy) return;
            var id = Field(account, "edtID").text.Trim(); var pw = Field(account, "edtPassword").text;
            if (id.Length == 0 || pw.Length == 0) { Say("Informe conta e senha.", true); return; }
            var chk = account.Get<Toggle>("chkID");
            if (chk != null && chk.isOn) PlayerPrefs.SetString(PrefUser, id); else PlayerPrefs.DeleteKey(PrefUser);
            busy = true; Say("Autenticando...");
            loginClient.Login(id, pw);
        }

        void DoRegister()
        {
            if (busy) return;
            string id = Field(register, "edtRegID").text.Trim(), p1 = Field(register, "edtRegPassword").text,
                   p2 = Field(register, "edtRegPassword2").text, mail = Field(register, "edtRegEmail").text.Trim();
            if (id.Length < 3 || p1.Length < 4) { Say("Conta (min. 3) e senha (min. 4) invalidas.", true); return; }
            if (p1 != p2) { Say("As senhas nao conferem.", true); return; }
            busy = true; Say("Criando conta...");
            loginClient.Register(id, p1, mail);
        }

        void OnLoginOk(string token, long accountId)
        {
            Say("Login ok. Conectando ao servidor...");
            if (TOPNetworkManager.Instance != null) TOPNetworkManager.Instance.StartHost();
            else { busy = false; Say("Servidor de rede nao encontrado na cena.", true); }
        }

        void OnRegisterOk()
        {
            busy = false;
            Field(account, "edtID").text = Field(register, "edtRegID").text.Trim();
            Field(account, "edtPassword").text = "";
            register.Close(); account.Open();
            Say("Conta criada! Entre com seus dados.");
        }

        void OnLoginError(string err) { busy = false; Say(string.IsNullOrEmpty(err) ? "Falha na conexao." : err, true); }

        // ---------- SELECAO ----------
        void SetupSelect()
        {
            cities = StartCities.All;
            NetworkClient.RegisterHandler<CharacterListResponse>(OnList);
            NetworkClient.RegisterHandler<CreateCharacterResponse>(OnCreated);
            NetworkClient.RegisterHandler<DeleteCharacterResponse>(OnDeleted);
            NetworkClient.RegisterHandler<SelectCharacterResponse>(OnSelected);
            handlersOn = true;

            BuildPreviewCameras();
            HideSceneCamera();

            select = Show("selectcha.clu/frmUserselect");
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                views[i] = select.Get<RawImage>("ui3dCha" + (i + 1));
                if (views[i] != null)
                {
                    views[i].texture = rts[i]; views[i].color = Color.white;
                    var click = views[i].gameObject.AddComponent<PkoClick>();
                    click.Clicked = n => { Pick(slot); if (n >= 2) EnterWorld(); };
                    click.Dragged = dx => { if (CharacterPreviewManager.Instance != null) CharacterPreviewManager.Instance.RotateCharacter(slot, -dx * 0.6f); };
                }
            }
            select.OnClick("btnCreate", OpenCreate);
            select.OnClick("btnDel", OpenDelete);
            select.OnClick("btnYes", EnterWorld);
            select.OnClick("btnNo", () => { if (GameFlowManager.Instance != null) GameFlowManager.Instance.Logout(); else SceneManager.LoadScene(LoginSceneName); });
            select.Draggable = false; Backplate(select);
            for (int i = 1; i <= 3; i++) select.SetText("labCha" + i, "");
            nextTry = 0;
            Say("Carregando personagens...");
        }

        // So as cameras de preview (RenderTexture) desenham o 3D; evita objetos soltos aparecendo no fundo.
        static void HideSceneCamera()
        {
            var cam = Camera.main; if (cam == null) return;
            cam.cullingMask = 0; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        }

        void BuildPreviewCameras()
        {
            var pm = CharacterPreviewManager.Instance;
            if (pm == null) { Say("CharacterPreviewManager ausente na cena.", true); return; }
            for (int i = 0; i < 3; i++)
            {
                var slot = pm.GetSlotTransform(i); if (slot == null) continue;
                rts[i] = new RenderTexture(320, 470, 16, RenderTextureFormat.ARGB32);
                var go = new GameObject("PkoPreviewCam" + i); var c = go.AddComponent<Camera>();
                c.targetTexture = rts[i]; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0, 0, 0, 0);
                c.fieldOfView = 28; c.nearClipPlane = .1f; c.farClipPlane = 30;
                go.transform.position = slot.position + new Vector3(0f, 1.2f, -3.4f);
                go.transform.LookAt(slot.position + Vector3.up * 1.0f);
                cams[i] = c;
                pm.RegisterCamera(i, c);
            }
        }

        void Update()
        {
            if (scene == SelectSceneName && !listRequested && Time.unscaledTime >= nextTry)
            {
                nextTry = Time.unscaledTime + .5f;
                if (NetworkClient.connection != null && NetworkClient.connection.isReady) { listRequested = true; NetworkClient.Send(new CharacterListRequest()); }
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (account != null && account.IsOpen) DoLogin();
                else if (register != null && register.IsOpen) DoRegister();
                else if (pwd != null && pwd.IsOpen) ConfirmDelete();
            }
            if (Input.GetKeyDown(KeyCode.F2) && account != null && account.IsOpen) OpenRegister();
        }

        void Pick(int slot) { selSlot = slot; Refresh(false); }

        void Refresh(bool respawn)
        {
            for (int i = 0; i < 3; i++)
            {
                string r = has[i] ? chars[i].Name + "\nLv " + chars[i].Level + "  " + RaceName(chars[i].Job) : "(vazio)";
                select.SetText("labCha" + (i + 1), r);
                if (views[i] != null) views[i].color = i == selSlot ? Color.white : new Color(.55f, .55f, .55f, 1f);
                if (respawn && CharacterPreviewManager.Instance != null)
                {
                    if (has[i]) CharacterPreviewManager.Instance.ShowExistingCharacter(i, ToPreview(chars[i]));
                    else CharacterPreviewManager.Instance.ClearPreview(i);
                }
            }
        }

        static string RaceName(byte job) { return job < Races.Length ? Races[job] : "?"; }

        static CharacterPreviewData ToPreview(NetworkCharacterPreview n)
        {
            return new CharacterPreviewData
            {
                Id = n.Id, SlotIndex = n.SlotIndex, Name = n.Name, Gender = n.Gender, Job = n.Job, Level = n.Level,
                MapName = n.MapName, PosX = n.PosX, PosY = n.PosY, PosZ = n.PosZ, RotationY = n.RotationY,
                HairStyle = n.HairStyle, HairColor = n.HairColor, FaceStyle = n.FaceStyle, Equipped = n.EquippedItems ?? new int[0]
            };
        }

        void OnList(CharacterListResponse r)
        {
            listRequested = true;
            if (!r.Success) { Say("Erro ao listar personagens: " + r.Error, true); return; }
            for (int i = 0; i < 3; i++) has[i] = false;
            if (r.Characters != null)
                foreach (var c in r.Characters) if (c.SlotIndex < 3) { chars[c.SlotIndex] = c; has[c.SlotIndex] = true; }
            if (!has[selSlot]) for (int i = 0; i < 3; i++) if (has[i]) { selSlot = i; break; }
            Refresh(true);
            Say(AnyChar() ? "Escolha um personagem e pressione OK." : "Nenhum personagem. Clique em Create para criar o seu.");
        }

        bool AnyChar() { return has[0] || has[1] || has[2]; }

        void EnterWorld()
        {
            if (busy) return;
            if (!has[selSlot]) { Say("Selecione um personagem.", true); return; }
            busy = true; Say("Entrando no mundo...");
            NetworkClient.Send(new SelectCharacterRequest { CharacterId = chars[selSlot].Id });
        }

        void OnSelected(SelectCharacterResponse r)
        {
            if (!r.Success) { busy = false; Say("Erro ao entrar: " + r.Error, true); return; }
            if (GameFlowManager.Instance != null) GameFlowManager.Instance.EnterGameWorld(r.MapName);
            else SceneManager.LoadScene("GameScene");
        }

        // ---- criacao ----
        void OpenCreate()
        {
            createSlot = -1;
            if (!has[selSlot]) createSlot = selSlot; else for (int i = 0; i < 3; i++) if (!has[i]) { createSlot = i; break; }
            if (createSlot < 0) { Say("Todos os slots estao ocupados.", true); return; }
            if (found == null)
            {
                found = ui.Get("selectcha.clu/frmUserfound");
                found.OnClick("btnLeftStyle", () => { race = (race + Races.Length - 1) % Races.Length; UpdateCreate(true); });
                found.OnClick("btnRightStyle", () => { race = (race + 1) % Races.Length; UpdateCreate(true); });
                found.OnClick("btnLeftHair", () => { hair = (hair + HairCount - 1) % HairCount; UpdateCreate(false); });
                found.OnClick("btnRightHair", () => { hair = (hair + 1) % HairCount; UpdateCreate(false); });
                found.OnClick("btnLeftFace", () => { face = (face + FaceCount - 1) % FaceCount; UpdateCreate(false); });
                found.OnClick("btnRightFace", () => { face = (face + 1) % FaceCount; UpdateCreate(false); });
                found.OnClick("btnLeftCity", () => { cityIdx = (cityIdx + cities.Length - 1) % cities.Length; UpdateCreate(false); });
                found.OnClick("btnRightCity", () => { cityIdx = (cityIdx + 1) % cities.Length; UpdateCreate(false); });
                found.OnClick("btnLeft3d", () => Spin(-45)); found.OnClick("btnRight3d", () => Spin(45));
                found.OnClick("btnYes", DoCreate);
                found.OnClick("btnNo", CloseCreate);
                found.Draggable = false; Backplate(found);
                if (found.Named.TryGetValue("memChaDescribe", out var memo))
                {
                    desc = ui.MakeLabel(memo, "", 11, TextAnchor.UpperLeft);
                    var dr = desc.rectTransform; dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.offsetMin = new Vector2(2, 2); dr.offsetMax = new Vector2(-2, -2);
                    desc.horizontalOverflow = HorizontalWrapMode.Wrap; desc.verticalOverflow = VerticalWrapMode.Truncate;
                    var fit = desc.GetComponent<PkoFitText>(); if (fit != null) Destroy(fit);
                }
                var preview = found.Get<RawImage>("ui3dCreateCha");
                if (preview != null) preview.color = Color.white;
            }
            race = hair = face = cityIdx = 0;
            Field(found, "edtName").text = "";
            select.Close();
            Show("selectcha.clu/frmUserfound");
            UpdateCreate(true);
            Say("Defina seu personagem.");
        }

        void UpdateCreate(bool respawn)
        {
            var city = cities.Length > 0 ? cities[cityIdx] : null;
            found.SetText("labStyleShow", Races[race]);
            found.SetText("labHairShow", "Estilo " + (hair + 1));
            found.SetText("labFaceShow", "Rosto " + (face + 1));
            found.SetText("labCityShow", city == null ? "-" : city.enabled ? city.name : city.name + " (em breve)");
            if (desc != null) desc.text = RaceInfo[race] + (city != null ? "\n\n" + city.description : "");
            var pm = CharacterPreviewManager.Instance;
            var preview = found.Get<RawImage>("ui3dCreateCha");
            if (preview != null && createSlot >= 0) preview.texture = rts[createSlot];
            if (pm != null && createSlot >= 0)
            {
                if (respawn) pm.ShowCreatePreview(createSlot, (byte)race, RaceGender[race], hair, face);
                else pm.ChangeLook(createSlot, (byte)race, hair, face);
            }
        }

        void Spin(float deg)
        {
            var pm = CharacterPreviewManager.Instance; if (pm == null || createSlot < 0) return;
            var t = pm.GetSlotTransform(createSlot); pm.RotateCharacter(createSlot, deg);
        }

        void CloseCreate()
        {
            found.Close();
            if (CharacterPreviewManager.Instance != null && createSlot >= 0 && !has[createSlot]) CharacterPreviewManager.Instance.ClearPreview(createSlot);
            createSlot = -1; select.Open(); Refresh(false);
        }

        void DoCreate()
        {
            if (busy) return;
            string n = Field(found, "edtName").text.Trim();
            if (!NameRegex.IsMatch(n)) { Say("Nome invalido: 3 a 16 letras, numeros ou _.", true); return; }
            var city = cities[cityIdx];
            if (!city.enabled) { Say(city.name + " ainda nao esta disponivel.", true); return; }
            busy = true; Say("Criando personagem...");
            NetworkClient.Send(new CreateCharacterRequest
            {
                SlotIndex = (byte)createSlot, Name = n, Gender = RaceGender[race], Job = (byte)race,
                HairStyle = (byte)hair, HairColor = 0, FaceStyle = (byte)face, StartCity = (byte)city.id
            });
        }

        void OnCreated(CreateCharacterResponse r)
        {
            busy = false;
            if (!r.Success)
            {
                Say(r.Error switch
                {
                    "NAME_TAKEN" => "Esse nome ja esta em uso.",
                    "NAME_INVALID" => "Nome invalido.",
                    "SLOT_OCCUPIED" => "Slot ocupado.",
                    "CITY_UNAVAILABLE" => "Cidade indisponivel.",
                    _ => "Erro ao criar personagem: " + r.Error
                }, true);
                return;
            }
            selSlot = createSlot; found.Close(); select.Open();
            listRequested = false; nextTry = 0;
            Say("Personagem criado!");
        }

        // ---- delecao ----
        void OpenDelete()
        {
            if (!has[selSlot]) { Say("Selecione um personagem para apagar.", true); return; }
            if (pwd == null)
            {
                pwd = ui.Get("login.clu/frmDoublePwdCreate");
                Field(pwd, "edtDoublePwdCreate", true).characterLimit = 0;
                pwd.SetVisible("lab2", false); pwd.SetVisible("edtDoublePwdCreateRetry", false);
                pwd.OnClick("btnYes", ConfirmDelete);
                pwd.OnClick("btnClear", () => pwd.Close());
            }
            pwd.SetText("lab1", "Senha da conta para apagar '" + chars[selSlot].Name + "':");
            Field(pwd, "edtDoublePwdCreate").text = "";
            Show("login.clu/frmDoublePwdCreate");
        }

        void ConfirmDelete()
        {
            if (busy || !has[selSlot]) return;
            string p = Field(pwd, "edtDoublePwdCreate").text;
            if (p.Length == 0) { Say("Informe a senha da conta.", true); return; }
            busy = true; Say("Apagando personagem...");
            NetworkClient.Send(new DeleteCharacterRequest { CharacterId = chars[selSlot].Id, Password = p });
        }

        void OnDeleted(DeleteCharacterResponse r)
        {
            busy = false;
            if (!r.Success) { Say("Nao foi possivel apagar: " + r.Error, true); return; }
            pwd.Close();
            if (CharacterPreviewManager.Instance != null) CharacterPreviewManager.Instance.ClearPreview(selSlot);
            listRequested = false; nextTry = 0;
            Say("Personagem apagado.");
        }
    }
}
