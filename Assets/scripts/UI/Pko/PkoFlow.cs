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
        Canvas bgCanvas, statusCanvas, loginCanvas;
        Text status;
        readonly List<PkoWindow> opened = new List<PkoWindow>();

        // login (card proprio, reproduz pixel-a-pixel o modelo "login_exact_pixel_v2" enviado pelo usuario)
        GameObject loginCard, registerCard;
        InputField idField, pwField, regIdField, regPwField, regPw2Field, regEmailField;
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
            if (loginCanvas != null) Destroy(loginCanvas.gameObject);
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

            // Na tela de login usamos a arte nova (doca/ilhas); nas demais mantemos o gradiente simples de sempre.
            var customBg = scene == LoginSceneName ? Resources.Load<Texture2D>("UI/LoginBackground") : null;
            if (customBg != null)
            {
                r.texture = customBg;
                var rt = r.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.pivot = new Vector2(.5f, .5f);
                // EnvelopeParent = mesmo comportamento de "background-size: cover" do CSS: preenche a tela
                // inteira preservando a proporcao da imagem (corta as bordas em vez de distorcer).
                var fit = bg.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = (float)customBg.width / customBg.height;
            }
            else
            {
                var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(new Color(.02f, .05f, .12f), new Color(.09f, .22f, .38f), y / 63f));
                tex.Apply(); r.texture = tex;
                var rt = r.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            }

            statusCanvas = MakeCanvas("FlowStatus", 60);
            var go = new GameObject("Status", typeof(Text)); go.transform.SetParent(statusCanvas.transform, false);
            status = go.GetComponent<Text>(); status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            status.fontSize = 14; status.alignment = TextAnchor.MiddleCenter; status.color = Color.white; status.raycastTarget = false;
            var sr = status.rectTransform; sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(1, 0); sr.pivot = new Vector2(.5f, 0);
            sr.sizeDelta = new Vector2(0, 28); sr.anchoredPosition = new Vector2(0, 14);
        }

        static Canvas MakeCanvas(string n, int order)
        {
            var go = new GameObject(n, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
        // Card proprio (nao usa mais login.clu/frmAccount ou frmRegister): desenhado em cima da arte de
        // fundo nova (doca/ilhas), com estilo similar (painel azul-marinho translucido + borda dourada).
        static readonly Color CardBg = new Color(0.035f, 0.08f, 0.16f, 0.88f);
        static readonly Color CardBorder = new Color(0.75f, 0.6f, 0.28f, 0.95f);
        static readonly Color Gold = new Color(0.95f, 0.84f, 0.55f);
        static readonly Color FieldBg = new Color(0.02f, 0.05f, 0.1f, 0.65f);
        static readonly Color LoginBlue = new Color(0.16f, 0.46f, 0.85f);
        static readonly Color ButtonSlate = new Color(0.12f, 0.18f, 0.3f, 0.9f);

        void SetupLogin()
        {
            loginClient = LoginNetworkClient.Instance;
            if (loginClient == null) loginClient = new GameObject("LoginNetworkClient").AddComponent<LoginNetworkClient>();
            loginClient.OnLoginSuccess += OnLoginOk; loginClient.OnRegisterSuccess += OnRegisterOk; loginClient.OnError += OnLoginError;

            loginCanvas = MakeCanvas("LoginCanvas", 50);

            BuildLoginCard();
            BuildRegisterCard();
            registerCard.SetActive(false);

            var savedUser = PlayerPrefs.GetString(PrefUser, "");
            if (!string.IsNullOrEmpty(savedUser)) idField.text = savedUser;

            Say("Informe sua conta para entrar.");
        }

        // Dimensoes exatas do modelo fornecido (login_exact_pixel_v2): painel de referencia 313x329px.
        // Todas as posicoes abaixo sao as mesmas porcentagens usadas no CSS original (left/top/width/height
        // relativos ao painel), convertidas para ancoras do RectTransform (0..1) - por isso escalam
        // perfeitamente com a tela sem perder o alinhamento pixel-a-pixel do design.
        const float PanelRefW = 313f, PanelRefH = 329f;
        static readonly Color FieldTextVisible = new Color(0.788f, 0.765f, 0.741f); // #c9c3bd
        static readonly Color FieldCaret = new Color(0.909f, 0.847f, 0.733f); // #e8d8bb

        static Texture2D LoginTex(string name) => Resources.Load<Texture2D>("UI/Login/" + name);

        void BuildLoginCard()
        {
            const float PanelW = 320f;
            const float PanelH = PanelW * PanelRefH / PanelRefW;

            loginCard = new GameObject("LoginCard", typeof(RectTransform));
            loginCard.transform.SetParent(loginCanvas.transform, false);
            var cardRt = (RectTransform)loginCard.transform;
            cardRt.anchorMin = cardRt.anchorMax = new Vector2(.5f, .5f); cardRt.pivot = new Vector2(.5f, .5f);
            cardRt.sizeDelta = new Vector2(PanelW, PanelH);

            // Moldura/titulo "Login" ja fazem parte da arte (panel.png) - nao sao textos separados.
            var art = new GameObject("PanelArt", typeof(RawImage)); art.transform.SetParent(cardRt, false);
            var artImg = art.GetComponent<RawImage>(); artImg.texture = LoginTex("panel"); artImg.raycastTarget = false;
            SetAnchors((RectTransform)art.transform, 0, 1, 0, 1);

            // left=35 top=121 w=229 h=35 (313x329)
            idField = BuildExactField(cardRt, "Account", LoginTex("account_original"), LoginTex("account_blank"),
                0.111821f, 0.843450f, 0.525836f, 0.632219f, leftPad: 0.172f, rightPad: 0.05f, password: false);

            // left=35 top=164 w=229 h=35
            pwField = BuildExactField(cardRt, "Password", LoginTex("password_original"), LoginTex("password_blank"),
                0.111821f, 0.843450f, 0.395137f, 0.501520f, leftPad: 0.172f, rightPad: 0.15f, password: true);
            BuildEyeToggle(pwField);

            // left=23 top=212 w=251 h=47
            var loginBtn = BuildExactButton(cardRt, LoginTex("login_button"), LoginTex("login_button_pressed"),
                0.073482f, 0.875399f, 0.212766f, 0.355623f);
            loginBtn.onClick.AddListener(DoLogin);

            // left=37 top=267 w=105 h=28
            var registerBtn = BuildExactButton(cardRt, LoginTex("register_button"), LoginTex("register_button_pressed"),
                0.118211f, 0.453674f, 0.103343f, 0.188450f);
            registerBtn.onClick.AddListener(OpenRegister);

            // left=157 top=267 w=104 h=28
            var exitBtn = BuildExactButton(cardRt, LoginTex("exit_button"), LoginTex("exit_button_pressed"),
                0.501597f, 0.833866f, 0.103343f, 0.188450f);
            exitBtn.onClick.AddListener(Application.Quit);
        }

        static void SetAnchors(RectTransform rt, float xMin, float xMax, float yMin, float yMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin); rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = rt.offsetMax = Vector2.zero; rt.pivot = new Vector2(.5f, .5f);
        }

        // Campo "image-exact": mostra a arte original (com o rotulo/icone ja desenhados) enquanto vazio e sem
        // foco; ao focar ou digitar, troca para a arte "blank" (sem rotulo) e revela o texto real digitado -
        // mesmo comportamento do field.editing no CSS/JS original.
        static InputField BuildExactField(Transform parent, string name, Texture2D original, Texture2D blank,
            float xMin, float xMax, float yMin, float yMax, float leftPad, float rightPad, bool password)
        {
            var go = new GameObject("Field_" + name, typeof(RawImage), typeof(InputField));
            go.transform.SetParent(parent, false);
            SetAnchors((RectTransform)go.transform, xMin, xMax, yMin, yMax);
            var bg = go.GetComponent<RawImage>(); bg.texture = original;

            var textGo = new GameObject("Text", typeof(Text)); textGo.transform.SetParent(go.transform, false);
            var text = textGo.GetComponent<Text>(); text.font = F(); text.fontSize = 16; text.color = new Color(1, 1, 1, 0);
            text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
            SetAnchors((RectTransform)textGo.transform, leftPad, 1f - rightPad, 0f, 1f);

            var input = go.GetComponent<InputField>();
            input.textComponent = text; input.targetGraphic = bg; input.transition = Selectable.Transition.None;
            input.customCaretColor = true; input.caretColor = FieldCaret;
            if (password) input.contentType = InputField.ContentType.Password;

            var sync = go.AddComponent<ExactFieldSync>();
            sync.Init(bg, original, blank, input, text, FieldTextVisible);
            return input;
        }

        // Area invisivel sobre o olho ja desenhado na arte do campo de senha (so alterna mostrar/ocultar).
        static void BuildEyeToggle(InputField pwField)
        {
            var go = new GameObject("EyeHit", typeof(Image), typeof(Button));
            go.transform.SetParent(pwField.transform, false);
            var img = go.GetComponent<Image>(); img.color = new Color(0, 0, 0, 0);
            SetAnchors((RectTransform)go.transform, 0.828f, 0.978f, 0.08f, 0.92f);
            var btn = go.GetComponent<Button>(); btn.transition = Selectable.Transition.None; btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                bool shown = pwField.contentType == InputField.ContentType.Standard;
                pwField.contentType = shown ? InputField.ContentType.Password : InputField.ContentType.Standard;
                pwField.ForceLabelUpdate();
            });
        }

        // Botao "image-exact": troca para a arte "pressed" durante o clique, igual ao :active do CSS.
        static Button BuildExactButton(Transform parent, Texture2D normal, Texture2D pressed,
            float xMin, float xMax, float yMin, float yMax)
        {
            var go = new GameObject("Button", typeof(RawImage), typeof(Button));
            go.transform.SetParent(parent, false);
            SetAnchors((RectTransform)go.transform, xMin, xMax, yMin, yMax);
            var img = go.GetComponent<RawImage>(); img.texture = normal;
            var btn = go.GetComponent<Button>(); btn.transition = Selectable.Transition.None; btn.targetGraphic = img;
            go.AddComponent<ExactButtonSwap>().Init(img, normal, pressed);
            return btn;
        }

        void BuildRegisterCard()
        {
            var cardRt = MakeCard(loginCanvas.transform, "RegisterCard", 300, 460, out registerCard);

            MakeLabel(cardRt, "Criar conta", 26, Gold, FontStyle.Bold, TextAnchor.MiddleCenter, 260, 40, -14);

            regIdField = MakeInput(cardRt, "Conta (3-16 caracteres)", false, 260, 40, -68);
            regPwField = MakeInput(cardRt, "Senha (min. 4)", true, 260, 40, -116);
            regPw2Field = MakeInput(cardRt, "Confirmar senha", true, 260, 40, -164);
            regEmailField = MakeInput(cardRt, "E-mail (opcional)", false, 260, 40, -212);

            var createBtn = MakeButton(cardRt, "Registrar", LoginBlue, 260, 46, -270);
            createBtn.onClick.AddListener(DoRegister);

            var backBtn = MakeButton(cardRt, "Voltar", ButtonSlate, 260, 40, -326);
            backBtn.onClick.AddListener(() => { registerCard.SetActive(false); loginCard.SetActive(true); Say("Informe sua conta para entrar."); });
        }

        void OpenRegister()
        {
            loginCard.SetActive(false); registerCard.SetActive(true);
            Say("Preencha os dados para criar sua conta.");
        }

        void DoLogin()
        {
            if (busy) return;
            var id = idField.text.Trim(); var pw = pwField.text;
            if (id.Length == 0 || pw.Length == 0) { Say("Informe conta e senha.", true); return; }
            busy = true; Say("Autenticando...");
            loginClient.Login(id, pw);
        }

        void DoRegister()
        {
            if (busy) return;
            string id = regIdField.text.Trim(), p1 = regPwField.text, p2 = regPw2Field.text, mail = regEmailField.text.Trim();
            if (id.Length < 3 || p1.Length < 4) { Say("Conta (min. 3) e senha (min. 4) invalidas.", true); return; }
            if (p1 != p2) { Say("As senhas nao conferem.", true); return; }
            busy = true; Say("Criando conta...");
            loginClient.Register(id, p1, mail);
        }

        void OnLoginOk(string token, long accountId)
        {
            PlayerPrefs.SetString(PrefUser, idField.text.Trim());
            Say("Login ok. Conectando ao servidor...");
            if (TOPNetworkManager.Instance == null)
            {
                busy = false;
                Say("Servico de rede nao encontrado na cena.", true);
                return;
            }

            if (!TOPNetworkManager.Instance.ConnectToGameServer(out string error))
            {
                busy = false;
                Say(error, true);
            }
        }

        void OnRegisterOk()
        {
            busy = false;
            idField.text = regIdField.text.Trim();
            pwField.text = "";
            registerCard.SetActive(false); loginCard.SetActive(true);
            Say("Conta criada! Entre com seus dados.");
        }

        void OnLoginError(string err) { busy = false; Say(string.IsNullOrEmpty(err) ? "Falha na conexao." : err, true); }

        // ---------- helpers de UI do login/registro (sem dependencia do sistema legado login.clu) ----------
        static Font F() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        static RectTransform MakeCard(Transform parent, string name, float w, float h, out GameObject root)
        {
            // Container unico: border e fundo sao filhos dele, entao SetActive(false) no root esconde os dois
            // (antes a borda era criada solta como irmao do fundo e nunca era desativada junto do card).
            root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rootRt = (RectTransform)root.transform; rootRt.anchorMin = rootRt.anchorMax = new Vector2(.5f, .5f);
            rootRt.pivot = new Vector2(.5f, .5f); rootRt.sizeDelta = new Vector2(w, h);

            var borderGo = new GameObject(name + "_Border", typeof(Image)); borderGo.transform.SetParent(root.transform, false);
            var borderImg = borderGo.GetComponent<Image>(); borderImg.color = CardBorder; borderImg.raycastTarget = false;
            var borderRt = (RectTransform)borderGo.transform; borderRt.anchorMin = borderRt.anchorMax = new Vector2(.5f, .5f);
            borderRt.pivot = new Vector2(.5f, .5f); borderRt.sizeDelta = new Vector2(w + 6, h + 6);

            var bgGo = new GameObject(name + "_Bg", typeof(Image)); bgGo.transform.SetParent(root.transform, false);
            var bgImg = bgGo.GetComponent<Image>(); bgImg.color = CardBg;
            var bgRt = (RectTransform)bgGo.transform; bgRt.anchorMin = bgRt.anchorMax = new Vector2(.5f, .5f);
            bgRt.pivot = new Vector2(.5f, .5f); bgRt.sizeDelta = new Vector2(w, h);
            return bgRt;
        }

        static Text MakeLabel(Transform parent, string text, int size, Color color, FontStyle style, TextAnchor anchor, float w, float h, float y)
        {
            var go = new GameObject("Label_" + text, typeof(Text)); go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>(); t.font = F(); t.fontSize = size; t.color = color; t.fontStyle = style;
            t.alignment = anchor; t.text = text; t.raycastTarget = false;
            var r = t.rectTransform; r.anchorMin = r.anchorMax = new Vector2(.5f, 1f); r.pivot = new Vector2(.5f, 1f);
            r.sizeDelta = new Vector2(w, h); r.anchoredPosition = new Vector2(0, y);
            return t;
        }

        static InputField MakeInput(Transform parent, string placeholder, bool password, float w, float h, float y)
        {
            var go = new GameObject("Input_" + placeholder, typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var bgImg = go.GetComponent<Image>(); bgImg.color = FieldBg;
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f); rt.pivot = new Vector2(.5f, 1f);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(0, y);

            var textGo = new GameObject("Text", typeof(Text)); textGo.transform.SetParent(go.transform, false);
            var text = textGo.GetComponent<Text>(); text.font = F(); text.fontSize = 15; text.color = Color.white; text.alignment = TextAnchor.MiddleLeft;
            var tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(12, 2); tr.offsetMax = new Vector2(-36, -2);

            var phGo = new GameObject("Placeholder", typeof(Text)); phGo.transform.SetParent(go.transform, false);
            var ph = phGo.GetComponent<Text>(); ph.font = F(); ph.fontSize = 15; ph.color = new Color(1, 1, 1, .4f);
            ph.fontStyle = FontStyle.Italic; ph.alignment = TextAnchor.MiddleLeft; ph.text = placeholder;
            var pr = ph.rectTransform; pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one; pr.offsetMin = new Vector2(12, 2); pr.offsetMax = new Vector2(-36, -2);

            var input = go.GetComponent<InputField>();
            input.textComponent = text; input.placeholder = ph; input.targetGraphic = bgImg;
            if (password) input.contentType = InputField.ContentType.Password;
            return input;
        }

        static void MakeEyeToggle(InputField pwField)
        {
            var go = new GameObject("EyeToggle", typeof(Image), typeof(Button));
            go.transform.SetParent(pwField.transform, false);
            var img = go.GetComponent<Image>(); img.color = new Color(1, 1, 1, .08f);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(1f, .5f); rt.pivot = new Vector2(1f, .5f);
            rt.sizeDelta = new Vector2(32, 28); rt.anchoredPosition = new Vector2(-4, 0);

            var txtGo = new GameObject("Text", typeof(Text)); txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.GetComponent<Text>(); txt.font = F(); txt.fontSize = 10; txt.color = new Color(1, 1, 1, .8f);
            txt.alignment = TextAnchor.MiddleCenter; txt.text = "Ver"; txt.raycastTarget = false;
            var tr = txt.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;

            var btn = go.GetComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                bool shown = pwField.contentType == InputField.ContentType.Standard;
                pwField.contentType = shown ? InputField.ContentType.Password : InputField.ContentType.Standard;
                txt.text = shown ? "Ver" : "Ocultar";
                pwField.ForceLabelUpdate();
            });
        }

        static Button MakeButton(Transform parent, string label, Color bg, float w, float h, float y, float x = 0)
        {
            var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.color = bg;
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(.5f, 1f); rt.pivot = new Vector2(.5f, 1f);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);

            var txtGo = new GameObject("Text", typeof(Text)); txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.GetComponent<Text>(); txt.font = F(); txt.fontSize = 16; txt.color = Color.white; txt.alignment = TextAnchor.MiddleCenter; txt.text = label; txt.raycastTarget = false;
            var tr = txt.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;

            var btn = go.GetComponent<Button>(); btn.targetGraphic = img;
            return btn;
        }

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
                if (loginCard != null && loginCard.activeSelf) DoLogin();
                else if (registerCard != null && registerCard.activeSelf) DoRegister();
                else if (pwd != null && pwd.IsOpen) ConfirmDelete();
            }
            if (Input.GetKeyDown(KeyCode.F2) && loginCard != null && loginCard.activeSelf) OpenRegister();
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

    // Campo "image-exact": alterna a arte de fundo (com rotulo desenhado) <-> arte "blank" (sem rotulo)
    // conforme o campo esta focado/preenchido, igual ao field.editing do CSS/JS do modelo original.
    class ExactFieldSync : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        static readonly Color Hidden = new Color(1, 1, 1, 0);
        RawImage bg; Texture2D original, blank; InputField field; Text text; Color visibleColor; bool selected;

        public void Init(RawImage bg, Texture2D original, Texture2D blank, InputField field, Text text, Color visibleColor)
        {
            this.bg = bg; this.original = original; this.blank = blank; this.field = field; this.text = text; this.visibleColor = visibleColor;
            field.onValueChanged.AddListener(_ => Sync());
            Sync();
        }

        public void OnSelect(BaseEventData e) { selected = true; Sync(); }
        public void OnDeselect(BaseEventData e) { selected = false; Sync(); }

        void Sync()
        {
            bool editing = selected || (field != null && field.text.Length > 0);
            if (bg != null) bg.texture = editing ? blank : original;
            if (text != null) text.color = editing ? visibleColor : Hidden;
        }
    }

    // Botao "image-exact": troca para a arte "pressed" enquanto o ponteiro esta pressionado, igual ao :active do CSS.
    class ExactButtonSwap : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        RawImage img; Texture2D normal, pressed;

        public void Init(RawImage img, Texture2D normal, Texture2D pressed)
        {
            this.img = img; this.normal = normal; this.pressed = pressed; img.texture = normal;
        }

        public void OnPointerDown(PointerEventData e) { if (img != null) img.texture = pressed; }
        public void OnPointerUp(PointerEventData e) { if (img != null) img.texture = normal; }
        public void OnPointerExit(PointerEventData e) { if (img != null) img.texture = normal; }
    }
}
