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
using System.Runtime.InteropServices;
using Unity.Collections;
using static OVRPlugin;

public partial struct OVRAnchor
{
    /// <summary>
    /// Options for <see cref="FetchAnchorsAsync"/>
    /// </summary>
    public struct FetchOptions
    {
        /// <summary>
        /// A UUID of an existing anchor to fetch.
        /// </summary>
        /// <remarks>
        /// Set this to fetch a single anchor with by UUID. If you want to fetch multiple anchors by UUID, use
        /// <see cref="Uuids"/>.
        /// </remarks>
        public Guid? SingleUuid;

        /// <summary>
        /// A collection of UUIDS to fetch.
        /// </summary>
        /// <remarks>
        /// If you want to retrieve only a single UUID, you can <see cref="SingleUuid"/> to avoid having to create
        /// a temporary container of length one.
        ///
        /// NOTE: Only the first 50 anchors are processed by
        /// <see cref="OVRAnchor.FetchAnchorsAsync(System.Collections.Generic.List{OVRAnchor},OVRAnchor.FetchOptions,System.Action{System.Collections.Generic.List{OVRAnchor},int})"/>
        /// </remarks>
        public IEnumerable<Guid> Uuids;

        /// <summary>
        /// Fetch anchors that support a given component type.
        /// </summary>
        /// <remarks>
        /// Each anchor supports one or more anchor types (types that implemented <see cref="IOVRAnchorComponent{T}"/>).
        ///
        /// If not null, <see cref="SingleComponentType"/> must be a type that implements
        /// <see cref="IOVRAnchorComponent{T}"/>, e.g., <see cref="OVRBounded2D"/> or <see cref="OVRRoomLayout"/>.
        ///
        /// If you have multiple component types, use <see cref="ComponentTypes"/> instead.
        /// </remarks>
        public Type SingleComponentType;

        /// <summary>
        /// Fetch anchors that support a given set of component types.
        /// </summary>
        /// <remarks>
        /// Each anchor supports one or more anchor types (types that implemented <see cref="IOVRAnchorComponent{T}"/>).
        ///
        /// If not null, <see cref="ComponentTypes"/> must be a collection of types that implement
        /// <see cref="IOVRAnchorComponent{T}"/>, e.g., <see cref="OVRBounded2D"/> or <see cref="OVRRoomLayout"/>.
        ///
        /// When multiple components are specified, all anchors that support any of those types are returned, i.e.,
        /// the component types are OR'd together to determine whether an anchor matches.
        ///
        /// If you only have a single component type, you can use <see cref="SingleComponentType"/> to avoid having
        /// to create a temporary container of length one.
        /// </remarks>
        public IEnumerable<Type> ComponentTypes;


        internal unsafe Result DiscoverSpaces(out ulong requestId)
        {
            SpaceComponentType GetSpaceComponentType(Type type)
            {
                if (type == null)
                    throw new ArgumentNullException(nameof(type));

                if (!_typeMap.TryGetValue(type, out var componentType))
                    throw new ArgumentException(
                        $"{type.FullName} is not a supported anchor component type (IOVRAnchorComponent).", nameof(type));

                return componentType;
            }

            var telemetryMarker = OVRTelemetry.Start((int)Telemetry.MarkerId.DiscoverSpaces);

            // Stores the filters
            using var filterStorage = new OVRNativeList<FilterUnion>(Allocator.Temp);

            // Pointers to the filters in filterStorage
            using var filters = new OVRNativeList<IntPtr>(Allocator.Temp);

            using var spaceComponentTypes = OVRNativeList.WithSuggestedCapacityFrom(ComponentTypes).AllocateEmpty<long>(Allocator.Temp);
            if (SingleComponentType != null)
            {
                var spaceComponentType = GetSpaceComponentType(SingleComponentType);
                spaceComponentTypes.Add((long)spaceComponentType);

                filterStorage.Add(new FilterUnion
                {
                    ComponentFilter = new SpaceDiscoveryFilterInfoComponents
                    {
                        Type = SpaceDiscoveryFilterType.Component,
                        Component = spaceComponentType,
                    }
                });
            }

            foreach (var componentType in ComponentTypes.ToNonAlloc())
            {
                var spaceComponentType = GetSpaceComponentType(componentType);
                spaceComponentTypes.Add((long)spaceComponentType);

                filterStorage.Add(new FilterUnion
                {
                    ComponentFilter = new SpaceDiscoveryFilterInfoComponents
                    {
                        Type = SpaceDiscoveryFilterType.Component,
                        Component = spaceComponentType,
                    }
                });
            }

            telemetryMarker.AddAnnotation(Telemetry.Annotation.ComponentTypes, spaceComponentTypes.Data,
                spaceComponentTypes.Count);

            using var uuids = Uuids.ToNativeList(Allocator.Temp);
            if (SingleUuid.HasValue)
            {
                uuids.Add(SingleUuid.Value);
            }

            if (SingleUuid != null || Uuids != null)
            {
                filterStorage.Add(new FilterUnion
                {
                    IdFilter = new SpaceDiscoveryFilterInfoIds
                    {
                        Type = SpaceDiscoveryFilterType.Ids,
                        Ids = uuids.Data,
                        NumIds = uuids.Count,
                    }
                });
            }

            telemetryMarker.AddAnnotation(Telemetry.Annotation.UuidCount, uuids.Count);


            // Gather pointers to each filter
            for (var i = 0; i < filterStorage.Count; i++)
            {
                filters.Add(new IntPtr(filterStorage.PtrToElementAt(i)));
            }

            telemetryMarker.AddAnnotation(Telemetry.Annotation.TotalFilterCount, filters.Count);

            var result = OVRPlugin.DiscoverSpaces(new SpaceDiscoveryInfo
            {
                NumFilters = (uint)filters.Count,
                Filters = (SpaceDiscoveryFilterInfoHeader**)filters.Data,
            }, out requestId);

            Telemetry.SetSyncResult(telemetryMarker, requestId, result);
            return result;
        }
    }

    internal static readonly Dictionary<Type, SpaceComponentType> _typeMap = new()
    {
        { typeof(OVRLocatable), SpaceComponentType.Locatable },
        { typeof(OVRStorable), SpaceComponentType.Storable },
        { typeof(OVRSharable), SpaceComponentType.Sharable },
        { typeof(OVRBounded2D), SpaceComponentType.Bounded2D },
        { typeof(OVRBounded3D), SpaceComponentType.Bounded3D },
        { typeof(OVRSemanticLabels), SpaceComponentType.SemanticLabels },
        { typeof(OVRRoomLayout), SpaceComponentType.RoomLayout },
        { typeof(OVRAnchorContainer), SpaceComponentType.SpaceContainer },
        { typeof(OVRTriangleMesh), SpaceComponentType.TriangleMesh },
    };

    [StructLayout(LayoutKind.Explicit)]
    internal struct FilterUnion
    {
        [FieldOffset(0)] public SpaceDiscoveryFilterType Type;
        [FieldOffset(0)] public SpaceDiscoveryFilterInfoComponents ComponentFilter;
        [FieldOffset(0)] public SpaceDiscoveryFilterInfoIds IdFilter;
    }
}
