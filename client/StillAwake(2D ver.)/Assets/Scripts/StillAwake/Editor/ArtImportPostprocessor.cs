using UnityEditor;
using UnityEngine;

namespace StillAwake.EditorTools
{
    /// <summary>
    /// Импорт арта из Assets/Art/Resources: спрайт, 100 px на юнит, без мипмапов и с лимитом 4096 px,
    /// чтобы кадры 3000×2000 не ужимались до 2048. Срабатывает только при первом импорте файла —
    /// дальше настройки можно менять вручную, при замене PNG они сохраняются.
    /// </summary>
    sealed class ArtImportPostprocessor : AssetPostprocessor
    {
        const string ArtRoot = "Assets/Art/Resources/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtRoot) || !assetImporter.importSettingsMissing) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 4096;
        }
    }
}
