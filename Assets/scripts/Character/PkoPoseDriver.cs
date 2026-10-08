using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TOP.Character
{
    // characterposeinfo.txt: each "big" pose (wait, run, attack...) maps to one real action per weapon class.
    public static class PkoPoses
    {
        public const int Wait = 1, Cool = 2, Funny = 3, Guard = 4, Run = 5, GuardRun = 6, Attack1 = 7, Skill1 = 11, Sit = 16, Death = 17, SitWait = 38;
        public const int FlyWait = 42, FlyRun = 43, FlyShow = 44, FlySit = 45;
        // Columns of characterposeinfo: fists, 1H, 2H, dual, gun, bow, dagger.
        public const int WieldFists = 0, Wield1H = 1, Wield2H = 2, WieldDual = 3, WieldGun = 4, WieldBow = 5, WieldDagger = 6;

        static Dictionary<int, int[]> table;

        public static int Real(int pose, int wield)
        {
            if (table == null) Load();
            return table.TryGetValue(pose, out var row) && wield >= 0 && wield < row.Length ? row[wield] : 0;
        }

        public static int FlyingPose(int pose) => pose switch
        {
            Wait or Guard => FlyWait,
            Run or GuardRun => FlyRun,
            Cool => FlyShow,
            Sit => FlySit,
            _ => pose
        };

        // Same rule as CCharacterModel::__wield_state in the original client (item type of left/right hand).
        public static int WieldOf(int left, int right)
        {
            switch (right)
            {
                case 2: return Wield2H;
                case 4: return WieldGun;
                case 7: return WieldDagger;
                case 1: return left == 1 ? WieldDual : Wield1H;
                case 5: case 9: case 10: case 18: case 19: return Wield1H;
            }
            return left == 3 ? WieldBow : WieldFists;
        }

        static void Load()
        {
            table = new Dictionary<int, int[]>();
            var asset = Resources.Load<TextAsset>("PKO/characterposeinfo");
            if (asset == null) { Debug.LogWarning("[PkoPoses] characterposeinfo ausente"); return; }
            foreach (var line in Encoding.GetEncoding(28591).GetString(asset.bytes).Split('\n'))
            {
                var c = line.TrimEnd('\r').Split('\t');
                if (c.Length < 9 || !int.TryParse(c[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) continue;
                var row = new int[7];
                for (int i = 0; i < 7; i++) int.TryParse(c[2 + i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out row[i]);
                table[id] = row;
            }
        }
    }

    // Plays the original character actions on a baked rig (legacy Animation): idle/run by movement,
    // guarded stance after combat, one-shot attacks/skills/emotes and death.
    public class PkoPoseDriver : MonoBehaviour
    {
        const float CombatStance = 8f;

        Animation anim; string rigId = "";
        string clipFolder;
        readonly HashSet<string> missingClips = new HashSet<string>();
        readonly Dictionary<int, int> alias = new Dictionary<int, int>();
        int wield;
        float busyUntil, combatUntil;
        bool dead, sitting;
        Vector3 last;
        string current;
        int attackIndex;
        Animation wingAnimation;
        AnimationState wingState;
        float flightStarted;
        bool flightLoop;

        public int Wield => wield;
        public bool IsDead => dead;
        public string CurrentClip => current;
        public bool IsFlying => wingState != null;

        public void Init(Animation animation, string rigId0, string lazyClipFolder = null)
        {
            anim = animation; rigId = rigId0; alias.Clear();
            clipFolder = lazyClipFolder; missingClips.Clear();
            var a = Resources.Load<TextAsset>("PkoChar/Alias_" + rigId0);
            if (a != null)
                foreach (var l in a.text.Split('\n'))
                {
                    var p = l.Trim().Split(' ');
                    if (p.Length == 2 && int.TryParse(p[0], out int id) && int.TryParse(p[1], out int to)) alias[id] = to;
                }
            last = transform.position; current = null;
        }

        public void SetWield(int w) { if (w == wield) return; wield = w; current = null; }

        public void SetFlight(Animation wings)
        {
            wingAnimation = null;
            wingState = null;
            flightLoop = false;
            current = null;
            if (wings == null) return;
            if (wings.clip == null || wings.clip.length <= 0f
                || ClipFor(PkoPoses.FlyWait) == null || ClipFor(PkoPoses.FlyRun) == null)
            {
                Debug.LogError("[PkoPoseDriver] Cannot enable wing flight: wing loop or character flight clips are missing for " + rigId);
                return;
            }
            wingAnimation = wings;
            wingState = wings[wings.clip.name];
            flightStarted = Time.time;
            wingState.wrapMode = WrapMode.Loop;
            wings.Play(wings.clip.name);
        }

        public bool Has(int pose) => ClipFor(pose) != null;
        string ClipFor(int pose)
        {
            if (anim == null) return null;
            if (IsFlying) pose = PkoPoses.FlyingPose(pose);
            foreach (int w in new[] { wield, PkoPoses.WieldFists })
            {
                int real = PkoPoses.Real(pose, w);
                if (real <= 0) continue;
                if (alias.TryGetValue(real, out int canonical)) real = canonical;
                string n = rigId + "_action" + real;
                if (HasClip(n)) return n;
            }
            return null;
        }

        // Rigs with a lazy clip folder ship only the idle clip; the others load from Resources the first time
        // they are needed (loading the whole library at once froze the client for over a minute).
        bool HasClip(string n)
        {
            if (anim.GetClip(n) != null) return true;
            if (clipFolder == null || missingClips.Contains(n)) return false;
            AnimationClip clip;
            using (TOP.Diagnostics.GameTrace.Measure("PkoPoseDriver.LoadClip", int.MinValue, 20))
                clip = Resources.Load<AnimationClip>(clipFolder + n);
            if (clip == null) { missingClips.Add(n); return false; }
            anim.AddClip(clip, n);
            return true;
        }

        // Loops until replaced (idle, run, sit...).
        public void Loop(int pose) { string n = ClipFor(pose); if (n != null) PlayClip(n, WrapMode.Loop, 0.15f, IsFlightPose(pose)); }

        bool IsFlightPose(int pose)
        {
            if (!IsFlying) return false;
            int flying = PkoPoses.FlyingPose(pose);
            return flying == PkoPoses.FlyWait || flying == PkoPoses.FlyRun || flying == PkoPoses.FlySit;
        }

        // Plays once and returns to the locomotion pose; returns the duration (0 if the action does not exist).
        public float Once(int pose, bool clamp = false)
        {
            string n = ClipFor(pose);
            if (n == null) return 0f;
            PlayClip(n, clamp ? WrapMode.ClampForever : WrapMode.Once, 0.08f);
            float len = anim[n].length;
            busyUntil = clamp ? float.MaxValue : Time.time + len;
            return len;
        }

        void PlayClip(string clip, WrapMode mode, float fade, bool synchronizedFlight = false)
        {
            flightLoop = synchronizedFlight;
            anim[clip].wrapMode = mode;
            anim[clip].time = 0f;
            anim[clip].speed = synchronizedFlight ? anim[clip].length / wingState.length : 1f;
            anim.CrossFade(clip, fade);
            current = clip;
        }

        public void Attack() { combatUntil = Time.time + CombatStance; sitting = false; Once(PkoPoses.Attack1 + (attackIndex++ % 3)); }
        public void Skill(int n) { combatUntil = Time.time + CombatStance; sitting = false; if (Once(PkoPoses.Skill1 + Mathf.Abs(n) % 5) <= 0f) Attack(); }
        public void Emote(int pose) { sitting = false; Once(pose); }
        public void Sit(bool on) { sitting = on; busyUntil = 0f; current = null; }
        public void Die() { dead = true; Once(PkoPoses.Death, true); }
        public void Revive() { dead = false; busyUntil = 0f; current = null; }
        public void EnterCombat() { combatUntil = Time.time + CombatStance; }

        void Update()
        {
            if (anim == null) return;
            float speed = (transform.position - last).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            last = transform.position;
            if (dead || Time.time < busyUntil) return;
            bool moving = speed > 0.4f, combat = Time.time < combatUntil;
            if (moving) sitting = false;
            int pose = moving ? (combat ? PkoPoses.GuardRun : PkoPoses.Run) : sitting ? PkoPoses.Sit : combat ? PkoPoses.Guard : PkoPoses.Wait;
            string n = ClipFor(pose);
            if (n != null && n != current) PlayClip(n, WrapMode.Loop, 0.15f, IsFlightPose(pose));
        }

        void LateUpdate()
        {
            SynchronizeFlight(Time.time - flightStarted);
        }

        void SynchronizeFlight(float elapsed)
        {
            if (!IsFlying || !flightLoop || dead || current == null) return;
            float phase = Mathf.Repeat(elapsed / wingState.length, 1f);
            wingState.normalizedTime = phase;
            anim[current].normalizedTime = phase;
            wingAnimation.Sample();
            anim.Sample();
        }
    }
}
