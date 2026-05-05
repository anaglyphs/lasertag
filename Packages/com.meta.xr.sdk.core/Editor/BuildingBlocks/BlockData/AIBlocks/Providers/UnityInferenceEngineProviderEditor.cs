/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#if UNITY_INFERENCE_INSTALLED
using System.IO;
using Unity.InferenceEngine;
using FF = Unity.InferenceEngine.Functional;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CustomEditor(typeof(UnityInferenceEngineProvider))]
    public class UnityInferenceEngineProviderEditor : UnityEditor.Editor
    {
        private enum ModelQuantizationType
        {
            None,
            Float16,
            Uint8
        }

        private SerializedProperty _modeProp;
        private SerializedProperty _useStreamingAssetProp;
        private SerializedProperty _streamingAssetModelProp;
        private SerializedProperty _streamingAssetFileNameProp;
        private SerializedProperty _modelFileProp;

        private SerializedProperty _backendProp;
        private SerializedProperty _classLabelsAssetProp;
        private SerializedProperty _selectedClassLabelIndicesProp;
        private SerializedProperty _splitOverFramesProp;
        private SerializedProperty _layersPerFrameProp;

        private SerializedProperty _inputWidthProp;
        private SerializedProperty _inputHeightProp;

        private SerializedProperty _scoreThresholdProp;

        private SerializedProperty _llmConfigProp;

        private string[] _availableClassLabels;
        private bool[] _selectedLabelFlags;
        private int[] _sortedIndices;

        private bool _showQuantization;
        private ModelQuantizationType _quantizationType = ModelQuantizationType.Uint8;
        private ModelAsset _modelToQuantize;
        private bool _convertOutputs = true;

        private void OnEnable()
        {
            _modeProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.mode));
            _useStreamingAssetProp =
                serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.useStreamingAsset));
            _streamingAssetModelProp =
                serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.streamingAssetModel));
            _streamingAssetFileNameProp =
                serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.streamingAssetFileName));
            _modelFileProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.modelFile));

            _backendProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.backend));
            _classLabelsAssetProp =
                serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.classLabelsAsset));
            _selectedClassLabelIndicesProp =
                serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.selectedClassLabelIndices));
            _splitOverFramesProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.splitOverFrames));
            _layersPerFrameProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.layersPerFrame));

            _inputWidthProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.inputWidth));
            _inputHeightProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.inputHeight));

            _scoreThresholdProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.scoreThreshold));
            _llmConfigProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.llmConfig));

            LoadClassLabels();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Inference Mode", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_modeProp, new GUIContent("Mode"));

            var mode = (UnityInferenceProviderMode)_modeProp.enumValueIndex;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Model Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_useStreamingAssetProp, new GUIContent("Use StreamingAssets (.sentis)"));

            if (_useStreamingAssetProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_streamingAssetModelProp, new GUIContent("Streaming Asset (.sentis)"));
                var selectionChanged = EditorGUI.EndChangeCheck();
                if (selectionChanged)
                {
                    var selectedSentis = _streamingAssetModelProp.objectReferenceValue;
                    if (selectedSentis != null)
                    {
                        var path = AssetDatabase.GetAssetPath(selectedSentis).Replace('\\', '/');
                        var file = Path.GetFileName(path);
                        _streamingAssetFileNameProp.stringValue = file ?? string.Empty;
                    }
                    else
                    {
                        _streamingAssetFileNameProp.stringValue = string.Empty;
                    }
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Derived File Name", _streamingAssetFileNameProp.stringValue);
                }

                var selectedSentisNow = _streamingAssetModelProp.objectReferenceValue;
                if (selectedSentisNow != null)
                {
                    var path = AssetDatabase.GetAssetPath(selectedSentisNow).Replace('\\', '/');
                    if (!path.StartsWith("Assets/StreamingAssets/"))
                    {
                        EditorGUILayout.HelpBox(
                            "Place the .sentis file under Assets/StreamingAssets so it loads at runtime.",
                            MessageType.Warning);
                    }

                    if (!Path.GetExtension(path).Equals(".sentis", System.StringComparison.OrdinalIgnoreCase))
                    {
                        EditorGUILayout.HelpBox("Expected a .sentis file.", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Assign a .sentis file stored in Assets/StreamingAssets.",
                        MessageType.Info);
                }

                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_modelFileProp, new GUIContent("Model Asset"));
                EditorGUI.indentLevel--;
            }

            switch (mode)
            {
                case UnityInferenceProviderMode.ObjectDetection or UnityInferenceProviderMode.ImageSegmentation:
                    DrawVisionModelSettings();
                    break;
                case UnityInferenceProviderMode.Chat:
                    DrawChatSettings();
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawVisionModelSettings()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Runtime Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_backendProp, new GUIContent("Backend"));

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_classLabelsAssetProp, new GUIContent("Class Labels"));
            if (EditorGUI.EndChangeCheck())
            {
                LoadClassLabels();
            }

            DrawClassLabelFilter();

            EditorGUILayout.PropertyField(_scoreThresholdProp, new GUIContent("Score Threshold"));

            EditorGUILayout.PropertyField(_splitOverFramesProp, new GUIContent("Split Over Frames"));
            using (new EditorGUI.DisabledScope(!_splitOverFramesProp.boolValue))
            {
                EditorGUILayout.PropertyField(_layersPerFrameProp, new GUIContent("Layers Per Frame"));
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Input Dimensions", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_inputWidthProp, new GUIContent("Input Width"));
            EditorGUILayout.PropertyField(_inputHeightProp, new GUIContent("Input Height"));

            DrawQuantizationSection();
        }

        private void DrawChatSettings()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.PropertyField(_llmConfigProp, new GUIContent("LLM Config"), true);

            if (_llmConfigProp.isExpanded)
            {
                EditorGUILayout.HelpBox(
                    "LLM Config contains tokenizer files (vocab.json, merges.txt, tokenizer_config.json), " +
                    "chat template format, model parameters (layers, attention heads, etc.), and backend settings.\n\n" +
                    "Supported models: SmolLM, Qwen, Phi and similar transformer-based text-only LLMs.",
                    MessageType.Info);
            }
        }

        private void LoadClassLabels()
        {
            if (_classLabelsAssetProp == null)
                return;

            var classLabelsAsset = _classLabelsAssetProp.objectReferenceValue as TextAsset;
            if (!classLabelsAsset)
            {
                _availableClassLabels = null;
                _selectedLabelFlags = null;
                _sortedIndices = null;
                return;
            }

            _availableClassLabels =
                classLabelsAsset.text.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            _selectedLabelFlags = new bool[_availableClassLabels.Length];

            _sortedIndices = new int[_availableClassLabels.Length];
            for (var i = 0; i < _sortedIndices.Length; i++)
            {
                _sortedIndices[i] = i;
            }

            System.Array.Sort(_sortedIndices, (a, b) => string.Compare(_availableClassLabels[a],
                _availableClassLabels[b],
                System.StringComparison.OrdinalIgnoreCase));

            var hasExistingSelection = _selectedClassLabelIndicesProp.arraySize > 0;

            if (hasExistingSelection)
            {
                for (var i = 0; i < _selectedClassLabelIndicesProp.arraySize; i++)
                {
                    var index = _selectedClassLabelIndicesProp.GetArrayElementAtIndex(i).intValue;
                    if (index >= 0 && index < _selectedLabelFlags.Length)
                    {
                        _selectedLabelFlags[index] = true;
                    }
                }
            }
            else
            {
                for (var i = 0; i < _selectedLabelFlags.Length; i++)
                {
                    _selectedLabelFlags[i] = true;
                }

                ApplyLabelFilter();
            }
        }

        private void DrawClassLabelFilter()
        {
            if (_availableClassLabels == null || _availableClassLabels.Length == 0)
            {
                return;
            }

            var selectedCount = 0;
            var selectedLabels = new System.Collections.Generic.List<string>();
            for (var i = 0; i < _selectedLabelFlags.Length; i++)
            {
                if (!_selectedLabelFlags[i])
                {
                    continue;
                }

                selectedCount++;
                if (selectedLabels.Count < 3)
                {
                    selectedLabels.Add(_availableClassLabels[i]);
                }
            }

            string buttonText;
            if (selectedCount == 0)
            {
                buttonText = "No Classes Selected";
            }
            else if (selectedCount == _availableClassLabels.Length)
            {
                buttonText = "All Classes";
            }
            else if (selectedCount <= 3)
            {
                buttonText = string.Join(", ", selectedLabels);
            }
            else
            {
                buttonText = "Mixed...";
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Filter Class Labels",
                "Select specific class labels to process. None selected = all classes."));

            if (EditorGUILayout.DropdownButton(new GUIContent(buttonText), FocusType.Keyboard))
            {
                ShowClassLabelDropdown();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ShowClassLabelDropdown()
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Select All"), false, () =>
            {
                for (var i = 0; i < _selectedLabelFlags.Length; i++)
                {
                    _selectedLabelFlags[i] = true;
                }

                ApplyLabelFilter();
            });

            menu.AddItem(new GUIContent("Deselect All"), false, () =>
            {
                for (var i = 0; i < _selectedLabelFlags.Length; i++)
                {
                    _selectedLabelFlags[i] = false;
                }

                ApplyLabelFilter();
            });

            menu.AddSeparator("");

            foreach (var originalIndex in _sortedIndices)
            {
                var itemName = $"[{originalIndex}] {_availableClassLabels[originalIndex]}";
                var index = originalIndex;
                menu.AddItem(new GUIContent(itemName), _selectedLabelFlags[originalIndex], () =>
                {
                    _selectedLabelFlags[index] = !_selectedLabelFlags[index];
                    ApplyLabelFilter();
                });
            }

            menu.ShowAsContext();
        }

        private void ApplyLabelFilter()
        {
            _selectedClassLabelIndicesProp.ClearArray();

            for (var i = 0; i < _selectedLabelFlags.Length; i++)
            {
                if (!_selectedLabelFlags[i])
                {
                    continue;
                }

                _selectedClassLabelIndicesProp.InsertArrayElementAtIndex(_selectedClassLabelIndicesProp.arraySize);
                _selectedClassLabelIndicesProp.GetArrayElementAtIndex(_selectedClassLabelIndicesProp.arraySize - 1)
                    .intValue = i;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawQuantizationSection()
        {
            EditorGUILayout.Space(12);
            _showQuantization = EditorGUILayout.Foldout(_showQuantization, "YOLO-style Model Optimization", true, EditorStyles.foldoutHeader);

            if (!_showQuantization)
            {
                return;
            }

            if (!_modelToQuantize && !_useStreamingAssetProp.boolValue)
            {
                var assignedModel = _modelFileProp.objectReferenceValue as ModelAsset;
                if (assignedModel)
                {
                    _modelToQuantize = assignedModel;
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField(
                "Optimize your YOLO-style ONNX model for use with AI Blocks. " +
                "Convert outputs transforms raw YOLO format (1 output) to the optimized 3-output format (boxes, " +
                "class IDs, scores) and YOLO-seg to the optimized 4-output format (boxes, class IDs, scores, mask).",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(10);

            _modelToQuantize = (ModelAsset)EditorGUILayout.ObjectField(
                new GUIContent("Model", "The ONNX or Sentis model to optimize."),
                _modelToQuantize,
                typeof(ModelAsset),
                false);

            EditorGUILayout.Space(8);

            _quantizationType = (ModelQuantizationType)EditorGUILayout.EnumPopup(
                new GUIContent("Quantization",
                    "Reduce model size. None = no change, Float16 = 16-bit, Uint8 = 8-bit (smallest)."),
                _quantizationType);

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(
                "• None: Keep original precision\n" +
                "• Float16: 16-bit, ~50% smaller, good accuracy\n" +
                "• Uint8: 8-bit, ~75% smaller, may impact accuracy",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(4);

            _convertOutputs = EditorGUILayout.Toggle(
                new GUIContent("Convert Outputs",
                    "Convert raw YOLO output (single tensor) to 3 separate outputs (boxes, class IDs, scores), " +
                    "and YOLO-Seg output to 4 separate ouputs (boxes, class IDs, scores, mask)."),
                _convertOutputs);

            EditorGUILayout.Space(10);

            var canOptimize = _modelToQuantize && (_convertOutputs || _quantizationType != ModelQuantizationType.None);
            EditorGUI.BeginDisabledGroup(!canOptimize);
            if (GUILayout.Button("Optimize and Save Model", GUILayout.Height(28)))
            {
                OptimizeModel();
            }
            EditorGUI.EndDisabledGroup();

            if (_modelToQuantize && !_convertOutputs && _quantizationType == ModelQuantizationType.None)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("Enable 'Convert Outputs' or select a quantization type.", MessageType.Info);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void OptimizeModel()
        {
            if (!_modelToQuantize)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a model to optimize.", "OK");
                return;
            }

            try
            {
                Model finalModel;
                var mode = (UnityInferenceProviderMode)_modeProp.enumValueIndex;

                if (_convertOutputs)
                {
                    var model = ModelLoader.Load(_modelToQuantize);
                    var graph = new FunctionalGraph();
                    var input = graph.AddInputs(model);

                    if (mode == UnityInferenceProviderMode.ImageSegmentation)
                    {
                        // Segmentation: 2 inputs → 4 outputs (boxes, scores, maskCoeffs, maskPrototypes)
                        var outs = FF.Forward(model, input);

                        if (outs.Length < 2)
                        {
                            EditorUtility.DisplayDialog("Error",
                                $"Segmentation model needs 2 outputs, got {outs.Length}.", "OK");
                            return;
                        }

                        var boxOut = outs[0];
                        var maskOut = outs[1];

                        // Assume 80 classes (COCO), mask coefficients are the rest after 4 box coords + classes
                        const int numClasses = 80;

                        var allData = boxOut[0].Transpose(0, 1); // (numBoxes, 4+numClasses+maskChannels)
                        var boxes = allData[.., ..4];
                        var scores = allData[.., 4..(4 + numClasses)];
                        var maskCoeffs = allData[.., (4 + numClasses)..];
                        var maskPrototypes = maskOut[0];

                        // Convert center format to corner format
                        var c2C = FF.Constant(new TensorShape(4, 4), new[]
                        {
                            1, 0, 1, 0,
                            0, 1, 0, 1,
                            -0.5f, 0, 0.5f, 0,
                            0, -0.5f, 0, 0.5f
                        });
                        var boxesCorner = FF.MatMul(boxes, c2C);

                        finalModel = graph.Compile(boxesCorner, scores, maskCoeffs, maskPrototypes);
                    }
                    else
                    {
                        // Object Detection: 1 input → 3 outputs (boxes, ids, scores)
                        var raw = FF.Forward(model, input)[0]; // shape (1, 4+numClasses, numBoxes)

                        var boxes = raw[0, 0..4, ..].Transpose(0, 1);
                        var cls = raw[0, 4.., ..].Transpose(0, 1);
                        var sc = FF.ReduceMax(cls, 1);
                        var ids = FF.ArgMax(cls, 1);

                        var c2C = FF.Constant(new TensorShape(4, 4), new[]
                        {
                            1, 0, 1, 0,
                            0, 1, 0, 1,
                            -0.5f, 0, 0.5f, 0,
                            0, -0.5f, 0, 0.5f
                        });
                        var corners = FF.MatMul(boxes, c2C);

                        finalModel = graph.Compile(corners, ids, sc);
                    }
                }
                else
                {
                    finalModel = ModelLoader.Load(_modelToQuantize);
                }

                // Apply quantization if selected
                if (_quantizationType != ModelQuantizationType.None)
                {
                    var quantType = _quantizationType == ModelQuantizationType.Float16
                        ? QuantizationType.Float16
                        : QuantizationType.Uint8;

                    ModelQuantizer.QuantizeWeights(quantType, ref finalModel);
                }

                // Generate output filename
                var originalPath = AssetDatabase.GetAssetPath(_modelToQuantize);
                var directory = Path.GetDirectoryName(originalPath);
                var originalName = Path.GetFileNameWithoutExtension(originalPath);

                var suffix = "";
                if (_convertOutputs)
                {
                    suffix += "_converted";
                }
                if (_quantizationType == ModelQuantizationType.Float16)
                {
                    suffix += "_fp16";
                }
                else if (_quantizationType == ModelQuantizationType.Uint8)
                {
                    suffix += "_uint8";
                }

                var newFileName = $"{originalName}{suffix}.sentis";
                var newPath = Path.Combine(directory, newFileName).Replace("\\", "/");

                ModelWriter.Save(newPath, finalModel);
                AssetDatabase.Refresh();

                var modeStr = mode == UnityInferenceProviderMode.ImageSegmentation
                    ? "segmentation (4 outputs)"
                    : "detection (3 outputs)";

                var message = $"Optimized model saved to:\n{newPath}";
                if (_convertOutputs)
                {
                    message += $"\n\nConverted for {modeStr}";
                }
                EditorUtility.DisplayDialog("Success", message, "OK");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UnityInferenceEngineProvider] Failed to optimize model: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to optimize model:\n{e.Message}", "OK");
            }
        }
    }
}
#endif
