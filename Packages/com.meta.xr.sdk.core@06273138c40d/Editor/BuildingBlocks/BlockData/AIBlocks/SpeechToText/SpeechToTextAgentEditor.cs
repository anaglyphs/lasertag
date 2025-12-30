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

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CustomEditor(typeof(SpeechToTextAgent))]
    public class SpeechToTextAgentEditor : UnityEditor.Editor
    {
        private SerializedProperty _providerProp;
        private SerializedProperty _onTranscriptProp;
        private SerializedProperty _deviceNameProp;
        private SerializedProperty _manualStopOnlyProp;
        private SerializedProperty _lastTranscriptProp;

        private readonly string[] _micProps = { "sampleRate", "channels" };

        private readonly string[] _vadProps =
        {
            "rmsWindowSeconds", "vadStartThresholdDb", "vadStopThresholdDb", "maxSilenceSeconds",
            "minSpeechSeconds", "endPadSeconds", "envelopeAttackSeconds", "envelopeReleaseSeconds",
            "noiseWindowSeconds", "noisePercentile", "quietMarginDb"
        };

        private readonly string[] _limitsProps = { "maxDurationSeconds", "prePadSeconds" };

        private readonly List<string> _micChoices = new();
        private bool _showAdvanced;
        private int _selectedMicIndex;

        private void OnEnable()
        {
            _providerProp = serializedObject.FindProperty("providerAsset");
            _onTranscriptProp = serializedObject.FindProperty("onTranscript");
            _deviceNameProp = serializedObject.FindProperty("deviceName");
            _manualStopOnlyProp = serializedObject.FindProperty("manualStopOnly");
            _lastTranscriptProp = serializedObject.FindProperty("lastTranscript");

            RefreshMics();
            _showAdvanced = SessionState.GetBool("STT_AdvancedFoldout", false);
        }

        private void RefreshMics()
        {
            _micChoices.Clear();
            _micChoices.Add("Default (system)");

            var devices = Microphone.devices ?? System.Array.Empty<string>();
            foreach (var d in devices)
                if (!string.IsNullOrEmpty(d))
                    _micChoices.Add(d);

            if (_deviceNameProp == null) return;

            var current = _deviceNameProp.stringValue ?? string.Empty;
            _selectedMicIndex = 0;
            if (string.IsNullOrEmpty(current)) return;

            for (var i = 1; i < _micChoices.Count; i++)
                if (_micChoices[i] == current)
                {
                    _selectedMicIndex = i;
                    break;
                }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space();

            if (_providerProp != null)
                EditorGUILayout.PropertyField(_providerProp, new GUIContent("Provider Asset"));

            // Mic dropdown
            if (_deviceNameProp != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var newIdx = EditorGUILayout.Popup("Input Device", _selectedMicIndex, _micChoices.ToArray());
                    if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                    {
                        RefreshMics();
                        newIdx = Mathf.Clamp(_selectedMicIndex, 0, _micChoices.Count - 1);
                    }

                    if (newIdx != _selectedMicIndex)
                    {
                        _selectedMicIndex = newIdx;
                        _deviceNameProp.stringValue =
                            (_selectedMicIndex == 0) ? string.Empty : _micChoices[_selectedMicIndex];
                    }
                }
            }

            DrawIfExists(_manualStopOnlyProp, "Manual Stop Only");

            EditorGUILayout.Space();

            _showAdvanced = EditorGUILayout.BeginFoldoutHeaderGroup(_showAdvanced, "Advanced Audio Options");
            SessionState.SetBool("STT_AdvancedFoldout", _showAdvanced);
            if (_showAdvanced)
            {
                EditorGUI.indentLevel++;
                DrawGroup("Microphone", _micProps);
                DrawGroup("Voice Activity Detection", _vadProps);
                DrawGroup("Capture Limits", _limitsProps);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space();

            // Events
            if (_onTranscriptProp != null)
            {
                EditorGUILayout.PropertyField(_onTranscriptProp);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Start Listening")) ((SpeechToTextAgent)target).StartListening();
                    if (GUILayout.Button("Stop Listening")) ((SpeechToTextAgent)target).StopNow();
                    if (GUILayout.Button("Clear Transcript"))
                    {
                        ((SpeechToTextAgent)target).ClearLastTranscript();
                        if (_lastTranscriptProp != null) _lastTranscriptProp.stringValue = "";
                    }
                }
            }

            // Last Transcript (read-only display)
            if (_lastTranscriptProp != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Last Transcript");
                using (new EditorGUI.DisabledScope(true))
                {
                    var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
                    _lastTranscriptProp.stringValue =
                        ((SpeechToTextAgent)target).LastTranscript ?? _lastTranscriptProp.stringValue;
                    EditorGUILayout.TextArea(_lastTranscriptProp.stringValue, style, GUILayout.MinHeight(48));
                }
            }

            EditorGUILayout.Space();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawGroup(string title, string[] props)
        {
            var any = false;
            foreach (var p in props)
            {
                var sp = serializedObject.FindProperty(p);
                if (sp == null) continue;
                any = true;
                break;
            }

            if (!any) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            foreach (var p in props)
            {
                var sp = serializedObject.FindProperty(p);
                if (sp != null) EditorGUILayout.PropertyField(sp);
            }
        }

        private static void DrawIfExists(SerializedProperty prop, string labelOverride = null)
        {
            if (prop == null) return;
            if (string.IsNullOrEmpty(labelOverride)) EditorGUILayout.PropertyField(prop);
            else EditorGUILayout.PropertyField(prop, new GUIContent(labelOverride));
        }
    }
}
