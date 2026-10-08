using System.Collections.Generic;
using UnityEngine;

namespace TOP.Character
{
    // Marks a race whose rig/body comes from its own model (bone axes and lengths differ from the base race).
    // Equipment baked for the base race is re-bound per bone: uniformly scaled around each base-race joint and
    // moved onto the matching joint of this rig, then padded to this rig's bone count.
    public class PkoRigFit : MonoBehaviour
    {
        public float equipmentScale = 1f;
        public int boneCount;
        public Matrix4x4[] boneCorrection = new Matrix4x4[0];

        static readonly Dictionary<(Mesh, PkoRigFit), Mesh> fitted = new Dictionary<(Mesh, PkoRigFit), Mesh>();
        static readonly Dictionary<Mesh, float> fittedScale = new Dictionary<Mesh, float>();

        public Mesh Fit(Mesh baseRaceMesh)
        {
            if (baseRaceMesh == null) return null;
            var key = (baseRaceMesh, this);
            if (fitted.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            mesh = Instantiate(baseRaceMesh);
            mesh.name = baseRaceMesh.name + "_fit";
            mesh.hideFlags = HideFlags.DontSave;
            var source = mesh.bindposes;
            var bindposes = new Matrix4x4[Mathf.Max(source.Length, boneCount)];
            var scale = Matrix4x4.Scale(Vector3.one * equipmentScale);
            for (int i = 0; i < bindposes.Length; i++)
                bindposes[i] = i >= source.Length ? Matrix4x4.identity
                    : i < boneCorrection.Length ? boneCorrection[i] * source[i]
                    : scale * source[i];
            mesh.bindposes = bindposes;
            fitted[key] = mesh;
            fittedScale[mesh] = equipmentScale;
            return mesh;
        }

        // Vertices and bind joint positions of fitted meshes stay in base-race space; only the skinning is scaled.
        public float SpaceScale(Mesh mesh) => mesh != null && fittedScale.TryGetValue(mesh, out float scale) ? scale : 1f;
    }
}
