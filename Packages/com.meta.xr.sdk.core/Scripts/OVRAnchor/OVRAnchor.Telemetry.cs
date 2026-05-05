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
using System.Collections.Concurrent;
using System.Collections.Generic;

partial struct OVRAnchor
{
    // This utility helps with the asynchronous nature of a marker which starts with one API call and completes when an
    // event is received, probably on a future frame.
    private
    static class Telemetry
    {
        // requestIds are not necessarily unique across separate APIs, so we need to construct a unique key
        // to map (eventName, requestId) pairs to a particular UnifiedEventData
        private readonly struct Key : IEquatable<Key>
        {
            private readonly string _eventName;
            private readonly ulong _requestId;
            public Key(string eventName, ulong requestId) => (_eventName, _requestId) = (eventName, requestId);

            public bool Equals(Key other) => _eventName == other._eventName && _requestId == other._requestId;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode() => unchecked((_eventName?.GetHashCode() ?? 0) * 486187739 + _requestId.GetHashCode());
        }

        private static ConcurrentDictionary<Key, OVRPlugin.UnifiedEventData> s_events = new();

        // Called from OVRAnchor.Init
        public static void OnInit()
        {
            s_events.Clear();
        }

        public static void AddEvent(ulong requestId, OVRPlugin.UnifiedEventData unifiedEvent)
        {
            if (unifiedEvent.eventName != null)
            {
                s_events.TryAdd(new(unifiedEvent.eventName, requestId), unifiedEvent);
            }
        }

        public static OVRPlugin.UnifiedEventData Start(string eventName, ulong requestId, OVRPlugin.Result result)
        {
            var unifiedEvent = !string.IsNullOrEmpty(eventName)
                ? new OVRPlugin.UnifiedEventData(eventName)
                {
                    isEssential = OVRPlugin.Bool.False,
                    productType = OVRPlugin.ProductType.Editor
                }
                : default;
            SetSyncResult(requestId, result, unifiedEvent);
            return unifiedEvent;
        }

        // Set the result of the synchronous function call (the one that initiates the async request).
        // If successful, the event is stored in a map so that we can mark it complete later.
        // If result indicates failure, then the event is completed immediately.
        public static void SetSyncResult(ulong requestId, OVRPlugin.Result result, OVRPlugin.UnifiedEventData unifiedEvent)
        {
            if (unifiedEvent.eventName != null)
            {
                unifiedEvent.SetMetadata(Annotation.SynchronousResult, (int)result);
            }

            if (result.IsSuccess())
            {
                if (requestId == 0)
                {
                    throw new ArgumentException($"{nameof(requestId)} must not be zero if the {nameof(OVRPlugin)} " +
                                             $"method returns a successful result.", nameof(requestId));
                }

                if (unifiedEvent.eventName != null)
                {
                    s_events.TryAdd(new(unifiedEvent.eventName, requestId), unifiedEvent);
                }
            }
            else
            {
                if (unifiedEvent.eventName != null)
                {
                    unifiedEvent.result = OVRPlugin.UnifiedEventResult.FAIL;
                    unifiedEvent.Send();
                }
            }
        }

        // Sets the asynchronous result (usually received in the OpenXR event queue) and ends the event.
        public static void SetAsyncResultAndSend(string eventName, ulong requestId, long result)
        {
            var unifiedEvent = SetAsyncResult(eventName, requestId, result);
            if (unifiedEvent.eventName != null)
            {
                unifiedEvent.Send();
            }
        }

        // Sets the asynchronous result with additional metadata and ends the event.
        // More memory-efficient than retrieving/modifying/storing the event separately - avoids dictionary re-insertion.
        public static void SetAsyncResultAndSend(string eventName, ulong requestId, long result,
            string additionalAnnotation, int additionalValue)
        {
            var unifiedEvent = SetAsyncResult(eventName, requestId, result);
            if (unifiedEvent.eventName != null)
            {
                unifiedEvent.SetMetadata(additionalAnnotation, additionalValue);
                unifiedEvent.Send();
            }
        }

        // Sets the asynchronous result (usually received in the OpenXR event queue) but does not end the event.
        public static OVRPlugin.UnifiedEventData SetAsyncResult(string eventName, ulong requestId, long result)
        {
            var key = new Key(eventName, requestId);

            if (!s_events.Remove(key, out var unifiedEvent))
                return default;

            unifiedEvent.SetMetadata(Annotation.AsynchronousResult, (int)result);
            unifiedEvent.result = result >= 0 ? OVRPlugin.UnifiedEventResult.SUCCESS : OVRPlugin.UnifiedEventResult.FAIL;

            return unifiedEvent;
        }

        public static OVRPlugin.UnifiedEventData? GetEvent(string eventName, ulong requestId)
            => TryGetEvent(eventName, requestId, out var unifiedEvent) ? unifiedEvent : null;

        public static bool TryGetEvent(string eventName, ulong requestId, out OVRPlugin.UnifiedEventData unifiedEvent)
            => s_events.TryGetValue(new(eventName, requestId), out unifiedEvent);

        public static bool Remove(string eventName, ulong requestId, out OVRPlugin.UnifiedEventData unifiedEvent)
        {
            return s_events.Remove(new(eventName, requestId), out unifiedEvent);
        }

        public static OVRPlugin.UnifiedEventData? GetRemove(string eventName, ulong requestId)
            => Remove(eventName, requestId, out var unifiedEvent) ? unifiedEvent : null;

        // Deprecated: MarkerId enum is kept for backward compatibility but is no longer used.
        // Use EventName constants instead.
        internal enum MarkerId
        {
            // XR_META_spatial_entity_discovery
            DiscoverSpaces = 163054959,
            // XR_META_spatial_entity_persistence
            SaveSpaces = 163056974,
            EraseSpaces = 163061838,
            // XR_FB_spatial_entity_query
            QuerySpaces = 163069062,
            // XR_FB_spatial_entity_storage_batch
            SaveSpaceList = 163065048,
            // XR_FB_spatial_entity_storage
            EraseSingleSpace = 163062284,
            // XR_META_dynamic_object_tracker
            ConfigureTracker = 163068237,
        }

        // Falco event names corresponding to MarkerId values
        internal static class EventName
        {
            // XR_META_spatial_entity_discovery
            public const string DiscoverSpaces = "DISCOVER_SPACES";
            // XR_META_spatial_entity_persistence
            public const string SaveSpaces = "SAVE_SPACES";
            public const string EraseSpaces = "ERASE_SPACES";
            // XR_FB_spatial_entity_query
            public const string QuerySpaces = "QUERY_SPACES";
            // XR_FB_spatial_entity_storage_batch
            public const string SaveSpaceList = "SAVE_SPACE_LIST";
            // XR_FB_spatial_entity_storage
            public const string EraseSingleSpace = "ERASE_SPACE";
            // XR_META_dynamic_object_tracker
            public const string ConfigureTracker = "OVRANCHOR_CONFIGURE_TRACKER";
        }

        internal static class Annotation
        {
            public const string ComponentTypes = "component_types";
            public const string UuidCount = "uuid_count";
            public const string SpaceCount = "space_count";
            public const string TotalFilterCount = "total_filter_count";
            public const string ResultsCount = "results_count";
            public const string SynchronousResult = "sync_result";
            public const string AsynchronousResult = "async_result";
            public const string StorageLocation = "storage_location";
            public const string Timeout = "timeout";
            public const string MaxResults = "max_results";
            public const string GroupCount = "group_count";
            public const string DynamicObjectClasses = "dynamic_object_classes";
            public const string MarkerTypes = "marker_types";
        }
    }
}
