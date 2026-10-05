using UnityEngine;
using System.Collections;

namespace TOP.Systems
{
    /// <summary>
    /// EffectManager - Gerencia efeitos visuais de combate
    /// </summary>
    public class EffectManager : MonoBehaviour
    {
        public static EffectManager Instance { get; private set; }

        /// <summary>
        /// Controlado pela janela de configuracoes (frmGame -> Effects Show/Hide). Quando falso,
        /// os efeitos de particula sao pulados, mas numeros de dano continuam aparecendo.
        /// </summary>
        public static bool EffectsEnabled = true;

        [Header("Hit Effects")]
        [SerializeField] private ParticleSystem hitEffectPrefab;
        [SerializeField] private float hitEffectDuration = 0.5f;

        [Header("Damage Numbers")]
        [SerializeField] private GameObject damageNumberPrefab;
        [SerializeField] private float damageNumberDuration = 1f;
        [SerializeField] private float damageNumberOffset = 1f;

        [Header("Blood Effects")]
        [SerializeField] private ParticleSystem bloodEffectPrefab;
        [SerializeField] private float bloodEffectDuration = 1f;

        [Header("Level Up Effect")]
        [SerializeField] private ParticleSystem levelUpEffectPrefab;
        [SerializeField] private float levelUpEffectDuration = 1.5f;

        [Header("Healing Effects")]
        [SerializeField] private ParticleSystem healEffectPrefab;
        [SerializeField] private float healEffectDuration = 0.8f;

        static Material particleMaterial;
        static void Burst(Vector3 position, Color color)
        {
            var go = new GameObject("CombatEffect"); go.transform.position = position;
            var particles = go.AddComponent<ParticleSystem>(); particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main; main.duration=.5f; main.loop=false; main.startLifetime=.4f; main.startSpeed=2; main.startSize=.13f; main.startColor=color; main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=particles.emission; emission.rateOverTime=0; emission.SetBursts(new[]{new ParticleSystem.Burst(0,18)});
            var shape=particles.shape; shape.shapeType=ParticleSystemShapeType.Sphere; shape.radius=.15f;
            if (particleMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                    ?? Shader.Find("Particles/Standard Unlit")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null) particleMaterial = new Material(shader);
            }

            if (particleMaterial != null)
                particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMaterial;
            particles.Play(); Destroy(go,1.5f);
        }
        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Reproduz efeito de hit
        /// </summary>
        public void PlayHitEffect(Vector3 position, Vector3 normal, Transform parent = null)
        {
            if (!EffectsEnabled) return;
            if (hitEffectPrefab == null) { Burst(position, new Color(1,.7f,.2f)); return; }

            ParticleSystem effect = Instantiate(hitEffectPrefab, position, Quaternion.LookRotation(normal), parent);
            Destroy(effect.gameObject, hitEffectDuration);
        }

        /// <summary>
        /// Reproduz efeito de sangue
        /// </summary>
        public void PlayBloodEffect(Vector3 position, Transform parent = null)
        {
            if (!EffectsEnabled) return;
            if (bloodEffectPrefab == null) return;

            ParticleSystem effect = Instantiate(bloodEffectPrefab, position, Quaternion.identity, parent);
            Destroy(effect.gameObject, bloodEffectDuration);
        }

        /// <summary>
        /// Cria número de dano flutuante
        /// </summary>
        public void ShowDamageNumber(Vector3 position, int damage, bool isCritical = false, Transform parent = null)
        {
            if (damageNumberPrefab == null) return;

            GameObject numberObj = Instantiate(damageNumberPrefab, position + Vector3.up * damageNumberOffset, Quaternion.identity, parent);

            TextMesh textMesh = numberObj.GetComponent<TextMesh>();
            if (textMesh != null)
            {
                textMesh.text = isCritical ? $"<color=red>{damage * 1.5:F0}!</color>" : damage.ToString();
                textMesh.fontSize = isCritical ? 30 : 20;
            }

            // Animação de fade out
            CanvasGroup canvasGroup = numberObj.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = numberObj.AddComponent<CanvasGroup>();
            }

            StartCoroutine(AnimateDamageNumber(numberObj, canvasGroup));
        }

        private IEnumerator AnimateDamageNumber(GameObject numberObj, CanvasGroup canvasGroup)
        {
            Vector3 startPos = numberObj.transform.position;
            float elapsedTime = 0f;

            while (elapsedTime < damageNumberDuration && numberObj != null)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / damageNumberDuration;

                // Move para cima
                numberObj.transform.position = startPos + Vector3.up * (progress * 2);

                // Fade out
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, progress);

                yield return null;
            }

            Destroy(numberObj);
        }

        /// <summary>
        /// Efeito de level up
        /// </summary>
        public void PlayLevelUpEffect(Vector3 position, Transform parent = null)
        {
            if (!EffectsEnabled) return;
            if (levelUpEffectPrefab == null) { Burst(position + Vector3.up, Color.cyan); return; }

            ParticleSystem effect = Instantiate(levelUpEffectPrefab, position, Quaternion.identity, parent);
            Destroy(effect.gameObject, levelUpEffectDuration);
        }

        /// <summary>
        /// Efeito de cura
        /// </summary>
        public void PlayHealEffect(Vector3 position, int healAmount, Transform parent = null)
        {
            if (EffectsEnabled && healEffectPrefab != null)
            {
                ParticleSystem effect = Instantiate(healEffectPrefab, position, Quaternion.identity, parent);
                Destroy(effect.gameObject, healEffectDuration);
            }

            ShowDamageNumber(position, healAmount, false, parent);
        }

        /// <summary>
        /// Efeito de morte
        /// </summary>
        public void PlayDeathEffect(Vector3 position, Transform parent = null)
        {
            // Pode ser expandido com múltiplos efeitos
            PlayBloodEffect(position, parent);
        }

        /// <summary>
        /// Efeito genérico de particula
        /// </summary>
        public void PlayParticleEffect(ParticleSystem effectPrefab, Vector3 position, float duration = 1f, Transform parent = null)
        {
            if (!EffectsEnabled || effectPrefab == null) return;

            ParticleSystem effect = Instantiate(effectPrefab, position, Quaternion.identity, parent);
            Destroy(effect.gameObject, duration);
        }
    }
}
