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
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Meta.XR
{
    partial class MetaXRFeature
    {
        internal unsafe OVRPlugin.Result ovrp_ShareSpaces(ulong* spaces, uint numSpaces, ulong* userHandles,
            uint numUsers, out ulong requestId)
        {
            requestId = 0;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrShareSpacesFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            var result = Command.xrShareSpacesFB(Session, new XrSpaceShareInfoFB
            {
                Type = XrSpaceShareInfoFB.StructureType,
                SpaceCount = numSpaces,
                Spaces = (XrSpace*)spaces,
                UserCount = numUsers,
                Users = (XrSpaceUserFB*)userHandles,
            }, out var xrRequestId)
                .OrLogFormat(LogPrefix + nameof(Command.xrShareSpacesFB));

            requestId = (ulong)xrRequestId;
            return result.ToOVRPluginType();
        }

        internal unsafe OVRPlugin.Result ovrp_ShareSpaces2(in OVRPlugin.ShareSpacesInfo info, out ulong requestId)
        {
            requestId = 0;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrShareSpacesMETA == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            var xrInfo = new XrShareSpacesInfoMETA
            {
                Type = XrShareSpacesInfoMETA.StructureType,
                SpaceCount = info.SpaceCount,
                Spaces = (XrSpace*)info.Spaces,
            };

            var groupRecipientInfo = new XrShareSpacesRecipientGroupsMETA
            {
                Type = XrShareSpacesRecipientGroupsMETA.StructureType,
            };

            switch (info.RecipientType)
            {
                case OVRPlugin.ShareSpacesRecipientType.Group:
                {
                    var recipientInfo = (OVRPlugin.ShareSpacesGroupRecipientInfo*)info.RecipientInfo;
                    groupRecipientInfo.GroupCount = recipientInfo->GroupCount;
                    groupRecipientInfo.Groups = recipientInfo->GroupUuids;
                    xrInfo.RecipientInfo = (XrShareSpacesRecipientBaseHeaderMETA*)&groupRecipientInfo;
                    break;
                }
            }

            var result = Command.xrShareSpacesMETA(Session, xrInfo, out var xrRequestId)
                .OrLogFormat(LogPrefix + nameof(Command.xrShareSpacesMETA));

            requestId = (ulong)xrRequestId;
            return result.ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_GetSpaceUserId(in ulong spaceUserHandle, out ulong spaceUserId)
        {
            spaceUserId = 0;
            if (Command.xrGetSpaceUserIdFB == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            var result = Command.xrGetSpaceUserIdFB((XrSpaceUserFB)spaceUserHandle, out var xrId)
                .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceUserIdFB));

            spaceUserId = (ulong)xrId;
            return result.ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_CreateSpaceUser(in ulong spaceUserId, out ulong spaceUserHandle)
        {
            spaceUserHandle = 0;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrCreateSpaceUserFB == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            var result = Command.xrCreateSpaceUserFB(Session, new XrSpaceUserCreateInfoFB
            {
                Type = XrSpaceUserCreateInfoFB.StructureType,
                UserId = (XrSpaceUserIdFB)spaceUserId,
            }, out var xrSpaceUser)
                .OrLogFormat(LogPrefix + nameof(Command.xrCreateSpaceUserFB));

            spaceUserHandle = (ulong)xrSpaceUser;
            return result.ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_DestroySpaceUser(in ulong userHandle)
        {
            if (Command.xrDestroySpaceUserFB == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            return Command.xrDestroySpaceUserFB((XrSpaceUserFB)userHandle)
                .OrLogFormat(LogPrefix + nameof(Command.xrDestroySpaceUserFB))
                .ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_CreateSpatialAnchor(ref OVRPlugin.SpatialAnchorCreateInfo createInfo,
            out ulong requestId)
        {
            requestId = 0;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrCreateSpatialAnchorFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            var result = Command.xrCreateSpatialAnchorFB(Session, new()
                {
                    Type = XrSpatialAnchorCreateInfoFB.StructureType,
                    Space = (XrSpace)OVRPlugin.GetAppSpace(),
                    PoseInSpace = createInfo.PoseInSpace.ToXrPosef(),
                    Time = XrTime.FromSeconds(createInfo.Time),
                }, out var asyncRequestId)
                .OrLogFormat(LogPrefix + nameof(Command.xrCreateSpatialAnchorFB));

            requestId = (ulong)asyncRequestId;
            return result.ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_GetSpaceUuid(in ulong space, out Guid uuid)
        {
            uuid = Guid.Empty;
            if (Command.xrGetSpaceUuidFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            return Command.xrGetSpaceUuidFB((XrSpace)space, out uuid)
                .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceUuidFB))
                .ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_QuerySpaces(ref OVRPlugin.SpaceQueryInfo queryInfo, out ulong requestId)
        {
            var queryInfo2 = new OVRPlugin.SpaceQueryInfo2
            {
                QueryType = queryInfo.QueryType,
                MaxQuerySpaces = queryInfo.MaxQuerySpaces,
                Timeout = queryInfo.Timeout,
                Location = queryInfo.Location,
                ActionType = queryInfo.ActionType,
                FilterType = queryInfo.FilterType,
                IdInfo = queryInfo.IdInfo,
                ComponentsInfo = queryInfo.ComponentsInfo,
            };

            return ovrp_QuerySpaces2(ref queryInfo2, out requestId);
        }

        internal unsafe OVRPlugin.Result ovrp_QuerySpaces2(ref OVRPlugin.SpaceQueryInfo2 queryInfo2, out ulong requestId)
        {
            requestId = 0;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrQuerySpacesFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            var locationFilter = new XrSpaceStorageLocationFilterInfoFB
            {
                Type = XrSpaceStorageLocationFilterInfoFB.StructureType,
                Location = queryInfo2.Location.ToXrSpaceStorageLocationFB(),
            };

            var componentFilter = new XrSpaceComponentFilterInfoFB
            {
                Type = XrSpaceComponentFilterInfoFB.StructureType,
            };

            var groupFilter = new XrSpaceGroupUuidFilterInfoMETA
            {
                Type = XrSpaceGroupUuidFilterInfoMETA.StructureType,
            };

            var queryInfo = new XrSpaceQueryInfoFB
            {
                Type = XrSpaceQueryInfoFB.StructureType,
                QueryAction = queryInfo2.ActionType.ToXrSpaceQueryActionFB(),
                MaxResultCount = (uint)queryInfo2.MaxQuerySpaces,
                Timeout = XrDuration.FromSeconds(queryInfo2.Timeout),
            };

            fixed (Guid* uuids = queryInfo2.IdInfo.Ids)
            {
                var uuidFilter = new XrSpaceUuidFilterInfoFB
                {
                    Type = XrSpaceUuidFilterInfoFB.StructureType,
                    UuidCount = (uint)queryInfo2.IdInfo.NumIds,
                    Uuids = uuids,
                };

                switch (queryInfo2.FilterType)
                {
                    case OVRPlugin.SpaceQueryFilterType.None:
                        break;
                    case OVRPlugin.SpaceQueryFilterType.Ids:
                    {
                        if (_storageEnabled)
                        {
                            uuidFilter.Next = &locationFilter;
                        }

                        queryInfo.Filter = (XrSpaceFilterInfoBaseHeaderFB*)&uuidFilter;
                        break;
                    }
                    case OVRPlugin.SpaceQueryFilterType.Components:
                    {
                        if (queryInfo2.ComponentsInfo.NumComponents != 1)
                        {
                            Debug.LogError($"Query-by-component must provide exactly one component. You provided {queryInfo2.ComponentsInfo.NumComponents}.");
                            return OVRPlugin.Result.Failure_InvalidOperation;
                        }

                        componentFilter.ComponentType =
                            queryInfo2.ComponentsInfo.Components[0].ToXrSpaceComponentTypeFB();

                        if (_storageEnabled)
                        {
                            componentFilter.Next = &locationFilter;
                        }

                        queryInfo.Filter = (XrSpaceFilterInfoBaseHeaderFB*)&componentFilter;
                        break;
                    }
                    case OVRPlugin.SpaceQueryFilterType.Group:
                    {
                        groupFilter.GroupUuid = queryInfo2.GroupUuidInfo;
                        var filter = queryInfo.Filter = (XrSpaceFilterInfoBaseHeaderFB*)&groupFilter;

                        if (queryInfo2.IdInfo.NumIds > 0)
                        {
                            filter = Unsafe.Append(filter, &uuidFilter);
                        }

                        if (_storageEnabled)
                        {
                            filter = Unsafe.Append(filter, &locationFilter);
                        }

                        break;
                    }
                }

                var result = Command.xrQuerySpacesFB(Session, (XrSpaceQueryInfoBaseHeaderFB*)&queryInfo,
                        out var asyncRequestId)
                    .OrLogFormat(LogPrefix + nameof(Command.xrQuerySpacesFB));

                requestId = (ulong)asyncRequestId;
                return result.ToOVRPluginType();
            }
        }

        internal unsafe OVRPlugin.Result ovrp_RetrieveSpaceQueryResults(ref ulong requestId, uint resultCapacityInput,
            out uint resultCountOutput, IntPtr results)
        {
            resultCountOutput = 0;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrRetrieveSpaceQueryResultsFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            var queryResults = new XrSpaceQueryResultsFB
            {
                Type = XrSpaceQueryResultsFB.StructureType,
                ResultCapacityInput = resultCapacityInput,
                Results = (XrSpaceQueryResultFB*)results,
            };

            var result = Command.xrRetrieveSpaceQueryResultsFB(Session, (XrAsyncRequestIdFB)requestId, ref queryResults)
                .OrLogFormat(LogPrefix + nameof(Command.xrRetrieveSpaceQueryResultsFB));

            resultCountOutput = queryResults.ResultCountOutput;
            return result.ToOVRPluginType();
        }

        [StructLayout(LayoutKind.Explicit)]
        struct FilterUnion
        {
            [FieldOffset(0)] public XrSpaceFilterUuidMETA UuidFilter;
            [FieldOffset(0)] public XrSpaceFilterComponentMETA ComponentFilter;
        }

        internal OVRPlugin.Result ovrp_DiscoverSpaces(in OVRPlugin.SpaceDiscoveryInfo info, out ulong requestId)
        {
            requestId = 0;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrDiscoverSpacesMETA == null)
                return OVRPlugin.Result.Failure_Unsupported;

            unsafe
            {
                if (info.NumFilters > 0 && info.Filters == null)
                    throw new InvalidOperationException(
                        $"{nameof(info)}.{nameof(info.Filters)} must not be null when {nameof(info)}.{nameof(info.NumFilters)} is greater than zero.");

                using var filterStorage = new OVRNativeList<FilterUnion>((int)info.NumFilters, Allocator.Temp);
                for (uint i = 0; i < info.NumFilters; i++)
                {
                    switch (info.Filters[i]->Type)
                    {
                        case OVRPlugin.SpaceDiscoveryFilterType.Ids:
                        {
                            var filter = (OVRPlugin.SpaceDiscoveryFilterInfoIds*)info.Filters[i];
                            filterStorage.Add(new()
                            {
                                UuidFilter = new()
                                {
                                    Type = XrSpaceFilterUuidMETA.StructureType,
                                    UuidCount = (uint)filter->NumIds,
                                    Uuids = filter->Ids,
                                }
                            });
                            break;
                        }
                        case OVRPlugin.SpaceDiscoveryFilterType.Component:
                        {
                            var filter = (OVRPlugin.SpaceDiscoveryFilterInfoComponents*)info.Filters[i];
                            filterStorage.Add(new()
                            {
                                ComponentFilter = new()
                                {
                                    Type = XrSpaceFilterComponentMETA.StructureType,
                                    ComponentType = filter->Component.ToXrSpaceComponentTypeFB(),
                                }
                            });
                            break;
                        }
                        default:
                            Debug.LogWarning($"Unsupported filter type {info.Filters[i]->Type} ignored.");
                            break;
                    }
                }

                using var filterArray = new OVRNativeList<IntPtr>(filterStorage.Count, Allocator.Temp);
                for (var i = 0; i < filterStorage.Count; i++)
                {
                    filterArray.Add(new IntPtr(filterStorage.Data + i));
                }

                var result = Command.xrDiscoverSpacesMETA(Session, new XrSpaceDiscoveryInfoMETA
                    {
                        Type = XrSpaceDiscoveryInfoMETA.StructureType,
                        FilterCount = (uint)filterArray.Count,
                        Filters = (XrSpaceFilterBaseHeaderMETA**)filterArray.Data,
                    }, out var asyncRequestId)
                    .OrLogFormat(LogPrefix + nameof(Command.xrDiscoverSpacesMETA));

                requestId = (ulong)asyncRequestId;
                return result.ToOVRPluginType();
            }
        }

        internal unsafe OVRPlugin.Result ovrp_RetrieveSpaceDiscoveryResults(ulong requestId,
            ref OVRPlugin.SpaceDiscoveryResults results)
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrRetrieveSpaceDiscoveryResultsMETA == null)
                return OVRPlugin.Result.Failure_Unsupported;

            var discoveryResults = new XrSpaceDiscoveryResultsMETA
            {
                Type = XrSpaceDiscoveryResultsMETA.StructureType,
                ResultCapacityInput = results.ResultCapacityInput,
                Results = (XrSpaceDiscoveryResultMETA*)results.Results,
            };

            var xrResult =
                Command.xrRetrieveSpaceDiscoveryResultsMETA(Session, (XrAsyncRequestIdFB)requestId, ref discoveryResults)
                    .OrLogFormat(LogPrefix + nameof(Command.xrRetrieveSpaceDiscoveryResultsMETA));

            results.ResultCountOutput = discoveryResults.ResultCountOutput;
            return xrResult.ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_GetSpaceComponentStatus(ref ulong space, OVRPlugin.SpaceComponentType componentType,
            out OVRPlugin.Bool isEnabled, out OVRPlugin.Bool changePending)
        {
            isEnabled = changePending = OVRPlugin.Bool.False;

            if (Command.xrGetSpaceComponentStatusFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            var status = new XrSpaceComponentStatusFB
            {
                Type = XrSpaceComponentStatusFB.StructureType,
            };

            var result = Command.xrGetSpaceComponentStatusFB((XrSpace)space, (XrSpaceComponentTypeFB)componentType, ref status)
                .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceComponentStatusFB));

            isEnabled = status.Enabled;
            changePending = status.ChangePending;
            return result.ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_SetSpaceComponentStatus(ref ulong space, OVRPlugin.SpaceComponentType componentType,
            OVRPlugin.Bool enable, double timeout, out ulong requestId)
        {
            requestId = 0;

            if (Command.xrSetSpaceComponentStatusFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            var result = Command.xrSetSpaceComponentStatusFB((XrSpace)space, new()
                {
                    Type = XrSpaceComponentStatusSetInfoFB.StructureType,
                    ComponentType = (XrSpaceComponentTypeFB)componentType,
                    Enabled = enable,
                    Timeout = XrDuration.FromSeconds(timeout)
                }, out var asyncRequestId)
                .OrLogFormat(LogPrefix + nameof(Command.xrSetSpaceComponentStatusFB));

            requestId = (ulong)asyncRequestId;
            return result.ToOVRPluginType();
        }

        internal unsafe OVRPlugin.Result ovrp_EnumerateSpaceSupportedComponents(ref ulong space,
            uint componentTypesCapacityInput, out uint componentTypesCountOutput,
            OVRPlugin.SpaceComponentType[] componentTypes)
        {
            fixed (OVRPlugin.SpaceComponentType* buffer = componentTypes)
            {
                return ovrp_EnumerateSpaceSupportedComponents(ref space, componentTypesCapacityInput, out componentTypesCountOutput, buffer);
            }
        }

        internal unsafe OVRPlugin.Result ovrp_EnumerateSpaceSupportedComponents(ref ulong space, uint capacityInput,
            out uint countOutput,
            OVRPlugin.SpaceComponentType* buffer)
        {
            countOutput = 0;
            if (Command.xrEnumerateSpaceSupportedComponentsFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            return Command.xrEnumerateSpaceSupportedComponentsFB((XrSpace)space, capacityInput, out countOutput,
                    (XrSpaceComponentTypeFB*)buffer)
                .OrLogFormat(LogPrefix + nameof(Command.xrEnumerateSpaceSupportedComponentsFB))
                .ToOVRPluginType();
        }

        internal unsafe OVRPlugin.Result ovrp_SaveSpaces(uint spaceCount, ulong* spaces, out ulong requestId)
        {
            requestId = 0;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrSaveSpacesMETA == null)
                return OVRPlugin.Result.Failure_Unsupported;

            var result = Command.xrSaveSpacesMETA(Session, new XrSpacesSaveInfoMETA
                {
                    Type = XrSpacesSaveInfoMETA.StructureType,
                    SpaceCount = spaceCount,
                    Spaces = (XrSpace*)spaces,
                }, out var asyncRequestId)
                .OrLogFormat(LogPrefix + nameof(Command.xrSaveSpacesMETA));

            requestId = (ulong)asyncRequestId;
            return result.ToOVRPluginType();
        }

        internal unsafe OVRPlugin.Result ovrp_EraseSpaces(uint spaceCount, ulong* spaces, uint uuidCount, Guid* uuids,
            out ulong requestId)
        {
            requestId = 0;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrEraseSpacesMETA == null)
                return OVRPlugin.Result.Failure_Unsupported;

            var result = Command.xrEraseSpacesMETA(Session, new XrSpacesEraseInfoMETA
                {
                    Type = XrSpacesEraseInfoMETA.StructureType,
                    SpaceCount = spaceCount,
                    Spaces = (XrSpace*)spaces,
                    UuidCount = uuidCount,
                    Uuids = uuids,
                }, out var asyncRequestId)
                .OrLogFormat(LogPrefix + nameof(Command.xrEraseSpacesMETA));

            requestId = (ulong)asyncRequestId;
            return result.ToOVRPluginType();
        }

        internal unsafe OVRPlugin.Result ovrp_GetSpaceContainer(ref ulong space, ref OVRPlugin.SpaceContainerInternal containerUuids)
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrGetSpaceContainerFB == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            var container = new XrSpaceContainerFB
            {
                Type = XrSpaceContainerFB.StructureType,
                UuidCapacityInput = (uint)containerUuids.uuidCapacityInput,
                Uuids = (Guid*)containerUuids.uuids,
            };

            var result = Command.xrGetSpaceContainerFB(Session, (XrSpace)space, ref container)
                    .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceContainerFB));

            containerUuids.uuidCountOutput = (int)container.UuidCountOutput;
            return result.ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_GetSpaceBoundingBox2D(ref ulong space, out OVRPlugin.Rectf rect)
        {
            rect = default;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrGetSpaceBoundingBox2DFB == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            var result = Command.xrGetSpaceBoundingBox2DFB(Session, (XrSpace)space, out var boundingBox)
                .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceBoundingBox2DFB));

            rect = boundingBox.ToOVRPluginType();
            return result.ToOVRPluginType();
        }

        internal unsafe OVRPlugin.Result ovrp_GetSpaceBoundary2D(ref ulong space, ref OVRPlugin.PolygonalBoundary2DInternal boundary)
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrGetSpaceBoundary2DFB == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            var info = new XrBoundary2DFB
            {
                Type = XrBoundary2DFB.StructureType,
                VertexCapacityInput = (uint)boundary.vertexCapacityInput,
                Vertices = (XrVector2f*)boundary.vertices,
            };

            var result = Command.xrGetSpaceBoundary2DFB(Session, (XrSpace)space, ref info)
                .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceBoundary2DFB));
            boundary.vertexCountOutput = (int)info.VertexCountOutput;

            return result.ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_GetSpaceBoundingBox3D(ref ulong space, out OVRPlugin.Boundsf bounds)
        {
            bounds = default;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrGetSpaceBoundingBox3DFB == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            var result = Command.xrGetSpaceBoundingBox3DFB(Session, (XrSpace)space, out var boundingBox)
                .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceBoundingBox3DFB));

            bounds = boundingBox.ToOVRPluginType();
            return result.ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_GetSpaceSemanticLabels(ref ulong space, ref OVRPlugin.SpaceSemanticLabelInternal labels)
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrGetSpaceSemanticLabelsFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            unsafe
            {
                var xrLabels = new XrSemanticLabelsFB
                {
                    Type = XrSemanticLabelsFB.StructureType,
                    BufferCapacityInput = (uint)labels.byteCapacityInput,
                    Buffer = (byte*)labels.labels,
                };

                var result = Command.xrGetSpaceSemanticLabelsFB(Session, (XrSpace)space, ref xrLabels)
                    .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceSemanticLabelsFB));

                labels.byteCountOutput = (int)xrLabels.BufferCountOutput;
                return result.ToOVRPluginType();
            }
        }

        internal OVRPlugin.Result ovrp_GetSpaceRoomLayout(ref ulong space, ref OVRPlugin.RoomLayoutInternal roomLayout)
        {
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrGetSpaceRoomLayoutFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            unsafe
            {
                var xrRoomLayout = new XrRoomLayoutFB
                {
                    Type = XrRoomLayoutFB.StructureType,
                    WallUuidCapacityInput = (uint)roomLayout.wallUuidCapacityInput,
                    WallUuids = (Guid*)roomLayout.wallUuids
                };

                var result = Command.xrGetSpaceRoomLayoutFB(Session, (XrSpace)space, ref xrRoomLayout)
                    .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceRoomLayoutFB));

                roomLayout.ceilingUuid = xrRoomLayout.CeilingUuid;
                roomLayout.floorUuid = xrRoomLayout.FloorUuid;
                roomLayout.wallUuidCountOutput = (int)xrRoomLayout.WallUuidCountOutput;

                return result.ToOVRPluginType();
            }
        }

        internal unsafe OVRPlugin.Result ovrp_GetSpaceTriangleMesh(ref ulong space, ref OVRPlugin.TriangleMeshInternal mesh)
        {
            if (Command.xrGetSpaceTriangleMeshMETA == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            var xrTriangleMesh = new XrSpaceTriangleMeshMETA
            {
                Type = XrSpaceTriangleMeshMETA.StructureType,
                VertexCapacityInput = (uint)mesh.vertexCapacityInput,
                Vertices = (XrVector3f*)mesh.vertices,
                IndexCapacityInput = (uint)mesh.indexCapacityInput,
                Indices = (uint*)mesh.indices,
            };

            var result = Command.xrGetSpaceTriangleMeshMETA((XrSpace)space, new XrSpaceTriangleMeshGetInfoMETA
            {
                Type = XrSpaceTriangleMeshGetInfoMETA.StructureType,
            }, ref xrTriangleMesh)
                .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceTriangleMeshMETA));

            mesh.vertexCountOutput = (int)xrTriangleMesh.VertexCountOutput;
            mesh.indexCountOutput = (int)xrTriangleMesh.IndexCountOutput;

            return result.ToOVRPluginType();
        }

        internal unsafe OVRPlugin.Result ovrp_GetSpaceRoomMesh(ulong space,
            ref OVRPlugin.RoomMeshInternal roomMeshOutput)
        {
            if (Command.xrGetSpaceRoomMeshMETA == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            if (roomMeshOutput.faces == null && roomMeshOutput.faceCapacityInput > 0)
                return OVRPlugin.Result.Failure_InvalidParameter;

            Span<XrSemanticLabelMETA> recognizedSemanticLabels = stackalloc XrSemanticLabelMETA[]
            {
                XrSemanticLabelMETA.Unknown,
                XrSemanticLabelMETA.Floor,
                XrSemanticLabelMETA.Ceiling,
                XrSemanticLabelMETA.WallFace,
                XrSemanticLabelMETA.InnerWallFace,
                XrSemanticLabelMETA.InvisibleWallFace,
                XrSemanticLabelMETA.DoorFrame,
                XrSemanticLabelMETA.WindowFrame,
            };

            using var xrFaces = new NativeArray<XrRoomMeshFaceMETA>((int)roomMeshOutput.faceCapacityInput, Allocator.Temp);

            var xrRoomMesh = new XrRoomMeshMETA
            {
                Type = XrRoomMeshMETA.StructureType,
                VertexCapacityInput = roomMeshOutput.vertexCapacityInput,
                Vertices = (XrVector3f*)roomMeshOutput.vertices,
                FaceCapacityInput = roomMeshOutput.faceCapacityInput,
                Faces = (XrRoomMeshFaceMETA*)xrFaces.GetUnsafePtr(),
            };

            XrResult result;
            fixed (XrSemanticLabelMETA* labelsPtr = recognizedSemanticLabels)
            {
                result = Command.xrGetSpaceRoomMeshMETA((XrSpace)space, new XrSpaceRoomMeshGetInfoMETA
                    {
                        Type = XrSpaceRoomMeshGetInfoMETA.StructureType,
                        RecognizedSemanticLabelCount = (uint)recognizedSemanticLabels.Length,
                        RecognizedSemanticLabels = labelsPtr
                    }, ref xrRoomMesh)
                    .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceRoomMeshMETA));
            }

            roomMeshOutput.vertexCountOutput = xrRoomMesh.VertexCountOutput;
            roomMeshOutput.faceCountOutput = xrRoomMesh.FaceCountOutput;

            for (var i = 0; i < roomMeshOutput.faceCapacityInput; i++)
            {
                var face = xrFaces[i];
                roomMeshOutput.faces[i] = new OVRPlugin.RoomFace
                {
                    uuid = face.Uuid,
                    parentUuid = face.ParentUuid,
                    semanticLabel = face.SemanticLabel.ToOVRPluginType(),
                };
            }

            return result.ToOVRPluginType();
        }

        internal unsafe OVRPlugin.Result ovrp_GetSpaceRoomFaceIndices(ulong space, Guid faceUuid,
            ref OVRPlugin.RoomFaceIndicesInternal roomFaceIndicesOutput)
        {
            if (Command.xrGetSpaceRoomMeshFaceIndicesMETA == null)
                return OVRPlugin.Result.Failure_SpaceComponentNotSupported;

            var xrRoomFaceIndices = new XrRoomMeshFaceIndicesMETA
            {
                Type = XrRoomMeshFaceIndicesMETA.StructureType,
                IndexCapacityInput = roomFaceIndicesOutput.indexCapacityInput,
                Indices = roomFaceIndicesOutput.indices,
            };

            var result = Command.xrGetSpaceRoomMeshFaceIndicesMETA((XrSpace)space, faceUuid, ref xrRoomFaceIndices)
                .OrLogFormat(LogPrefix + nameof(Command.xrGetSpaceRoomMeshFaceIndicesMETA));

            roomFaceIndicesOutput.indexCountOutput = xrRoomFaceIndices.IndexCountOutput;
            return result.ToOVRPluginType();
        }

        internal OVRPlugin.Result ovrp_DestroySpace(ref ulong space)
        {
            if (Command.xrDestroySpace == null)
                return OVRPlugin.Result.Failure_Unsupported;

            return Command.xrDestroySpace((XrSpace)space)
                .OrLogFormat(LogPrefix + nameof(Command.xrDestroySpace))
                .ToOVRPluginType();
        }

        internal unsafe OVRPlugin.Result ovrp_RequestSceneCapture(ref OVRPlugin.SceneCaptureRequestInternal request,
            out ulong requestId)
        {
            requestId = 0;
            if (Session == 0)
                return OVRPlugin.Result.Failure_NotInitialized;

            if (Command.xrRequestSceneCaptureFB == null)
                return OVRPlugin.Result.Failure_Unsupported;

            var requestBytes = Marshal.StringToCoTaskMemUTF8(request.request);

            try
            {
                var result = Command.xrRequestSceneCaptureFB(Session, new XrSceneCaptureRequestInfoFB
                    {
                        Type = XrSceneCaptureRequestInfoFB.StructureType,
                        RequestByteCount = (uint)request.requestByteCount,
                        Request = (byte*)requestBytes,
                    }, out var xrRequestId)
                    .OrLogFormat(LogPrefix + nameof(Command.xrRequestSceneCaptureFB));

                requestId = (ulong)xrRequestId;
                return result.ToOVRPluginType();
            }
            finally
            {
                Marshal.FreeCoTaskMem(requestBytes);
            }
        }
    }

    partial class Extensions
    {
        public static XrSpaceStorageLocationFB ToXrSpaceStorageLocationFB(this OVRPlugin.SpaceStorageLocation value) => value switch
        {
            OVRPlugin.SpaceStorageLocation.Local => XrSpaceStorageLocationFB.Local,
            OVRPlugin.SpaceStorageLocation.Cloud => XrSpaceStorageLocationFB.Cloud,
            _ => XrSpaceStorageLocationFB.Invalid,
        };

        public static XrSpaceQueryActionFB ToXrSpaceQueryActionFB(this OVRPlugin.SpaceQueryActionType value)
            => (XrSpaceQueryActionFB)value;

        public static XrSpaceComponentTypeFB ToXrSpaceComponentTypeFB(this OVRPlugin.SpaceComponentType value)
            => (XrSpaceComponentTypeFB)value;

        public static OVRPlugin.SemanticLabel ToOVRPluginType(this XrSemanticLabelMETA value) => value switch
        {
            XrSemanticLabelMETA.Unknown => OVRPlugin.SemanticLabel.Unknown,
            XrSemanticLabelMETA.Floor => OVRPlugin.SemanticLabel.Floor,
            XrSemanticLabelMETA.Ceiling => OVRPlugin.SemanticLabel.Ceiling,
            XrSemanticLabelMETA.WallFace => OVRPlugin.SemanticLabel.WallFace,
            XrSemanticLabelMETA.InnerWallFace => OVRPlugin.SemanticLabel.InnerWallFace,
            XrSemanticLabelMETA.InvisibleWallFace => OVRPlugin.SemanticLabel.InvisibleWallFace,
            XrSemanticLabelMETA.DoorFrame => OVRPlugin.SemanticLabel.DoorFrame,
            XrSemanticLabelMETA.WindowFrame => OVRPlugin.SemanticLabel.WindowFrame,
            _ => OVRPlugin.SemanticLabel.Unknown,
        };

        public static XrSemanticLabelMETA ToXrSemanticLabelMETA(this OVRPlugin.SemanticLabel value) => value switch
        {
            OVRPlugin.SemanticLabel.Unknown => XrSemanticLabelMETA.Unknown,
            OVRPlugin.SemanticLabel.Floor => XrSemanticLabelMETA.Floor,
            OVRPlugin.SemanticLabel.Ceiling => XrSemanticLabelMETA.Ceiling,
            OVRPlugin.SemanticLabel.WallFace => XrSemanticLabelMETA.WallFace,
            OVRPlugin.SemanticLabel.InnerWallFace => XrSemanticLabelMETA.InnerWallFace,
            OVRPlugin.SemanticLabel.InvisibleWallFace => XrSemanticLabelMETA.InvisibleWallFace,
            OVRPlugin.SemanticLabel.DoorFrame => XrSemanticLabelMETA.DoorFrame,
            OVRPlugin.SemanticLabel.WindowFrame => XrSemanticLabelMETA.WindowFrame,
            _ => XrSemanticLabelMETA.Unknown,
        };
    }
}

#endif // USING_XR_SDK_OPENXR
