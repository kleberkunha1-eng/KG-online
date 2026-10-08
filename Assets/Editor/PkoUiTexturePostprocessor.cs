using UnityEditor;
using UnityEngine;

public class PkoUiTexturePostprocessor : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("Resources/PKOUI/")) return;
        var i = (TextureImporter)assetImporter;
        bool blueMageIcon = assetPath.Replace('\\', '/').Contains("Resources/PKOUI/icon/bluemage_");
        i.textureType = blueMageIcon ? TextureImporterType.Sprite : TextureImporterType.Default;
        if (blueMageIcon) i.spriteImportMode = SpriteImportMode.Single;
        i.mipmapEnabled = false; i.textureCompression = TextureImporterCompression.Uncompressed;
        i.filterMode = FilterMode.Bilinear; i.wrapMode = TextureWrapMode.Clamp; i.alphaIsTransparency = true;
        i.npotScale = TextureImporterNPOTScale.None; i.maxTextureSize = blueMageIcon ? 256 : 4096; i.isReadable = false;
    }
}