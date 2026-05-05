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

#if USING_XR_SDK_OPENXR
using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEditor.Build.Reporting;
#endif

namespace Meta.XR
{
    /// <summary>
    /// MetaXR Feature for OpenXR
    /// </summary>
#if UNITY_EDITOR
    [MetaOpenXRFeature(
        featureId: featureId,
        uiName: "Meta XR Feature",
        desc: "Meta XR Feature for OpenXR, enables Meta features for Quest devices",
        version: "0.0.1",
        targetApiVersion: "1.1.45",
        extensions: new[]
        {
            "XR_KHR_vulkan_enable",
            "XR_KHR_D3D11_enable",
            "XR_OCULUS_common_reference_spaces",
            "XR_FB_display_refresh_rate",
            "XR_EXT_performance_settings",
            "XR_FB_composition_layer_image_layout",
            "XR_KHR_android_surface_swapchain",
            "XR_FB_android_surface_swapchain_create",
            "XR_KHR_composition_layer_color_scale_bias",
            "XR_FB_color_space",
            "XR_EXT_hand_tracking",
            "XR_FB_swapchain_update_state",
            "XR_FB_swapchain_update_state_opengl_es",
            "XR_FB_swapchain_update_state_vulkan",
            "XR_FB_composition_layer_alpha_blend",
            "XR_KHR_composition_layer_depth",
            "XR_KHR_composition_layer_cylinder",
            "XR_KHR_composition_layer_cube",
            "XR_KHR_composition_layer_equirect2",
            "XR_KHR_convert_timespec_time",
            "XR_KHR_visibility_mask",
            "XR_FB_render_model",
            "XR_FB_spatial_entity",
            "XR_FB_spatial_entity_user",
            "XR_FB_spatial_entity_query",
            "XR_FB_spatial_entity_storage",
            "XR_FB_spatial_entity_storage_batch",
            "XR_META_spatial_entity_mesh",
            "XR_META_performance_metrics",
            "XR_FB_spatial_entity_sharing",
            "XR_FB_scene",
            "XR_FB_spatial_entity_container",
            "XR_FB_scene_capture",
            "XR_FB_face_tracking",
            "XR_FB_face_tracking2",
            "XR_FB_eye_tracking",
            "XR_FB_eye_tracking_social",
            "XR_FB_body_tracking",
            "XR_META_body_tracking_full_body",
            "XR_META_body_tracking_calibration",
            "XR_META_body_tracking_fidelity",
            "XR_FB_keyboard_tracking",
            "XR_META_virtual_keyboard",
            "XR_FB_passthrough",
            "XR_FB_triangle_mesh",
            "XR_FB_passthrough_keyboard_hands",
            "XR_META_passthrough_layer_resumed_event",
            "XR_META_passthrough_color_lut",
            "XR_META_passthrough_preferences",
            "XR_OCULUS_audio_device_guid",
            "XR_FB_common_events",
            "XR_FB_hand_tracking_capsules",
            "XR_FB_hand_tracking_mesh",
            "XR_FB_hand_tracking_aim",
            "XR_FB_touch_controller_pro",
            "XR_FB_touch_controller_proximity",
            "XR_FB_composition_layer_depth_test",
            "XR_FB_haptic_amplitude_envelope",
            "XR_FB_haptic_pcm",
            "XR_EXTX1_haptic_parametric",
            "XR_META_local_dimming",
            "XR_META_hand_tracking_wide_motion_mode",
            "XR_EXT_hand_tracking_data_source",
            "XR_EXT_hand_joints_motion_range",
            "XR_META_touch_controller_plus",
            "XR_META_simultaneous_hands_and_controllers",
            "XR_MSFT_hand_interaction",
            "XR_EXT_hand_interaction",
            "XR_FB_hand_tracking_confidence",
            "XR_META_detached_controllers",
            "XR_LOGITECH_mx_ink_stylus_interaction",
            "XR_META_colocation_discovery",
            "XR_META_spatial_entity_sharing",
            "XR_META_spatial_entity_group_sharing",
            "XR_EXT_debug_utils",
            "XR_META_dynamic_object_tracker",
            "XR_META_dynamic_object_keyboard",
            "XR_META_hand_tracking_microgestures",
            "XR_META_spatial_entity_persistence",
            "XR_META_spatial_entity_discovery",
            "XR_META_spatial_entity_room_mesh",
            "XR_META_boundary_visibility",
            "XR_META_face_tracking_visemes",
            "XR_META_headset_id",
            "XR_FB_composition_layer_settings",
            "XR_META_automatic_layer_filter",
            "XR_FB_composition_layer_secure_content",
            "XR_METAX1_spatial_entity_marker",
            "XR_EXT_spatial_entity",
            "XR_EXT_spatial_marker_tracking",
            "XR_META_environment_raycast",
            "XR_EXT_future",
            "XR_METAX2_debug_utils_region_profiling",
            "XR_METAX3_debug_utils_region_profiling",
            "XR_EXTX2_stationary_reference_space",
            "XR_META_recommended_layer_resolution",
            "XR_EXT_local_floor",
            "XR_METAX1_passthrough_camera_data",
            "XR_METAX1_hand_tracking_frequency_hint",
            "XR_METAX1_hand_tracking_unextrapolated_poses",
            "XR_METAX1_hand_tracking_wide_motion_mode2",
        })]
#endif
    public partial class MetaXRFeature : MetaFeatureBase<MetaXRFeature>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeOnLoad() => s_featureInstance = null;

        /// <summary>
        /// The feature id string. This is used to give the feature a well known id for reference.
        /// </summary>
        public const string featureId = "com.meta.openxr.feature.metaxr";

        /// <summary>
        /// A set of commands (functions) associated with this <see cref="OpenXRFeature"/>.
        /// </summary>
        /// <remarks>
        /// Each command corresponds to a function defined by the OpenXR specification
        /// https://registry.khronos.org/OpenXR/specs/1.1/html/xrspec.html
        ///
        /// These function delegates are assigned automatically by <see cref="BindFunctionPointers"/>, and
        /// overriding them with your own method is undefined behavior.
        /// </remarks>
        public struct OpenXRCommand
        {
            public OpenXRNativeFuncs.xrApplyHapticFeedback xrApplyHapticFeedback;
            public OpenXRNativeFuncs.xrHapticParametricGetPropertiesEXTX1 xrHapticParametricGetPropertiesEXTX1;
            public OpenXRNativeFuncs.xrGetDeviceSampleRateFB xrGetDeviceSampleRateFB;
            public OpenXRNativeFuncs.xrResumeSimultaneousHandsAndControllersTrackingMETA xrResumeSimultaneousHandsAndControllersTrackingMETA;
            public OpenXRNativeFuncs.xrPauseSimultaneousHandsAndControllersTrackingMETA xrPauseSimultaneousHandsAndControllersTrackingMETA;
            public OpenXRNativeFuncs.xrQuerySpacesFB xrQuerySpacesFB;
            public OpenXRNativeFuncs.xrGetSpaceContainerFB xrGetSpaceContainerFB;
            public OpenXRNativeFuncs.xrCreateSpatialAnchorFB xrCreateSpatialAnchorFB;
            public OpenXRNativeFuncs.xrGetSpaceUuidFB xrGetSpaceUuidFB;
            public OpenXRNativeFuncs.xrEnumerateSpaceSupportedComponentsFB xrEnumerateSpaceSupportedComponentsFB;
            public OpenXRNativeFuncs.xrGetSpaceComponentStatusFB xrGetSpaceComponentStatusFB;
            public OpenXRNativeFuncs.xrRetrieveSpaceQueryResultsFB xrRetrieveSpaceQueryResultsFB;
            public OpenXRNativeFuncs.xrSetSpaceComponentStatusFB xrSetSpaceComponentStatusFB;
            public OpenXRNativeFuncs.xrSaveSpacesMETA xrSaveSpacesMETA;
            public OpenXRNativeFuncs.xrEraseSpacesMETA xrEraseSpacesMETA;
            public OpenXRNativeFuncs.xrDiscoverSpacesMETA xrDiscoverSpacesMETA;
            public OpenXRNativeFuncs.xrRetrieveSpaceDiscoveryResultsMETA xrRetrieveSpaceDiscoveryResultsMETA;
            public OpenXRNativeFuncs.xrGetSpaceBoundary2DFB xrGetSpaceBoundary2DFB;
            public OpenXRNativeFuncs.xrGetSpaceBoundingBox2DFB xrGetSpaceBoundingBox2DFB;
            public OpenXRNativeFuncs.xrGetSpaceBoundingBox3DFB xrGetSpaceBoundingBox3DFB;
            public OpenXRNativeFuncs.xrGetSpaceRoomLayoutFB xrGetSpaceRoomLayoutFB;
            public OpenXRNativeFuncs.xrGetSpaceSemanticLabelsFB xrGetSpaceSemanticLabelsFB;
            public OpenXRNativeFuncs.xrGetSpaceTriangleMeshMETA xrGetSpaceTriangleMeshMETA;
            public OpenXRNativeFuncs.xrCreateSpaceUserFB xrCreateSpaceUserFB;
            public OpenXRNativeFuncs.xrGetSpaceUserIdFB xrGetSpaceUserIdFB;
            public OpenXRNativeFuncs.xrDestroySpaceUserFB xrDestroySpaceUserFB;
            public OpenXRNativeFuncs.xrShareSpacesFB xrShareSpacesFB;
            public OpenXRNativeFuncs.xrGetSpaceRoomMeshFaceIndicesMETA xrGetSpaceRoomMeshFaceIndicesMETA;
            public OpenXRNativeFuncs.xrGetSpaceRoomMeshMETA xrGetSpaceRoomMeshMETA;
            public OpenXRNativeFuncs.xrShareSpacesMETA xrShareSpacesMETA;
            public OpenXRNativeFuncs.xrDestroySpace xrDestroySpace;
            public OpenXRNativeFuncs.xrRequestSceneCaptureFB xrRequestSceneCaptureFB;
        }

        /// <summary>
        /// The commands (functions) associated with this <see cref="OpenXRFeature"/>.
        /// </summary>
        /// <remarks>
        /// Each command corresponds to a function defined by the OpenXR specification
        /// https://registry.khronos.org/OpenXR/specs/1.1/html/xrspec.html
        ///
        /// You can use this property to invoke an OpenXR function directly.
        ///
        /// When this <see cref="OpenXRFeature"/> is enabled, these functions are also used
        /// by the associated methods in <see cref="OVRPlugin"/> instead of calling into
        /// the compiled native OVRPlugin shared library.
        /// </remarks>
        public OpenXRCommand Command => _command;

        private OpenXRCommand _command;

        // Matches the logging style we use in OVRPlugin
        private const string LogPrefix = "[" + nameof(MetaXRFeature) + "]: {0} ";

        /// <summary>
        /// For detecting when the user has mounted or unmounted the headset.
        /// </summary>
        public bool userPresent
        {
            get
            {
                if (OVRPlugin.UnityOpenXR.Enabled)
                    return OVRPlugin.userPresent;
                else
                    return false;
            }
        }

        // Enabled booleans for checking if a certain extension has been enabled by the OpenXR runtime.
        private bool _parametricHapticsEnabled = false;
        private bool _hapticsAmplitudeEnvelopeEnabled = false;
        private bool _hapticPcmEnabled = false;
        private bool _simultaneousHandsAndControllersEnabled = false;

        // Supported booleans that hold information about if a feature is supported after calling xrGetSystemProperties.
        private bool _parametricHapticsSupported = false;
        private bool _simultaneousHandsAndControllersSupported = false;

        /// <inheritdoc />
        protected override IntPtr HookGetInstanceProcAddr(IntPtr func)
        {
            OVRPlugin.UnityOpenXR.Enabled = true;

            Debug.Log($"[MetaXRFeature] HookGetInstanceProcAddr: {func}");

            Debug.Log($"[MetaXRFeature] SetClientVersion");
            OVRPlugin.UnityOpenXR.SetClientVersion();

#if UNITY_ANDROID && !UNITY_EDITOR
            OVRRuntimeSettings runtimeSettings = OVRRuntimeSettings.GetRuntimeSettings();
            OVRPlugin.UnityOpenXR.AllowVisibilityMesh(runtimeSettings.VisibilityMesh);
#endif
            base.HookGetInstanceProcAddr(func);
            return OVRPlugin.UnityOpenXR.HookGetInstanceProcAddr(func);
        }

        /// <inheritdoc />
        protected override bool OnInstanceCreate(ulong xrInstance)
        {
            bool isMetaHeadsetIdSupported = false;
            string[] extensions = OpenXRRuntime.GetAvailableExtensions();
            foreach (string extension in extensions)
            {
                if (extension == "XR_META_headset_id")
                {
                    isMetaHeadsetIdSupported = true;
                    break;
                }
            }

            if (isMetaHeadsetIdSupported)
            {
                Debug.Log("[MetaXRFeature] OpenXR runtime supports XR_META_headset_id extension. MetaXRFeature is enabled.");
            }
            else
            {
                // The runtime name string will be used to support old runtime versions which misses XR_META_headset_id extension.
                // This path should be removed in the future.
                string runtimeNameLowercase = OpenXRRuntime.name.ToLower();
                if (!runtimeNameLowercase.Contains("meta") && !runtimeNameLowercase.Contains("oculus"))
                {
                    // disable MetaXRFeature from non-Oculus/Meta OpenXR runtimes
                    Debug.LogWarningFormat("[MetaXRFeature] MetaXRFeature is disabled on non-Oculus/Meta OpenXR Runtime. Runtime name: {0}", OpenXRRuntime.name);
                    return false;
                }
            }

            // here's one way you can grab the instance
            Debug.Log($"[MetaXRFeature] OnInstanceCreate: {xrInstance}");
            bool result = OVRPlugin.UnityOpenXR.OnInstanceCreate(xrInstance);
            if (!result)
            {
                Debug.LogWarning("[MetaXRFeature] OnInstanceCreate returned an error. If you are using Quest Link, please verify if it's started.");
            }
            return base.OnInstanceCreate(xrInstance) && result;
        }

        /// <inheritdoc />
        protected override void OnInstanceDestroy(ulong xrInstance)
        {
            // here's one way you can grab the instance
            Debug.Log($"[MetaXRFeature] OnInstanceDestroy: {xrInstance}");
            OVRPlugin.UnityOpenXR.OnInstanceDestroy(xrInstance);
            base.OnInstanceDestroy(xrInstance);
        }

        /// <inheritdoc />
        protected override void OnSessionCreate(ulong xrSession)
        {
            // here's one way you can grab the session
            Debug.Log($"[MetaXRFeature] OnSessionCreate: {xrSession}");
            OVRPlugin.UnityOpenXR.OnSessionCreate(xrSession);
            base.OnSessionCreate(xrSession);
        }

        /// <inheritdoc />
        protected override void OnAppSpaceChange(ulong xrSpace)
        {
            Debug.Log($"[MetaXRFeature] OnAppSpaceChange: {xrSpace}");
#if !UNITY_OPENXR_1_9_0
            OVRPlugin.UnityOpenXR.OnAppSpaceChange(xrSpace);
#else
            int spaceFlags = 0;
            if (OpenXRSettings.AllowRecentering)
                spaceFlags |= (int)OVRPlugin.SpaceFlags.AllowRecentering;
            OVRPlugin.UnityOpenXR.OnAppSpaceChange2(xrSpace, spaceFlags);
#endif
            base.OnAppSpaceChange(xrSpace);
        }

        /// <inheritdoc />
        protected override void OnSessionStateChange(int oldState, int newState)
        {
            Debug.Log($"[MetaXRFeature] OnSessionStateChange: {oldState} -> {newState}");
            OVRPlugin.UnityOpenXR.OnSessionStateChange(oldState, newState);
            base.OnSessionStateChange(oldState, newState);
        }

        /// <inheritdoc />
        protected override void OnSessionBegin(ulong xrSession)
        {
            Debug.Log($"[MetaXRFeature] OnSessionBegin: {xrSession}");
            OVRPlugin.UnityOpenXR.OnSessionBegin(xrSession);
            base.OnSessionBegin(xrSession);
        }

        /// <inheritdoc />
        protected override void OnSessionEnd(ulong xrSession)
        {
            Debug.Log($"[MetaXRFeature] OnSessionEnd: {xrSession}");
            OVRPlugin.UnityOpenXR.OnSessionEnd(xrSession);
            base.OnSessionEnd(xrSession);
        }

        /// <inheritdoc />
        protected override void OnSessionExiting(ulong xrSession)
        {
            Debug.Log($"[MetaXRFeature] OnSessionExiting: {xrSession}");
            OVRPlugin.UnityOpenXR.OnSessionExiting(xrSession);
            base.OnSessionExiting(xrSession);
        }

        /// <inheritdoc />
        protected override void OnSessionDestroy(ulong xrSession)
        {
            Debug.Log($"[MetaXRFeature] OnSessionDestroy: {xrSession}");
            OVRPlugin.UnityOpenXR.OnSessionDestroy(xrSession);
            base.OnSessionDestroy(xrSession);
        }

        protected override void BindFunctionPointers()
        {
            _parametricHapticsEnabled = OpenXRRuntime.IsExtensionEnabled("XR_EXTX1_haptic_parametric");
            _hapticPcmEnabled = OpenXRRuntime.IsExtensionEnabled("XR_FB_haptic_pcm");
            _hapticsAmplitudeEnvelopeEnabled = OpenXRRuntime.IsExtensionEnabled("XR_FB_haptic_amplitude_envelope");
            _simultaneousHandsAndControllersEnabled = OpenXRRuntime.IsExtensionEnabled("XR_META_simultaneous_hands_and_controllers");

            unsafe
            {
                if (GetInstanceDelegate<OpenXRNativeFuncs.xrGetSystem>(nameof(OpenXRNativeFuncs.xrGetSystem), out var xrGetSystem).IsSuccess() &&
                    GetInstanceDelegate<OpenXRNativeFuncs.xrGetSystemProperties>(nameof(OpenXRNativeFuncs.xrGetSystemProperties), out var xrGetSystemProperties).IsSuccess())
                {
                    if (xrGetSystem(Instance, new XrSystemGetInfo
                    {
                        Type = XrSystemGetInfo.StructureType,
                        FormFactor = XrFormFactor.HeadMountedDisplay,
                    }, out var systemId)
                        .OrLogFormat(LogPrefix + nameof(xrGetSystem))
                        .IsSuccess())
                    {
                        var systemProperties = new XrSystemProperties
                        {
                            Type = XrSystemProperties.StructureType,
                        };

                        var hapticsParametricProperties = new XrSystemHapticParametricPropertiesEXTX1
                        {
                            Type = XrSystemHapticParametricPropertiesEXTX1.StructureType,
                            SupportsParametricHaptics = false
                        };

                        if (_parametricHapticsEnabled)
                        {
                            Unsafe.InsertFirst(&systemProperties, &hapticsParametricProperties);
                        }

                        var simultaneousHandsAndControllers = new XrSystemSimultaneousHandsAndControllersPropertiesMETA
                        {
                            Type = XrSystemSimultaneousHandsAndControllersPropertiesMETA.StructureType,
                            SupportsSimultaneousHandsAndControllers = false
                        };

                        if (_simultaneousHandsAndControllersEnabled)
                        {
                            Unsafe.InsertFirst(&systemProperties, &simultaneousHandsAndControllers);
                        }

                        if (xrGetSystemProperties(Instance, systemId, ref systemProperties)
                            .OrLogFormat(LogPrefix + nameof(xrGetSystemProperties))
                            .IsSuccess())
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine("System Properties:");
                            sb.AppendLine($"\tSystemId={systemProperties.SystemId}");
                            sb.AppendLine($"\tVendorId={systemProperties.VendorId}");
                            sb.AppendLine($"\tSystemName={Marshal.PtrToStringUTF8(new(systemProperties.SystemName))}");

                            sb.AppendLine($"\tSupportsParametricHaptics={hapticsParametricProperties.SupportsParametricHaptics}");
                            sb.AppendLine($"\tSupportsSimultaneousHandsAndControllers={simultaneousHandsAndControllers.SupportsSimultaneousHandsAndControllers}");

                            Debug.Log(sb);

                            _parametricHapticsSupported = hapticsParametricProperties.SupportsParametricHaptics;
                            _simultaneousHandsAndControllersSupported = simultaneousHandsAndControllers.SupportsSimultaneousHandsAndControllers;
                        }
                    }
                }
            }

            GetInstanceDelegate(nameof(_command.xrApplyHapticFeedback), out _command.xrApplyHapticFeedback);
            GetInstanceDelegate(nameof(_command.xrHapticParametricGetPropertiesEXTX1), out _command.xrHapticParametricGetPropertiesEXTX1);
            GetInstanceDelegate(nameof(_command.xrGetDeviceSampleRateFB), out _command.xrGetDeviceSampleRateFB);
            GetInstanceDelegate(nameof(_command.xrResumeSimultaneousHandsAndControllersTrackingMETA), out _command.xrResumeSimultaneousHandsAndControllersTrackingMETA);
            GetInstanceDelegate(nameof(_command.xrPauseSimultaneousHandsAndControllersTrackingMETA), out _command.xrPauseSimultaneousHandsAndControllersTrackingMETA);
            BindSpatialEntityFunctionPointers();
        }

        protected override void UnbindFunctionPointers() => _command = default;

        public static ulong GetInteractionProfile(string userPath)
        {
            return GetCurrentInteractionProfile(userPath);
        }

        // protected override void OnSessionLossPending(ulong xrSession) {}
        // protected override void OnInstanceLossPending (ulong xrInstance) {}
        // protected override void OnSystemChange(ulong xrSystem) {}
        // protected override void OnFormFactorChange (int xrFormFactor) {}
        // protected override void OnViewConfigurationTypeChange (int xrViewConfigurationType) {}
        // protected override void OnEnvironmentBlendModeChange (int xrEnvironmentBlendMode) {}
        // protected override void OnEnabledChange() {}

        protected void LogError(string message)
        {
            Debug.LogErrorFormat(LogPrefix, message);
        }
    }

#if UNITY_EDITOR && UNITY_OPENXR_BOOT_CONFIG
    internal class MetaXRBootConfig : OpenXRFeatureBuildHooks
    {
        public override int callbackOrder => 1;
        public override Type featureType => typeof(MetaXRFeature);

        protected override void OnPostGenerateGradleAndroidProjectExt(string path) {}
        protected override void OnPostprocessBuildExt(BuildReport report) {}
        protected override void OnPreprocessBuildExt(BuildReport report) {}

        protected override void OnProcessBootConfigExt(BuildReport report, BootConfigBuilder builder)
        {
            builder.SetBootConfigBoolean("xr-meta-enabled", true);
            builder.SetBootConfigBoolean("xr-vulkan-extension-fragment-density-map-enabled", true);
        }
    }
#endif
}

#endif
