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

using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CustomEditor(typeof(ImageSegmentationAgent))]
    public class ImageSegmentationAgentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ImageSegmentationAgent.providerAsset)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ImageSegmentationAgent.segmentEveryNFrames)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(ImageSegmentationAgent.captureMaxResolution)));

            serializedObject.ApplyModifiedProperties();

            GUILayout.Space(10);
            if (Application.isPlaying)
            {
                if (GUILayout.Button("Run Segmentation"))
                {
                    ((ImageSegmentationAgent)target).CallInference();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to run segmentation.", MessageType.Info);
            }
        }
    }
}
