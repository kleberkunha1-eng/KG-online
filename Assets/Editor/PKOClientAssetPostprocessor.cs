using System;
using UnityEditor;
using UnityEngine;

namespace TOP.EditorTools
{
    public sealed class PKOClientAssetPostprocessor : AssetPostprocessor
    {
        static readonly string[] ImportedRoots =
        {
            "Assets/PKO_Data/ClientImport/",
            "Assets/ImportedClient/"
        };

        public override uint GetVersion() => 2;

        bool IsImportedAsset(string category)
        {
            foreach (string root in ImportedRoots)
                if (assetPath.StartsWith(root + category + "/", StringComparison.Ordinal))
                    return true;
            return false;
        }

        void OnPreprocessModel()
        {
            var importer = (ModelImporter)assetImporter;
            if (IsImportedAsset("Models"))
            {
                importer.globalScale = 1f;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialSearch = ModelImporterMaterialSearch.Local;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.addCollider = false;
                return;
            }

            if (assetPath == "Assets/1LancePKO/1LancePKO.fbx" || assetPath == "Assets/Mobs/Mob1.fbx")
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.resampleCurves = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialSearch = ModelImporterMaterialSearch.Local;
            }
        }

        void OnPreprocessTexture()
        {
            if (!IsImportedAsset("Textures"))
                return;

            var importer = (TextureImporter)assetImporter;
            string path = assetPath.ToLowerInvariant();
            bool sprite = path.Contains("/icon/") || path.Contains("/ui/") || path.Contains("/logo/");
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.spriteImportMode = sprite ? SpriteImportMode.Single : SpriteImportMode.None;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = !sprite;
            importer.alphaIsTransparency = sprite || path.Contains("/effect/");
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.wrapMode = sprite ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
        }

        void OnPostprocessMaterial(Material material)
        {
            if (!IsImportedAsset("Models"))
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || material.shader == shader)
                return;

            Texture mainTexture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            Color baseColor = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            bool transparent = baseColor.a < 0.999f || assetPath.IndexOf("/effect/", StringComparison.OrdinalIgnoreCase) >= 0;

            material.shader = shader;
            material.SetColor("_BaseColor", baseColor);
            if (mainTexture != null)
                material.SetTexture("_BaseMap", mainTexture);
            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", transparent ? 5f : 1f);
            material.SetFloat("_DstBlend", transparent ? 10f : 0f);
            material.SetFloat("_ZWrite", transparent ? 0f : 1f);
            material.renderQueue = transparent ? (int)UnityEngine.Rendering.RenderQueue.Transparent : -1;
            if (transparent)
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}