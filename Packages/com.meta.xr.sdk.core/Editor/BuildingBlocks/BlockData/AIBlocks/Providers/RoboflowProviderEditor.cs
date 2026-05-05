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
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// Build processor that validates HTTP settings when using local inference with Roboflow.
    /// </summary>
    public class RoboflowBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
            {
                return;
            }

            var guids = AssetDatabase.FindAssets("t:RoboflowProvider");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var provider = AssetDatabase.LoadAssetAtPath<RoboflowProvider>(path);
                if (provider == null) continue;

                if (provider.inferenceMode == RoboflowProvider.InferenceMode.LocalServer)
                {
                    ValidateLocalServerSettings(provider, path);
                }
            }
        }

        private static void ValidateLocalServerSettings(RoboflowProvider provider, string assetPath)
        {
            var endpoint = provider.localServerEndpoint ?? "";

            // Check for localhost usage
            if (endpoint.Contains("localhost") || endpoint.Contains("127.0.0.1"))
            {
                Debug.LogWarning($"[RoboflowProvider] Asset '{assetPath}' uses 'localhost' for local inference. " +
                    "This will NOT connect to Quest/Android devices. Please update 'localServerEndpoint' to your PC's " +
                    "IP address (e.g., 'http://192.168.1.100:9001').");
            }

            // Check for HTTP (non-secure) endpoint
            if (endpoint.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase))
            {
                if (PlayerSettings.insecureHttpOption == InsecureHttpOption.NotAllowed)
                {
                    throw new BuildFailedException(
                        $"[RoboflowProvider] Asset '{assetPath}' uses HTTP (non-secure) for local inference, " +
                        "but 'Allow downloads over HTTP' is set to 'Not Allowed' in Player Settings.\n\n" +
                        "To fix this:\n" +
                        "1. Go to Edit → Project Settings → Player → Android → Other Settings\n" +
                        "2. Set 'Allow downloads over HTTP' to 'Always allowed'\n\n" +
                        "Alternatively, configure your Roboflow inference server with HTTPS.");
                }

                if (PlayerSettings.insecureHttpOption == InsecureHttpOption.DevelopmentOnly &&
                    !EditorUserBuildSettings.development)
                {
                    throw new BuildFailedException(
                        $"[RoboflowProvider] Asset '{assetPath}' uses HTTP (non-secure) for local inference. " +
                        "'Allow downloads over HTTP' is set to 'Development builds only', but this is NOT a development build.\n\n" +
                        "To fix this, either:\n" +
                        "1. Enable 'Development Build' in Build Settings, OR\n" +
                        "2. Set 'Allow downloads over HTTP' to 'Always allowed' in Player Settings");
                }
            }
        }
    }

    [CustomEditor(typeof(RoboflowProvider))]
    public class RoboflowProviderEditor : AIProviderEditorBase
    {
        private SerializedProperty _modelId;
        private SerializedProperty _inferenceMode;
        private SerializedProperty _localServerEndpoint;
        private SerializedProperty _cloudEndpoint;
        private SerializedProperty _confidenceThreshold;
        private SerializedProperty _maxDetections;
        private SerializedProperty _segmentationMaskWidth;
        private SerializedProperty _segmentationMaskHeight;

        private bool _showDetectionSettings;
        private bool _showSegmentationSettings;
        private bool _showLocalServerSetup;
        private bool _showOptionInstaller;
        private bool _showOptionDocker;
        private bool _showOptionCli;
        private bool _showOptionModels;
        private bool _showOptionVrNetwork;

        private void OnEnable()
        {
            InitializeCredentialStorage(nameof(RoboflowProvider.apiKey));

            _modelId = serializedObject.FindProperty(nameof(RoboflowProvider.modelId));
            _inferenceMode = serializedObject.FindProperty(nameof(RoboflowProvider.inferenceMode));
            _localServerEndpoint = serializedObject.FindProperty(nameof(RoboflowProvider.localServerEndpoint));
            _cloudEndpoint = serializedObject.FindProperty(nameof(RoboflowProvider.cloudEndpoint));
            _confidenceThreshold = serializedObject.FindProperty(nameof(RoboflowProvider.confidenceThreshold));
            _maxDetections = serializedObject.FindProperty(nameof(RoboflowProvider.maxDetections));
            _segmentationMaskWidth = serializedObject.FindProperty(nameof(RoboflowProvider.segmentationMaskWidth));
            _segmentationMaskHeight = serializedObject.FindProperty(nameof(RoboflowProvider.segmentationMaskHeight));
        }

        private void OnDisable()
        {
            CleanupValidationRequest();
        }

        protected override void OnTestConnection()
        {
            var provider = target as RoboflowProvider;
            if (provider is IUsesCredential credentialProvider)
            {
                var config = credentialProvider.GetTestConfig();
                TestConnection(config.Endpoint, config.Model, config.ProviderId);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Roboflow Provider", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            var provider = target as RoboflowProvider;
            if (provider is IUsesCredential credProvider)
            {
                var config = credProvider.GetTestConfig();
                TryLoadCachedValidation(config.Endpoint, config.Model, config.ProviderId);
            }

            DrawApiKeyField("API Key", "https://app.roboflow.com/settings/api",
                drawExtraTopRight: () => DrawTestConnectionButton());
            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(_modelId, new GUIContent("Model ID",
                "Roboflow model ID (e.g., 'my-model/1' or 'workspace/project/version')."));

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_inferenceMode, new GUIContent("Inference Mode",
                "Cloud: Uses Roboflow serverless API.\nLocalServer: Connects to a self-hosted Roboflow inference server."));

            var isCloud = _inferenceMode.enumValueIndex == (int)RoboflowProvider.InferenceMode.Cloud;

            EditorGUILayout.Space();
            if (isCloud)
            {
                EditorGUILayout.PropertyField(_cloudEndpoint, new GUIContent("Cloud Endpoint",
                    "Roboflow serverless API endpoint (default: https://serverless.roboflow.com)."));
            }
            else
            {
                EditorGUILayout.PropertyField(_localServerEndpoint, new GUIContent("Local Server Endpoint",
                    "URL of your self-hosted Roboflow inference server (e.g., http://192.168.1.100:9001)."));

                EditorGUILayout.Space(4);
                DrawHttpSecurityWarnings();

                EditorGUILayout.Space(4);
                DrawLocalServerSetupGuide();
            }

            EditorGUILayout.Space(6);

            _showDetectionSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showDetectionSettings, "Detection Settings");
            if (_showDetectionSettings)
            {
                Indent(() =>
                {
                    EditorGUILayout.PropertyField(_confidenceThreshold, new GUIContent("Confidence Threshold",
                        "Minimum confidence score for detections (0-1)."));
                    EditorGUILayout.PropertyField(_maxDetections, new GUIContent("Max Detections",
                        "Maximum number of detections to return per frame."));
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space();

            _showSegmentationSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showSegmentationSettings, "Segmentation Settings");
            if (_showSegmentationSettings)
            {
                Indent(() =>
                {
                    EditorGUILayout.PropertyField(_segmentationMaskWidth, new GUIContent("Mask Width",
                        "Width of the segmentation mask output in pixels. Higher values provide more detail but use more memory."));
                    EditorGUILayout.PropertyField(_segmentationMaskHeight, new GUIContent("Mask Height",
                        "Height of the segmentation mask output in pixels. Higher values provide more detail but use more memory."));
                    EditorGUILayout.HelpBox(
                        "Mask dimensions control the resolution of segmentation output. " +
                        "Lower values (e.g., 80x80) improve performance, higher values (e.g., 320x320) provide finer detail.",
                        MessageType.Info);
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHttpSecurityWarnings()
        {
            var endpoint = _localServerEndpoint.stringValue ?? "";

            // Warning for HTTP security settings
            if (endpoint.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase))
            {
                if (PlayerSettings.insecureHttpOption == InsecureHttpOption.NotAllowed)
                {
                    EditorGUILayout.HelpBox(
                        "Error: HTTP connections are disabled in Player Settings.\n\n" +
                        "Go to Edit → Project Settings → Player → Android → Other Settings\n" +
                        "Set 'Allow downloads over HTTP' to 'Always allowed'.",
                        MessageType.Error);

                    if (GUILayout.Button("Fix: Enable HTTP Downloads"))
                    {
                        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
                        Debug.Log("[RoboflowProvider] Enabled 'Allow downloads over HTTP' in Player Settings.");
                    }
                }
                else if (PlayerSettings.insecureHttpOption == InsecureHttpOption.DevelopmentOnly)
                {
                    EditorGUILayout.HelpBox(
                        "Note: HTTP is only allowed in Development Builds.\n" +
                        "Ensure 'Development Build' is checked in Build Settings, or set " +
                        "'Allow downloads over HTTP' to 'Always allowed'.",
                        MessageType.Info);
                }
            }
        }

        private void DrawLocalServerSetupGuide()
        {
            _showLocalServerSetup = EditorGUILayout.BeginFoldoutHeaderGroup(_showLocalServerSetup, "Local Server Setup Guide");
            if (_showLocalServerSetup)
            {
                EditorGUI.indentLevel++;

                // Option 1 - Native installer
                _showOptionInstaller = EditorGUILayout.Foldout(_showOptionInstaller, "Option 1: Roboflow Inference App", true);
                if (_showOptionInstaller)
                {
                    DrawOptionWithButton(
                        "Download and run the Roboflow Inference app.\nStarts server at http://localhost:9001",
                        "Download",
                        "https://inference.roboflow.com/install/");
                }

                // Option 2 - Docker
                _showOptionDocker = EditorGUILayout.Foldout(_showOptionDocker, "Option 2: Docker", true);
                if (_showOptionDocker)
                {
                    DrawInfoBox(() =>
                    {
                        EditorGUILayout.LabelField("CPU (Recommended/Faster):", EditorStyles.miniLabel);
                        EditorGUILayout.SelectableLabel(
                            "docker run -d -p 9001:9001 roboflow/roboflow-inference-server-cpu:latest",
                            EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                        EditorGUILayout.LabelField("GPU (Can cause semaphore.WaitForSignal):", EditorStyles.miniLabel);
                        EditorGUILayout.SelectableLabel(
                            "docker run -d --gpus all -p 9001:9001 roboflow/roboflow-inference-server-gpu:latest",
                            EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    });
                }

                // Option 3 - CLI
                _showOptionCli = EditorGUILayout.Foldout(_showOptionCli, "Option 3: Command Line", true);
                if (_showOptionCli)
                {
                    DrawInfoBox(() =>
                    {
                        EditorGUILayout.SelectableLabel(
                            "pip install inference-cli && inference server start",
                            EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    });
                }

                // Models
                _showOptionModels = EditorGUILayout.Foldout(_showOptionModels, "Browse Models", true);
                if (_showOptionModels)
                {
                    DrawOptionWithButton(
                        "Models download automatically on first use.\nBrowse thousands on Roboflow Universe.",
                        "Browse",
                        "https://universe.roboflow.com/");
                }

                // VR/Network setup
                _showOptionVrNetwork = EditorGUILayout.Foldout(_showOptionVrNetwork, "How to connect to Meta Quest", true);

                var endpoint = _localServerEndpoint.stringValue ?? "";
                if (endpoint.Contains("localhost") || endpoint.Contains("127.0.0.1"))
                {
                    EditorGUILayout.HelpBox(
                        "'localhost' / '127.0.0.1' will NOT connect to Quest unless using ADB port forwarding.\n" +
                        "Either use USB + adb reverse, or replace with your PC's IP address.",
                        MessageType.Warning);
                }

                if (_showOptionVrNetwork)
                {
                    DrawInfoBox(() =>
                    {
                        EditorGUILayout.LabelField("Option 1: USB Cable (Recommended)", EditorStyles.miniBoldLabel);
                        EditorGUILayout.LabelField("• Connect Quest via USB", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField("• Run: adb reverse tcp:9001 tcp:9001", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField("• Set endpoint: http://127.0.0.1:9001", EditorStyles.miniLabel);
                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("Option 2: Wi-Fi (Same Network)", EditorStyles.miniBoldLabel);
                        EditorGUILayout.LabelField("• Find PC IP: run 'ipconfig' (avoid 169.254.x.x)", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField("• Set endpoint: http://<pc-ip>:9001", EditorStyles.miniLabel);
                        EditorGUILayout.LabelField("• Open port 9001 in Windows Firewall", EditorStyles.miniLabel);
                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("Required: Edit → Project Settings → Player → Android:", EditorStyles.miniBoldLabel);
                        EditorGUILayout.LabelField("• Set 'Allow downloads over HTTP' to 'Always allowed'", EditorStyles.miniLabel);
                    });
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawInfoBox(System.Action drawContent)
        {
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            drawContent();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel = indent;
        }

        private static void DrawOptionWithButton(string helpText, string buttonLabel, string url)
        {
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var helpBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = EditorStyles.miniLabel.fontSize,
                wordWrap = true,
                padding = new RectOffset(8, 8, 6, 6)
            };

            var content = new GUIContent(helpText);
            var availableWidth = EditorGUIUtility.currentViewWidth - 120 - (indent * 15);
            var height = helpBoxStyle.CalcHeight(content, availableWidth);
            height = Mathf.Max(height, 38);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);
            EditorGUILayout.LabelField(content, helpBoxStyle, GUILayout.Height(height));
            if (GUILayout.Button(buttonLabel, GUILayout.Width(80), GUILayout.Height(height)))
            {
                Application.OpenURL(url);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel = indent;
        }
    }
}
