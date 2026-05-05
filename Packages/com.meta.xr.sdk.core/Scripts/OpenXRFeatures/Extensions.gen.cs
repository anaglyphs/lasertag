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

namespace Meta.XR
{
#region XR_FB_spatial_entity
    public enum XrAsyncRequestIdFB : ulong { }

    public enum XrSpaceComponentTypeFB
    {
        /// <summary>
        /// Enables tracking the 6 DOF pose of the <see cref="XrSpace"/> with <see cref="OpenXRNativeFuncs.xrLocateSpace"/>.
        /// </summary>
        Locatable = 0,

        /// <summary>
        /// Enables persistence operations: save and erase.
        /// </summary>
        Storable = 1,

        /// <summary>
        /// Enables sharing of spatial entities.
        /// </summary>
        Sharable = 2,

        /// <summary>
        /// Bounded 2D component.
        /// </summary>
        Bounded2D = 3,

        /// <summary>
        /// Bounded 3D component.
        /// </summary>
        Bounded3D = 4,

        /// <summary>
        /// Semantic labels component.
        /// </summary>
        SemanticLabels = 5,

        /// <summary>
        /// Room layout component.
        /// </summary>
        RoomLayout = 6,

        /// <summary>
        /// Space container component.
        /// </summary>
        SpaceContainer = 7,

        /// <summary>
        /// Triangle mesh component.
        /// </summary>
        TriangleMesh = 1000269000,

        RoomMesh = 1000553000,
    }

    public partial struct XrSystemSpatialEntityPropertiesFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000113004;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrBool32 SupportsSpatialEntity;
    }

    public partial struct XrSpatialAnchorCreateInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000113003;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrSpace Space;
        public XrPosef PoseInSpace;
        public XrTime Time;
    }

    public partial struct XrSpaceComponentStatusSetInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000113007;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrSpaceComponentTypeFB ComponentType;
        public XrBool32 Enabled;
        public XrDuration Timeout;
    }

    public partial struct XrSpaceComponentStatusFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000113001;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrBool32 Enabled;
        public XrBool32 ChangePending;
    }

    public partial struct XrEventDataSpatialAnchorCreateCompleteFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000113005;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
        public XrSpace Space;
        public Guid Uuid;
    }

    public partial struct XrEventDataSpaceSetStatusCompleteFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000113006;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
        public XrSpace Space;
        public Guid Uuid;
        public XrSpaceComponentTypeFB ComponentType;
        public XrBool32 Enabled;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrCreateSpatialAnchorFB(
            XrSession session,
            in XrSpatialAnchorCreateInfoFB info,
            out XrAsyncRequestIdFB requestId);

        public delegate XrResult xrGetSpaceUuidFB(
            XrSpace space,
            out Guid uuid);

        public unsafe delegate XrResult xrEnumerateSpaceSupportedComponentsFB(
            XrSpace space,
            uint componentTypeCapacityInput,
            out uint componentTypeCountOutput,
            XrSpaceComponentTypeFB* componentTypes);

        public delegate XrResult xrSetSpaceComponentStatusFB(
            XrSpace space,
            in XrSpaceComponentStatusSetInfoFB info,
            out XrAsyncRequestIdFB requestId);

        public delegate XrResult xrGetSpaceComponentStatusFB(
            XrSpace space,
            XrSpaceComponentTypeFB componentType,
            ref XrSpaceComponentStatusFB status);
    }
#endregion

#region XR_FB_spatial_entity_container
    public partial struct XrSpaceContainerFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000199000;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint UuidCapacityInput;
        public uint UuidCountOutput;
        public unsafe Guid* Uuids;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrGetSpaceContainerFB(
            XrSession session,
            XrSpace space,
            ref XrSpaceContainerFB spaceContainerOutput);
    }
#endregion

#region XR_FB_spatial_entity_query
    public enum XrSpaceQueryActionFB
    {
        /// <summary>
        /// Tells the query to perform a load operation on any <see cref="XrSpace"/> returned by the query.
        /// </summary>
        Load = 0,
    }

    public partial struct XrSpaceQueryInfoBaseHeaderFB
    {
        public XrStructureType Type;
        public unsafe void* Next;
    }

    public partial struct XrSpaceFilterInfoBaseHeaderFB
    {
        public XrStructureType Type;
        public unsafe void* Next;
    }

    public partial struct XrSpaceQueryInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000156001;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrSpaceQueryActionFB QueryAction;
        public uint MaxResultCount;
        public XrDuration Timeout;
        public unsafe XrSpaceFilterInfoBaseHeaderFB* Filter;
        public unsafe XrSpaceFilterInfoBaseHeaderFB* ExcludeFilter;
    }

    public partial struct XrSpaceStorageLocationFilterInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000156003;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrSpaceStorageLocationFB Location;
    }

    public partial struct XrSpaceUuidFilterInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000156054;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint UuidCount;
        public unsafe Guid* Uuids;
    }

    public partial struct XrSpaceComponentFilterInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000156052;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrSpaceComponentTypeFB ComponentType;
    }

    public partial struct XrSpaceQueryResultFB
    {
        public XrSpace Space;
        public Guid Uuid;
    }

    public partial struct XrSpaceQueryResultsFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000156002;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint ResultCapacityInput;
        public uint ResultCountOutput;
        public unsafe XrSpaceQueryResultFB* Results;
    }

    public partial struct XrEventDataSpaceQueryResultsAvailableFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000156103;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
    }

    public partial struct XrEventDataSpaceQueryCompleteFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000156104;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
    }

    partial class OpenXRNativeFuncs
    {
        public unsafe delegate XrResult xrQuerySpacesFB(
            XrSession session,
            XrSpaceQueryInfoBaseHeaderFB* info,
            out XrAsyncRequestIdFB requestId);

        public delegate XrResult xrRetrieveSpaceQueryResultsFB(
            XrSession session,
            XrAsyncRequestIdFB requestId,
            ref XrSpaceQueryResultsFB results);
    }
#endregion

#region XR_FB_spatial_entity_sharing
    public partial struct XrSpaceShareInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000169001;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint SpaceCount;
        public unsafe XrSpace* Spaces;
        public uint UserCount;
        public unsafe XrSpaceUserFB* Users;
    }

    public partial struct XrEventDataSpaceShareCompleteFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000169002;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrShareSpacesFB(
            XrSession session,
            in XrSpaceShareInfoFB info,
            out XrAsyncRequestIdFB requestId);
    }
#endregion

#region XR_FB_spatial_entity_storage
    public enum XrSpaceStorageLocationFB
    {
        /// <summary>
        /// Invalid storage location
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// Local device storage
        /// </summary>
        Local = 1,

        /// <summary>
        /// Cloud storage
        /// </summary>
        Cloud = 2,
    }

    public enum XrSpacePersistenceModeFB
    {
        /// <summary>
        /// Invalid storage persistence
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// Store <see cref="XrSpace"/> indefinitely, or until erased
        /// </summary>
        Indefinite = 1,
    }

    public partial struct XrSpaceSaveInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000158000;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrSpace Space;
        public XrSpaceStorageLocationFB Location;
        public XrSpacePersistenceModeFB PersistenceMode;
    }

    public partial struct XrSpaceEraseInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000158001;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrSpace Space;
        public XrSpaceStorageLocationFB Location;
    }

    public partial struct XrEventDataSpaceSaveCompleteFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000158106;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
        public XrSpace Space;
        public Guid Uuid;
        public XrSpaceStorageLocationFB Location;
    }

    public partial struct XrEventDataSpaceEraseCompleteFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000158107;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
        public XrSpace Space;
        public Guid Uuid;
        public XrSpaceStorageLocationFB Location;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrSaveSpaceFB(
            XrSession session,
            in XrSpaceSaveInfoFB info,
            out XrAsyncRequestIdFB requestId);

        public delegate XrResult xrEraseSpaceFB(
            XrSession session,
            in XrSpaceEraseInfoFB info,
            out XrAsyncRequestIdFB requestId);
    }
#endregion

#region XR_FB_spatial_entity_storage_batch
    public partial struct XrSpaceListSaveInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000238000;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint SpaceCount;
        public unsafe XrSpace* Spaces;
        public XrSpaceStorageLocationFB Location;
    }

    public partial struct XrEventDataSpaceListSaveCompleteFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000238001;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrSaveSpaceListFB(
            XrSession session,
            in XrSpaceListSaveInfoFB info,
            out XrAsyncRequestIdFB requestId);
    }
#endregion

#region XR_FB_spatial_entity_user
    public partial struct XrSpaceUserCreateInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000241001;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrSpaceUserIdFB UserId;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrCreateSpaceUserFB(
            XrSession session,
            in XrSpaceUserCreateInfoFB info,
            out XrSpaceUserFB user);

        public delegate XrResult xrGetSpaceUserIdFB(
            XrSpaceUserFB user,
            out XrSpaceUserIdFB userId);

        public delegate XrResult xrDestroySpaceUserFB(
            XrSpaceUserFB user);
    }
#endregion

#region XR_FB_scene
    public partial struct XrExtent3Df
    {
        public float Width;
        public float Height;
        public float Depth;
    }

    public partial struct XrOffset3DfFB
    {
        public float X;
        public float Y;
        public float Z;
    }

    public partial struct XrRect3DfFB
    {
        public XrOffset3DfFB Offset;
        public XrExtent3Df Extent;
    }

    public partial struct XrSemanticLabelsFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000175000;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint BufferCapacityInput;
        public uint BufferCountOutput;
        public unsafe byte* Buffer;
    }

    public partial struct XrRoomLayoutFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000175001;
        public XrStructureType Type;
        public unsafe void* Next;
        public Guid FloorUuid;
        public Guid CeilingUuid;
        public uint WallUuidCapacityInput;
        public uint WallUuidCountOutput;
        public unsafe Guid* WallUuids;
    }

    public partial struct XrBoundary2DFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000175002;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint VertexCapacityInput;
        public uint VertexCountOutput;
        public unsafe XrVector2f* Vertices;
    }

    public partial struct XrSemanticLabelsSupportInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000175010;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrSemanticLabelsSupportFlagsFB Flags;
        public unsafe byte* RecognizedLabels;
    }

    [Flags]
    public enum XrSemanticLabelsSupportFlagsFB : ulong
    {
        /// <summary>
        /// If set, and the runtime reports the `extensionVersion` as 2 or greater, the runtime _may_ return multiple semantic labels separated by a comma without spaces. Otherwise, the runtime _must_ return a single semantic label.
        /// </summary>
        MultipleSemanticLabelsBit = 1 << 0,

        /// <summary>
        /// If set, and the runtime reports the `extensionVersion` as 3 or greater, the runtime _must_ return "TABLE" instead of "DESK" as a semantic label to the application. Otherwise, the runtime _must_ return "DESK" instead of "TABLE" as a semantic label to the application, when applicable.
        /// </summary>
        AcceptDeskToTableMigrationBit = 1 << 1,

        /// <summary>
        /// If set, and the runtime reports the `extensionVersion` as 4 or greater, the runtime _may_ return "INVISIBLE_WALL_FACE" instead of "WALL_FACE" as a semantic label to the application in order to represent an invisible wall used to conceptually separate a space (e.g., separate a living space from a kitchen space in an open floor plan house even though there is no real wall between the two spaces) instead of a real wall. Otherwise, the runtime _must_ return "WALL_FACE" as a semantic label to the application in order to represent both an invisible and real wall, when applicable.
        /// </summary>
        AcceptInvisibleWallFaceBit = 1 << 2,
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrGetSpaceBoundingBox2DFB(
            XrSession session,
            XrSpace space,
            out XrRect2Df boundingBox2DOutput);

        public delegate XrResult xrGetSpaceBoundingBox3DFB(
            XrSession session,
            XrSpace space,
            out XrRect3DfFB boundingBox3DOutput);

        public delegate XrResult xrGetSpaceSemanticLabelsFB(
            XrSession session,
            XrSpace space,
            ref XrSemanticLabelsFB semanticLabelsOutput);

        public delegate XrResult xrGetSpaceBoundary2DFB(
            XrSession session,
            XrSpace space,
            ref XrBoundary2DFB boundary2DOutput);

        public delegate XrResult xrGetSpaceRoomLayoutFB(
            XrSession session,
            XrSpace space,
            ref XrRoomLayoutFB roomLayoutOutput);
    }
#endregion

#region XR_FB_scene_capture
    public partial struct XrEventDataSceneCaptureCompleteFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000198001;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
    }

    public partial struct XrSceneCaptureRequestInfoFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000198050;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint RequestByteCount;
        public unsafe byte* Request;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrRequestSceneCaptureFB(
            XrSession session,
            in XrSceneCaptureRequestInfoFB info,
            out XrAsyncRequestIdFB requestId);
    }
#endregion

#region XR_META_spatial_entity_discovery
    public partial struct XrSystemSpaceDiscoveryPropertiesMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000247000;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrBool32 SupportsSpaceDiscovery;
    }

    public partial struct XrSpaceFilterBaseHeaderMETA
    {
        public XrStructureType Type;
        public unsafe void* Next;
    }

    public partial struct XrSpaceDiscoveryInfoMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000247001;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint FilterCount;
        public unsafe XrSpaceFilterBaseHeaderMETA** Filters;
    }

    public partial struct XrSpaceFilterUuidMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000247003;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint UuidCount;
        public unsafe Guid* Uuids;
    }

    public partial struct XrSpaceFilterComponentMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000247004;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrSpaceComponentTypeFB ComponentType;
    }

    public partial struct XrSpaceDiscoveryResultMETA
    {
        public XrSpace Space;
        public Guid Uuid;
    }

    public partial struct XrSpaceDiscoveryResultsMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000247006;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint ResultCapacityInput;
        public uint ResultCountOutput;
        public unsafe XrSpaceDiscoveryResultMETA* Results;
    }

    public partial struct XrEventDataSpaceDiscoveryResultsAvailableMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000247007;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
    }

    public partial struct XrEventDataSpaceDiscoveryCompleteMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000247008;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrDiscoverSpacesMETA(
            XrSession session,
            in XrSpaceDiscoveryInfoMETA info,
            out XrAsyncRequestIdFB requestId);

        public delegate XrResult xrRetrieveSpaceDiscoveryResultsMETA(
            XrSession session,
            XrAsyncRequestIdFB requestId,
            ref XrSpaceDiscoveryResultsMETA results);
    }
#endregion

#region XR_META_spatial_entity_persistence
    public partial struct XrSystemSpacePersistencePropertiesMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000259000;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrBool32 SupportsSpacePersistence;
    }

    public partial struct XrSpacesSaveInfoMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000259001;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint SpaceCount;
        public unsafe XrSpace* Spaces;
    }

    public partial struct XrEventDataSpacesSaveResultMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000259002;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
    }

    public partial struct XrSpacesEraseInfoMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000259003;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint SpaceCount;
        public unsafe XrSpace* Spaces;
        public uint UuidCount;
        public unsafe Guid* Uuids;
    }

    public partial struct XrEventDataSpacesEraseResultMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000259004;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrSaveSpacesMETA(
            XrSession session,
            in XrSpacesSaveInfoMETA info,
            out XrAsyncRequestIdFB requestId);

        public delegate XrResult xrEraseSpacesMETA(
            XrSession session,
            in XrSpacesEraseInfoMETA info,
            out XrAsyncRequestIdFB requestId);
    }
#endregion

#region XR_META_spatial_entity_sharing
    public partial struct XrSystemSpatialEntitySharingPropertiesMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000290000;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrBool32 SupportsSpatialEntitySharing;
    }

    public partial struct XrShareSpacesRecipientBaseHeaderMETA
    {
        public XrStructureType Type;
        public unsafe void* Next;
    }

    public partial struct XrShareSpacesInfoMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000290001;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint SpaceCount;
        public unsafe XrSpace* Spaces;
        public unsafe XrShareSpacesRecipientBaseHeaderMETA* RecipientInfo;
    }

    public partial struct XrEventDataShareSpacesCompleteMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000290002;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAsyncRequestIdFB RequestId;
        public XrResult Result;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrShareSpacesMETA(
            XrSession session,
            in XrShareSpacesInfoMETA info,
            out XrAsyncRequestIdFB requestId);
    }
#endregion

#region XR_META_spatial_entity_group_sharing
    public partial struct XrSystemSpatialEntityGroupSharingPropertiesMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000572100;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrBool32 SupportsSpatialEntityGroupSharing;
    }

    public partial struct XrShareSpacesRecipientGroupsMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000572000;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint GroupCount;
        public unsafe Guid* Groups;
    }

    public partial struct XrSpaceGroupUuidFilterInfoMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000572001;
        public XrStructureType Type;
        public unsafe void* Next;
        public Guid GroupUuid;
    }
#endregion

#region XR_META_spatial_entity_mesh
    public partial struct XrSpaceTriangleMeshGetInfoMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000269001;
        public XrStructureType Type;
        public unsafe void* Next;
    }

    public partial struct XrSpaceTriangleMeshMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000269002;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint VertexCapacityInput;
        public uint VertexCountOutput;
        public unsafe XrVector3f* Vertices;
        public uint IndexCapacityInput;
        public uint IndexCountOutput;
        public unsafe uint* Indices;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrGetSpaceTriangleMeshMETA(
            XrSpace space,
            in XrSpaceTriangleMeshGetInfoMETA getInfo,
            ref XrSpaceTriangleMeshMETA triangleMeshOutput);
    }
#endregion

#region XR_META_spatial_entity_semantic_label
    public enum XrSemanticLabelMETA
    {
        /// <summary>
        /// Unknown. This is a valid label that is used when the runtime is not able to classify the entity using another label, or when the application does not recognize the label.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Floor of a room or space.
        /// </summary>
        Floor = 1,

        /// <summary>
        /// Ceiling of a room or space.
        /// </summary>
        Ceiling = 2,

        /// <summary>
        /// Wall face of a room or space. Wall faces, along with invisible wall faces, are used to define the outer boundary of a room.
        /// </summary>
        WallFace = 3,

        /// <summary>
        /// Inner wall face, which is a wall face that exists inside a room and is not connected to the outer boundary of the room. For example, a pillar that exists at the center of a room _may_ be represented by using four inner wall faces.
        /// </summary>
        InnerWallFace = 4,

        /// <summary>
        /// Invisible wall face, which is used to conceptually separate a space (e.g., separate a living space from a kitchen space in an open floor plan house even though there is no real wall between the two spaces).
        /// </summary>
        InvisibleWallFace = 5,

        /// <summary>
        /// Door frame, which usually exists on a wall face.
        /// </summary>
        DoorFrame = 6,

        /// <summary>
        /// Window frame, which usually exists on a wall face.
        /// </summary>
        WindowFrame = 7,
    }
#endregion

#region XR_META_spatial_entity_room_mesh
    public partial struct XrRoomMeshFaceIndicesMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000553000;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint IndexCapacityInput;
        public uint IndexCountOutput;
        public unsafe uint* Indices;
    }

    public partial struct XrRoomMeshFaceMETA
    {
        public Guid Uuid;
        public Guid ParentUuid;
        public XrSemanticLabelMETA SemanticLabel;
    }

    public partial struct XrRoomMeshMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000553002;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint VertexCapacityInput;
        public uint VertexCountOutput;
        public unsafe XrVector3f* Vertices;
        public uint FaceCapacityInput;
        public uint FaceCountOutput;
        public unsafe XrRoomMeshFaceMETA* Faces;
    }

    public partial struct XrSpaceRoomMeshGetInfoMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000553001;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint RecognizedSemanticLabelCount;
        public unsafe XrSemanticLabelMETA* RecognizedSemanticLabels;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrGetSpaceRoomMeshFaceIndicesMETA(
            XrSpace space,
            in Guid faceUuid,
            ref XrRoomMeshFaceIndicesMETA roomMeshFaceIndicesOutput);

        public delegate XrResult xrGetSpaceRoomMeshMETA(
            XrSpace space,
            in XrSpaceRoomMeshGetInfoMETA getInfo,
            ref XrRoomMeshMETA roomMeshOutput);
    }
#endregion

#region XR_FB_haptic_amplitude_envelope
    public partial struct XrHapticAmplitudeEnvelopeVibrationFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000173001;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrDuration Duration;
        public uint AmplitudeCount;
        public unsafe float* Amplitudes;
    }
#endregion

#region XR_FB_haptic_pcm
    public partial struct XrHapticPcmVibrationFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000209001;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint BufferSize;
        public unsafe float* Buffer;
        public float SampleRate;
        public XrBool32 Append;
        public unsafe uint* SamplesConsumed;
    }

    public partial struct XrDevicePcmSampleRateStateFB
    {
        public const XrStructureType StructureType = (XrStructureType)1000209002;
        public XrStructureType Type;
        public unsafe void* Next;
        public float SampleRate;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrGetDeviceSampleRateFB(
            XrSession session,
            in XrHapticActionInfo hapticActionInfo,
            ref XrDevicePcmSampleRateStateFB deviceSampleRate);
    }
#endregion

#region XR_EXTX1_haptic_parametric
    public enum XrHapticParametricStreamFrameTypeEXTX1
    {
        None = 0,
        FirstFrame = 1,
        IntermediateFrame = 2,
        LastFrame = 3,
    }

    public partial struct XrHapticParametricPointEXTX1
    {
        public XrDuration Time;
        public float Value;
    }

    public partial struct XrHapticParametricPropertiesEXTX1
    {
        public const XrStructureType StructureType = (XrStructureType)1000775001;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrDuration IdealFrameSubmissionRate;
        public XrDuration MinimumFirstFrameDuration;
        public float MinFrequencyHz;
        public float MaxFrequencyHz;
    }

    public partial struct XrHapticParametricTransientEXTX1
    {
        public XrDuration Time;
        public float Amplitude;
        public float Frequency;
    }

    public partial struct XrHapticParametricVibrationEXTX1
    {
        public const XrStructureType StructureType = (XrStructureType)1000775000;
        public XrStructureType Type;
        public unsafe void* Next;
        public uint AmplitudePointCount;
        public unsafe XrHapticParametricPointEXTX1* AmplitudePoints;
        public uint FrequencyPointCount;
        public unsafe XrHapticParametricPointEXTX1* FrequencyPoints;
        public uint TransientCount;
        public unsafe XrHapticParametricTransientEXTX1* Transients;
        public float MinFrequencyHz;
        public float MaxFrequencyHz;
        public XrHapticParametricStreamFrameTypeEXTX1 StreamFrameType;
    }

    public partial struct XrSystemHapticParametricPropertiesEXTX1
    {
        public const XrStructureType StructureType = (XrStructureType)1000775002;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrBool32 SupportsParametricHaptics;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrHapticParametricGetPropertiesEXTX1(
            XrSession session,
            in XrHapticActionInfo hapticActionInfo,
            ref XrHapticParametricPropertiesEXTX1 parametricProperties);
    }
#endregion

#region XR_META_simultaneous_hands_and_controllers
    public partial struct XrSystemSimultaneousHandsAndControllersPropertiesMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000532001;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrBool32 SupportsSimultaneousHandsAndControllers;
    }

    public partial struct XrSimultaneousHandsAndControllersTrackingResumeInfoMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000532002;
        public XrStructureType Type;
        public unsafe void* Next;
    }

    public partial struct XrSimultaneousHandsAndControllersTrackingPauseInfoMETA
    {
        public const XrStructureType StructureType = (XrStructureType)1000532003;
        public XrStructureType Type;
        public unsafe void* Next;
    }

    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrResumeSimultaneousHandsAndControllersTrackingMETA(
            XrSession session,
            in XrSimultaneousHandsAndControllersTrackingResumeInfoMETA resumeInfo);

        public delegate XrResult xrPauseSimultaneousHandsAndControllersTrackingMETA(
            XrSession session,
            in XrSimultaneousHandsAndControllersTrackingPauseInfoMETA pauseInfo);
    }
#endregion

#region Core Spec
    partial class OpenXRNativeFuncs
    {
        public delegate XrResult xrDestroySpace(
            XrSpace space);
    }
#endregion

#region Haptics
    public partial struct XrHapticActionInfo
    {
        public const XrStructureType StructureType = (XrStructureType)59;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrAction Action;
        public XrPath SubactionPath;
    }

    public partial struct XrHapticBaseHeader
    {
        public XrStructureType Type;
        public unsafe void* Next;
    }

    public partial struct XrHapticVibration
    {
        public const XrStructureType StructureType = (XrStructureType)13;
        public XrStructureType Type;
        public unsafe void* Next;
        public XrDuration Duration;
        public float Frequency;
        public float Amplitude;
    }

    partial class OpenXRNativeFuncs
    {
        public unsafe delegate XrResult xrApplyHapticFeedback(
            XrSession session,
            in XrHapticActionInfo hapticActionInfo,
            XrHapticBaseHeader* hapticFeedback);
    }
#endregion
}
