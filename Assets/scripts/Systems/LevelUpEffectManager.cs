using UnityEngine;

namespace TOP.Systems
{

    public class LevelUpEffectManager : MonoBehaviour
    {
        public static LevelUpEffectManager Instance { get; private set; }

        [Header("Effects")]
        [SerializeField] private ParticleSystem levelUpEffect;
        [SerializeField] private AudioClip levelUpSound;
        [SerializeField] private GameObject levelUpTextPrefab;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // Scene UI follows the lifetime of its canvas.
        }

        public void PlayEffect(Vector3 position)
        {
            if (levelUpEffect != null)
            {
                ParticleSystem effect = Instantiate(levelUpEffect, position + Vector3.up, Quaternion.identity);
                effect.Play();
                Destroy(effect.gameObject, 3f);
            }

            if (levelUpSound != null)
                AudioSource.PlayClipAtPoint(levelUpSound, position);

            if (levelUpTextPrefab != null)
            {
                GameObject text = Instantiate(levelUpTextPrefab, position + Vector3.up * 2f, Quaternion.identity);
                Destroy(text, 2f);
            }

            CreateLevelUpRing(position);
        }

        private void CreateLevelUpRing(Vector3 position)
        {
            GameObject ring = new GameObject("LevelUpRing");
            ring.transform.position = position;
            Destroy(ring, 3f);
        }
    }
}
