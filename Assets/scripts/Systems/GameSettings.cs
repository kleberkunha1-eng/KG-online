using UnityEngine;

namespace TOP.Systems
{
    /// <summary>
    /// Preferencias de video/jogo salvas pelo jogador nas janelas frmVideo/frmAudio/frmGame
    /// (menu ESC -> Configuracoes). Tudo e persistido em PlayerPrefs e aplicado imediatamente
    /// quando o jogador confirma (btnYes) nas janelas de configuracao.
    /// </summary>
    public static class GameSettings
    {
        // ---------- Video ----------
        public static int QualityLevel { get => PlayerPrefs.GetInt("pko_quality", QualitySettings.GetQualityLevel()); set => PlayerPrefs.SetInt("pko_quality", value); }
        public static bool Fullscreen { get => PlayerPrefs.GetInt("pko_fullscreen", Screen.fullScreen ? 1 : 0) == 1; set => PlayerPrefs.SetInt("pko_fullscreen", value ? 1 : 0); }
        public static int ResolutionIndex { get => PlayerPrefs.GetInt("pko_resolution", -1); set => PlayerPrefs.SetInt("pko_resolution", value); }
        public static bool ViewFar { get => PlayerPrefs.GetInt("pko_viewfar", 1) == 1; set => PlayerPrefs.SetInt("pko_viewfar", value ? 1 : 0); }

        // ---------- HUD / jogo ----------
        public static bool HpAsPercent { get => PlayerPrefs.GetInt("pko_hp_percent", 0) == 1; set => PlayerPrefs.SetInt("pko_hp_percent", value ? 1 : 0); }
        public static bool ShowHudBars { get => PlayerPrefs.GetInt("pko_show_bars", 1) == 1; set => PlayerPrefs.SetInt("pko_show_bars", value ? 1 : 0); }
        public static bool ShowPlayerInfo { get => PlayerPrefs.GetInt("pko_show_info", 1) == 1; set => PlayerPrefs.SetInt("pko_show_info", value ? 1 : 0); }
        public static bool ShowFps { get => PlayerPrefs.GetInt("pko_show_fps", 0) == 1; set => PlayerPrefs.SetInt("pko_show_fps", value ? 1 : 0); }
        public static int CameraMode { get => PlayerPrefs.GetInt("pko_camera_mode", 0); set => PlayerPrefs.SetInt("pko_camera_mode", value); }
        public static bool ShowEffects { get => PlayerPrefs.GetInt("pko_show_effects", 1) == 1; set => PlayerPrefs.SetInt("pko_show_effects", value ? 1 : 0); }
        /// <summary>Limite de quadros por segundo escolhido em frmGame (opcoes "30"/"60").</summary>
        public static int TargetFps { get => PlayerPrefs.GetInt("pko_fps_cap", 60); set => PlayerPrefs.SetInt("pko_fps_cap", value); }

        // Preferencias salvas mas sem sistema de jogo correspondente ainda implementado
        // (nao ha Nameplate/AutoLock/MountController no projeto). Ficam disponiveis para
        // quando esses sistemas existirem, sem efeito visual por enquanto.
        public static bool AlwaysRun { get => PlayerPrefs.GetInt("pko_always_run", 0) == 1; set => PlayerPrefs.SetInt("pko_always_run", value ? 1 : 0); }
        public static bool AutoLockTarget { get => PlayerPrefs.GetInt("pko_autolock", 0) == 1; set => PlayerPrefs.SetInt("pko_autolock", value ? 1 : 0); }
        public static bool ShowApparel { get => PlayerPrefs.GetInt("pko_show_apparel", 1) == 1; set => PlayerPrefs.SetInt("pko_show_apparel", value ? 1 : 0); }
        public static bool ShowMounts { get => PlayerPrefs.GetInt("pko_show_mounts", 1) == 1; set => PlayerPrefs.SetInt("pko_show_mounts", value ? 1 : 0); }
        public static bool ShowEnemyNames { get => PlayerPrefs.GetInt("pko_show_names", 1) == 1; set => PlayerPrefs.SetInt("pko_show_names", value ? 1 : 0); }

        static readonly Resolution[] Common =
        {
            new Resolution { width = 1280, height = 720 },
            new Resolution { width = 1366, height = 768 },
            new Resolution { width = 1600, height = 900 },
            new Resolution { width = 1920, height = 1080 },
            new Resolution { width = 2560, height = 1440 },
        };
        public static Resolution[] Resolutions => Common;

        /// <summary>Aplica qualidade/resolucao/fullscreen/far-clip atuais na tela e na camera ativa.</summary>
        public static void ApplyVideo()
        {
            QualitySettings.SetQualityLevel(Mathf.Clamp(QualityLevel, 0, Mathf.Max(0, QualitySettings.names.Length - 1)), true);
            int ri = ResolutionIndex;
            if (ri >= 0 && ri < Common.Length)
            {
                var r = Common[ri];
                if (Screen.currentResolution.width != r.width || Screen.currentResolution.height != r.height || Screen.fullScreen != Fullscreen)
                    Screen.SetResolution(r.width, r.height, Fullscreen);
            }
            else if (Screen.fullScreen != Fullscreen)
            {
                Screen.fullScreen = Fullscreen;
            }
            EffectManager.EffectsEnabled = ShowEffects;
            if (Camera.main != null) Camera.main.farClipPlane = ViewFar ? 500f : 120f;
        }

        /// <summary>Aplica as preferencias de frmGame que tem efeito real (efeitos, fps, camera).</summary>
        public static void ApplyGame()
        {
            EffectManager.EffectsEnabled = ShowEffects;
            Application.targetFrameRate = TargetFps;
            var cc = UnityEngine.Object.FindAnyObjectByType<CameraController>();
            cc?.SetPreset(CameraMode);
        }
    }
}
