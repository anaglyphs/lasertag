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

using System;
using Meta.XR.Samples.Telemetry;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Meta.XR.Samples
{
    [ExecuteAlways]
    internal class SampleMetadata : MonoBehaviour
    {
#if UNITY_EDITOR
        private static bool _scriptReloaded;

        [InitializeOnLoadMethod]
        private static void OnScriptReloaded()
        {
            if (!Application.isPlaying)
            {
                _scriptReloaded = true;
                EditorApplication.update += ScriptReloadedDismiss;
            }
        }

        private static void ScriptReloadedDismiss()
        {
            EditorApplication.update -= ScriptReloadedDismiss;
            _scriptReloaded = false;
        }
#endif

        private float _timestampOpen;

        private void Awake()
        {
            _timestampOpen = Time.realtimeSinceStartup;

#if UNITY_EDITOR
            Meta.XR.Editor.Callbacks.Shutdown.editorShutdown += OnEditorShutdown;
#endif
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            Meta.XR.Editor.Callbacks.Shutdown.editorShutdown -= OnEditorShutdown;
#endif
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                SendRunEvent();
            }
            else
            {
#if UNITY_EDITOR
                if (!_scriptReloaded)
#endif
                {
                    SendOpenEvent();
                }
            }
        }

        public void OnEditorShutdown()
        {
            SendCloseEvent();
        }

        private void SendOpenEvent()
        {
            SendEvent(SampleTelemetryEvents.EventTypes.Open,
                SampleTelemetryEvents.EventTypes.OpenFalcoEventName,
                OVRPlugin.ProductType.Editor);
        }

        private void SendCloseEvent()
        {
            SendEvent(SampleTelemetryEvents.EventTypes.Close,
                SampleTelemetryEvents.EventTypes.CloseFalcoEventName,
                OVRPlugin.ProductType.Editor);
        }

        private void SendRunEvent()
        {
            SendEvent(SampleTelemetryEvents.EventTypes.Run,
                SampleTelemetryEvents.EventTypes.RunFalcoEventName,
                OVRPlugin.ProductType.Editor);
        }

        private void SendEvent(int eventType, string falcoEventName, OVRPlugin.ProductType productType)
        {
            var timeSpent = Time.realtimeSinceStartup - _timestampOpen;
            var unifiedEvent = new OVRPlugin.UnifiedEventData(falcoEventName)
            {
                isEssential = OVRPlugin.Bool.False,
                productType = productType
            };
            unifiedEvent.SetMetadata(SampleTelemetryEvents.AnnotationTypes.Sample, gameObject.scene.name);
#if UNITY_EDITOR
            unifiedEvent.SetMetadata(SampleTelemetryEvents.AnnotationTypes.BuildTarget, EditorUserBuildSettings.selectedBuildTargetGroup.ToString());
#endif
            unifiedEvent.SetMetadata(SampleTelemetryEvents.AnnotationTypes.RuntimePlatform, Application.platform.ToString());
            unifiedEvent.SetMetadata(SampleTelemetryEvents.AnnotationTypes.InEditor, Application.isEditor.ToString());
            unifiedEvent.SetMetadata(SampleTelemetryEvents.AnnotationTypes.TimeSinceEditorStart, Time.realtimeSinceStartup.ToString("F0"));
            unifiedEvent.SetMetadata(SampleTelemetryEvents.AnnotationTypes.TimeSpent, timeSpent.ToString("F0"));
            unifiedEvent.Send();
        }
    }
}
