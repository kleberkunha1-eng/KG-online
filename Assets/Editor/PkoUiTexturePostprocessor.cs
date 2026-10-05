using UnityEditor;
using UnityEngine;

public class PkoUiTexturePostprocessor : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("Resources/PKOUI/")) return;
        var i = (TextureImporter)assetImporter;
        i.textureType = TextureImporterType.Default; i.mipmapEnabled = false; i.textureCompression = TextureImporterCompression.Uncompressed;
        i.filterMode = FilterMode.Bilinear; i.wrapMode = TextureWrapMode.Clamp; i.alphaIsTransparency = true;
        i.npotScale = TextureImporterNPOTScale.None; i.maxTextureSize = 4096; i.isReadable = false;
    }
}