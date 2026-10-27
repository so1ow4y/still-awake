using KraImporter.Core;
using KraImporter.Data;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace KraImporter.Editor
{
    [ScriptedImporter(1, "kra")]
    internal class KraScriptedImporter : ScriptedImporter
    {
        [NonSerialized] internal List<LayerDatabase> LayersDatabase = new();
        [SerializeField] internal List<LayerInfo> LayersInfo = new();
        [SerializeField] internal List<LayerSave> LayersSave = new();
        [NonSerialized] internal Texture2D finalImage;

        internal enum DrawType
        {
            Texture = 0, 
            Sprite = 1,
            SpriteSheet = 2,
        }

        internal enum ExportType
        {
            SubAsset = 0, 
            Png = 1
        }

        internal enum SubLayerSourceType
        {
            KraLayer = 0,
            LayerSave = 1
        }

        [Serializable]
        internal class LayerInfo
        {
            [SerializeField] internal string LayerId;
        }

        internal class LayerDatabase
        {
            internal string LayerId;
            internal string LayerName;
            internal KraLayerData.NodeTypeEnum LayerType;
            internal int Depth;
            internal int Width;
            internal int Height;
            internal Color32[] LayerPixels;
            internal int[] LayersIndexesInGroup;
            internal int[] MasksIndexesInElement;
        }

        [Serializable]
        internal class LayerSave
        {
            [SerializeField] internal string Id;
            [SerializeField] internal string Name;
            [SerializeField] internal DrawType DrawType;
            [SerializeField] internal ExportType ExportType;
            [SerializeField] internal List<SubLayerSave> subLayerSaves = new();
        }

        [Serializable]
        internal class SubLayerSave
        {
            [SerializeField] internal string LayerId;
            [SerializeField] internal SubLayerSourceType SourceType;
            [SerializeField] internal bool UseAsTransparentMask;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var kraParser = new KraParser();
            string fullKraPath = Path.GetFullPath(ctx.assetPath);

            KraImageData kraImageData = kraParser.ParseKraFile(fullKraPath);

            RecreateDatabase(kraImageData);

            CreateMergedLayerSubAssets(ctx, fullKraPath, out Texture2D firstMerged);

            var mainAsset = ScriptableObject.CreateInstance<KraImportedAsset>();
            mainAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);

            ctx.AddObjectToAsset("main", mainAsset, firstMerged);
            ctx.SetMainObject(mainAsset);
        }

        internal void RecreateDatabase(KraImageData imageData)
        {
            LayersDatabase.Clear();
            LayersInfo.Clear();

            if (imageData == null || imageData.Layers == null)
                return;

            for (int i = 0; i < imageData.Layers.Count; i++)
            {
                KraLayerData layer = imageData.Layers[i];

                if (layer.TileCount == 0 && layer.NodeType == KraLayerData.NodeTypeEnum.paintlayer)
                    FillRgbLayerWithUnifyColor(layer, imageData);

                LayersDatabase.Add(new LayerDatabase
                {
                    LayerId = layer.Uuid,
                    LayerName = layer.LayerName,
                    LayerType = layer.NodeType,
                    Width = imageData.Width,
                    Height = imageData.Height,
                    LayerPixels = layer.LayerPixels,
                    Depth = layer.Depth
                });

                LayersInfo.Add(new LayerInfo
                {
                    LayerId = layer.Uuid,
                });
            }

            // Convert ids into layerDatabase indexes for later quicker data accesibility 
            for (int i = 0; i < LayersDatabase.Count; i++)
            {
                var layerInDatabase = LayersDatabase[i];
                var layerInImageData = imageData.Layers[i];

                layerInDatabase.LayersIndexesInGroup = new int[layerInImageData.LayersIdsInGroup.Count];
                for (int i1 = 0; i1 < layerInImageData.LayersIdsInGroup.Count; i1++)
                {
                    string layerIdToFind = layerInImageData.LayersIdsInGroup[i1];
                    layerInDatabase.LayersIndexesInGroup[i1] = LayersDatabase.FindIndex(l => l.LayerId.Equals(layerIdToFind));
                }

                layerInDatabase.MasksIndexesInElement = new int[layerInImageData.MasksIds.Count];
                for (int i1 = 0; i1 < layerInImageData.MasksIds.Count; i1++)
                {
                    string layerIdToFind = layerInImageData.MasksIds[i1];
                    layerInDatabase.MasksIndexesInElement[i1] = LayersDatabase.FindIndex(l => l.LayerId.Equals(layerIdToFind));
                }
            }

            // Special layers
            LayersDatabase.Add(new LayerDatabase
            {
                LayerId = "MargedTexture",
                LayerName = "MargedTexture",
                Width = imageData.Width,
                Height = imageData.Height,
                LayerPixels = imageData.MergedImage.GetPixels32(),
                Depth = 0
            });

            LayersInfo.Add(new LayerInfo
            {
                LayerId = "MargedTexture",
            });
        }

        void FillRgbLayerWithUnifyColor(KraLayerData layer, KraImageData imageData)
        {
            int pixelCount = imageData.Width * imageData.Height;
            layer.LayerPixels = new Color32[pixelCount];
            for (int p = 0; p < pixelCount; p++)
                layer.LayerPixels[p] = imageData.BackgroundSimpleColor;
        }

        void CreateMergedLayerSubAssets(AssetImportContext ctx, string fullPath, out Texture2D firstMerged)
        {
            firstMerged = default;

            for (int layerSaveIndex = 0; layerSaveIndex < LayersSave.Count; layerSaveIndex++)
            {
                LayerSave layerSave = LayersSave[layerSaveIndex];

                Texture2D texture = BuildMergedTexture(layerSave);

                if (texture == null)
                    continue;


                texture.name = string.IsNullOrWhiteSpace(layerSave.Name)
                    ? $"Layer {layerSaveIndex + 1}"
                    : layerSave.Name;

                if (firstMerged == null)
                    firstMerged = texture;

                var folderPath = Path.GetDirectoryName(fullPath);

                
                if (layerSave.ExportType == ExportType.SubAsset)
                    ctx.AddObjectToAsset($"texture-{layerSaveIndex}", texture);
                if (layerSave.ExportType == ExportType.Png)
                {
                    // save texture as png
                    var pngFilePath = Path.Combine(folderPath, $"{texture.name}.png");
                    KraTextureUtils.SaveTextureAsAsset(texture, pngFilePath, 
                        refreshAssetDatabaseInstant: false, 
                        refreshAssetDatabaseWithDelay: true, 
                        setImporterSettings: true);
                }
                

                if (layerSave.DrawType == DrawType.Sprite)
                {
                        texture.hideFlags = HideFlags.HideInHierarchy;
                    var sprite = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);

                    sprite.name = layerSave.Name;

                    if (layerSave.ExportType == ExportType.SubAsset)
                        ctx.AddObjectToAsset($"sprite-{layerSaveIndex}", sprite);
                    if (layerSave.ExportType == ExportType.Png)
                    {
                        // save sprite as png
                        var pngFilePath = Path.Combine(folderPath, $"{texture.name}.png");
                        KraTextureUtils.SaveTextureAsSprite(texture, pngFilePath, 
                            refreshAssetDatabaseInstant: false, 
                            refreshAssetDatabaseWithDelay: true);
                    }
                }

                if (layerSave.DrawType == DrawType.SpriteSheet)
                {
                    var spriteSheet = BuildSpriteSheetTexture(layerSave);
                    if (layerSave.ExportType == ExportType.SubAsset)
                        ctx.AddObjectToAsset($"spriteSheet-{layerSaveIndex}", spriteSheet);
                    if (layerSave.ExportType == ExportType.Png)
                    {
                        // save sprite as png
                        var pngFilePath = Path.Combine(folderPath, $"{texture.name}.png");
                        KraTextureUtils.SaveTextureAsAsset(spriteSheet, pngFilePath,
                            refreshAssetDatabaseInstant: false,
                            refreshAssetDatabaseWithDelay: true);
                    }
                }
            }
        }

        internal Texture2D BuildLayerSavePreviewTexture(KraScriptedImporter importer, LayerSave layerSave)
        {
            if (layerSave == null)
                return null;

            if (layerSave.DrawType == DrawType.SpriteSheet)
                return importer.BuildSpriteSheetTexture(layerSave);

            List<Color32[]> layersPixels = importer.CollectLayerPixelsForMerge(
                layerSave,
                out int width,
                out int height
            );

            if (layersPixels.Count == 0)
                return null;

            return KraTextureUtils.CreateTextureFromLayerPixels(
                width,
                height,
                KraTextureUtils.MergeLayerPixels(width, height, layersPixels)
            );
        }

        Texture2D BuildSpriteSheetTexture(LayerSave layerSave)
        {
            HashSet<string> visitedLayerSaveIds = new();

            return BuildSpriteSheetTexture(
                layerSave,
                visitedLayerSaveIds
            );
        }

        Texture2D BuildSpriteSheetTexture(LayerSave layerSave, HashSet<string> visitedLayerSaveIds)
        {
            List<Color32[]> spritesPixels = new();

            int width = LayersDatabase[0].Width;
            int height = LayersDatabase[0].Height;

            if (layerSave == null)
                return null;

            if (!string.IsNullOrEmpty(layerSave.Id))
            {
                if (!visitedLayerSaveIds.Add(layerSave.Id))
                {
                    Debug.LogWarning($"Skipped recursive SpriteSheet reference: {layerSave.Name}");
                    return null;
                }
            }

            foreach (SubLayerSave subLayerSave in layerSave.subLayerSaves)
            {
                if (subLayerSave.SourceType == SubLayerSourceType.LayerSave)
                {
                    LayerSave combinedLayerSave = LayersSave.Find(
                        save => save.Id == subLayerSave.LayerId
                    );

                    if (combinedLayerSave == null)
                        continue;

                    List<Color32[]> combinedLayerPixels = CollectLayerPixelsForMerge(
                        combinedLayerSave,
                        out width,
                        out height
                    );

                    if (combinedLayerPixels.Count == 0)
                        continue;

                    Color32[] mergedCombinedPixels = KraTextureUtils.MergeLayerPixels(
                        width,
                        height,
                        combinedLayerPixels
                    );

                    spritesPixels.Add(mergedCombinedPixels);

                    continue;
                }

                int databaseLayerIndex = LayersDatabase.FindIndex(
                    layer => layer.LayerId == subLayerSave.LayerId
                );

                if (databaseLayerIndex < 0)
                    continue;

                LayerDatabase layer = LayersDatabase[databaseLayerIndex];

                if (layer.LayerType == KraLayerData.NodeTypeEnum.transparencymask)
                    continue;

                spritesPixels.Add(layer.LayerPixels);
            }

            if (!string.IsNullOrEmpty(layerSave.Id))
                visitedLayerSaveIds.Remove(layerSave.Id);

            if (spritesPixels.Count == 0)
                return null;

            var spriteSheetTexture = KraTextureUtils.CreateSpriteSheetTexture(
                width,
                height,
                spritesPixels
            );
            spriteSheetTexture.name = layerSave.Name;
            return spriteSheetTexture;
        }

        Texture2D BuildMergedTexture(LayerSave layerSave)
        {
            if (LayersDatabase == null || LayersDatabase.Count == 0)
                return null;

            var layersPixels = CollectLayerPixelsForMerge(layerSave, out int width, out int height);

            return KraTextureUtils.CreateTextureFromLayerPixels(
                width,
                height,
                KraTextureUtils.MergeLayerPixels(width, height, layersPixels)
            );
        }

        internal List<Color32[]> CollectLayerPixelsForMerge(LayerSave layerSave, out int width, out int height)
        {
            HashSet<string> visitedLayerSaveIds = new();

            return CollectLayerPixelsForMerge(
                layerSave,
                out width,
                out height,
                visitedLayerSaveIds
            );
        }

        List<Color32[]> CollectLayerPixelsForMerge(LayerSave layerSave, out int width, out int height, HashSet<string> visitedLayerSaveIds)
        {
            List<Color32[]> layersPixels = new();
            List<Color32[]> masksPixels = new();

            width = LayersDatabase[0].Width;
            height = LayersDatabase[0].Height;

            if (layerSave == null)
                return layersPixels;

            if (!string.IsNullOrEmpty(layerSave.Id))
            {
                if (!visitedLayerSaveIds.Add(layerSave.Id))
                {
                    Debug.LogWarning($"Skipped recursive LayerSave reference: {layerSave.Name}");
                    return layersPixels;
                }
            }

            foreach (SubLayerSave subLayerSave in layerSave.subLayerSaves)
            {
                if (subLayerSave.SourceType == SubLayerSourceType.LayerSave)
                {
                    LayerSave combinedLayerSave = LayersSave.Find(save => save.Id == subLayerSave.LayerId);

                    if (combinedLayerSave == null)
                        continue;

                    List<Color32[]> combinedLayerPixels = CollectLayerPixelsForMerge(combinedLayerSave, out width, out height, visitedLayerSaveIds);

                    if (combinedLayerPixels.Count == 0)
                        continue;

                    Color32[] mergedCombinedPixels = KraTextureUtils.MergeLayerPixels(width, height, combinedLayerPixels);

                    layersPixels.Add(mergedCombinedPixels);

                    continue;
                }

                int databaseLayerIndex = LayersDatabase.FindIndex(layer => layer.LayerId == subLayerSave.LayerId);

                if (databaseLayerIndex < 0)
                    continue;

                // transparency and masks logic

                LayerDatabase layer = LayersDatabase[databaseLayerIndex];
                if (layer.LayerType == KraLayerData.NodeTypeEnum.transparencymask)
                {
                    if (subLayerSave.UseAsTransparentMask)
                        masksPixels.Add(layer.LayerPixels);
                    else
                        layersPixels.Add(layer.LayerPixels);

                    continue;
                }

                if (layer.LayerType == KraLayerData.NodeTypeEnum.grouplayer)
                {
                    layersPixels.Add(layer.LayerPixels);
                    continue;
                }

                layersPixels.Add(layer.LayerPixels);
            }

            if (!string.IsNullOrEmpty(layerSave.Id))
                visitedLayerSaveIds.Remove(layerSave.Id);

            List<Color32[]> finalOutput = new();

            Color32[] combinedMaskPixels = KraTextureUtils.CombineMasksPixels(
                width,
                height,
                masksPixels
            );

            layersPixels.Reverse();

            foreach (Color32[] layerPixels in layersPixels)
            {
                finalOutput.Add(KraTextureUtils.ApplyMaskToPixels(
                    layerPixels,
                    combinedMaskPixels
                ));
            }

            return finalOutput;
        }
    }


}