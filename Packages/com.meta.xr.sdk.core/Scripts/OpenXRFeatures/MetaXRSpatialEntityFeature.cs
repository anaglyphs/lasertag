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

using UnityEngine;

#if USING_XR_SDK_OPENXR
using UnityEngine.XR.OpenXR;
#endif

namespace Meta.XR
{
#if USING_XR_SDK_OPENXR
    partial class MetaXRFeature
    {
        private bool _storageEnabled;

        private void BindSpatialEntityFunctionPointers()
        {
            _storageEnabled = OpenXRRuntime.IsExtensionEnabled("XR_FB_spatial_entity_storage");
            GetInstanceDelegate(nameof(_command.xrQuerySpacesFB), out _command.xrQuerySpacesFB);
            GetInstanceDelegate(nameof(_command.xrGetSpaceContainerFB), out _command.xrGetSpaceContainerFB);
            GetInstanceDelegate(nameof(_command.xrCreateSpatialAnchorFB), out _command.xrCreateSpatialAnchorFB);
            GetInstanceDelegate(nameof(_command.xrGetSpaceUuidFB), out _command.xrGetSpaceUuidFB);
            GetInstanceDelegate(nameof(_command.xrEnumerateSpaceSupportedComponentsFB), out _command.xrEnumerateSpaceSupportedComponentsFB);
            GetInstanceDelegate(nameof(_command.xrGetSpaceComponentStatusFB), out _command.xrGetSpaceComponentStatusFB);
            GetInstanceDelegate(nameof(_command.xrRetrieveSpaceQueryResultsFB), out _command.xrRetrieveSpaceQueryResultsFB);
            GetInstanceDelegate(nameof(_command.xrSetSpaceComponentStatusFB), out _command.xrSetSpaceComponentStatusFB);
            GetInstanceDelegate(nameof(_command.xrSaveSpacesMETA), out _command.xrSaveSpacesMETA);
            GetInstanceDelegate(nameof(_command.xrEraseSpacesMETA), out _command.xrEraseSpacesMETA);
            GetInstanceDelegate(nameof(_command.xrDiscoverSpacesMETA), out _command.xrDiscoverSpacesMETA);
            GetInstanceDelegate(nameof(_command.xrRetrieveSpaceDiscoveryResultsMETA), out _command.xrRetrieveSpaceDiscoveryResultsMETA);
            GetInstanceDelegate(nameof(_command.xrGetSpaceBoundary2DFB), out _command.xrGetSpaceBoundary2DFB);
            GetInstanceDelegate(nameof(_command.xrGetSpaceBoundingBox2DFB), out _command.xrGetSpaceBoundingBox2DFB);
            GetInstanceDelegate(nameof(_command.xrGetSpaceBoundingBox3DFB), out _command.xrGetSpaceBoundingBox3DFB);
            GetInstanceDelegate(nameof(_command.xrGetSpaceRoomLayoutFB), out _command.xrGetSpaceRoomLayoutFB);
            GetInstanceDelegate(nameof(_command.xrGetSpaceSemanticLabelsFB), out _command.xrGetSpaceSemanticLabelsFB);
            GetInstanceDelegate(nameof(_command.xrGetSpaceTriangleMeshMETA), out _command.xrGetSpaceTriangleMeshMETA);
            GetInstanceDelegate(nameof(_command.xrCreateSpaceUserFB), out _command.xrCreateSpaceUserFB);
            GetInstanceDelegate(nameof(_command.xrGetSpaceUserIdFB), out _command.xrGetSpaceUserIdFB);
            GetInstanceDelegate(nameof(_command.xrDestroySpaceUserFB), out _command.xrDestroySpaceUserFB);
            GetInstanceDelegate(nameof(_command.xrShareSpacesFB), out _command.xrShareSpacesFB);
            GetInstanceDelegate(nameof(_command.xrShareSpacesMETA), out _command.xrShareSpacesMETA);
            GetInstanceDelegate(nameof(_command.xrGetSpaceRoomMeshFaceIndicesMETA), out _command.xrGetSpaceRoomMeshFaceIndicesMETA);
            GetInstanceDelegate(nameof(_command.xrGetSpaceRoomMeshMETA), out _command.xrGetSpaceRoomMeshMETA);
            GetInstanceDelegate(nameof(_command.xrDestroySpace), out _command.xrDestroySpace);
            GetInstanceDelegate(nameof(_command.xrRequestSceneCaptureFB), out _command.xrRequestSceneCaptureFB);
        }
    }
#endif // USING_XR_SDK_OPENXR

    public enum XrSpaceUserIdFB : ulong { }

    public enum XrSpaceUserFB : ulong { }
}
