using UnityEngine;

namespace TOP.Character
{
    // Baked equipment part (skinned mesh + materials) loaded by PkoCharacterVisual from Resources/PkoChar/Parts.
    public class PkoPartAsset : ScriptableObject
    {
        public Mesh mesh;
        public Material[] materials;
    }
}