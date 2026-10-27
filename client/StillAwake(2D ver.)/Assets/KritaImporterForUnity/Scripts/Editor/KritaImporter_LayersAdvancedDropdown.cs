using KraImporter.Core;
using KraImporter.Data;
using KraImporter.Editor;
using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using static KraImporter.Editor.KraScriptedImporter;

namespace KraImporter.Editor
{
    class KritaImporter_LayersAdvancedDropdown : AdvancedDropdown
    {
        readonly KraScriptedImporter _importer;
        readonly Action<LayerDropdownItem> _onLayerSelected;
        readonly int _currentLayerSaveIndex;
        readonly Dictionary<string, Texture2D> _combinedPreviewCache = new();

        public KritaImporter_LayersAdvancedDropdown(AdvancedDropdownState state, KraScriptedImporter importer, Action<LayerDropdownItem> onLayerSelected, int currentSaveIndex) : base(state)
        {
            _importer = importer;
            _onLayerSelected = onLayerSelected;
            minimumSize = new Vector2(400, 300);
            _currentLayerSaveIndex = currentSaveIndex;
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Layers");

            root.AddChild(BuildHierarchyLayers());
            root.AddChild(BuildAllLayersFlat());
            root.AddChild(BuildPaintLayersFlat());
            root.AddChild(BuildCombinedLayers());

            return root;
        }

        AdvancedDropdownItem BuildCombinedLayers()
        {
            var combinedLayers = new AdvancedDropdownItem("Combined Layers");

            for (int i = 0; i < _importer.LayersSave.Count; i++)
            {
                if (i == _currentLayerSaveIndex)
                    continue;

                LayerSave layerSave = _importer.LayersSave[i];

                if (string.IsNullOrEmpty(layerSave.Id))
                    continue;

                string name = string.IsNullOrWhiteSpace(layerSave.Name)
                    ? $"Layer {i + 1}"
                    : layerSave.Name;

                combinedLayers.AddChild(new LayerDropdownItem(
                    name,
                    layerSave.Id,
                    SubLayerSourceType.LayerSave,
                    GetCombinedLayerPreviewIcon(layerSave)
                ));
            }

            return combinedLayers;
        }

        Texture2D GetCombinedLayerPreviewIcon(LayerSave layerSave)
        {
            if (layerSave == null || string.IsNullOrEmpty(layerSave.Id))
                return null;

            if (_combinedPreviewCache.TryGetValue(layerSave.Id, out Texture2D cached))
                return cached;
            Texture2D preview = _importer.BuildLayerSavePreviewTexture(_importer, layerSave);

            preview.name = $"{layerSave.Name}_CombinedPreview";

            _combinedPreviewCache[layerSave.Id] = preview;

            return preview;
        }


        AdvancedDropdownItem BuildHierarchyLayers()
        {
            var hierarchyLayers = new AdvancedDropdownItem("Hierarchy Layers");

            for (int i = 0; i < _importer.LayersDatabase.Count; i++)
            {
                LayerDatabase layer = _importer.LayersDatabase[i];

                if (layer.Depth != 0)
                    continue;

                hierarchyLayers.AddChild(CreateHierarchyLayerItem(i));
            }

            return hierarchyLayers;
        }

        AdvancedDropdownItem CreateHierarchyLayerItem(int layerIndex)
        {
            LayerDatabase layer = _importer.LayersDatabase[layerIndex];

            bool hasChildren =
                layer.LayersIndexesInGroup is { Length: > 0 } ||
                layer.MasksIndexesInElement is { Length: > 0 };

            if (!hasChildren)
                return CreateLayerDropdownItem(layer);

            var folderItem = new AdvancedDropdownItem($"{layer.LayerName} ({layer.LayerType})")
            {
                icon = GetLayerPreviewIcon(layer)
            };

            folderItem.AddChild(CreateLayerDropdownItem(
                layer,
                $"This {layer.LayerType}"
            ));

            if (layer.LayersIndexesInGroup != null)
                foreach (int childIndex in layer.LayersIndexesInGroup)
                    folderItem.AddChild(CreateHierarchyLayerItem(childIndex));

            if (layer.MasksIndexesInElement != null)
                foreach (int maskIndex in layer.MasksIndexesInElement)
                    folderItem.AddChild(CreateHierarchyLayerItem(maskIndex));

            return folderItem;
        }

        LayerDropdownItem CreateLayerDropdownItem(LayerDatabase layer, string customName = null)
        {
            string itemName = customName ?? $"{layer.LayerName} ({layer.LayerType})";

            return new LayerDropdownItem(
                itemName,
                layer.LayerId,
                SubLayerSourceType.KraLayer,
                GetLayerPreviewIcon(layer)
            );
        }

        Texture2D GetLayerPreviewIcon(LayerDatabase layer)
        {
            Texture2D preview = KraTextureUtils.CreateTextureFromLayerPixels(
                   layer.Width,
                   layer.Height,
                   layer.LayerPixels
               );

            return preview;
        }

        AdvancedDropdownItem BuildAllLayersFlat()
        {
            var allLayers = new AdvancedDropdownItem("All Layers");

            for (int i = 0; i < _importer.LayersDatabase.Count; i++)
            {
                LayerDatabase layer = _importer.LayersDatabase[i];

                allLayers.AddChild(new LayerDropdownItem(
                    $"{layer.LayerName} ({layer.LayerType})",
                    layer.LayerId,
                    SubLayerSourceType.KraLayer
                )
                {
                    icon = GetLayerPreviewIcon(layer)
                });
            }

            return allLayers;
        }

        AdvancedDropdownItem BuildPaintLayersFlat()
        {
            var paintLayers = new AdvancedDropdownItem("Paint Layers");

            for (int i = 0; i < _importer.LayersDatabase.Count; i++)
            {
                LayerDatabase layer = _importer.LayersDatabase[i];

                if (layer.LayerType != KraLayerData.NodeTypeEnum.paintlayer)
                    continue;

                paintLayers.AddChild(new LayerDropdownItem(
                    $"{layer.LayerName}",
                    layer.LayerId,
                    SubLayerSourceType.KraLayer
                )
                {
                    icon = GetLayerPreviewIcon(layer)
                });
            }

            return paintLayers;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is not LayerDropdownItem layerItem)
                return;

            _onLayerSelected?.Invoke(layerItem);
        }
    }

    class LayerDropdownItem : AdvancedDropdownItem
    {
        public string LayerId { get; }
        public SubLayerSourceType SourceType { get; }

        public LayerDropdownItem(string name, string layerId, SubLayerSourceType sourceType, Texture2D previewIcon = null) : base(name)
        {
            LayerId = layerId;
            SourceType = sourceType;
            if (previewIcon != null)
                icon = previewIcon;
        }
    }
}