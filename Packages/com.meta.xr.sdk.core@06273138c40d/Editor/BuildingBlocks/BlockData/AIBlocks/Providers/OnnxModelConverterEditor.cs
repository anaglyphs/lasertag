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
using Unity.InferenceEngine;
using FF = Unity.InferenceEngine.Functional;
using UnityEditor;
using UnityEngine;
using System.IO;

namespace Meta.XR.Editor.BuildingBlocks.AIBlocks
{
    public class OnnxModelConverterEditor : EditorWindow
    {
        [MenuItem("Meta/Tools/Unity Inference Engine/ONNX Model Converter")]
        public static void ShowWindow() => GetWindow<OnnxModelConverterEditor>("Sentis Converter");

        private ModelAsset _onnxAsset;

        private string _outputFolder = "Assets/MetaXR";
        private float _iouThreshold = 0.5f;
        private float _scoreThreshold = 0.5f;
        private QuantizationType _quantType = QuantizationType.Float16;

        private readonly GUIContent _onnxContent = new(
            "ONNX Model Asset",
            "The ONNX model that you would like to convert."
        );

        private readonly GUIContent _outputContent = new(
            "Output Folder",
            "Destination folder where the converted .sentis file will be saved. The filename is automatically derived from the ONNX asset name."
        );

        private readonly GUIContent _iouContent = new(
            "IoU Threshold",
            "Intersection‑over‑Union threshold used by Non‑Maximum Suppression (NMS).\n" +
            "If two boxes overlap by more than this fraction, the one with lower confidence is discarded.\n" +
            "0 = keep everything, 1 = keep only boxes that do NOT overlap at all. Typical range: 0.3–0.7."
        );

        private readonly GUIContent _scoreContent = new(
            "Score Threshold",
            "Minimum confidence a detection must have to be kept *before* running NMS.\n" +
            "Lower this value to see more (but noisier) boxes; raise it to ignore weak detections entirely. Typical range: 0.2–0.6."
        );

        private readonly GUIContent _quantContent = new(
            "Quantization Type",
            "To reduce the model's storage size on disk and memory, use model quantization.\n" +
            "Quantization represents the weight values in a lower‑precision format. At runtime,\n" +
            "Inference Engine converts these values back to higher precision before processing."
        );

        private void OnGUI()
        {
            GUILayout.Label("Sentis Model Converter", EditorStyles.boldLabel);

            // ONNX asset field with tooltip
            _onnxAsset = (ModelAsset)EditorGUILayout.ObjectField(
                _onnxContent,
                _onnxAsset,
                typeof(ModelAsset),
                false
            );

            // Output folder field
            _outputFolder = EditorGUILayout.TextField(
                _outputContent,
                _outputFolder
            );

            GUILayout.Space(10);
            GUILayout.Label("NMS Thresholds", EditorStyles.boldLabel);

            // IoU slider
            _iouThreshold = EditorGUILayout.Slider(
                _iouContent,
                _iouThreshold,
                0f,
                1f
            );

            // Score slider
            _scoreThreshold = EditorGUILayout.Slider(
                _scoreContent,
                _scoreThreshold,
                0f,
                1f
            );

            GUILayout.Space(10);
            GUILayout.Label("Quantization", EditorStyles.boldLabel);

            // Quantization enum popup
            _quantType = (QuantizationType)EditorGUILayout.EnumPopup(
                _quantContent,
                _quantType
            );

            GUILayout.Space(20);
            if (!GUILayout.Button("Convert and Save Sentis Model", GUILayout.Height(40)))
            {
                return;
            }

            if (!_onnxAsset)
            {
                EditorUtility.DisplayDialog("Error", "Please assign an ONNX Model Asset.", "OK");
            }
            else
            {
                ConvertToSentis(_onnxAsset, _outputFolder, _iouThreshold, _scoreThreshold, _quantType);
            }
        }

        private static void ConvertToSentis(
            ModelAsset onnxAsset,
            string outputFolder,
            float iou,
            float score,
            QuantizationType qType)
        {
            // Derive the filename from the ONNX asset
            string fileName = $"{onnxAsset.name}.sentis";
            string relativeSentisPath = Path.Combine(outputFolder, fileName).Replace("\\", "/");

            // Ensure the destination folder exists on disk
            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            string fullFolderPath = Path.Combine(projectRoot, outputFolder);
            Directory.CreateDirectory(fullFolderPath);

            // Load original ONNX
            var model = ModelLoader.Load(onnxAsset);
            var graph = new FunctionalGraph();
            var input = graph.AddInputs(model);
            var raw = FF.Forward(model, input)[0]; // shape=(1,C,B)

            // Split coords and class-scores
            var box = raw[0, 0..4, ..].Transpose(0, 1);
            var cls = raw[0, 4.., ..].Transpose(0, 1);
            var sc = FF.ReduceMax(cls, 1);
            var argId = FF.ArgMax(cls, 1);

            // Centre -> corner conversion
            var c2C = FF.Constant(new TensorShape(4, 4), new[]
            {
                1f, 0f, 1f, 0f,
                0f, 1f, 0f, 1f,
                -0.5f, 0f, 0.5f, 0f,
                0f, -0.5f, 0f, 0.5f
            });
            var corners = FF.MatMul(box, c2C);

            // Bake in NMS
            var keep = FF.NMS(corners, sc, iou, score);
            var outBox = corners.IndexSelect(0, keep);
            var outId = argId.IndexSelect(0, keep);
            var outSc = sc.IndexSelect(0, keep);

            // Compile with three outputs
            var finalModel = graph.Compile(outBox, outId, outSc);

            // Quantize
            ModelQuantizer.QuantizeWeights(qType, ref finalModel);

            // Save the .sentis model (project-relative path for AssetDatabase)
            ModelWriter.Save(relativeSentisPath, finalModel);

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Success", $"Saved Sentis model to {relativeSentisPath}", "OK");
        }
    }
}
#endif
