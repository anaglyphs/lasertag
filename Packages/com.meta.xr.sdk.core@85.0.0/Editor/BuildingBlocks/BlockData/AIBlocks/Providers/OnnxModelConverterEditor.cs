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
        private enum ModelType
        {
            ObjectDetection,
            Segmentation
        }

        private ModelType _modelType = ModelType.ObjectDetection;

        private ModelAsset _onnxAsset;
        private string _outputFolder = "Assets/MetaXR";

        private float _iouThreshold = 0.5f;
        private float _scoreThreshold = 0.5f;
        private QuantizationType _quantType = QuantizationType.Uint8;
        private bool _addNms = true;

        // Segmentation-specific
        private int _maskChannels = 32;
        private int _maskSize = 160;
        private int _numClasses = 80;

        [MenuItem("Meta/Tools/AI/Unity Inference Engine/ONNX model converter")]
        private static void ShowWindow()
        {
            GetWindow<OnnxModelConverterEditor>("ONNX model converter");
        }

        private void OnGUI()
        {
            GUILayout.Label("ONNX → Sentis Converter", EditorStyles.boldLabel);

            _onnxAsset = (ModelAsset)EditorGUILayout.ObjectField(
                new GUIContent("ONNX Model", "The ONNX model to convert."),
                _onnxAsset,
                typeof(ModelAsset),
                false);

            _outputFolder = EditorGUILayout.TextField(
                new GUIContent("Output Folder", "Project-relative folder for the .sentis file."),
                _outputFolder);

            _modelType = (ModelType)EditorGUILayout.EnumPopup(
                new GUIContent("Model Type", "Choose Object Detection or Segmentation."),
                _modelType);

            _addNms = EditorGUILayout.Toggle(
                new GUIContent("Add NMS Layer",
                    "Bake a Non-Max-Suppression layer into the Sentis graph."),
                _addNms);

            GUILayout.Space(8);
            GUILayout.Label("Thresholds", EditorStyles.boldLabel);
            _iouThreshold = EditorGUILayout.Slider("IoU Threshold", _iouThreshold, 0f, 1f);
            _scoreThreshold = EditorGUILayout.Slider("Score Threshold", _scoreThreshold, 0f, 1f);

            GUILayout.Space(8);
            GUILayout.Label("Quantization", EditorStyles.boldLabel);
            _quantType = (QuantizationType)EditorGUILayout.EnumPopup("Quantization Type", _quantType);

            if (_modelType == ModelType.Segmentation)
            {
                GUILayout.Space(8);
                GUILayout.Label("Segmentation Settings", EditorStyles.boldLabel);
                _maskChannels = EditorGUILayout.IntField(
                    new GUIContent("Mask Channels", "Number of prototype mask channels."),
                    _maskChannels);
                _maskSize = EditorGUILayout.IntField(
                    new GUIContent("Mask Resolution", "Width/Height of prototype mask grid."),
                    _maskSize);
                _numClasses = EditorGUILayout.IntField(
                    new GUIContent("Class Count", "Number of object categories in your dataset."),
                    _numClasses);
            }

            GUILayout.Space(15);
            if (!GUILayout.Button("Convert to Sentis", GUILayout.Height(40)))
            {
                return;
            }

            if (!_onnxAsset)
            {
                EditorUtility.DisplayDialog("Error", "Please assign an ONNX Model Asset.", "OK");
            }
            else
            {
                if (_modelType == ModelType.ObjectDetection)
                {
                    ConvertObjectDetection();
                }
                else
                {
                    ConvertSegmentation();
                }
            }
        }

        private void ConvertObjectDetection()
        {
            var model = ModelLoader.Load(_onnxAsset);
            var graph = new FunctionalGraph();
            var input = graph.AddInputs(model);
            var raw = FF.Forward(model, input)[0]; // shape (1,C,B)

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

            var outBoxes = corners;
            var outIds = ids;
            var outScores = sc;

            if (_addNms)
            {
                var keep = FF.NMS(corners, sc, _iouThreshold, _scoreThreshold);
                outBoxes = corners.IndexSelect(0, keep);
                outIds = ids.IndexSelect(0, keep);
                outScores = sc.IndexSelect(0, keep);
            }

            var finalModel = graph.Compile(outBoxes, outIds, outScores);
            ModelQuantizer.QuantizeWeights(_quantType, ref finalModel);
            SaveModel(finalModel);
        }

        private void ConvertSegmentation()
        {
            var model = ModelLoader.Load(_onnxAsset);
            var graph = new FunctionalGraph();
            var input = graph.AddInputs(model);
            var outs = FF.Forward(model, input);

            var boxOut = outs[0];
            var maskOut = outs[1];

            // Slice according to user-provided class count & mask channels
            var allCoords = boxOut[0, ..4, ..].Transpose(0, 1);
            var allScores = boxOut[0, 4..(4 + _numClasses), ..].Transpose(0, 1);
            var allMasks = boxOut[0, (4 + _numClasses).., ..].Transpose(0, 1);

            var scores = FF.ReduceMax(allScores, 1);
            var ids = FF.ArgMax(allScores, 1);

            var c2C = FF.Constant(new TensorShape(4, 4), new[]
            {
                1, 0, 1, 0,
                0, 1, 0, 1,
                -0.5f, 0, 0.5f, 0,
                0, -0.5f, 0, 0.5f
            });

            var corners = FF.MatMul(allCoords, c2C);
            var selCoords = allCoords;
            var selIds = ids;
            var selMasks = allMasks;

            if (_addNms)
            {
                var keep = FF.NMS(corners, scores, _iouThreshold, _scoreThreshold);
                var idx4 = keep.Unsqueeze(-1).BroadcastTo(new[] { 4 });
                var idxCh = keep.Unsqueeze(-1).BroadcastTo(new[] { _maskChannels });

                selCoords = allCoords.Gather(0, idx4);
                selIds = ids.Gather(0, keep);
                selMasks = allMasks.Gather(0, idxCh);
            }

            var reshaped = maskOut.Reshape(new[] { 1, _maskChannels, _maskSize * _maskSize })[0];
            var maskWeights = FF.MatMul(selMasks, reshaped);
            maskWeights = FF.Sigmoid(maskWeights);
            maskWeights = maskWeights.Reshape(new[] { -1, _maskSize, _maskSize });

            var finalModel = graph.Compile(selCoords, selIds, selMasks, maskWeights);
            ModelQuantizer.QuantizeWeights(_quantType, ref finalModel);
            SaveModel(finalModel);
        }

        private void SaveModel(Model finalModel)
        {
            var fileName = $"{_onnxAsset.name}.sentis";
            var relPath = Path.Combine(_outputFolder, fileName).Replace("\\", "/");
            var projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);

            Directory.CreateDirectory(Path.Combine(projectRoot, _outputFolder));
            ModelWriter.Save(relPath, finalModel);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Success", $"Saved Sentis model to {relPath}", "OK");
        }
    }
}
#endif
