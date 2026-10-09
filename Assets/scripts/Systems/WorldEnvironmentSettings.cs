using System;
using Mirror;
using UnityEngine;

namespace TOP.World
{
    public enum SkyWeather { Automatic, Clear, Sunless, Rainy, Snowy }

    [Serializable]
    public struct EnvironmentControls
    {
        public SkyWeather weather;
        public bool moonless;
        public float sunAzimuth;
        public float sunIntensity;
        public float sunDiameter;
        public float moonDiameter;
        public float skyRotation;
        public float skyExposure;

        public bool IsValid => Enum.IsDefined(typeof(SkyWeather), weather)
            && Valid(sunAzimuth, -180, 180) && Valid(sunIntensity, 0, 5)
            && Valid(sunDiameter, .1f, 5) && Valid(skyRotation, -180, 180)
            && Valid(moonDiameter, .1f, 30)
            && Valid(skyExposure, .1f, 4);

        static bool Valid(float value, float min, float max) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }

    public struct EnvironmentSnapshot : NetworkMessage
    {
        public double localSeconds;
        public double networkTime;
        public int utcOffsetMinutes;
        public EnvironmentControls controls;
    }

    public struct EnvironmentChange : NetworkMessage
    {
        public bool restoreClock;
        public float hour;
        public EnvironmentControls controls;
    }

    public struct EnvironmentReply : NetworkMessage
    {
        public bool success;
        public string message;
    }

    [CreateAssetMenu(menuName = "TOP/World Environment")]
    public sealed class WorldEnvironmentSettings : ScriptableObject
    {
        public Material day, sunless, rainy, snowy, sunrise, sunset, night, moonless;
        public Shader blendShader;
        public EnvironmentControls defaults = new EnvironmentControls
        {
            weather = SkyWeather.Automatic, sunIntensity = 1.2f,
            sunDiameter = .6f, moonDiameter = 6, skyExposure = 1
        };
        [Range(1, 60)] public float transitionSeconds = 15;
        public Color dayAmbient = new Color(.45f, .48f, .55f);
        public Color nightAmbient = new Color(.035f, .045f, .08f);

        public Material[] Materials => new[] { day, sunless, rainy, snowy, sunrise, sunset, night, moonless };
    }

    public static class EnvironmentCycle
    {
        public static float Hour(double localSeconds) => (float)(Repeat(localSeconds, 86400) / 3600);
        public static double Repeat(double value, double length) => value - Math.Floor(value / length) * length;

        public static Vector3 SunDirection(float hour, float azimuth)
        {
            double phase = (hour - 6) * Math.PI / 12;
            return Quaternion.Euler(0, azimuth, 0)
                * new Vector3((float)Math.Cos(phase), (float)Math.Sin(phase), 0);
        }

        public static float Elevation(float hour) =>
            Mathf.Asin(SunDirection(hour, 0).y) * Mathf.Rad2Deg;

        public static Vector3 MoonDirection(float hour, float azimuth)
        {
            double phase = (hour - 18) * Math.PI / 12;
            return Quaternion.Euler(0, azimuth, 0) * new Vector3((float)Math.Cos(phase),
                Mathf.Tan(25 * Mathf.Deg2Rad) * (float)Math.Sin(phase), (float)Math.Sin(phase)).normalized;
        }

        public static float MoonVisibility(float hour, float clearNightWeight, bool moonless) => moonless ? 0
            : Mathf.Clamp01(clearNightWeight) * Mathf.SmoothStep(0, 1,
                Mathf.InverseLerp(0, 8, -Elevation(hour)));

        public static SkyWeather WeatherForSlot(long slot)
        {
            unchecked
            {
                uint hash = (uint)slot * 747796405u + 2891336453u;
                hash = ((hash >> (int)((hash >> 28) + 4)) ^ hash) * 277803737u;
                hash = (hash >> 22) ^ hash;
                uint choice = hash % 10;
                return choice < 6 ? SkyWeather.Clear : choice < 8 ? SkyWeather.Sunless
                    : choice == 8 ? SkyWeather.Rainy : SkyWeather.Snowy;
            }
        }

        public static void Weights(double seconds, EnvironmentControls controls, float[] weights)
        {
            if (weights == null || weights.Length != 8)
                throw new ArgumentException("Oito pesos de materiais sao obrigatorios.", nameof(weights));
            Array.Clear(weights, 0, weights.Length);
            float elevation = Elevation(Hour(seconds));
            float daylight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, 12, elevation));
            float darkness = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-12, 0, elevation));
            float twilight = Mathf.Max(0, 1 - daylight - darkness);
            weights[Hour(seconds) < 12 ? 4 : 5] = twilight;
            if (controls.weather != SkyWeather.Automatic)
            {
                AddWeather(controls.weather, 1, daylight, darkness, controls.moonless, weights);
                return;
            }
            long slot = (long)Math.Floor(seconds / 10800);
            float weatherBlend = Mathf.SmoothStep(0, 1, (float)(Repeat(seconds, 10800) / 600));
            AddWeather(WeatherForSlot(slot - 1), 1 - weatherBlend, daylight, darkness, controls.moonless, weights);
            AddWeather(WeatherForSlot(slot), weatherBlend, daylight, darkness, controls.moonless, weights);
        }

        static void AddWeather(SkyWeather weather, float amount, float day, float dark, bool noMoon, float[] weights)
        {
            weights[(int)weather - 1] += day * amount;
            weights[noMoon || weather == SkyWeather.Rainy || weather == SkyWeather.Snowy ? 7 : 6] += dark * amount;
        }
    }
}
