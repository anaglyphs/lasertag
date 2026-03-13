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
using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CustomEditor(typeof(UnityInferenceEngineProvider))]
    public class UnityInferenceEngineProviderEditor : UnityEditor.Editor
    {
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

        private SerializedProperty _nmsShaderProp;
        private SerializedProperty _maxDetectionsProp;
        private SerializedProperty _scoreThresholdProp;

        private SerializedProperty _llmConfigProp;

        private string[] _availableClassLabels;
        private bool[] _selectedLabelFlags;
        private int[] _sortedIndices;

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

            _nmsShaderProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.nmsShader));
            _maxDetectionsProp = serializedObject.FindProperty(nameof(UnityInferenceEngineProvider.maxDetections));
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

            if (mode == UnityInferenceProviderMode.ObjectDetection ||
                mode == UnityInferenceProviderMode.ImageSegmentation)
            {
                DrawVisionModelSettings();
            }
            else if (mode == UnityInferenceProviderMode.Chat)
            {
                DrawChatSettings();
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

            EditorGUILayout.PropertyField(_splitOverFramesProp, new GUIContent("Split Over Frames"));
            using (new EditorGUI.DisabledScope(!_splitOverFramesProp.boolValue))
            {
                EditorGUILayout.PropertyField(_layersPerFrameProp, new GUIContent("Layers Per Frame"));
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Input Dimensions", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_inputWidthProp, new GUIContent("Input Width"));
            EditorGUILayout.PropertyField(_inputHeightProp, new GUIContent("Input Height"));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Post Processing (NMS)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_nmsShaderProp, new GUIContent("NMS Compute Shader"));
            EditorGUILayout.PropertyField(_maxDetectionsProp, new GUIContent("Max Detections"));
            EditorGUILayout.PropertyField(_scoreThresholdProp, new GUIContent("Score Threshold"));
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
    }
}
#endif
