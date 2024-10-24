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
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Meta.XR.ImmersiveDebugger
{
    [Serializable]
    public class DebugData
    {
        [SerializeField]
        public string AssemblyName;
        [SerializeField]
        public List<string> DebugTypes;
        public DebugData(string assemblyName, List<string> types)
        {
            AssemblyName = assemblyName;
            DebugTypes = types;
        }
    }

    /// <summary>
    /// Runtime settings and Cache for Immersive Debugger to understand which assemblies and types are interested to populate for debugging
    /// </summary>
    public class RuntimeSettings : OVRRuntimeAssetsBase, ISerializationCallbackReceiver
    {
        public enum DistanceOption
        {
            Close, Default, Far
        }

        internal static string InstanceAssetName = "ImmersiveDebuggerSettings";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init() // reset static fields in case of domain reload disabled
        {
            _instance = null;
        }
        private static RuntimeSettings _instance;
        public static RuntimeSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    LoadAsset(out RuntimeSettings debugTypes, InstanceAssetName);
                    _instance = debugTypes;
                }

                return _instance;
            }
        }

        [SerializeField]
        private List<DebugData> debugTypes;
        // the actual underlying data structure to perform update etc.
        internal Dictionary<string, List<string>> debugTypesDict;

        [SerializeField] private bool immersiveDebuggerEnabled = false;
        public static event Action OnImmersiveDebuggerEnabledChanged;
        public bool ImmersiveDebuggerEnabled
        {
            get => immersiveDebuggerEnabled;
            set
            {
                if (immersiveDebuggerEnabled != value)
                {
                    immersiveDebuggerEnabled = value;
                    OnImmersiveDebuggerEnabledChanged?.Invoke();
                }
            }
        }

        [SerializeField] private bool immersiveDebuggerDisplayAtStartup = false;
        public bool ImmersiveDebuggerDisplayAtStartup
        {
            get => immersiveDebuggerDisplayAtStartup;
            set => immersiveDebuggerDisplayAtStartup = value;
        }

        [SerializeField] private OVRInput.Button immersiveDebuggerToggleDisplayButton = OVRInput.Button.Two;
        public OVRInput.Button ImmersiveDebuggerToggleDisplayButton
        {
            get => immersiveDebuggerToggleDisplayButton;
            set => immersiveDebuggerToggleDisplayButton = value;
        }

        [SerializeField] private bool showInspectors = false;
        public bool ShowInspectors
        {
            get => showInspectors;
            set => showInspectors = value;
        }

        [SerializeField] private bool showConsole = false;
        public bool ShowConsole
        {
            get => showConsole;
            set => showConsole = value;
        }

        [SerializeField] private bool followOverride = true;
        public bool FollowOverride
        {
            get => followOverride;
            set => followOverride = value;
        }

        [SerializeField] private bool rotateOverride = false;
        public bool RotateOverride
        {
            get => rotateOverride;
            set => rotateOverride = value;
        }

        [SerializeField] private bool showInfoLog = false;
        public bool ShowInfoLog
        {
            get => showInfoLog;
            set => showInfoLog = value;
        }

        [SerializeField] private bool showWarningLog = true;
        public bool ShowWarningLog
        {
            get => showWarningLog;
            set => showWarningLog = value;
        }

        [SerializeField] private bool showErrorLog = true;
        public bool ShowErrorLog
        {
            get => showErrorLog;
            set => showErrorLog = value;
        }

        [SerializeField] private bool collapsedIdenticalLogEntries = false;
        public bool CollapsedIdenticalLogEntries
        {
            get => collapsedIdenticalLogEntries;
            set => collapsedIdenticalLogEntries = value;
        }

        [SerializeField] private int maximumNumberOfLogEntries = 1000;
        public int MaximumNumberOfLogEntries
        {
            get => maximumNumberOfLogEntries;
            set => maximumNumberOfLogEntries = value;
        }

        [SerializeField] private DistanceOption panelDistance = DistanceOption.Default;
        public DistanceOption PanelDistance
        {
            get => panelDistance;
            set => panelDistance = value;
        }

        [SerializeField] private bool createEventSystem = true;
        public bool CreateEventSystem
        {
            get => createEventSystem;
            set => createEventSystem = value;
        }

        [SerializeField] private bool automaticLayerCullingUpdate = true;
        public bool AutomaticLayerCullingUpdate
        {
            get => automaticLayerCullingUpdate;
            set => automaticLayerCullingUpdate = value;
        }

        [SerializeField] private int panelLayer = 20;
        public int PanelLayer
        {
            get => panelLayer;
            set => panelLayer = value;
        }

        [SerializeField] private int meshRendererLayer = 21;
        public int MeshRendererLayer
        {
            get => meshRendererLayer;
            set => meshRendererLayer = value;
        }

        [SerializeField] private int overlayDepth = 10;
        public int OverlayDepth
        {
            get => overlayDepth;
            set => overlayDepth = value;
        }

        [SerializeField] private List<bool> inspectedDataEnabled = new();
        public List<bool> InspectedDataEnabled
        {
            get => inspectedDataEnabled;
            set => inspectedDataEnabled = value;
        }

        [SerializeField] private List<InspectedData> inspectedDataAssets = new();
        public List<InspectedData> InspectedDataAssets
        {
            get => inspectedDataAssets;
            set => inspectedDataAssets = value;
        }

        [SerializeField] private bool useCustomIntegrationConfig = false;
        public bool UseCustomIntegrationConfig
        {
            get => useCustomIntegrationConfig;
            set => useCustomIntegrationConfig = value;
        }

        [SerializeField] private string customIntegrationConfigClassName = null;

        public string CustomIntegrationConfigClassName
        {
            get => customIntegrationConfigClassName;
            set => customIntegrationConfigClassName = value;
        }

#if UNITY_EDITOR
        public static void UpdateAllDebugTypesForInstance(List<Type> types)
        {
            UpdateAllDebugTypes(types, Instance);
        }

        internal static void UpdateAllDebugTypes(List<Type> types, RuntimeSettings instance = null)
        {
            var targetInstance = instance ? instance : Instance;
            targetInstance.debugTypesDict.Clear();
            foreach (var type in types)
            {
                var assemblyName = type.Assembly.GetName().Name;
                if (!targetInstance.debugTypesDict.ContainsKey(assemblyName))
                {
                    targetInstance.debugTypesDict[assemblyName] = new List<string>();
                }
                targetInstance.debugTypesDict[assemblyName].Add(type.FullName);
            }
        }

        public static void UpdateTypes(string assemblyName, List<string> types, RuntimeSettings instance = null)
        {
            var targetInstance = instance ? instance : Instance;
            var existingDebugTypesDict = targetInstance.debugTypesDict;
            if (types.Count == 0)
            {
                if (existingDebugTypesDict.ContainsKey(assemblyName))
                {
                    existingDebugTypesDict.Remove(assemblyName);
                    CommitDebugTypes(targetInstance);
                }
            }
            else
            {
                existingDebugTypesDict[assemblyName] = types;
                CommitDebugTypes(targetInstance);
            }
        }

        private static void CommitDebugTypes(RuntimeSettings runtimeSettings)
        {
            EditorUtility.SetDirty(runtimeSettings);
        }

        internal static string GetDebugTypesInstanceAssetPath()
        {
            return GetAssetPath(InstanceAssetName);
        }
#endif

        public RuntimeSettings()
        {
            debugTypes = new List<DebugData>();
            debugTypesDict = new Dictionary<string, List<string>>();
        }

        public void OnBeforeSerialize()
        {
            debugTypes.Clear();
            foreach (var (assemblyName, typeList) in debugTypesDict)
            {
                debugTypes.Add(new DebugData(assemblyName, typeList));
            }
        }

        public void OnAfterDeserialize()
        {
            foreach (var debugDataPair in debugTypes)
            {
                debugTypesDict[debugDataPair.AssemblyName] = debugDataPair.DebugTypes;
            }
        }

    }
}
