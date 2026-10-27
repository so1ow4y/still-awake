using KraImporter.Core;
using KraImporter.Data;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KraImporter.Editor
{
    public class KraImageDataViewerWindow : EditorWindow
    {
        ObjectField _kraFileField;
        ScrollView _scrollView;

        KraImageData _imageData;

        [MenuItem("Tools/KRA Importer/Debug KRA Image Data Viewer")]
        public static void Open()
        {
            KraImageDataViewerWindow window = GetWindow<KraImageDataViewerWindow>();
            window.titleContent = new GUIContent("KRA Data Viewer");
            window.minSize = new Vector2(500, 600);
        }

        void CreateGUI()
        {
            VisualElement root = rootVisualElement;

            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            Label title = new Label("KRA Image Data Viewer");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 16;
            title.style.marginBottom = 8;
            root.Add(title);

            _kraFileField = new ObjectField("KRA File")
            {
                objectType = typeof(KraImportedAsset),
                allowSceneObjects = false
            };

            root.Add(_kraFileField);

            Button parseButton = new Button(ParseSelectedKra)
            {
                text = "Parse KRA"
            };

            parseButton.style.marginTop = 6;
            parseButton.style.marginBottom = 8;
            root.Add(parseButton);

            _scrollView = new ScrollView();
            _scrollView.style.flexGrow = 1;
            root.Add(_scrollView);
        }

        void ParseSelectedKra()
        {
            _scrollView.Clear();

            if (_kraFileField.value == null)
            {
                _scrollView.Add(new Label("No KRA file selected."));
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(_kraFileField.value);

            if (string.IsNullOrEmpty(assetPath))
            {
                _scrollView.Add(new Label("Invalid asset path."));
                return;
            }

            if (Path.GetExtension(assetPath).ToLower() != ".kra")
            {
                _scrollView.Add(new Label("Selected file is not a .kra file."));
                return;
            }

            string fullPath = Path.GetFullPath(assetPath);

            KraParser parser = new KraParser();
            _imageData = parser.ParseKraFile(fullPath);

            if (_imageData == null)
            {
                _scrollView.Add(new Label("Failed to parse KRA file."));
                return;
            }

            DrawImageData(_imageData);
        }

        void DrawImageData(KraImageData imageData)
        {
            _scrollView.Clear();

            VisualElement imageBox = CreateBox();

            imageBox.Add(CreateHeader("KraImageData"));
            imageBox.Add(CreateInfo("Name", imageData.Name));
            imageBox.Add(CreateInfo("Width", imageData.Width.ToString()));
            imageBox.Add(CreateInfo("Height", imageData.Height.ToString()));
            imageBox.Add(CreateInfo("Background Color", imageData.BackgroundSimpleColor.ToString()));
            imageBox.Add(CreateInfo("Layers Count", imageData.Layers?.Count.ToString() ?? "0"));
            imageBox.Add(CreateInfo("Merged Image", imageData.MergedImage != null ? "Available" : "Null"));

            if (imageData.MergedImage != null)
            {
                Image mergedPreview = new Image
                {
                    image = imageData.MergedImage,
                    scaleMode = ScaleMode.ScaleToFit
                };

                mergedPreview.style.width = 160;
                mergedPreview.style.height = 160;
                mergedPreview.style.marginTop = 6;

                imageBox.Add(mergedPreview);
            }

            _scrollView.Add(imageBox);

            if (imageData.Layers == null)
                return;

            for (int i = 0; i < imageData.Layers.Count; i++)
            {
                DrawLayerData(imageData.Layers[i], i);
            }
        }

        void DrawLayerData(KraLayerData layer, int index)
        {
            Foldout foldout = new Foldout
            {
                text = $"{index}: {layer.LayerName} ({layer.NodeType})",
                value = false
            };

            foldout.style.marginTop = 6;

            VisualElement box = CreateBox();

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;

            VisualElement infoContainer = new VisualElement();
            infoContainer.style.flexGrow = 1;
            infoContainer.style.marginRight = 10;

            VisualElement previewContainer = new VisualElement();
            previewContainer.style.width = 180;
            previewContainer.style.flexShrink = 0;
            previewContainer.style.alignItems = Align.Center;

            infoContainer.Add(CreateHeader("Basic Data"));
            infoContainer.Add(CreateInfo("Layer Name", layer.LayerName));
            infoContainer.Add(CreateInfo("File Name", layer.FileName));
            infoContainer.Add(CreateInfo("UUID", layer.Uuid));
            infoContainer.Add(CreateInfo("Node Type", layer.NodeType.ToString()));
            infoContainer.Add(CreateInfo("Opacity", layer.Opacity.ToString()));
            infoContainer.Add(CreateInfo("Depth", layer.Depth.ToString()));

            infoContainer.Add(CreateHeader("File Data"));
            infoContainer.Add(CreateInfo("Version", layer.Version.ToString()));
            infoContainer.Add(CreateInfo("Tile Width", layer.TileWidth.ToString()));
            infoContainer.Add(CreateInfo("Tile Height", layer.TileHeight.ToString()));
            infoContainer.Add(CreateInfo("Pixel Size", layer.PixelSize.ToString()));
            infoContainer.Add(CreateInfo("Tile Count", layer.TileCount.ToString()));
            infoContainer.Add(CreateInfo("Layer Pixels", layer.LayerPixels != null ? layer.LayerPixels.Length.ToString() : "Null"));

            infoContainer.Add(CreateHeader("Group Data"));
            infoContainer.Add(CreateInfo("LayersIdsInGroup Count", layer.LayersIdsInGroup != null ? layer.LayersIdsInGroup.Count.ToString() : "Null"));

            if (layer.LayersIdsInGroup != null && layer.LayersIdsInGroup.Count > 0)
            {
                for (int i = 0; i < layer.LayersIdsInGroup.Count; i++)
                    infoContainer.Add(CreateInfo($"Group Layer Id {i}", layer.LayersIdsInGroup[i]));
            }

            infoContainer.Add(CreateHeader("Mask Data"));
            infoContainer.Add(CreateInfo("MasksIds Count", layer.MasksIds != null ? layer.MasksIds.Count.ToString() : "Null"));

            if (layer.MasksIds != null && layer.MasksIds.Count > 0)
            {
                for (int i = 0; i < layer.MasksIds.Count; i++)
                    infoContainer.Add(CreateInfo($"Mask Id {i}", layer.MasksIds[i]));
            }

            previewContainer.Add(CreateHeader("Preview"));

            if (layer.LayerPixels != null &&
                layer.LayerPixels.Length == _imageData.Width * _imageData.Height)
            {
                Texture2D preview = KraTextureUtils.CreateTextureFromLayerPixels(
                    _imageData.Width,
                    _imageData.Height,
                    layer.LayerPixels
                );

                preview.name = layer.LayerName + "_Preview";

                Image image = new Image
                {
                    image = preview,
                    scaleMode = ScaleMode.ScaleToFit
                };

                image.style.width = 160;
                image.style.height = 160;
                image.style.marginTop = 4;
                image.style.marginBottom = 4;

                previewContainer.Add(image);
            }
            else
            {
                previewContainer.Add(new Label("No valid layer pixels."));
            }

            row.Add(infoContainer);
            row.Add(previewContainer);

            box.Add(row);
            foldout.Add(box);
            _scrollView.Add(foldout);
        }

        VisualElement CreateBox()
        {
            VisualElement box = new VisualElement();

            box.style.paddingLeft = 8;
            box.style.paddingRight = 8;
            box.style.paddingTop = 8;
            box.style.paddingBottom = 8;

            box.style.marginBottom = 8;

            box.style.borderLeftWidth = 1;
            box.style.borderRightWidth = 1;
            box.style.borderTopWidth = 1;
            box.style.borderBottomWidth = 1;

            box.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f);
            box.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            box.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f);
            box.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);

            box.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);

            return box;
        }

        Label CreateHeader(string text)
        {
            Label label = new Label(text);

            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 6;
            label.style.marginBottom = 4;

            return label;
        }

        VisualElement CreateInfo(string label, string value)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;

            Label keyLabel = new Label(label + ":");
            keyLabel.style.width = 170;
            keyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            Label valueLabel = new Label(value);
            valueLabel.style.flexGrow = 1;
            valueLabel.style.whiteSpace = WhiteSpace.Normal;

            row.Add(keyLabel);
            row.Add(valueLabel);

            return row;
        }
    }
}