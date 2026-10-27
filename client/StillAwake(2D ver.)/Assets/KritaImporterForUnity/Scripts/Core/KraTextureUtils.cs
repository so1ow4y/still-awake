using System.IO;
using UnityEngine;
using KraImporter.Data;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KraImporter.Core
{
    public class KraTextureUtils
    {
        /// <summary>
        /// Pastes a tile pixel buffer into a full image-sized layer pixel buffer. It also converts Krita Y coordinates into Unity texture coordinates.
        /// </summary>
        public static void PasteTilePixelsIntoLayerPixels(Color32[] layerPixels, int imageWidth, int imageHeight, 
                                               Color32[] tilePixels, int tileW, int tileH, int tileX, int tileY)
        {
            for (int ty = 0; ty < tileH; ty++)
            {
                for (int tx = 0; tx < tileW; tx++)
                {
                    int dstX = tileX + tx;

                    // Krita Y -> Unity texture Y
                    int dstY = imageHeight - tileY - tileH + ty;

                    if (dstX < 0 || dstX >= imageWidth || dstY < 0 || dstY >= imageHeight)
                        continue;

                    int srcIndex = (tileH - 1 - ty) * tileW + tx;
                    int dstIndex = dstY * imageWidth + dstX;

                    layerPixels[dstIndex] = tilePixels[srcIndex];
                }
            }
        }

        /// <summary>
        /// Merges multiple layer pixel arrays into one final pixel array. This method is more efficient than repeatedly blending through Texture2D.GetPixels32 and Texture2D.Apply.
        /// </summary>
        public static Color32[] MergeLayerPixels(int width, int height, IReadOnlyList<Color32[]> layersPixels)
        {
            int pixelCount = width * height;
            Color32[] result = new Color32[pixelCount];

            if (layersPixels == null)
                return result;

            for (int layerIndex = 0; layerIndex < layersPixels.Count; layerIndex++)
            {
                Color32[] srcPixels = layersPixels[layerIndex];

                if (srcPixels == null || srcPixels.Length != pixelCount)
                    continue;

                BlendLayerIntoPixels(result, srcPixels);
            }

            return result;
        }

        /// <summary>
        /// Blends one source layer into a destination pixel buffer. Fully transparent pixels are skipped. Fully opaque pixels replace the destination directly.
        /// </summary>
        static void BlendLayerIntoPixels(Color32[] dstPixels, Color32[] srcPixels)
        {
            int length = dstPixels.Length;

            for (int i = 0; i < length; i++)
            {
                Color32 src = srcPixels[i];

                if (src.a == 0)
                    continue;

                if (src.a == 255)
                {
                    dstPixels[i] = src;
                    continue;
                }

                Color32 dst = dstPixels[i];

                if (dst.a == 0)
                {
                    dstPixels[i] = src;
                    continue;
                }

                dstPixels[i] = AlphaBlend(src, dst);
            }
        }

        static Color32 AlphaBlend(Color32 src, Color32 dst)
        {
            float srcA = src.a / 255f;
            float dstA = dst.a / 255f;

            float outA = srcA + dstA * (1f - srcA);

            if (outA <= 0f)
                return new Color32(0, 0, 0, 0);

            byte r = (byte)(((src.r * srcA) + (dst.r * dstA * (1f - srcA))) / outA);
            byte g = (byte)(((src.g * srcA) + (dst.g * dstA * (1f - srcA))) / outA);
            byte b = (byte)(((src.b * srcA) + (dst.b * dstA * (1f - srcA))) / outA);
            byte a = (byte)(outA * 255f);

            return new Color32(r, g, b, a);
        }

#if UNITY_EDITOR
        public static void SaveTextureAsAsset(Texture2D tex, string path, bool refreshAssetDatabaseInstant = true, bool refreshAssetDatabaseWithDelay = false, bool setImporterSettings = true)
        {
            if (tex == null)
                return;

            string fullPath = Path.Combine(Application.dataPath, path);
            string assetPath = FullPathToAssetPath(fullPath);

            if (string.IsNullOrEmpty(assetPath))
                return;

            bool hadImporterBeforeWrite = AssetImporter.GetAtPath(assetPath) != null;

            byte[] png = tex.EncodeToPNG();
            File.WriteAllBytes(fullPath, png);

            void RefreshAndMaybeApplySettings()
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                if (!setImporterSettings)
                    return;

                if (hadImporterBeforeWrite)
                    return;

                TextureImporter textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;

                if (textureImporter == null)
                    return;

                textureImporter.textureType = TextureImporterType.Default;

                textureImporter.SaveAndReimport();
            }

            if (refreshAssetDatabaseInstant)
                RefreshAndMaybeApplySettings();

            if (refreshAssetDatabaseWithDelay)
            {
                EditorApplication.delayCall += () =>
                {
                    RefreshAndMaybeApplySettings();
                };
            }
        }

        /// <summary>
        /// Converts a full system path inside the Unity project into an asset path beginning with Assets/.
        /// </summary>
        public static string FullPathToAssetPath(string fullPath)
        {
            fullPath = fullPath.Replace("\\", "/");

            string assetsPath = Application.dataPath.Replace("\\", "/");

            if (!fullPath.StartsWith(assetsPath))
            {
                Debug.LogWarning($"Path is not inside Assets folder: {fullPath}");
                return "Assets";
            }

            return "Assets" + fullPath.Substring(assetsPath.Length);
        }

        public static Texture2D CreateSpriteSheetTexture(int spriteWidth, int spriteHeight, List<Color32[]> spritesPixels)
        {
            if (spritesPixels == null || spritesPixels.Count == 0)
                return null;

            int spriteCount = spritesPixels.Count;

            int columns = Mathf.CeilToInt(Mathf.Sqrt(spriteCount));
            int rows = Mathf.CeilToInt(spriteCount / (float)columns);

            int sheetWidth = spriteWidth * columns;
            int sheetHeight = spriteHeight * rows;

            Color32[] sheetPixels = new Color32[sheetWidth * sheetHeight];

            for (int i = 0; i < sheetPixels.Length; i++)
                sheetPixels[i] = new Color32(0, 0, 0, 0);

            for (int spriteIndex = 0; spriteIndex < spriteCount; spriteIndex++)
            {
                Color32[] spritePixels = spritesPixels[spriteIndex];

                if (spritePixels == null || spritePixels.Length != spriteWidth * spriteHeight)
                    continue;

                int column = spriteIndex % columns;
                int row = spriteIndex / columns;

                int offsetX = column * spriteWidth;
                int offsetY = (rows - 1 - row) * spriteHeight;

                for (int y = 0; y < spriteHeight; y++)
                {
                    for (int x = 0; x < spriteWidth; x++)
                    {
                        int srcIndex = y * spriteWidth + x;

                        int dstX = offsetX + x;
                        int dstY = offsetY + y;
                        int dstIndex = dstY * sheetWidth + dstX;

                        sheetPixels[dstIndex] = spritePixels[srcIndex];
                    }
                }
            }

            Texture2D spriteSheet = new Texture2D(
                sheetWidth,
                sheetHeight,
                TextureFormat.RGBA32,
                false
            );

            spriteSheet.SetPixels32(sheetPixels);
            spriteSheet.Apply(false, false);

            return spriteSheet;
        }

        public static void SaveTextureAsSprite(Texture2D texture, string fullPath, float pixelsPerUnit = 100f, bool refreshAssetDatabaseInstant = true, bool refreshAssetDatabaseWithDelay = false)
        {
            if (texture == null)
                return;
            string assetPath = FullPathToAssetPath(fullPath);

            TextureImporter existingImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            bool hadImporterBeforeWrite = existingImporter != null;

            byte[] pngBytes = texture.EncodeToPNG();
            File.WriteAllBytes(fullPath, pngBytes);

            void RefreshAndMaybeApplySettings()
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                if (hadImporterBeforeWrite)
                    return;

                TextureImporter textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;

                if (textureImporter == null)
                    return;

                textureImporter.textureType = TextureImporterType.Sprite;

                textureImporter.SaveAndReimport();
            }

            if (refreshAssetDatabaseInstant)
            {
                AssetDatabase.Refresh();
                RefreshAndMaybeApplySettings();
            }

            if (refreshAssetDatabaseWithDelay)
            {
                EditorApplication.delayCall += () =>
                {
                    AssetDatabase.Refresh();
                    RefreshAndMaybeApplySettings();
                };
            }
        }
#endif

        public static void AddLayerPixelsToTexture(Texture2D targetTexture, Color32[] layerPixels, int imageWidth, int imageHeight)
        {
            if (targetTexture == null)
            {
                Debug.LogError("[KRA Importer] Debug target texture is null.");
                return;
            }

            if (targetTexture.width != imageWidth || targetTexture.height != imageHeight)
            {
                Debug.LogError(
                    $"[KRA Importer] Debug texture size mismatch. Target: {targetTexture.width}x{targetTexture.height}, Layer: {imageWidth}x{imageHeight}"
                );
                return;
            }

            if (layerPixels == null || layerPixels.Length != imageWidth * imageHeight)
            {
                Debug.LogError(
                    $"[KRA Importer] Invalid layer pixels size. Got {layerPixels?.Length ?? 0}, expected {imageWidth * imageHeight}"
                );
                return;
            }

            Color32[] targetPixels = targetTexture.GetPixels32();

            for (int i = 0; i < layerPixels.Length; i++)
            {
                Color32 src = layerPixels[i];

                if (src.a == 0)
                    continue;

                Color32 dst = targetPixels[i];

                targetPixels[i] = AlphaBlend(src, dst);
            }

            targetTexture.SetPixels32(targetPixels);
            targetTexture.Apply();
        }

        public static void AddLayerTextureToTexture(Texture2D targetTexture, Texture2D layerTexture, int imageWidth, int imageHeight)
        {
            if (targetTexture == null)
            {
                Debug.LogError("[KRA Importer] Debug target texture is null.");
                return;
            }

            if (targetTexture.width != imageWidth || targetTexture.height != imageHeight)
            {
                Debug.LogError(
                    $"[KRA Importer] Debug texture size mismatch. Target: {targetTexture.width}x{targetTexture.height}, Layer: {imageWidth}x{imageHeight}"
                );
                return;
            }

            if (layerTexture == null || layerTexture.width * layerTexture.height != imageWidth * imageHeight)
            {
                Debug.LogError(
                    $"[KRA Importer] Invalid layer texture size. Got {layerTexture?.width ?? 0}x{layerTexture?.height ?? 0}, expected {imageWidth}x{imageHeight}"
                );
                return;
            }

            Color32[] targetPixels = targetTexture.GetPixels32();
            Color32[] layerPixels = layerTexture.GetPixels32();
            for (int i = 0; i < layerTexture.width * layerTexture.height; i++)
            {
                Color32 src = layerPixels[i];

                if (src.a == 0)
                    continue;

                Color32 dst = targetPixels[i];

                targetPixels[i] = AlphaBlend(src, dst);
            }

            targetTexture.SetPixels32(targetPixels);
            targetTexture.Apply();
        }

        public static Texture2D CreateTextureFromLayerPixels(KraImageData imageData, int layerIndex)
        {
            var texture = new Texture2D(imageData.Width, imageData.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(imageData.Layers[layerIndex].LayerPixels);
            texture.Apply();
            return texture;
        }

        public static Texture2D CreateTextureFromLayerPixels(int width, int height, Color32[] layerPixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            if (layerPixels == null || layerPixels.Length == 0)
                return texture;
            texture.SetPixels32(layerPixels);
            texture.Apply();
            return texture;
        }

        public static Color32[] ApplyMaskToPixels(Color32[] layerPixels, Color32[] maskPixels)
        {
            Color32[] result = new Color32[layerPixels.Length];

            for (int i = 0; i < layerPixels.Length; i++)
            {
                Color32 layerPixel = layerPixels[i];
                Color32 maskPixel = maskPixels[i];

                byte finalAlpha = (byte)((layerPixel.a * maskPixel.r) / 255);

                result[i] = new Color32(
                    layerPixel.r,
                    layerPixel.g,
                    layerPixel.b,
                    finalAlpha
                );
            }

            return result;
        }

        public static Color32[] CombineMasksPixels(int width, int height, List<Color32[]> masksPixels)
        {
            int pixelCount = width * height;
            Color32[] result = new Color32[pixelCount];

            for (int i = 0; i < pixelCount; i++)
                result[i] = new Color32(255, 255, 255, 255);

            if (masksPixels == null || masksPixels.Count == 0)
                return result;

            for (int maskIndex = 0; maskIndex < masksPixels.Count; maskIndex++)
            {
                Color32[] maskPixels = masksPixels[maskIndex];

                for (int i = 0; i < pixelCount; i++)
                {
                    byte maskValue = maskPixels[i].r;
                    byte currentValue = result[i].r;

                    byte combinedValue = (byte)((currentValue * maskValue) / 255);

                    result[i] = new Color32(
                        combinedValue,
                        combinedValue,
                        combinedValue,
                        255
                    );
                }
            }

            return result;
        }
    }
}
