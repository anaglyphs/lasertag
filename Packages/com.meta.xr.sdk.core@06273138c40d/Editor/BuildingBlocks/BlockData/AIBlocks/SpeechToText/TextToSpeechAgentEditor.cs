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
    [CustomEditor(typeof(TextToSpeechAgent))]
    public sealed class TextToSpeechAgentEditor : UnityEditor.Editor
    {
        private SerializedProperty _providerAsset, _audioSource;
        private SerializedProperty _preset, _text;
        private SerializedProperty _onClipReady, _onSpeakStarting, _onSpeakFinished;

        private void OnEnable()
        {
            _providerAsset = serializedObject.FindProperty("providerAsset");
            _audioSource = serializedObject.FindProperty("audioSource");
            _preset = serializedObject.FindProperty("preset");
            _text = serializedObject.FindProperty("text");
            _onClipReady = serializedObject.FindProperty("onClipReady");
            _onSpeakStarting = serializedObject.FindProperty("onSpeakStarting");
            _onSpeakFinished = serializedObject.FindProperty("onSpeakFinished");
        }

        public override void OnInspectorGUI()
        {
            var agent = (TextToSpeechAgent)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(_providerAsset, new GUIContent("Provider Asset"));
            EditorGUILayout.PropertyField(_audioSource, new GUIContent("Audio Source"));

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_preset, new GUIContent("Preset"));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(agent, "Change TTS Preset");
                agent.ApplyPreset((TextToSpeechAgent.TtsPreset)_preset.enumValueIndex);
                _text.stringValue = agent.CurrentText;
            }

            EditorGUILayout.PropertyField(_text, new GUIContent("Text"));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Speak", GUILayout.Height(22))) agent.SpeakText();
                if (GUILayout.Button("Stop", GUILayout.Height(22))) agent.StopSpeaking();
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
            if (_onClipReady != null)
                EditorGUILayout.PropertyField(_onClipReady, new GUIContent("On Clip Ready (AudioClip)"));
            if (_onSpeakStarting != null)
                EditorGUILayout.PropertyField(_onSpeakStarting, new GUIContent("On Speak Starting (string)"));
            if (_onSpeakFinished != null)
                EditorGUILayout.PropertyField(_onSpeakFinished, new GUIContent("On Speak Finished"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
