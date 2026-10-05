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
        // Columns of characterposeinfo: fists, 1H, 2H, dual, gun, bow, dagger.
        public const int WieldFists = 0, Wield1H = 1, Wield2H = 2, WieldDual = 3, WieldGun = 4, WieldBow = 5, WieldDagger = 6;

        static Dictionary<int, int[]> table;

        public static int Real(int pose, int wield)
        {
            if (table == null) Load();
            return table.TryGetValue(pose, out var row) && wield >= 0 && wield < row.Length ? row[wield] : 0;
        }

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
        readonly Dictionary<int, int> alias = new Dictionary<int, int>();
        int wield;
        float busyUntil, combatUntil;
        bool dead, sitting;
        Vector3 last;
        string current;
        int attackIndex;

        public int Wield => wield;
        public bool IsDead => dead;
        public string CurrentClip => current;

        public void Init(Animation animation, string rigId0)
        {
            anim = animation; rigId = rigId0; alias.Clear();
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

        public bool Has(int pose) => ClipFor(pose) != null;
        string ClipFor(int pose)
        {
            if (anim == null) return null;
            foreach (int w in new[] { wield, PkoPoses.WieldFists })
            {
                int real = PkoPoses.Real(pose, w);
                if (real <= 0) continue;
                if (alias.TryGetValue(real, out int canonical)) real = canonical;
                string n = rigId + "_action" + real;
                if (anim.GetClip(n) != null) return n;
            }
            return null;
        }

        // Loops until replaced (idle, run, sit...).
        public void Loop(int pose) { string n = ClipFor(pose); if (n != null) PlayClip(n, WrapMode.Loop, 0.15f); }

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

        void PlayClip(string clip, WrapMode mode, float fade)
        {
            anim[clip].wrapMode = mode;
            anim[clip].time = 0f;
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
            if (n != null && n != current) PlayClip(n, WrapMode.Loop, 0.15f);
        }
    }
}
