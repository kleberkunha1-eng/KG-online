using System;
using Mirror;
using TOP.Network;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TOP.World
{
    [ExecuteAlways]
    public sealed class WorldEnvironment : MonoBehaviour
    {
        public const float MoonLightIntensity = .12f;
        public WorldEnvironmentSettings settings;
        public bool preview;
        [Range(0, 23.999f)] public float previewHour = 12;
        public EnvironmentControls previewControls;
        public static WorldEnvironment Instance { get; private set; }
        public static string Status { get; private set; } = "";
        public static bool HasServerClock { get; private set; }
        public static bool BlocksMouse
        {
            get
            {
                if (Instance == null || !Application.isPlaying || !LoginNetworkClient.IsAdmin) return false;
                var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                return new Rect(10, 100, 130, 28).Contains(mouse)
                    || Instance.open && Instance.window.Contains(mouse);
            }
        }
        static EnvironmentSnapshot snapshot;
        static double receivedAt;
        static bool subscribed;
        Material sky, previousSky;
        Light sun;
        Light moon;
        bool ownSun;
        Quaternion originalRotation;
        float originalIntensity;
        Color originalColor, originalAmbient;
        Light previousSun;
        Camera worldCamera;
        CameraClearFlags previousClearFlags;
        Skybox cameraSkybox;
        bool previousCameraSkyboxEnabled;
        AmbientMode previousAmbientMode;
        readonly float[] weights = new float[8], target = new float[8];
        float lastTime, nextProbe;
        bool initialized, open;
        Rect window = new Rect(30, 60, 410, 460);
        Vector2 menuScroll;
        EnvironmentControls draft;
        float draftHour;

        public float CurrentHour => preview ? previewHour : EnvironmentCycle.Hour(LocalSeconds);
        public EnvironmentControls Controls => preview ? previewControls
            : HasServerClock ? snapshot.controls : settings.defaults;
        public int ServerUtcOffset => HasServerClock ? snapshot.utcOffsetMinutes : (int)DateTimeOffset.Now.Offset.TotalMinutes;
        double LocalSeconds => HasServerClock
            ? snapshot.localSeconds + NetworkTime.time - snapshot.networkTime
            : DateTimeOffset.Now.ToUnixTimeMilliseconds() / 1000d + DateTimeOffset.Now.Offset.TotalSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            if (subscribed) SceneManager.sceneLoaded -= SceneLoaded;
            subscribed = false;
            Instance = null;
            ResetClock();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded += SceneLoaded;
            subscribed = true;
            SceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
#if !UNITY_SERVER
            if (scene.name != "GameScene" || Instance != null || Dedicated()) return;
            var configuration = Resources.Load<WorldEnvironmentSettings>("WorldEnvironment");
            if (configuration == null)
            {
                Debug.LogError("[Environment] Execute Tools/World/Configurar Fantasy Sky para instalar os materiais.");
                return;
            }
            var root = new GameObject("WorldEnvironment");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.SetActive(false);
            var environment = root.AddComponent<WorldEnvironment>();
            environment.settings = configuration;
            root.SetActive(true);
#endif
        }

        static bool Dedicated() => Array.Exists(System.Environment.GetCommandLineArgs(),
            argument => argument.Equals("--server", StringComparison.OrdinalIgnoreCase));

        public static void Receive(EnvironmentSnapshot state)
        {
            if (!state.controls.IsValid || double.IsNaN(state.localSeconds) || double.IsInfinity(state.localSeconds)
                || double.IsNaN(state.networkTime) || double.IsInfinity(state.networkTime))
            {
                Debug.LogError("[Environment] Estado de servidor invalido.");
                return;
            }
            snapshot = state;
            receivedAt = NetworkTime.time;
            HasServerClock = true;
        }

        public static void ReceiveReply(EnvironmentReply reply)
        {
            Status = reply.message;
            if (!reply.success) Debug.LogWarning("[Environment] " + reply.message);
        }

        public static void ResetClock()
        {
            HasServerClock = false;
            Status = "Aguardando horario do servidor.";
        }

        void OnEnable()
        {
#if UNITY_SERVER
            return;
#else
            if (Application.isPlaying && Dedicated()) return;
            if (settings == null) settings = Resources.Load<WorldEnvironmentSettings>("WorldEnvironment");
            if (settings == null) return;
            if (!settings.defaults.IsValid || settings.blendShader == null)
            {
                Debug.LogError("[Environment] Shader ou controles de ambiente invalidos.", this);
                return;
            }
            var materials = settings.Materials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == null || materials[i].GetTexture("_MainTex") == null)
                {
                    Debug.LogError("[Environment] Material panoramico ausente no indice " + i, this);
                    return;
                }
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Environment] Apenas um controlador de ceu pode estar ativo.", this);
                return;
            }
            Instance = this;
            ownSun = false;
            previousSky = RenderSettings.skybox;
            previousSun = RenderSettings.sun;
            originalAmbient = RenderSettings.ambientLight;
            previousAmbientMode = RenderSettings.ambientMode;
            sky = new Material(settings.blendShader) { hideFlags = HideFlags.HideAndDontSave };
            for (int i = 0; i < materials.Length; i++)
                sky.SetTexture("_Sky" + i, materials[i == 6 ? 7 : i].GetTexture("_MainTex"));
            sun = RenderSettings.sun;
            if (sun == null || sun.type != LightType.Directional)
                foreach (var candidate in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (candidate.type == LightType.Directional && candidate.gameObject.scene == gameObject.scene)
                    { sun = candidate; break; }
            if (sun == null || sun.type != LightType.Directional)
            {
                var root = new GameObject("World Sun") { hideFlags = HideFlags.DontSave };
                root.transform.SetParent(transform);
                sun = root.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                ownSun = true;
            }
            originalRotation = sun.transform.rotation;
            originalIntensity = sun.intensity;
            originalColor = sun.color;
            var moonRoot = new GameObject("World Moon") { hideFlags = HideFlags.DontSave };
            moonRoot.transform.SetParent(transform);
            moon = moonRoot.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(.72f, .78f, .9f);
            moon.shadows = LightShadows.Soft;
            moon.intensity = 0;
            RenderSettings.sun = sun;
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Flat;
            lastTime = Time.realtimeSinceStartup;
            initialized = true;
            EnvironmentCycle.Weights(preview ? previewHour * 3600d : LocalSeconds, Controls, weights);
            Apply();
#endif
        }

        void Update()
        {
            if (!initialized) return;
            if (Application.isPlaying && NetworkClient.active && !HasServerClock) return;
            if (Application.isPlaying && Input.GetKeyDown(KeyCode.F9) && LoginNetworkClient.IsAdmin
                && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
            {
                open = !open;
                draft = Controls;
                draftHour = CurrentHour;
            }
            float now = Time.realtimeSinceStartup;
            float delta = Mathf.Max(0, now - lastTime);
            lastTime = now;
            EnvironmentCycle.Weights(preview ? previewHour * 3600d : LocalSeconds, Controls, target);
            float blend = 1 - Mathf.Exp(-delta * 5 / Mathf.Max(1, settings.transitionSeconds));
            for (int i = 0; i < weights.Length; i++) weights[i] = Mathf.Lerp(weights[i], target[i], blend);
            Apply();
            if (Application.isPlaying && now >= nextProbe)
            {
                nextProbe = now + 30;
                DynamicGI.UpdateEnvironment();
            }
        }

        void Apply()
        {
            ConfigureCamera();
            var controls = Controls;
            Vector3 direction = EnvironmentCycle.SunDirection(CurrentHour, controls.sunAzimuth);
            Vector3 moonDirection = EnvironmentCycle.MoonDirection(CurrentHour, controls.sunAzimuth);
            float moonVisibility = EnvironmentCycle.MoonVisibility(CurrentHour, weights[6], controls.moonless);
            float day = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, 15, EnvironmentCycle.Elevation(CurrentHour)));
            float clear = weights[0] + weights[1] + weights[4] + weights[5];
            sky.SetVector("_WeightsA", new Vector4(weights[0], weights[1], weights[2], weights[3]));
            sky.SetVector("_WeightsB", new Vector4(weights[4], weights[5], weights[6], weights[7]));
            sky.SetFloat("_Rotation", controls.skyRotation);
            sky.SetFloat("_Exposure", controls.skyExposure);
            sky.SetVector("_SunDirection", direction);
            sky.SetFloat("_SunRadius", controls.sunDiameter * .5f * Mathf.Deg2Rad);
            sky.SetFloat("_SunBrightness", controls.sunIntensity * clear
                * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, 2, EnvironmentCycle.Elevation(CurrentHour))));
            sky.SetVector("_MoonDirection", moonDirection);
            sky.SetFloat("_MoonRadius", controls.moonDiameter * .5f * Mathf.Deg2Rad);
            sky.SetFloat("_MoonBrightness", .65f * moonVisibility);
            moon.transform.rotation = Quaternion.LookRotation(-moonDirection,
                Mathf.Abs(moonDirection.y) > .99f ? Vector3.forward : Vector3.up);
            moon.intensity = MoonLightIntensity * moonVisibility;
            sun.transform.rotation = Quaternion.LookRotation(-direction,
                Mathf.Abs(direction.y) > .99f ? Vector3.forward : Vector3.up);
            sun.intensity = controls.sunIntensity * day * Mathf.Lerp(.2f, 1, clear);
            sun.color = Color.Lerp(new Color(1, .55f, .3f), Color.white, day);
            RenderSettings.sun = moon.intensity > sun.intensity ? moon : sun;
            Color lunarAmbient = settings.nightAmbient + new Color(.035f, .045f, .055f) * moonVisibility;
            RenderSettings.ambientLight = Color.Lerp(lunarAmbient, settings.dayAmbient, day);
        }

        void ConfigureCamera()
        {
            var main = Camera.main;
            if (main == null || main.gameObject.scene != gameObject.scene) return;
            if (worldCamera != main)
            {
                RestoreCamera();
                worldCamera = main;
                previousClearFlags = main.clearFlags;
                cameraSkybox = main.GetComponent<Skybox>();
                if (cameraSkybox != null) previousCameraSkyboxEnabled = cameraSkybox.enabled;
            }
            main.clearFlags = CameraClearFlags.Skybox;
            if (cameraSkybox != null) cameraSkybox.enabled = false;
        }

        void RestoreCamera()
        {
            if (worldCamera != null) worldCamera.clearFlags = previousClearFlags;
            if (cameraSkybox != null) cameraSkybox.enabled = previousCameraSkyboxEnabled;
            worldCamera = null;
            cameraSkybox = null;
        }

        void OnDisable()
        {
            if (!initialized) return;
            RestoreCamera();
            if (RenderSettings.skybox == sky)
            {
                RenderSettings.skybox = previousSky;
                RenderSettings.sun = previousSun;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = originalAmbient;
            }
            if (sun != null && !ownSun)
            {
                sun.transform.rotation = originalRotation;
                sun.intensity = originalIntensity;
                sun.color = originalColor;
            }
            if (Instance == this) Instance = null;
            initialized = false;
            if (sky != null)
            {
                if (Application.isPlaying) Destroy(sky); else DestroyImmediate(sky);
            }
            if (ownSun && sun != null)
            {
                if (Application.isPlaying) Destroy(sun.gameObject); else DestroyImmediate(sun.gameObject);
            }
            if (moon != null)
            {
                if (Application.isPlaying) Destroy(moon.gameObject); else DestroyImmediate(moon.gameObject);
            }
        }

        void OnGUI()
        {
            if (!initialized || !Application.isPlaying || !LoginNetworkClient.IsAdmin) return;
            if (GUI.Button(new Rect(10, 100, 130, 28), "Ceu / Tempo (F9)"))
            { open = !open; draft = Controls; draftHour = CurrentHour; }
            if (!open) return;
            window = TOP.UI.GameWindowControls.Window(7923, window, DrawMenu,
                "Ambiente do servidor (F9)", () => open = false);
        }

        void DrawMenu(int id)
        {
            menuScroll = GUILayout.BeginScrollView(menuScroll);
            GUILayout.Label($"Servidor {FormatHour(CurrentHour)} | UTC {ServerUtcOffset / 60f:+0.##;-0.##;0}");
            GUILayout.Label("Ceu ativo: " + ActiveSky());
            float lunarElevation = Mathf.Asin(EnvironmentCycle.MoonDirection(CurrentHour, Controls.sunAzimuth).y) * Mathf.Rad2Deg;
            GUILayout.Label(Controls.moonless ? "Lua desativada: Noite sem lua."
                : "Lua: altura " + lunarElevation.ToString("0") + " graus | "
                    + (sky.GetFloat("_MoonBrightness") > .001f ? "visivel no ceu" : "abaixo do horizonte ou encoberta"));
            GUILayout.Label(HasServerClock && NetworkTime.time - receivedAt < 10
                ? "Relogio sincronizado" : "Sem sincronizacao recente; nao aplicar.");
            draftHour = Slider("Horario", draftHour, 0, 23.999f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Amanhecer")) draftHour = 6;
            if (GUILayout.Button("Meio-dia")) draftHour = 12;
            if (GUILayout.Button("Entardecer")) draftHour = 18;
            if (GUILayout.Button("Noite")) draftHour = 0;
            GUILayout.EndHorizontal();
            GUILayout.Label("Horario solicitado: " + FormatHour(draftHour) + ". Use Aplicar para todos para confirmar.");
            draft.weather = (SkyWeather)GUILayout.SelectionGrid((int)draft.weather,
                new[] { "Automatico", "Dia", "Sem sol", "Chuva", "Neve" }, 3);
            draft.moonless = GUILayout.Toggle(draft.moonless, "Noite sem lua");
            draft.sunAzimuth = Slider("Direcao do sol", draft.sunAzimuth, -180, 180);
            draft.sunIntensity = Slider("Intensidade do sol", draft.sunIntensity, 0, 5);
            draft.sunDiameter = Slider("Diametro aparente", draft.sunDiameter, .1f, 5);
            draft.moonDiameter = Slider("Tamanho da lua", draft.moonDiameter, .1f, 30);
            draft.skyRotation = Slider("Rotacao do sky", draft.skyRotation, -180, 180);
            draft.skyExposure = Slider("Exposicao do sky", draft.skyExposure, .1f, 4);
            bool enabled = GUI.enabled;
            GUI.enabled = NetworkClient.isConnected && HasServerClock && NetworkTime.time - receivedAt < 10;
            if (GUILayout.Button("Aplicar para todos")) Send(false);
            if (GUILayout.Button("Voltar ao horario real do servidor")) Send(true);
            GUI.enabled = enabled;
            GUILayout.Label(Status);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 400, 22));
        }

        void Send(bool restore)
        {
            Status = "Aguardando autorizacao da API...";
            NetworkClient.Send(new EnvironmentChange { controls = draft, hour = draftHour, restoreClock = restore });
        }

        static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(145));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString("0.00"), GUILayout.Width(48));
            GUILayout.EndHorizontal();
            return value;
        }

        static string FormatHour(float hour)
        {
            int minutes = Mathf.FloorToInt(hour * 60) % 1440;
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        string ActiveSky()
        {
            int index = 0;
            for (int i = 1; i < weights.Length; i++) if (weights[i] > weights[index]) index = i;
            return settings.Materials[index].name;
        }
    }
}
