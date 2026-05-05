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


#if !(UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || (UNITY_ANDROID && !UNITY_EDITOR))
#define OVRPLUGIN_UNSUPPORTED_PLATFORM
#endif

using Meta.XR;
using System;

partial class OVRPlugin
{
#if !OVRPLUGIN_UNSUPPORTED_PLATFORM
    private static class Shim
    {
        public static Result ovrp_CreateSpatialAnchor(ref SpatialAnchorCreateInfo createInfo, out ulong requestId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_CreateSpatialAnchor(ref createInfo, out requestId);
            }
#endif
            return OVRP_1_72_0.ovrp_CreateSpatialAnchor(ref createInfo, out requestId);
        }

        public static Result ovrp_SetSpaceComponentStatus(ref ulong space, SpaceComponentType componentType, Bool enable, double timeout, out ulong requestId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_SetSpaceComponentStatus(ref space, componentType, enable, timeout, out requestId);
            }
#endif
            return OVRP_1_72_0.ovrp_SetSpaceComponentStatus(ref space, componentType, enable, timeout, out requestId);
        }

        public static Result ovrp_GetSpaceComponentStatus(ref ulong space, SpaceComponentType componentType, out Bool enabled, out Bool changePending)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceComponentStatus(ref space, componentType, out enabled, out changePending);
            }
#endif
            return OVRP_1_72_0.ovrp_GetSpaceComponentStatus(ref space, componentType, out enabled, out changePending);
        }

        public static Result ovrp_EnumerateSpaceSupportedComponents(ref ulong space,
            uint componentTypesCapacityInput, out uint componentTypesCountOutput,
            SpaceComponentType[] componentTypes)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_EnumerateSpaceSupportedComponents(ref space, componentTypesCapacityInput,
                    out componentTypesCountOutput, componentTypes);
            }
#endif
            return OVRP_1_72_0.ovrp_EnumerateSpaceSupportedComponents(ref space, componentTypesCapacityInput,
                out componentTypesCountOutput, componentTypes);
        }

        public static unsafe Result ovrp_EnumerateSpaceSupportedComponents(ref ulong space,
            uint componentTypesCapacityInput, out uint componentTypesCountOutput,
            SpaceComponentType* componentTypes)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_EnumerateSpaceSupportedComponents(ref space, componentTypesCapacityInput, out componentTypesCountOutput, componentTypes);
            }
#endif
            return OVRP_1_72_0.ovrp_EnumerateSpaceSupportedComponents(ref space, componentTypesCapacityInput, out componentTypesCountOutput, componentTypes);
        }

        public static Result ovrp_QuerySpaces(ref SpaceQueryInfo queryInfo, out ulong requestId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_QuerySpaces(ref queryInfo, out requestId);
            }
#endif
            return OVRP_1_72_0.ovrp_QuerySpaces(ref queryInfo, out requestId);
        }

        public static Result ovrp_QuerySpaces2(ref SpaceQueryInfo2 queryInfo, out UInt64 requestId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_QuerySpaces2(ref queryInfo, out requestId);
            }
#endif
            return OVRP_1_103_0.ovrp_QuerySpaces2(ref queryInfo, out requestId);
        }

        public static Result ovrp_RetrieveSpaceQueryResults(ref ulong requestId, uint resultCapacityInput,
            ref uint resultCountOutput, IntPtr results)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_RetrieveSpaceQueryResults(ref requestId, resultCapacityInput, out resultCountOutput, results);
            }
#endif
            return OVRP_1_72_0.ovrp_RetrieveSpaceQueryResults(ref requestId, resultCapacityInput, ref resultCountOutput, results);
        }

        public static Result ovrp_GetSpaceContainer(ref ulong space, ref SpaceContainerInternal containerInternal)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceContainer(ref space, ref containerInternal);
            }
#endif
            return OVRP_1_72_0.ovrp_GetSpaceContainer(ref space, ref containerInternal);
        }

        public static Result ovrp_GetSpaceBoundingBox2D(ref ulong space, out Rectf rect)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceBoundingBox2D(ref space, out rect);
            }
#endif
            return OVRP_1_72_0.ovrp_GetSpaceBoundingBox2D(ref space, out rect);
        }

        public static Result ovrp_GetSpaceBoundingBox3D(ref ulong space, out Boundsf bounds)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceBoundingBox3D(ref space, out bounds);
            }
#endif
            return OVRP_1_72_0.ovrp_GetSpaceBoundingBox3D(ref space, out bounds);
        }

        public static Result ovrp_GetSpaceSemanticLabels(ref ulong space,
            ref SpaceSemanticLabelInternal labelsInternal)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceSemanticLabels(ref space, ref labelsInternal);
            }
#endif
            return OVRP_1_72_0.ovrp_GetSpaceSemanticLabels(ref space, ref labelsInternal);
        }

        public static Result ovrp_GetSpaceRoomLayout(ref ulong space,
            ref RoomLayoutInternal roomLayoutInternal)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceRoomLayout(ref space, ref roomLayoutInternal);
            }
#endif
            return OVRP_1_72_0.ovrp_GetSpaceRoomLayout(ref space, ref roomLayoutInternal);
        }

        public static Result ovrp_GetSpaceBoundary2D(ref ulong space,
            ref PolygonalBoundary2DInternal boundaryInternal)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceBoundary2D(ref space, ref boundaryInternal);
            }
#endif
            return OVRP_1_72_0.ovrp_GetSpaceBoundary2D(ref space, ref boundaryInternal);
        }

        public static Result ovrp_GetSpaceUuid(in ulong space, out Guid uuid)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceUuid(in space, out uuid);
            }
#endif
            return OVRP_1_74_0.ovrp_GetSpaceUuid(in space, out uuid);
        }

        public static unsafe Result ovrp_ShareSpaces(ulong* spaces, uint numSpaces, ulong* userHandles,
            uint numUsers, out ulong requestId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_ShareSpaces(spaces, numSpaces, userHandles, numUsers, out requestId);
            }
#endif
            return OVRP_1_79_0.ovrp_ShareSpaces(spaces, numSpaces, userHandles, numUsers, out requestId);
        }

        public static Result ovrp_ShareSpaces2(in ShareSpacesInfo info, out ulong requestId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_ShareSpaces2(in info, out requestId);
            }
#endif
            return OVRP_1_103_0.ovrp_ShareSpaces2(in info, out requestId);
        }

        public static Result ovrp_GetSpaceUserId(in ulong spaceUserHandle, out ulong spaceUserId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceUserId(in spaceUserHandle, out spaceUserId);
            }
#endif
            return OVRP_1_79_0.ovrp_GetSpaceUserId(in spaceUserHandle, out spaceUserId);
        }

        public static Result ovrp_CreateSpaceUser(in ulong spaceUserId, out ulong spaceUserHandle)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_CreateSpaceUser(in spaceUserId, out spaceUserHandle);
            }
#endif
            return OVRP_1_79_0.ovrp_CreateSpaceUser(in spaceUserId, out spaceUserHandle);
        }

        public static Result ovrp_DestroySpaceUser(in ulong userHandle)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_DestroySpaceUser(in userHandle);
            }
#endif
            return OVRP_1_79_0.ovrp_DestroySpaceUser(in userHandle);
        }

        public static Result ovrp_GetSpaceTriangleMesh(ref ulong space,
            ref TriangleMeshInternal triangleMeshInternal)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceTriangleMesh(ref space, ref triangleMeshInternal);
            }
#endif
            return OVRP_1_82_0.ovrp_GetSpaceTriangleMesh(ref space, ref triangleMeshInternal);
        }

        public static Result ovrp_DiscoverSpaces(in SpaceDiscoveryInfo info, out ulong requestId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_DiscoverSpaces(in info, out requestId);
            }
#endif
            return OVRP_1_97_0.ovrp_DiscoverSpaces(in info, out requestId);
        }

        public static Result ovrp_RetrieveSpaceDiscoveryResults(ulong requestId, ref SpaceDiscoveryResults results)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_RetrieveSpaceDiscoveryResults(requestId, ref results);
            }
#endif
            return OVRP_1_97_0.ovrp_RetrieveSpaceDiscoveryResults(requestId, ref results);
        }

        public static unsafe Result ovrp_SaveSpaces(uint spaceCount, ulong* spaces, out ulong requestId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_SaveSpaces(spaceCount, spaces, out requestId);
            }
#endif
            return OVRP_1_97_0.ovrp_SaveSpaces(spaceCount, spaces, out requestId);
        }

        public static unsafe Result ovrp_EraseSpaces(uint spaceCount, ulong* spaces, uint uuidCount, Guid* uuids, out ulong requestId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_EraseSpaces(spaceCount, spaces, uuidCount, uuids, out requestId);
            }
#endif
            return OVRP_1_97_0.ovrp_EraseSpaces(spaceCount, spaces, uuidCount, uuids, out requestId);
        }

        public static Result ovrp_GetSpaceRoomMesh(ulong space, ref RoomMeshInternal roomMeshOutput)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceRoomMesh(space, ref roomMeshOutput);
            }
#endif
            return OVRP_1_114_0.ovrp_GetSpaceRoomMesh(space, ref roomMeshOutput);
        }

        public static Result ovrp_GetSpaceRoomFaceIndices(ulong space, Guid faceUuid, ref RoomFaceIndicesInternal roomFaceIndicesOutput)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetSpaceRoomFaceIndices(space, faceUuid, ref roomFaceIndicesOutput);
            }
#endif
            return OVRP_1_114_0.ovrp_GetSpaceRoomFaceIndices(space, faceUuid, ref roomFaceIndicesOutput);
        }

        public static Result ovrp_DestroySpace(ref ulong space)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_DestroySpace(ref space);
            }
#endif
            return OVRP_1_65_0.ovrp_DestroySpace(ref space);
        }

        public static Result ovrp_RequestSceneCapture(ref SceneCaptureRequestInternal request, out ulong requestId)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_RequestSceneCapture(ref request, out requestId);
            }
#endif
            return OVRP_1_72_0.ovrp_RequestSceneCapture(ref request, out requestId);
        }

        public static OVRPlugin.Bool ovrp_SetControllerVibration(uint controllerMask, float frequency, float amplitude)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_SetControllerVibration(controllerMask, frequency, amplitude);
            }
#endif
            return OVRP_0_1_2.ovrp_SetControllerVibration(controllerMask, frequency, amplitude);
        }

        public static Result ovrp_SetControllerLocalizedVibration(OVRPlugin.Controller controllerMask,
            OVRPlugin.HapticsLocation hapticsLocationMask, float frequency, float amplitude)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_SetControllerLocalizedVibration(controllerMask, hapticsLocationMask, frequency, amplitude);
            }
#endif
            return OVRP_1_78_0.ovrp_SetControllerLocalizedVibration(controllerMask, hapticsLocationMask, frequency, amplitude);
        }

        public static Result ovrp_SetControllerHapticsAmplitudeEnvelope(
            OVRPlugin.Controller controllerMask,
            OVRPlugin.HapticsAmplitudeEnvelopeVibration hapticsVibration)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_SetControllerHapticsAmplitudeEnvelope(controllerMask, hapticsVibration);
            }
#endif
            return OVRP_1_78_0.ovrp_SetControllerHapticsAmplitudeEnvelope(controllerMask, hapticsVibration);
        }

        public static Result ovrp_SetControllerHapticsPcm(
            OVRPlugin.Controller controllerMask,
            OVRPlugin.HapticsPcmVibration hapticsVibration)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_SetControllerHapticsPcm(controllerMask, hapticsVibration);
            }
#endif
            return OVRP_1_78_0.ovrp_SetControllerHapticsPcm(controllerMask, hapticsVibration);
        }

        public static Result ovrp_SetControllerHapticsParametric(
            OVRPlugin.Controller controllerMask,
            OVRPlugin.HapticsParametricVibration hapticsVibration)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_SetControllerHapticsParametric(controllerMask, hapticsVibration);
            }
#endif
            return OVRP_1_78_0.ovrp_SetControllerHapticsParametric(controllerMask, hapticsVibration);
        }

        public static Result ovrp_GetControllerSampleRateHz(OVRPlugin.Controller controller, out float sampleRateHz)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetControllerSampleRateHz(controller, out sampleRateHz);
            }
#endif
            return OVRP_1_78_0.ovrp_GetControllerSampleRateHz(controller, out sampleRateHz);
        }

        public static Result ovrp_GetControllerParametricProperties(OVRPlugin.Controller controllerMask,
            out OVRPlugin.HapticsParametricProperties hapticsProperties)
        {
#if USING_XR_SDK_OPENXR
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_GetControllerParametricProperties(controllerMask, out hapticsProperties);
            }
#endif
            return OVRP_1_78_0.ovrp_GetControllerParametricProperties(controllerMask, out hapticsProperties);
        }

        public static Result ovrp_SetSimultaneousHandsAndControllersEnabled(OVRPlugin.Bool enabled)
        {
#if USING_XR_SDK_OPENXR && USING_INPUT_SYSTEM_PACKAGE
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_SetSimultaneousHandsAndControllersEnabled(enabled);
            }
#endif
            return OVRP_1_88_0.ovrp_SetSimultaneousHandsAndControllersEnabled(enabled);
        }

        public static Result ovrp_IsMultimodalHandsControllersSupported(ref OVRPlugin.Bool enabled)
        {
#if USING_XR_SDK_OPENXR && USING_INPUT_SYSTEM_PACKAGE
            if (MetaXRFeature.TryGet(out var feature))
            {
                return feature.ovrp_IsMultimodalHandsControllersSupported(ref enabled);
            }
#endif
            return OVRP_1_86_0.ovrp_IsMultimodalHandsControllersSupported(ref enabled);
        }
    }
#endif // !OVRPLUGIN_UNSUPPORTED_PLATFORM
}
