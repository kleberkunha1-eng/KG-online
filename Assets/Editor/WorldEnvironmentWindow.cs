using System;
using System.IO;
using System.Linq;
using System.Text;
using TOP.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class WorldEnvironmentWindow : EditorWindow
{
    const string SettingsPath = "Assets/Resources/WorldEnvironment.asset";
    const string InstallRequest = "Tools/configure-sky.request";
    const string ValidateRequest = "Tools/validate-sky.request";
    const string MaterialFolder = "Assets/Fantasy Skybox FREE/Panoramics/FS002/";
    static readonly string[] Names =
    {
        "FS002_Day", "FS002_Day_Sunless", "FS002_Rainy", "FS002_Snowy",
        "FS002_Sunrise", "FS002_Sunset", "FS002_Night", "FS002_Night_Moonless"
    };
    WorldEnvironmentSettings settings;
    WorldEnvironment preview;
    bool showPreview;
    float hour = 12;
    EnvironmentControls controls;
    Vector2 scroll;

    [InitializeOnLoadMethod]
    static void Watch()
    {
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(InstallRequest))
        {
            File.Delete(InstallRequest);
            try { Install(); Validate(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                File.WriteAllText("Tools/sky-validation-results.txt", "Failed\n" + e);
            }
        }
        if (File.Exists(ValidateRequest))
        {
            File.Delete(ValidateRequest);
            try { Validate(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                File.WriteAllText("Tools/sky-validation-results.txt", "Failed\n" + e);
            }
        }
    }

    [MenuItem("Tools/World/Ceu e Tempo")]
    public static void Open() => GetWindow<WorldEnvironmentWindow>("Ceu e Tempo");

    [MenuItem("Tools/World/Configurar Fantasy Sky")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Saia do Play antes de configurar o ceu.");
        var materials = Names.Select(name => AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + name + ".mat")).ToArray();
        if (materials.Any(material => material == null || material.GetTexture("_MainTex") == null))
            throw new InvalidDataException("Um dos oito materiais FS002 ou sua textura nao foi encontrado.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/WorldSkyBlend.shader");
        if (shader == null) throw new InvalidDataException("Shader WorldSkyBlend ausente.");
        var asset = AssetDatabase.LoadAssetAtPath<WorldEnvironmentSettings>(SettingsPath);
        if (asset == null)
        {
            asset = CreateInstance<WorldEnvironmentSettings>();
            asset.day = materials[0]; asset.sunless = materials[1]; asset.rainy = materials[2]; asset.snowy = materials[3];
            asset.sunrise = materials[4]; asset.sunset = materials[5]; asset.night = materials[6]; asset.moonless = materials[7];
            asset.blendShader = shader;
            AssetDatabase.CreateAsset(asset, SettingsPath);
            AssetDatabase.SaveAssets();
        }
        Debug.Log("[Environment] Materiais instalados. Tools/World/Ceu e Tempo: previa. No jogo: F9 para admin.");
    }

    void OnEnable()
    {
        settings = AssetDatabase.LoadAssetAtPath<WorldEnvironmentSettings>(SettingsPath);
        if (settings != null) controls = settings.defaults;
        EditorApplication.update += UpdatePreview;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorSceneManager.sceneSaving += SceneSaving;
    }

    void OnDisable()
    {
        RemovePreview();
        EditorApplication.update -= UpdatePreview;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorSceneManager.sceneSaving -= SceneSaving;
    }

    void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode) RemovePreview();
    }

    void SceneSaving(Scene scene, string path) => RemovePreview();

    void RemovePreview()
    {
        if (preview != null) DestroyImmediate(preview.gameObject);
        preview = null;
    }

    void UpdatePreview()
    {
        var scene = SceneManager.GetActiveScene();
        if (!showPreview || settings == null || EditorApplication.isPlayingOrWillChangePlaymode
            || BuildPipeline.isBuildingPlayer || scene.name != "GameScene")
        {
            RemovePreview();
            return;
        }
        if (preview != null && preview.gameObject.scene != scene) RemovePreview();
        if (preview == null)
        {
            if (WorldEnvironment.Instance != null) return;
            var root = new GameObject("WorldEnvironment Preview") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            preview = root.AddComponent<WorldEnvironment>();
            preview.settings = settings;
            preview.preview = true;
            preview.previewHour = hour;
            preview.previewControls = controls;
            root.SetActive(true);
        }
        preview.previewHour = hour;
        preview.previewControls = controls;
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("Ciclo real: amanhecer 06h, por do sol 18h no fuso do servidor. "
            + "A previa e apenas local; F9 no jogo controla o servidor com permissao admin.", MessageType.Info);
        if (settings == null)
        {
            if (GUILayout.Button("Instalar materiais FS002")) { Install(); settings = AssetDatabase.LoadAssetAtPath<WorldEnvironmentSettings>(SettingsPath); controls = settings.defaults; }
            EditorGUILayout.EndScrollView();
            return;
        }
        EditorGUILayout.ObjectField("Configuracao", settings, typeof(WorldEnvironmentSettings), false);
        showPreview = EditorGUILayout.Toggle("Previa na GameScene", showPreview);
        hour = EditorGUILayout.Slider("Horario de previa", hour, 0, 23.999f);
        controls.weather = (SkyWeather)EditorGUILayout.EnumPopup("Clima", controls.weather);
        controls.moonless = EditorGUILayout.Toggle("Noite sem lua", controls.moonless);
        controls.sunAzimuth = EditorGUILayout.Slider("Direcao do sol", controls.sunAzimuth, -180, 180);
        controls.sunIntensity = EditorGUILayout.Slider("Intensidade do sol", controls.sunIntensity, 0, 5);
        controls.sunDiameter = EditorGUILayout.Slider("Diametro do sol (graus)", controls.sunDiameter, .1f, 5);
        controls.skyRotation = EditorGUILayout.Slider("Rotacao do sky", controls.skyRotation, -180, 180);
        controls.skyExposure = EditorGUILayout.Slider("Exposicao do sky", controls.skyExposure, .1f, 4);
        EditorGUILayout.HelpBox("Skybox e um panorama no infinito: sua posicao e controlada por rotacao, "
            + "nao translacao. FS002_Day pode conter um sol pintado; selecione Sunless para apenas o sol movel. "
            + "Rainy/Snowy alteram o ceu, sem criar particulas de chuva/neve.", MessageType.Info);
        if (GUILayout.Button("Salvar como padrao do servidor (proxima inicializacao)")
            && EditorUtility.DisplayDialog("Padrao do ambiente", "Salvar clima/sol/sky no asset compartilhado? "
                + "Nao altera o servidor ja rodando nem fixa a hora de previa.", "Salvar", "Cancelar"))
        {
            Undo.RecordObject(settings, "World environment defaults");
            settings.defaults = controls;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
        if (GUILayout.Button("Validar ciclo e materiais")) Validate();
        EditorGUILayout.EndScrollView();
    }

    public static void Validate()
    {
        var asset = AssetDatabase.LoadAssetAtPath<WorldEnvironmentSettings>(SettingsPath);
        if (asset == null) throw new InvalidDataException("WorldEnvironment.asset ausente.");
        var report = new StringBuilder();
        int passed = 0;
        Action<bool, string> check = (ok, message) =>
        {
            if (!ok) throw new InvalidDataException(message);
            passed++; report.AppendLine("PASS " + message);
        };
        check(asset.defaults.IsValid, "Controles padrao validos.");
        check(asset.Materials.All(material => material != null && material.GetTexture("_MainTex") != null),
            "Oito materiais e texturas FS002 referenciados.");
        check(asset.blendShader != null && !ShaderUtil.GetShaderMessages(asset.blendShader)
            .Any(message => message.severity.ToString() == "Error"), "Shader sem erros reportados.");
        var weights = new float[8];
        var before = new float[8];
        foreach (SkyWeather weather in Enum.GetValues(typeof(SkyWeather)))
        {
            var control = asset.defaults; control.weather = weather;
            for (int minute = 0; minute <= 1440; minute++)
            {
                EnvironmentCycle.Weights(20000d * 86400 + minute * 60, control, weights);
                if (weights.Any(weight => float.IsNaN(weight) || weight < -.00001f)
                    || Mathf.Abs(weights.Sum() - 1) > .0001f)
                    throw new InvalidDataException("Pesos invalidos: " + weather + " minuto " + minute);
            }
            check(true, "Pesos normalizados durante 24h: " + weather);
        }
        foreach (double hour in new[] { 0d, 3, 6, 9, 12, 15, 18, 21, 24 })
        {
            double seconds = 20000d * 86400 + hour * 3600;
            EnvironmentCycle.Weights(seconds - .01, asset.defaults, before);
            EnvironmentCycle.Weights(seconds + .01, asset.defaults, weights);
            check(before.Zip(weights, (a, b) => Mathf.Abs(a - b)).Max() < .001,
                "Continuidade de fase/clima em " + hour + "h.");
        }
        check(Mathf.Abs(EnvironmentCycle.Elevation(6)) < .001f
            && Mathf.Abs(EnvironmentCycle.Elevation(18)) < .001f
            && EnvironmentCycle.Elevation(12) > 89 && EnvironmentCycle.Elevation(0) < -89,
            "Sol no horizonte 06/18h, zenite 12h e abaixo do horizonte a noite.");
        var clear = asset.defaults; clear.weather = SkyWeather.Clear; clear.moonless = false;
        EnvironmentCycle.Weights(6 * 3600, clear, weights); check(weights[4] > .999f, "Sunrise no horizonte matinal.");
        EnvironmentCycle.Weights(18 * 3600, clear, weights); check(weights[5] > .999f, "Sunset no horizonte vespertino.");
        foreach (float hour in new[] { 5.5f, 6.5f, 17.5f, 18.5f })
        {
            EnvironmentCycle.Weights(hour * 3600, clear, weights);
            check(weights[hour < 12 ? 4 : 5] > .3f,
                "Material de transicao presente antes/depois do horizonte em " + hour + "h.");
        }
        EnvironmentCycle.Weights(12 * 3600, clear, weights); check(weights[0] > .999f, "FS002_Day no meio-dia.");
        EnvironmentCycle.Weights(0, clear, weights); check(weights[6] > .999f, "FS002_Night a meia-noite.");
        clear.moonless = true;
        EnvironmentCycle.Weights(0, clear, weights); check(weights[7] > .999f, "Variacao Moonless.");
        clear.sunIntensity = float.NaN; check(!clear.IsValid, "NaN rejeitado no controle do servidor.");
        clear = asset.defaults; clear.sunDiameter = 20; check(!clear.IsValid, "Diametro fora do limite rejeitado.");
        check(Mathf.Abs(Mathf.Asin(EnvironmentCycle.MoonDirection(0, 45).y) * Mathf.Rad2Deg - 25) < .001f
            && Mathf.Abs(EnvironmentCycle.MoonDirection(18, 45).y) < .001f
            && Mathf.Abs(EnvironmentCycle.MoonDirection(6, 45).y) < .001f,
            "Lua no horizonte 18/06h e trajetoria baixa de 25 graus a meia-noite.");
        check(EnvironmentCycle.MoonVisibility(0, 1, false) > .999f
            && EnvironmentCycle.MoonVisibility(12, 1, false) == 0
            && EnvironmentCycle.MoonVisibility(0, 1, true) == 0
            && EnvironmentCycle.MoonVisibility(0, 0, false) == 0,
            "Lua visivel a noite; oculta de dia, Moonless e clima sem noite clara.");
        var lunarControls = asset.defaults; lunarControls.weather = SkyWeather.Clear; lunarControls.moonless = false;
        var sunlessControls = lunarControls; sunlessControls.weather = SkyWeather.Sunless;
        EnvironmentCycle.Weights(0, sunlessControls, weights);
        check(weights[6] > .999f && EnvironmentCycle.MoonVisibility(0, weights[6], false) > .999f,
            "Sem sol nao significa sem lua: noite Sunless mantem disco e luz lunar.");
        foreach (float hour in new[] { 18f, 18.25f, 18.5f, 19f, 5f, 5.5f, 5.75f, 6f })
        {
            EnvironmentCycle.Weights(hour * 3600, lunarControls, weights);
            float visibility = EnvironmentCycle.MoonVisibility(hour, weights[6], false);
            check(hour == 18 || hour == 6 ? visibility < .0001f : visibility > 0,
                "Lua aparece apos Sunset e desaparece em Sunrise: " + hour + "h.");
        }
        ValidateRendering(asset, check);
        File.WriteAllText("Tools/sky-validation-results.txt", "Passed=" + passed + " Failed=0\n" + report);
        Debug.Log("[Environment] " + report);
    }

    static void ValidateRendering(WorldEnvironmentSettings asset, Action<bool, string> check)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            throw new InvalidOperationException("Validacao visual exige Editor com dispositivo grafico, sem -nographics.");
        if (WorldEnvironment.Instance != null)
            throw new InvalidOperationException("Desative a previa antes da validacao visual.");
        Material previousSky = RenderSettings.skybox;
        Light previousSun = RenderSettings.sun;
        Color ambient = RenderSettings.ambientLight;
        var cameraRoot = new GameObject("Sky validation camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraRoot.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.cullingMask = 0;
        camera.transform.rotation = Quaternion.Euler(-25, 30, 0);
        var target = new RenderTexture(64, 64, 16);
        var image = new Texture2D(64, 64, TextureFormat.RGB24, false);
        var active = RenderTexture.active;
        camera.targetTexture = target;
        var root = new GameObject("Sky validation") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var environment = root.AddComponent<WorldEnvironment>();
        environment.settings = asset;
        environment.preview = true;
        environment.previewControls = asset.defaults;
        environment.previewControls.weather = SkyWeather.Clear;
        Color[] noon = null;
        Color[] dawn = null;
        try
        {
            foreach (float hour in new[] { 12f, 0f, 6f, 18f })
            {
                environment.previewHour = hour;
                root.SetActive(true);
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                image.Apply();
                var pixels = image.GetPixels();
                check(pixels.All(pixel => !float.IsNaN(pixel.r) && !float.IsNaN(pixel.g) && !float.IsNaN(pixel.b)),
                    "Renderizacao finita em " + hour + "h.");
                if (noon == null) noon = pixels;
                else
                {
                    float difference = noon.Zip(pixels, (a, b) =>
                        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b)).Average();
                    check(difference > .01f, "Ceu " + hour + "h difere visualmente do meio-dia.");
                }
                if (hour == 6) dawn = pixels;
                if (hour == 18)
                    check(dawn.Zip(pixels, (a, b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g)
                        + Mathf.Abs(a.b - b.b)).Average() > .01f, "Sunrise e Sunset sao panoramas distintos na GPU.");
                root.SetActive(false);
            }
            environment.previewHour = 0;
            root.SetActive(true);
            camera.fieldOfView = 12;
            camera.transform.rotation = Quaternion.LookRotation(EnvironmentCycle.MoonDirection(0,
                environment.previewControls.sunAzimuth), Vector3.up);
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            image.Apply();
            var moonPixels = image.GetPixels();
            check(Mathf.Abs(RenderSettings.skybox.GetFloat("_MoonRadius") * Mathf.Rad2Deg * 2 - 6) < .001f,
                "Lua com diametro aparente de 6 graus, cinco vezes maior.");
            var lunarLight = root.GetComponentsInChildren<Light>().Single(light => light.name == "World Moon");
            check(Mathf.Abs(lunarLight.intensity - WorldEnvironment.MoonLightIntensity) < .00001f
                && lunarLight.color.b > lunarLight.color.r && lunarLight.intensity < .2f,
                "Luz lunar suave (0,12), azul-acinzentada e independente da intensidade solar.");
            check(RenderSettings.sun == lunarLight, "Lua selecionada como luz principal noturna do renderizador.");
            ValidateMoonTerrain(camera, target, image, lunarLight, check);
            RenderSettings.skybox.SetFloat("_MoonBrightness", 0);
            camera.Render();
            image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            image.Apply();
            var noMoonPixels = image.GetPixels();
            check(moonPixels.Zip(noMoonPixels, (a, b) => Mathf.Abs(a.grayscale - b.grayscale)).Max() > .1f,
                "Disco lunar realmente renderizado na GPU.");
            var center = moonPixels[32 * 64 + 32];
            check(center.grayscale > .15f && center.grayscale < .85f && center.b >= center.r,
                "Disco lunar cinza frio, sem saturacao branca ou brilho solar.");
            var lunarSurface = Enumerable.Range(0, moonPixels.Length).Where(index =>
            {
                float x = (index % 64 - 31.5f) / 8;
                float y = (index / 64 - 31.5f) / 8;
                return x*x+y*y < .6f;
            }).Select(index => moonPixels[index].grayscale).ToArray();
            check(lunarSurface.Max()-lunarSurface.Min() > .03f,
                "Superficie lunar com variacao de albedo/crateras, nao um disco de luz uniforme.");
            root.SetActive(false);
            environment.previewControls.moonless = true;
            root.SetActive(true);
            camera.Render();
            image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            image.Apply();
            check(root.GetComponentsInChildren<Light>().Single(light => light.name == "World Moon").intensity == 0
                && image.GetPixels().Zip(noMoonPixels, (a, b) => Mathf.Abs(a.grayscale-b.grayscale)).Max() < .001f,
                "Moonless remove disco e luz lunar na renderizacao real.");
            root.SetActive(false);
            check(RenderSettings.skybox == previousSky && RenderSettings.sun == previousSun
                && RenderSettings.ambientLight == ambient, "Previa restaura ceu, sol e ambiente originais.");
            check(!ShaderUtil.GetShaderMessages(asset.blendShader).Any(message => message.severity.ToString() == "Error"),
                "Shader compilado para renderizacao sem erros.");
        }
        finally
        {
            RenderTexture.active = active;
            DestroyImmediate(root);
            DestroyImmediate(cameraRoot);
            target.Release();
            DestroyImmediate(target);
            DestroyImmediate(image);
        }

        static void ValidateMoonTerrain(Camera camera, RenderTexture target, Texture2D image, Light moon,
            Action<bool, string> check)
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/ImportedClient/GarnerArgent.asset");
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/ImportedClient/GarnerTerrain.mat");
            if (data == null || material == null) throw new InvalidDataException("Terreno/material da cidade ausente.");
            var terrainRoot = Terrain.CreateTerrainGameObject(data);
            terrainRoot.hideFlags = HideFlags.HideAndDontSave;
            terrainRoot.layer = 31;
            terrainRoot.transform.position = new Vector3(2000, 1000, 2000);
            var terrain = terrainRoot.GetComponent<Terrain>();
            terrain.materialTemplate = material;
            var previousPosition = camera.transform.position;
            var previousRotation = camera.transform.rotation;
            int previousMask = camera.cullingMask;
            float previousFov = camera.fieldOfView;
            try
            {
                var point = terrainRoot.transform.position + new Vector3(512, 0, 512);
                point.y = terrain.SampleHeight(point) + terrainRoot.transform.position.y;
                camera.transform.position = point + Vector3.up * 12;
                camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                camera.fieldOfView = 45;
                camera.cullingMask = 1 << 31;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                image.Apply();
                var lit = image.GetPixels();
                moon.intensity = 0;
                camera.Render();
                image.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                image.Apply();
                var unlit = image.GetPixels();
                float gain = lit.Zip(unlit, (a,b) => a.grayscale - b.grayscale).Average();
                check(gain > .001f, "Luz lunar ilumina Terrain/Lit real na GPU; ganho medio=" + gain);
            }
            finally
            {
                moon.intensity = WorldEnvironment.MoonLightIntensity;
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.cullingMask = previousMask;
                camera.fieldOfView = previousFov;
                DestroyImmediate(terrainRoot);
            }
        }
    }
}
