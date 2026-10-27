using KraImporter.Core;
using KraImporter.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;
using static KraImporter.Editor.KraScriptedImporter;

namespace KraImporter.Editor
{
    [CustomEditor(typeof(KraScriptedImporter))]
    internal class KritaImporterEditor : ScriptedImporterEditor
    {
        VisualElement _layersContainer;
        readonly Dictionary<int, VisualElement> _layerSaveElements = new();
        readonly Dictionary<string, Texture2D> _layerPreviewCache = new();
        readonly AdvancedDropdownState _dropdownState = new();

        StyleSheet LoadLocalStyleSheet(string ussFileName)
        {
            MonoScript script = MonoScript.FromScriptableObject(this);
            string scriptPath = AssetDatabase.GetAssetPath(script);

            if (string.IsNullOrEmpty(scriptPath))
            {
                Debug.LogError("[KRA Importer] Could not resolve editor script path.");
                return null;
            }

            string directory = Path.GetDirectoryName(scriptPath);
            string ussPath = $"{directory}/{ussFileName}".Replace("\\", "/");

            return AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement().WithClass("kra-root");
            string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));

            var styleSheet = LoadLocalStyleSheet("KritaImporterEditor.uss");

            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            serializedObject.Update();

            var importer = (KraScriptedImporter)target;

            EnsureLayerInfoLoaded(importer);
            EnsureLayerSaveIds();

            var title = new Label("KRA Importer Settings");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            root.Add(title);

            var layerFoldout = new Foldout
            {
                text = "Layers",
                value = true
            }.WithClass("layer-Foldout");

            _layersContainer = new VisualElement();
            layerFoldout.Add(_layersContainer);

            var addLayerButton = new Button(() =>
            {
                AddNewLayerSave(importer);
                RebuildLayersUI(importer);
            }).WithTextButton("+").WithClass("add-layer");

            layerFoldout.Add(addLayerButton);

            root.Add(layerFoldout);

            RebuildLayersUI(importer);

            var reimportButton = new Button(() =>
            {
                serializedObject.ApplyModifiedProperties();

                _layerPreviewCache.Clear();

                importer.SaveAndReimport();
            }).WithTextButton("Reimport KRA").WithClass("reimportKra-btn");

            root.Add(reimportButton);

            root.Add(new IMGUIContainer(() =>
            {
                ApplyRevertGUI();
            }));

            return root;
        }

        void EnsureLayerSaveIds()
        {
            serializedObject.Update();

            SerializedProperty layersSaveProperty =
                serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

            bool changed = false;

            for (int i = 0; i < layersSaveProperty.arraySize; i++)
            {
                SerializedProperty layerSaveProperty =
                    layersSaveProperty.GetArrayElementAtIndex(i);

                SerializedProperty idProperty =
                    layerSaveProperty.FindPropertyRelative("Id");

                if (string.IsNullOrEmpty(idProperty.stringValue))
                {
                    idProperty.stringValue = Guid.NewGuid().ToString();
                    changed = true;
                }
            }

            if (changed)
                serializedObject.ApplyModifiedProperties();
        }

        void EnsureLayerInfoLoaded(KraScriptedImporter importer)
        {
            if (importer.LayersInfo != null && importer.LayersInfo.Count > 0)
                return;

            EnsureLayersDatabaseLoaded(importer);

            serializedObject.Update();
            EditorUtility.SetDirty(importer);
        }

        void EnsureLayersDatabaseLoaded(KraScriptedImporter importer)
        {
            if (importer.LayersDatabase != null && importer.LayersDatabase.Count > 0)
                return;

            string fullPath = Path.GetFullPath(importer.assetPath);

            var parser = new KraParser();
            KraImageData imageData = parser.ParseKraFile(fullPath);
            importer.finalImage = imageData.MergedImage;
            importer.RecreateDatabase(imageData);
        }

        void RebuildLayersUI(KraScriptedImporter importer)
        {
            _layersContainer.Clear();
            _layerSaveElements.Clear();

            serializedObject.Update();

            SerializedProperty layersSaveProperty =
                serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

            for (int layerSaveIndex = 0; layerSaveIndex < layersSaveProperty.arraySize; layerSaveIndex++)
            {
                VisualElement layerElement = CreateLayerVisualElement(
                    importer,
                    layerSaveIndex,
                    layersSaveProperty
                );

                _layerSaveElements[layerSaveIndex] = layerElement;

                _layersContainer.Add(layerElement);
            }
        }

        void RefreshAffectedLayersUI(
    KraScriptedImporter importer,
    int changedLayerSaveIndex
)
        {
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();

            _layerPreviewCache.Clear();

            List<int> affectedIndexes = GetAffectedLayerSaveIndexes(
                importer,
                changedLayerSaveIndex
            );

            SerializedProperty layersSaveProperty =
                serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

            foreach (int layerSaveIndex in affectedIndexes)
            {
                if (!_layerSaveElements.TryGetValue(layerSaveIndex, out VisualElement oldElement))
                    continue;

                VisualElement newElement = CreateLayerVisualElement(
                    importer,
                    layerSaveIndex,
                    layersSaveProperty
                );

                oldElement.parent.Insert(
                    oldElement.parent.IndexOf(oldElement),
                    newElement
                );

                oldElement.RemoveFromHierarchy();

                _layerSaveElements[layerSaveIndex] = newElement;
            }
        }

        string GetFoldoutSessionKey(KraScriptedImporter importer, string layerSaveId)
        {
            return $"KRA_IMPORTER_FOLDOUT_{importer.assetPath}_{layerSaveId}";
        }

        bool GetFoldoutState(KraScriptedImporter importer, string layerSaveId)
        {
            string key = GetFoldoutSessionKey(importer, layerSaveId);
            return SessionState.GetBool(key, false);
        }

        void SetFoldoutState(KraScriptedImporter importer, string layerSaveId, bool value)
        {
            string key = GetFoldoutSessionKey(importer, layerSaveId);
            SessionState.SetBool(key, value);
        }

        VisualElement CreateLayerVisualElement(KraScriptedImporter importer, int layerSaveIndex, SerializedProperty layersSaveProperty)
        {
            SerializedProperty layerSaveProperty =
                layersSaveProperty.GetArrayElementAtIndex(layerSaveIndex);

            SerializedProperty layerSaveIdProperty = layerSaveProperty.FindPropertyRelative("Id");

            string layerSaveId = layerSaveIdProperty.stringValue;

            SerializedProperty layerSaveNameProperty =
                layerSaveProperty.FindPropertyRelative("Name");

            string layerName = string.IsNullOrWhiteSpace(layerSaveNameProperty.stringValue)
                ? $"Layer {layerSaveIndex + 1}"
                : layerSaveNameProperty.stringValue;

            var wrapper = new VisualElement().WithClass("layer-wrapper");

            bool foldoutValue = GetFoldoutState(importer, layerSaveId);

            var foldout = new Foldout
            {
                text = "",
                value = foldoutValue
            }.WithClass("layer-inner-foldout");

            foldout.RegisterValueChangedCallback(evt =>
            {
                SetFoldoutState(importer, layerSaveId, evt.newValue);
            });

            Toggle foldoutToggle = foldout.Q<Toggle>();

            if (foldoutToggle != null)
            {
                foldoutToggle.pickingMode = PickingMode.Position;

                foldoutToggle.AddToClassList("layer-inner-foldout-toggle");

                TextField nameField = CreateTextField(layerSaveIndex, layersSaveProperty).WithClass("layer-foldout-name-field") as TextField;

                nameField.SetValueWithoutNotify(layerName);

                foldoutToggle.Add(nameField);

                var removeLayerButton = new Button(() =>
                {
                    SessionState.EraseBool(GetFoldoutSessionKey(importer, layerSaveId));
                    SessionState.EraseBool(GetExportFoldoutSessionKey(importer, layerSaveId));

                    RemoveLayerSaveAt(layerSaveIndex);
                    RebuildLayersUI(importer);
                })
                {
                    text = "X"
                }.WithClass("remove-layer");

                foldoutToggle.Add(removeLayerButton);
            }

            var middleWrapper = new VisualElement().WithClass("layer-wrapper-middle");
            var botWrapper = new VisualElement().WithClass("layer-wrapper-bot");

            var previewContainer = new VisualElement().WithClass("preview-container");
            var subLayersContainer = new VisualElement().WithClass("sublayers-container");

            var labelContainer = new Label("<").WithClass("layer-label");

            RebuildLayerPreview(importer, layerSaveIndex, previewContainer);
            RebuildSubLayersUI(importer, layerSaveIndex, subLayersContainer, previewContainer);

            middleWrapper.Add(previewContainer);
            middleWrapper.Add(labelContainer);
            middleWrapper.Add(subLayersContainer);

            botWrapper.Add(CreateExportSettings(importer, layerSaveIndex, layerSaveId));

            foldout.Add(middleWrapper);
            foldout.Add(botWrapper);

            wrapper.Add(foldout);

            return wrapper;
        }

        string GetExportFoldoutSessionKey(KraScriptedImporter importer, string layerSaveId)
        {
            return $"KRA_IMPORTER_EXPORT_FOLDOUT_{importer.assetPath}_{layerSaveId}";
        }

        bool GetExportFoldoutState(KraScriptedImporter importer, string layerSaveId)
        {
            return SessionState.GetBool(
                GetExportFoldoutSessionKey(importer, layerSaveId),
                false
            );
        }

        void SetExportFoldoutState(KraScriptedImporter importer, string layerSaveId, bool value)
        {
            SessionState.SetBool(
                GetExportFoldoutSessionKey(importer, layerSaveId),
                value
            );
        }

        VisualElement CreateExportSettings(KraScriptedImporter importer, int layerSaveIndex, string layerSaveId)
        {
            SerializedProperty layersSaveProperty = serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

            SerializedProperty layerSaveProperty = layersSaveProperty.GetArrayElementAtIndex(layerSaveIndex);

            var DrawTypeProprty = layerSaveProperty.FindPropertyRelative("DrawType");
            var ExportTypeProprty = layerSaveProperty.FindPropertyRelative("ExportType");

            var drawType = (KraScriptedImporter.DrawType)DrawTypeProprty.enumValueIndex;
            var exportType = (KraScriptedImporter.ExportType)ExportTypeProprty.enumValueIndex;

            Foldout exportFoldout = new Foldout().WithTextFoldout($"Export settings (Draw: {drawType}, Export: {exportType})").WithClass("export-foldout");
            exportFoldout.value = GetExportFoldoutState(importer, layerSaveId);

            exportFoldout.RegisterValueChangedCallback(evt =>
            {
                SetExportFoldoutState(importer, layerSaveId, evt.newValue);
            });

            // generation
            ToggleButtonGroup drawTypeBtnGroup = new() { allowEmptySelection = false, isMultipleSelection = false };
            Button textureBtn = new Button().WithTextButton(nameof(KraScriptedImporter.DrawType.Texture));
            Button spriteBtn = new Button().WithTextButton(nameof(KraScriptedImporter.DrawType.Sprite));
            Button spriteSheetBtn = new Button().WithTextButton(nameof(KraScriptedImporter.DrawType.SpriteSheet));
            drawTypeBtnGroup.Add(textureBtn);
            drawTypeBtnGroup.Add(spriteBtn);
            drawTypeBtnGroup.Add(spriteSheetBtn);
            exportFoldout.Add(drawTypeBtnGroup);


            ToggleButtonGroup exportTypeBtnGroup = new() { allowEmptySelection = false, isMultipleSelection = false };
            Button subAssetbtn = new Button().WithTextButton(nameof(KraScriptedImporter.ExportType.SubAsset));
            Button png = new Button().WithTextButton(nameof(KraScriptedImporter.ExportType.Png));
            exportTypeBtnGroup.Add(subAssetbtn);
            exportTypeBtnGroup.Add(png);
            exportFoldout.Add(exportTypeBtnGroup);

            // event
            drawTypeBtnGroup.RegisterValueChangedCallback(evt =>
            {
                ExportSettingsBtnGroup(evt.newValue, layerSaveIndex, drawTypeBtnGroup, "DrawType");
            });

            exportTypeBtnGroup.RegisterValueChangedCallback(evt =>
            {
                ExportSettingsBtnGroup(evt.newValue, layerSaveIndex, exportTypeBtnGroup, "ExportType");
            });

            // Load
            serializedObject.Update();

            var drawTypeValues = drawTypeBtnGroup.value;
            var exportTypeValues = exportTypeBtnGroup.value;

            int savedDrawIndex = Mathf.Clamp(DrawTypeProprty.enumValueIndex, 0, System.Enum.GetValues(typeof(KraScriptedImporter.DrawType)).Length - 1);
            int savedExportIndex = Mathf.Clamp(ExportTypeProprty.enumValueIndex, 0, System.Enum.GetValues(typeof(KraScriptedImporter.ExportType)).Length - 1);

            for (int i = 0; i < drawTypeValues.length; i++)
                drawTypeValues[i] = i == savedDrawIndex;

            for (int i = 0; i < exportTypeValues.length; i++)
                exportTypeValues[i] = i == savedExportIndex;

            drawTypeBtnGroup.SetValueWithoutNotify(drawTypeValues);
            exportTypeBtnGroup.SetValueWithoutNotify(exportTypeValues);

            return exportFoldout;
        }

        void ExportSettingsBtnGroup(ToggleButtonGroupState state, int layerSaveIndex, ToggleButtonGroup btnGroup, string proprtyName)
        {
            serializedObject.Update();
            SerializedProperty layersSaveProperty = serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

            SerializedProperty layerSaveProperty = layersSaveProperty.GetArrayElementAtIndex(layerSaveIndex);

            var typeProprty = layerSaveProperty.FindPropertyRelative(proprtyName);

            for (int i = 0; i < btnGroup.childCount; i++)
                if (state[i])
                {
                    typeProprty.enumValueIndex = i;
                    break;
                }

            serializedObject.ApplyModifiedProperties();
        }

        VisualElement CreateTextField(int layerSaveIndex, SerializedProperty layersSaveProperty)
        {
            var textField = new TextField().WithClass("layer-textField");

            var layerSave = layersSaveProperty.GetArrayElementAtIndex(layerSaveIndex);
            var layerSaveName = layerSave.FindPropertyRelative("Name");

            textField.SetValueWithoutNotify(layerSaveName.stringValue);
            textField.isDelayed = true;

            textField.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();

                SerializedProperty currentLayersSaveProperty =
                    serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

                if (layerSaveIndex < 0 || layerSaveIndex >= currentLayersSaveProperty.arraySize)
                    return;

                SerializedProperty currentLayerSave =
                    currentLayersSaveProperty.GetArrayElementAtIndex(layerSaveIndex);

                SerializedProperty currentName =
                    currentLayerSave.FindPropertyRelative("Name");

                currentName.stringValue = evt.newValue;

                serializedObject.ApplyModifiedProperties();
            });

            return textField;
        }

        void RebuildSubLayersUI(KraScriptedImporter importer, int layerSaveIndex, VisualElement subLayersContainer, VisualElement previewContainer)
        {
            subLayersContainer.Clear();

            serializedObject.Update();

            SerializedProperty subLayerSavesProperty = GetSubLayerSavesProperty(layerSaveIndex);

            for (int subLayerSaveIndex = 0; subLayerSaveIndex < subLayerSavesProperty.arraySize; subLayerSaveIndex++)
            {
                subLayersContainer.Add(CreateSubLayerVisualElement(
                    importer,
                    layerSaveIndex,
                    subLayerSaveIndex,
                    subLayersContainer,
                    previewContainer
                ));
            }
            var addSubLayerButton = new Button(() =>
            {
                AddNewSubLayerSave(importer, layerSaveIndex);

                RefreshAffectedLayersUI(importer, layerSaveIndex);
            }).WithTextButton("+").WithClass("add-subLayer");

            subLayersContainer.Add(addSubLayerButton);
        }

        List<string> GetLayersNames(KraScriptedImporter importer)
        {
            List<string> layerNames = importer.LayersDatabase
               .Select(layer => layer.LayerName)
               .ToList();
            return layerNames;
        }

        VisualElement CreateSubLayerVisualElement(KraScriptedImporter importer, int layerSaveIndex, int subLayerSaveIndex, VisualElement subLayersContainer, VisualElement previewContainer)
        {
            var recordWrapper = new VisualElement().WithClass("subLayer-record");

            List<string> layerNames = GetLayersNames(importer);

            serializedObject.Update();

            SerializedProperty subLayerSavesProperty = GetSubLayerSavesProperty(layerSaveIndex);

            SerializedProperty subLayerProperty = subLayerSavesProperty.GetArrayElementAtIndex(subLayerSaveIndex);

            SerializedProperty layerIdProperty = subLayerProperty.FindPropertyRelative("LayerId");

            SerializedProperty sourceTypeProperty = subLayerProperty.FindPropertyRelative("SourceType");

            SubLayerSourceType sourceType = (SubLayerSourceType)sourceTypeProperty.enumValueIndex;

            int selectedLayerInfoIndex = importer.LayersInfo.FindIndex(layer => layer.LayerId == layerIdProperty.stringValue);

            if (selectedLayerInfoIndex < 0)
                selectedLayerInfoIndex = 0;

            var subPreviewContainer = new VisualElement().WithClass("preview-sublayer-container");

            VisualElement moveBtnsGroup = new VisualElement();

            var moveUpBtn = new Button(clickEvent: () =>
            {
                MoveSubLayerSave(layerSaveIndex, subLayerSaveIndex, subLayerSaveIndex - 1);

                RefreshAffectedLayersUI(importer, layerSaveIndex);
            }).WithTextButton("↑").WithClass("moveBtn");
            var moveDownBtn = new Button(clickEvent: () =>
            {
                MoveSubLayerSave(layerSaveIndex, subLayerSaveIndex, subLayerSaveIndex + 1);

                RefreshAffectedLayersUI(importer, layerSaveIndex);
            }).WithTextButton("↓").WithClass("moveBtn");

            if (subLayerSaveIndex != 0)
                moveBtnsGroup.Add(moveUpBtn);

            if (subLayerSaveIndex != subLayerSavesProperty.arraySize - 1)
                moveBtnsGroup.Add(moveDownBtn);

            subPreviewContainer.Add(CreateSubLayerPreview(importer, layerIdProperty.stringValue, sourceType));

            string layerId = importer.LayersInfo[selectedLayerInfoIndex].LayerId;
            int databaseLayerIndex = importer.LayersDatabase.FindIndex(
                    layer => layer.LayerId == layerId
                );
            var databaseLayer = importer.LayersDatabase[databaseLayerIndex];                

            var button = new Button(() =>
            {
                var dropdown = new KritaImporter_LayersAdvancedDropdown(_dropdownState, importer, 
                    onLayerSelected: (selectedItem) =>
                    {
                        serializedObject.Update();

                        SerializedProperty currentSubLayerProperty = GetSubLayerSavesItemProperty(layerSaveIndex, subLayerSaveIndex);

                        SerializedProperty currentLayerIdProperty = currentSubLayerProperty.FindPropertyRelative("LayerId");

                        SerializedProperty currentSourceTypeProperty = currentSubLayerProperty.FindPropertyRelative("SourceType");

                        currentLayerIdProperty.stringValue = selectedItem.LayerId;
                        currentSourceTypeProperty.enumValueIndex = (int)selectedItem.SourceType;

                        serializedObject.ApplyModifiedProperties();
                        RefreshAffectedLayersUI(importer, layerSaveIndex);
                    }, layerSaveIndex);
                dropdown.Show(new Rect(Event.current.mousePosition, Vector2.zero));
            }).WithTextButton($"Select layer ({GetSubLayerDisplayName(importer, layerIdProperty.stringValue, sourceType)})").WithClass("LayerDropdown");


            var removeSubLayerButton = new Button(() =>
            {
                RemoveSubLayerSaveAt(layerSaveIndex, subLayerSaveIndex);

                RefreshAffectedLayersUI(importer, layerSaveIndex);
            })
            {
                text = "X"
            }.WithClass("remove-subLayer");

            recordWrapper.Add(moveBtnsGroup);
            recordWrapper.Add(subPreviewContainer);
            recordWrapper.Add(button);
            if (sourceType == SubLayerSourceType.KraLayer && databaseLayer.LayerType == KraLayerData.NodeTypeEnum.transparencymask)
                recordWrapper.Add(DrawTransparencyToggle(layerSaveIndex, subLayerSaveIndex));
            recordWrapper.Add(removeSubLayerButton);

            return recordWrapper;
        }
        List<int> GetAffectedLayerSaveIndexes(KraScriptedImporter importer, int changedLayerSaveIndex)
        {
            List<int> affectedIndexes = new();
            Queue<int> queue = new();

            affectedIndexes.Add(changedLayerSaveIndex);
            queue.Enqueue(changedLayerSaveIndex);

            while (queue.Count > 0)
            {
                int currentIndex = queue.Dequeue();

                if (currentIndex < 0 || currentIndex >= importer.LayersSave.Count)
                    continue;

                string changedLayerSaveId = importer.LayersSave[currentIndex].Id;

                if (string.IsNullOrEmpty(changedLayerSaveId))
                    continue;

                for (int i = 0; i < importer.LayersSave.Count; i++)
                {
                    if (affectedIndexes.Contains(i))
                        continue;

                    LayerSave layerSave = importer.LayersSave[i];

                    bool usesChangedLayer = layerSave.subLayerSaves.Any(subLayer =>
                        subLayer.SourceType == SubLayerSourceType.LayerSave &&
                        subLayer.LayerId == changedLayerSaveId
                    );

                    if (!usesChangedLayer)
                        continue;

                    affectedIndexes.Add(i);
                    queue.Enqueue(i);
                }
            }

            return affectedIndexes;
        }


        string GetSubLayerDisplayName(KraScriptedImporter importer, string layerId, SubLayerSourceType sourceType)
        {
            if (sourceType == SubLayerSourceType.LayerSave)
            {
                LayerSave layerSave = importer.LayersSave.FirstOrDefault(
                    layer => layer.Id == layerId
                );

                if (layerSave == null)
                    return "Missing Combined Layer";

                return string.IsNullOrWhiteSpace(layerSave.Name)
                    ? "Combined Layer"
                    : layerSave.Name;
            }

            LayerDatabase databaseLayer = importer.LayersDatabase.FirstOrDefault(
                layer => layer.LayerId == layerId
            );

            if (databaseLayer == null)
                return "Missing KRA Layer";

            return databaseLayer.LayerName;
        }

        Toggle DrawTransparencyToggle(int layerSaveIndex, int subLayerSaveIndex)
        {
            serializedObject.Update();

            SerializedProperty subLayerSavesProperty = GetSubLayerSavesProperty(layerSaveIndex);
            SerializedProperty subLayerProperty = subLayerSavesProperty.GetArrayElementAtIndex(subLayerSaveIndex);
            SerializedProperty useAsTransparentMaskProperty = subLayerProperty.FindPropertyRelative("UseAsTransparentMask");

            Toggle transparencyToggle = new Toggle("T").WithClass("transparencyToggle");

            transparencyToggle.SetValueWithoutNotify(useAsTransparentMaskProperty.boolValue);

            transparencyToggle.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();

                SerializedProperty currentSubLayerSavesProperty = GetSubLayerSavesProperty(layerSaveIndex);
                SerializedProperty currentSubLayerProperty = currentSubLayerSavesProperty.GetArrayElementAtIndex(subLayerSaveIndex);
                SerializedProperty currentUseAsTransparentMaskProperty = currentSubLayerProperty.FindPropertyRelative("UseAsTransparentMask");

                currentUseAsTransparentMaskProperty.boolValue = evt.newValue;

                serializedObject.ApplyModifiedProperties();
            });

            return transparencyToggle;
        }

        void MoveSubLayerSave(int layerSaveIndex, int oldIndex, int newIndex)
        {
            serializedObject.Update();

            SerializedProperty subLayerSavesProperty = GetSubLayerSavesProperty(layerSaveIndex);

            if (oldIndex < 0 || oldIndex >= subLayerSavesProperty.arraySize)
                return;

            if (newIndex < 0 || newIndex >= subLayerSavesProperty.arraySize)
                return;

            subLayerSavesProperty.MoveArrayElement(oldIndex, newIndex);

            serializedObject.ApplyModifiedProperties();
        }

        void RebuildLayerPreview(KraScriptedImporter importer, int layerSaveIndex, VisualElement previewContainer)
        {
            previewContainer.Clear();
            previewContainer.Add(CreatePreviewLayerImage(importer, layerSaveIndex));
        }

        SerializedProperty GetSubLayerSavesProperty(int layerSaveIndex)
        {
            SerializedProperty layersSaveProperty = serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

            SerializedProperty layerSaveProperty = layersSaveProperty.GetArrayElementAtIndex(layerSaveIndex);

            return layerSaveProperty.FindPropertyRelative("subLayerSaves");
        }

        SerializedProperty GetSubLayerSavesItemProperty(int layerSaveIndex, int subLayerSaveIndex)
        {
            SerializedProperty layersSaveProperty = serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

            SerializedProperty layerSaveProperty = layersSaveProperty.GetArrayElementAtIndex(layerSaveIndex);

            SerializedProperty currentSubLayerSavesProperty = layerSaveProperty.FindPropertyRelative("subLayerSaves");

            SerializedProperty currentSubLayerProperty = currentSubLayerSavesProperty.GetArrayElementAtIndex(subLayerSaveIndex);

            return currentSubLayerProperty;
        }

        SerializedProperty GetLayerSavesProperty(int layerSaveIndex)
        {
            SerializedProperty layersSaveProperty = serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

            SerializedProperty layerSaveProperty = layersSaveProperty.GetArrayElementAtIndex(layerSaveIndex);

            return layerSaveProperty;
        }

        void AddNewLayerSave(KraScriptedImporter importer)
        {
            if (importer.LayersInfo == null || importer.LayersInfo.Count == 0)
            {
                Debug.LogError("No layers available in LayersInfo.");
                return;
            }

            serializedObject.Update();

            SerializedProperty layersSaveProperty =
                serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

            int newLayerSaveIndex = layersSaveProperty.arraySize;

            layersSaveProperty.InsertArrayElementAtIndex(newLayerSaveIndex);

            SerializedProperty newLayerSaveProperty = layersSaveProperty.GetArrayElementAtIndex(newLayerSaveIndex);

            SerializedProperty nameProperty =
                newLayerSaveProperty.FindPropertyRelative("Name");

            nameProperty.stringValue = $"Layer {newLayerSaveIndex + 1}";

            SerializedProperty idProperty = newLayerSaveProperty.FindPropertyRelative("Id");

            idProperty.stringValue = Guid.NewGuid().ToString();

            SerializedProperty subLayersProperty = newLayerSaveProperty.FindPropertyRelative("subLayerSaves");

            subLayersProperty.ClearArray();
            subLayersProperty.InsertArrayElementAtIndex(0);

            SerializedProperty firstSubLayerProperty = subLayersProperty.GetArrayElementAtIndex(0);

            SerializedProperty firstSubLayerIdProperty = firstSubLayerProperty.FindPropertyRelative("LayerId");

            firstSubLayerIdProperty.stringValue = importer.LayersInfo[0].LayerId;

            serializedObject.ApplyModifiedProperties();
        }

        void RemoveLayerSaveAt(int layerSaveIndex)
        {
            serializedObject.Update();

            SerializedProperty layersSaveProperty = serializedObject.FindProperty(nameof(KraScriptedImporter.LayersSave));

            if (layerSaveIndex < 0 || layerSaveIndex >= layersSaveProperty.arraySize)
                return;

            layersSaveProperty.DeleteArrayElementAtIndex(layerSaveIndex);

            serializedObject.ApplyModifiedProperties();
        }

        void AddNewSubLayerSave(KraScriptedImporter importer, int layerSaveIndex)
        {
            if (importer.LayersInfo == null || importer.LayersInfo.Count == 0)
            {
                Debug.LogError("No layers available in LayersInfo.");
                return;
            }

            serializedObject.Update();

            SerializedProperty subLayerSavesProperty = GetSubLayerSavesProperty(layerSaveIndex);

            int newSubLayerIndex = subLayerSavesProperty.arraySize;

            subLayerSavesProperty.InsertArrayElementAtIndex(newSubLayerIndex);

            SerializedProperty newSubLayerProperty = subLayerSavesProperty.GetArrayElementAtIndex(newSubLayerIndex);

            SerializedProperty layerIdProperty = newSubLayerProperty.FindPropertyRelative("LayerId");

            layerIdProperty.stringValue = importer.LayersInfo[0].LayerId;

            SerializedProperty sourceTypeProperty = newSubLayerProperty.FindPropertyRelative("SourceType");

            sourceTypeProperty.enumValueIndex = (int)SubLayerSourceType.KraLayer;

            serializedObject.ApplyModifiedProperties();
        }

        void RemoveSubLayerSaveAt(int layerSaveIndex, int subLayerSaveIndex)
        {
            serializedObject.Update();

            SerializedProperty subLayerSavesProperty = GetSubLayerSavesProperty(layerSaveIndex);

            if (subLayerSaveIndex < 0 || subLayerSaveIndex >= subLayerSavesProperty.arraySize)
                return;

            subLayerSavesProperty.DeleteArrayElementAtIndex(subLayerSaveIndex);

            serializedObject.ApplyModifiedProperties();
        }

        void ChangeSubLayer(KraScriptedImporter importer, int layerSaveIndex, int subLayerSaveIndex, int newLayerInfoIndex)
        {
            serializedObject.Update();

            SerializedProperty subLayerSavesProperty =
                GetSubLayerSavesProperty(layerSaveIndex);

            if (subLayerSaveIndex < 0 || subLayerSaveIndex >= subLayerSavesProperty.arraySize)
                return;

            if (newLayerInfoIndex < 0 || newLayerInfoIndex >= importer.LayersInfo.Count)
                return;

            SerializedProperty subLayerProperty = subLayerSavesProperty.GetArrayElementAtIndex(subLayerSaveIndex);

            SerializedProperty layerIdProperty = subLayerProperty.FindPropertyRelative("LayerId");

            layerIdProperty.stringValue = importer.LayersInfo[newLayerInfoIndex].LayerId;

            serializedObject.ApplyModifiedProperties();
        }

        VisualElement CreatePreviewLayerImage(KraScriptedImporter importer, int layerSaveIndex)
        {
            var image = new Image().WithClass("layer-preview");

            EnsureLayersDatabaseLoaded(importer);

            serializedObject.Update();

            LayerSave layerSave = importer.LayersSave[layerSaveIndex];

            Texture2D previewTexture = importer.BuildLayerSavePreviewTexture(importer, layerSave);

            if (previewTexture == null)
                return image;

            image.image = previewTexture;
            image.scaleMode = ScaleMode.ScaleToFit;

            return image;
        }

        Texture2D GetSubLayerPreviewTexture(KraScriptedImporter importer, string layerId, SubLayerSourceType sourceType)
        {
            if (sourceType == SubLayerSourceType.LayerSave)
            {
                LayerSave layerSave = importer.LayersSave.FirstOrDefault(
                    layer => layer.Id == layerId
                );

                if (layerSave == null)
                    return null;

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

            if (_layerPreviewCache.TryGetValue(layerId, out Texture2D cached))
                return cached;

            EnsureLayersDatabaseLoaded(importer);

            int databaseLayerIndex = importer.LayersDatabase.FindIndex(
                layer => layer.LayerId == layerId
            );

            if (databaseLayerIndex < 0)
                return null;

            LayerDatabase layerDatabase = importer.LayersDatabase[databaseLayerIndex];

            Texture2D preview = KraTextureUtils.CreateTextureFromLayerPixels(
                layerDatabase.Width,
                layerDatabase.Height,
                layerDatabase.LayerPixels
            );

            _layerPreviewCache[layerId] = preview;

            return preview;

        }

        VisualElement CreateSubLayerPreview(KraScriptedImporter importer, string layerId, SubLayerSourceType sourceType)
        {
            var image = new Image().WithClass("preview-sublayer");

            image.image = GetSubLayerPreviewTexture(importer, layerId, sourceType);
            image.scaleMode = ScaleMode.ScaleToFit;

            return image;
        }
    }
}
