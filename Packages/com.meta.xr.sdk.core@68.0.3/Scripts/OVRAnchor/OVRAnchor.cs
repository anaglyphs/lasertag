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
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static OVRPlugin;
using TaskResult = OVRResult<System.Collections.Generic.List<OVRAnchor>, OVRAnchor.FetchResult>;

/// <summary>
/// Represents an anchor.
/// </summary>
/// <remarks>
/// Scenes anchors are uniquely identified with their <see cref="Uuid"/>.
/// <para>You may dispose of an anchor by calling their <see cref="Dispose"/> method.</para>
/// </remarks>
public readonly partial struct OVRAnchor : IEquatable<OVRAnchor>, IDisposable
{
    [OVRResultStatus]
    public enum SaveResult
    {
        /// <summary>
        /// The operation succeeded.
        /// </summary>
        Success = Result.Success,

        /// <summary>
        /// The operation failed.
        /// </summary>
        Failure = Result.Failure,

        /// <summary>
        /// At least one anchor is invalid.
        /// </summary>
        FailureInvalidAnchor = Result.Failure_HandleInvalid,

        /// <summary>
        /// Invalid data.
        /// </summary>
        FailureDataIsInvalid = Result.Failure_DataIsInvalid,

        /// <summary>
        /// Resource limitation prevented this operation from executing.
        /// </summary>
        /// <remarks>
        ///  Recommend retrying, perhaps after a short delay and/or reducing memory consumption.
        /// </remarks>
        FailureInsufficientResources = Result.Failure_SpaceInsufficientResources,

        /// <summary>
        /// Operation could not be completed until resources used are reduced or storage expanded.
        /// </summary>
        FailureStorageAtCapacity = Result.Failure_SpaceStorageAtCapacity,

        /// <summary>
        /// Insufficient view.
        /// </summary>
        /// <remarks>
        /// The user needs to look around the environment more for anchor tracking to function.
        /// </remarks>
        FailureInsufficientView = Result.Failure_SpaceInsufficientView,

        /// <summary>
        /// Insufficient permission.
        /// </summary>
        /// <remarks>
        /// Recommend confirming the status of the required permissions needed for using anchor APIs.
        /// </remarks>
        FailurePermissionInsufficient = Result.Failure_SpacePermissionInsufficient,

        /// <summary>
        /// Operation canceled due to rate limiting.
        /// </summary>
        /// <remarks>
        /// Recommend retrying after a short delay.
        /// </remarks>
        FailureRateLimited = Result.Failure_SpaceRateLimited,

        /// <summary>
        /// Too dark.
        /// </summary>
        /// <remarks>
        /// The environment is too dark to save the anchor.
        /// </remarks>
        FailureTooDark = Result.Failure_SpaceTooDark,

        /// <summary>
        /// Too bright.
        /// </summary>
        /// <remarks>
        /// The environment is too bright to save the anchor.
        /// </remarks>
        FailureTooBright = Result.Failure_SpaceTooBright,

        /// <summary>
        /// Save is not supported.
        /// </summary>
        FailureUnsupported = Result.Failure_Unsupported,

        /// <summary>
        /// Persistence not enabled
        /// </summary>
        /// <remarks>
        /// One or more anchors do not have the <see cref="OVRStorable"/> component enabled.
        /// </remarks>
        FailurePersistenceNotEnabled = Result.Failure_SpaceComponentNotEnabled,
    }

    [OVRResultStatus]
    public enum EraseResult
    {
        /// <summary>
        /// The operation succeeded.
        /// </summary>
        Success = Result.Success,

        /// <summary>
        /// The operation failed.
        /// </summary>
        Failure = Result.Failure,

        /// <summary>
        /// At least one anchor is invalid.
        /// </summary>
        FailureInvalidAnchor = Result.Failure_HandleInvalid,

        /// <summary>
        /// Invalid data.
        /// </summary>
        FailureDataIsInvalid = Result.Failure_DataIsInvalid,

        /// <summary>
        /// Resource limitation prevented this operation from executing.
        /// </summary>
        /// <remarks>
        ///  Recommend retrying, perhaps after a short delay and/or reducing memory consumption.
        /// </remarks>
        FailureInsufficientResources = Result.Failure_SpaceInsufficientResources,

        /// <summary>
        /// Insufficient permission.
        /// </summary>
        /// <remarks>
        /// Recommend confirming the status of the required permissions needed for using anchor APIs.
        /// </remarks>
        FailurePermissionInsufficient = Result.Failure_SpacePermissionInsufficient,

        /// <summary>
        /// Operation canceled due to rate limiting.
        /// </summary>
        /// <remarks>
        /// Recommend retrying after a short delay.
        /// </remarks>
        FailureRateLimited = Result.Failure_SpaceRateLimited,

        /// <summary>
        /// Erase is not supported.
        /// </summary>
        FailureUnsupported = Result.Failure_Unsupported,

        /// <summary>
        /// Persistence not enabled
        /// </summary>
        /// <remarks>
        /// One or more anchors do not have the <see cref="OVRStorable"/> component enabled.
        /// </remarks>
        FailurePersistenceNotEnabled = Result.Failure_SpaceComponentNotEnabled,
    }

    [OVRResultStatus]
    public enum FetchResult
    {
        /// <summary>
        /// The operation succeeded.
        /// </summary>
        Success = Result.Success,

        /// <summary>
        /// The operation failed.
        /// </summary>
        Failure = Result.Failure,

        /// <summary>
        /// Invalid data.
        /// </summary>
        FailureDataIsInvalid = Result.Failure_DataIsInvalid,

        /// <summary>
        /// One of the <see cref="FetchOptions"/> was invalid.
        /// </summary>
        /// <remarks>
        /// This can happen, for example, if you query for an invalid component type.
        /// </remarks>
        FailureInvalidOption = Result.Failure_InvalidParameter,

        /// <summary>
        /// Resource limitation prevented this operation from executing.
        /// </summary>
        /// <remarks>
        ///  Recommend retrying, perhaps after a short delay and/or reducing memory consumption.
        /// </remarks>
        FailureInsufficientResources = Result.Failure_SpaceInsufficientResources,

        /// <summary>
        /// Insufficient view.
        /// </summary>
        /// <remarks>
        /// The user needs to look around the environment more for anchor tracking to function.
        /// </remarks>
        FailureInsufficientView = Result.Failure_SpaceInsufficientView,

        /// <summary>
        /// Insufficient permission.
        /// </summary>
        /// <remarks>
        /// Recommend confirming the status of the required permissions needed for using anchor APIs.
        /// </remarks>
        FailurePermissionInsufficient = Result.Failure_SpacePermissionInsufficient,

        /// <summary>
        /// Operation canceled due to rate limiting.
        /// </summary>
        /// <remarks>
        /// Recommend retrying after a short delay.
        /// </remarks>
        FailureRateLimited = Result.Failure_SpaceRateLimited,

        /// <summary>
        /// Too dark.
        /// </summary>
        /// <remarks>
        /// The environment is too dark to load anchors.
        /// </remarks>
        FailureTooDark = Result.Failure_SpaceTooDark,

        /// <summary>
        /// Too bright.
        /// </summary>
        /// <remarks>
        /// The environment is too bright to load anchors.
        /// </remarks>
        FailureTooBright = Result.Failure_SpaceTooBright,

        /// <summary>
        /// Fetch is not supported.
        /// </summary>
        FailureUnsupported = Result.Failure_Unsupported,
    }

    [OVRResultStatus]
    public enum ShareResult
    {
        /// <summary>
        /// The operation succeeded.
        /// </summary>
        Success = Result.Success,

        /// <summary>
        /// The operation failed.
        /// </summary>
        Failure = Result.Failure,

        /// <summary>
        /// Invalid handle.
        /// </summary>
        FailureHandleInvalid = Result.Failure_HandleInvalid,

        /// <summary>
        /// Invalid data.
        /// </summary>
        FailureDataIsInvalid = Result.Failure_DataIsInvalid,

        /// <summary>
        /// A network timeout occurred.
        /// </summary>
        /// <remarks>
        /// Recommend ensuring network speed is sufficient.
        /// </remarks>
        FailureNetworkTimeout = Result.Failure_SpaceNetworkTimeout,

        /// <summary>
        /// Network request failed.
        /// </summary>
        /// <remarks>
        /// Recommend ensuring network connectivity.
        /// </remarks>
        FailureNetworkRequestFailed = Result.Failure_SpaceNetworkRequestFailed,

        /// <summary>
        /// The device has not built a sufficient map of the environment to share the anchor(s).
        /// </summary>
        /// <remarks>
        /// Recommend further exploration the space around the anchors to be shared.
        /// </remarks>
        FailureMappingInsufficient = Result.Failure_SpaceMappingInsufficient,

        /// <summary>
        /// The device was not able to localize the anchor(s) being shared.
        /// </summary>
        FailureLocalizationFailed = Result.Failure_SpaceLocalizationFailed,

        /// <summary>
        /// Sharable component not enabled on the anchor(s) being shared.
        /// </summary>
        /// <remarks>
        /// Make sure that the <see cref="OVRSharable"/> component has been added and enabled on this anchor before attempting to share.
        /// </remarks>
        FailureSharableComponentNotEnabled = Result.Failure_SpaceComponentNotEnabled,

        /// <summary>
        /// Sharing failed because device has not enabled the `Share Point Cloud Data` setting.
        /// </summary>
        /// <remarks>
        /// Recommend informing the end user that this permission must be set via the system settings.
        /// </remarks>
        FailureCloudStorageDisabled = Result.Failure_SpaceCloudStorageDisabled,

        /// <summary>
        /// Anchor Sharing is not supported.
        /// </summary>
        FailureUnsupported = Result.Failure_Unsupported,
    }

    #region Static

    public static readonly OVRAnchor Null = new OVRAnchor(0, Guid.Empty);

    // Called by OVRManager event loop
    internal static void OnSpaceDiscoveryComplete(OVRDeserialize.SpaceDiscoveryCompleteData data)
    {
        TaskResult result;
        var task = OVRTask.GetExisting<TaskResult>(data.RequestId);
        if (task.TryGetInternalData<FetchTaskData>(out var taskData))
        {
            Telemetry.GetMarker(Telemetry.MarkerId.DiscoverSpaces, data.RequestId)
                ?.AddAnnotation(Telemetry.Annotation.ResultsCount, taskData.Anchors?.Count ?? 0);
            result = OVRResult.From(taskData.Anchors, (FetchResult)data.Result);
        }
        else
        {
            Debug.LogError(
                $"SpaceDiscovery completed but its task does not have an associated anchor List. This is likely a bug. RequestId={data.RequestId}, Result={data.Result}");
            result = OVRResult.From((List<OVRAnchor>)null, (FetchResult)data.Result);
        }

        Telemetry.SetAsyncResultAndSend(Telemetry.MarkerId.DiscoverSpaces, data.RequestId, data.Result);

        task.SetResult(result);
    }

    // Called by OVRManager event loop
    internal static void OnSpaceDiscoveryResultsAvailable(OVRDeserialize.SpaceDiscoveryResultsData data)
    {
        var requestId = data.RequestId;

        // never calls task.SetResult() as that completes the task
        var task = OVRTask.GetExisting<TaskResult>(requestId);
        if (!task.IsPending) return;

        if (!task.TryGetInternalData<FetchTaskData>(out var taskData))
            return;

        NativeArray<SpaceDiscoveryResult> results = default;
        Result result;
        int count;

        unsafe
        {
            result = RetrieveSpaceDiscoveryResults(requestId, null, 0, out count);
            if (!result.IsSuccess()) return;

            do
            {
                if (results.IsCreated)
                {
                    results.Dispose();
                }

                results = new NativeArray<SpaceDiscoveryResult>(count, Allocator.Temp);
                result = RetrieveSpaceDiscoveryResults(requestId, (SpaceDiscoveryResult*)results.GetUnsafePtr(),
                    results.Length, out count);
            } while (result == Result.Failure_InsufficientSize);
        }

        var startingIndex = taskData.Anchors.Count;

        using (results)
        {
            if (!result.IsSuccess() || count == 0)
            {
                return;
            }

            // always add to anchors, as the results are consumed
            for (var i = 0; i < count; i++)
            {
                var item = results[i];
                taskData.Anchors.Add(new OVRAnchor(item.Space, item.Uuid));
            }
        }

        // notify potential subscribers to the incremental results
        taskData.IncrementalResultsCallback?.Invoke(taskData.Anchors, startingIndex);
    }

    struct FetchTaskData
    {
        public List<OVRAnchor> Anchors;
        public Action<List<OVRAnchor>, int> IncrementalResultsCallback;
    }

    /// <summary>
    /// Fetch anchors matching a query.
    /// </summary>
    /// <remarks>
    /// This method queries for anchors that match the corresponding <paramref name="options"/>. This method is
    /// asynchronous; use the returned <see cref="OVRTask{TResult}"/> to check for completion.
    ///
    /// Anchors may be returned in batches. If <paramref name="incrementalResultsCallback"/> is not `null`, then this
    /// delegate is invoked whenever results become available prior to the completion of the entire operation. New anchors
    /// are appended to <paramref name="anchors"/>. The delegate receives a reference to <paramref name="anchors"/> and
    /// the starting index of the anchors that have been added. The parameters are:
    /// - `anchors`: The same `List` provided by <paramref name="anchors"/>.
    /// - `index`: The starting index of the newly available anchors
    /// </remarks>
    /// <param name="anchors">Container to store the results. The list is cleared before adding any anchors.</param>
    /// <param name="options">Options describing which anchors to fetch.</param>
    /// <param name="incrementalResultsCallback">(Optional) A callback invoked when incremental results are available.</param>
    /// <returns>An <see cref="OVRTask{TResult}"/> that can be used to track the asynchronous fetch.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="anchors"/> is `null`.</exception>
    public static OVRTask<TaskResult> FetchAnchorsAsync(
        List<OVRAnchor> anchors, FetchOptions options, Action<List<OVRAnchor>, int> incrementalResultsCallback = null)
    {
        if (anchors == null)
            throw new ArgumentNullException(nameof(anchors));

        anchors.Clear();

        var result = options.DiscoverSpaces(out var requestId);
        if (!result.IsSuccess())
        {
            return OVRTask.FromResult(OVRResult.From(anchors, (FetchResult)result));
        }

        var task = OVRTask.FromRequest<TaskResult>(requestId);
        task.SetInternalData(new FetchTaskData
        {
            Anchors = anchors,
            IncrementalResultsCallback = incrementalResultsCallback,
        });

        return task;
    }

    /// <summary>
    /// Creates a new spatial anchor.
    /// </summary>
    /// <remarks>
    /// Spatial anchor creation is asynchronous. This method initiates a request to create a spatial anchor at
    /// <paramref name="trackingSpacePose"/>. The returned <see cref="OVRTask{TResult}"/> can be awaited or used to
    /// track the completion of the request.
    ///
    /// If spatial anchor creation fails, the resulting <see cref="OVRAnchor"/> will be <see cref="OVRAnchor.Null"/>.
    /// </remarks>
    /// <param name="trackingSpacePose">The pose, in tracking space, at which you wish to create the spatial anchor.</param>
    /// <returns>A task which can be used to track completion of the request.</returns>
    public static OVRTask<OVRAnchor> CreateSpatialAnchorAsync(Pose trackingSpacePose)
        => CreateSpatialAnchor(new SpatialAnchorCreateInfo
        {
            BaseTracking = GetTrackingOriginType(),
            PoseInSpace = new Posef
            {
                Orientation = trackingSpacePose.rotation.ToFlippedZQuatf(),
                Position = trackingSpacePose.position.ToFlippedZVector3f(),
            },
            Time = GetTimeInSeconds(),
        }, out var requestId)
            ? OVRTask.FromRequest<OVRAnchor>(requestId)
            : OVRTask.FromResult(Null);

    /// <summary>
    /// Creates a new spatial anchor.
    /// </summary>
    /// <remarks>
    /// Spatial anchor creation is asynchronous. This method initiates a request to create a spatial anchor at
    /// <paramref name="transform"/>. The returned <see cref="OVRTask{TResult}"/> can be awaited or used to
    /// track the completion of the request.
    ///
    /// If spatial anchor creation fails, the resulting <see cref="OVRAnchor"/> will be <see cref="OVRAnchor.Null"/>.
    /// </remarks>
    /// <param name="transform">The transform at which you wish to create the spatial anchor.</param>
    /// <param name="centerEyeCamera">The `Camera` associated with the Meta Quest's center eye.</param>
    /// <returns>A task which can be used to track completion of the request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transform"/> is `null`.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="centerEyeCamera"/> is `null`.</exception>
    public static OVRTask<OVRAnchor> CreateSpatialAnchorAsync(Transform transform, Camera centerEyeCamera)
    {
        if (transform == null)
            throw new ArgumentNullException(nameof(transform));

        if (centerEyeCamera == null)
            throw new ArgumentNullException(nameof(centerEyeCamera));

        var pose = transform.ToTrackingSpacePose(centerEyeCamera);
        return CreateSpatialAnchorAsync(new Pose
        {
            position = pose.position,
            rotation = pose.orientation,
        });
    }

    #endregion

    /// <summary>
    /// Save this anchor.
    /// </summary>
    /// <remarks>
    /// This method persists the anchor so that it may be retrieved later, e.g., by using
    /// <see cref="FetchAnchorsAsync(List{OVRAnchor},FetchOptions,Action{List{OVRAnchor},int})"/>.
    ///
    /// This operation is asynchronous. Use the returned <see cref="OVRTask{TResult}"/> to track the result of the
    /// asynchronous operation.
    ///
    /// NOTE: When saving multiple anchors, it is more efficient to save them in a batch using
    /// <see cref="SaveAsync(IEnumerable{OVRAnchor})"/>.
    /// </remarks>
    /// <returns>An awaitable <see cref="OVRTask{TResult}"/> representing the asynchronous request.</returns>
    /// <seealso cref="FetchAnchorsAsync(List{OVRAnchor},FetchOptions,Action{List{OVRAnchor},int})"/>
    /// <seealso cref="SaveAsync(IEnumerable{OVRAnchor})"/>
    /// <seealso cref="EraseAsync()"/>
    public OVRTask<OVRResult<SaveResult>> SaveAsync()
    {
        var handle = Handle;
        unsafe
        {
            return SaveSpacesAsync(new(&handle, 1));
        }
    }

    /// <summary>
    /// Save a collection of anchors.
    /// </summary>
    /// <remarks>
    /// This method persists up to 32 anchors so that they may be retrieved later.
    ///
    /// This operation is asynchronous. Use the returned <see cref="OVRTask{TResult}"/> to track the result of the
    /// asynchronous operation.
    /// </remarks>
    /// <param name="anchors">A collection of anchors to persist.</param>
    /// <returns>An awaitable <see cref="OVRTask{TResult}"/> representing the asynchronous request.</returns>
    /// <seealso cref="FetchAnchorsAsync(List{OVRAnchor},FetchOptions,Action{List{OVRAnchor},int})"/>
    /// <seealso cref="SaveAsync()"/>
    /// <seealso cref="EraseAsync(IEnumerable{OVRAnchor},IEnumerable{Guid})"/>
    /// <exception cref="ArgumentException">Thrown if <paramref name="anchors"/> contains more than 32 anchors.</exception>
    public static OVRTask<OVRResult<SaveResult>> SaveAsync(IEnumerable<OVRAnchor> anchors)
    {
        using var spaces = OVRNativeList.WithSuggestedCapacityFrom(anchors).AllocateEmpty<ulong>(Allocator.Temp);
        foreach (var anchor in anchors.ToNonAlloc())
        {
            spaces.Add(anchor.Handle);
        }

        if (spaces.Count > MaxPersistentAnchorBatchSize)
            throw new ArgumentException($"There must not be more than {MaxPersistentAnchorBatchSize} anchors at once ({spaces.Count} were provided).", nameof(anchors));

        if (spaces.Count == 0)
        {
            return OVRTask.FromResult(OVRResult.From(SaveResult.Success));
        }

        return SaveSpacesAsync(spaces);
    }

    internal static unsafe OVRTask<OVRResult<SaveResult>> SaveSpacesAsync(ReadOnlySpan<ulong> spaces)
    {
        if (spaces.Length > MaxPersistentAnchorBatchSize)
            throw new ArgumentOutOfRangeException(nameof(spaces), spaces.Length, $"Cannot save more than {MaxPersistentAnchorBatchSize} anchors at once ({spaces.Length} were provided).");

        var telemetryMarker = OVRTelemetry
            .Start((int)Telemetry.MarkerId.SaveSpaces)
            .AddAnnotation(Telemetry.Annotation.SpaceCount, (long)spaces.Length);

        fixed (ulong* ptr = spaces)
        {
            var result = SaveSpaces(ptr, spaces.Length, out var requestId);
            Telemetry.SetSyncResult(telemetryMarker, requestId, result);

            return result.IsSuccess()
                ? OVRTask.FromRequest<OVRResult<SaveResult>>(requestId)
                : OVRTask.FromResult(OVRResult.From((SaveResult)result));
        }
    }

    // Invoked by OVRManager event loop
    internal static void OnSaveSpacesResult(OVRDeserialize.SpacesSaveResultData eventData)
        => Telemetry.SetAsyncResultAndSend(Telemetry.MarkerId.SaveSpaces, eventData.RequestId, (long)eventData.Result);

    /// <summary>
    /// Erases this anchor.
    /// </summary>
    /// <remarks>
    /// This method removes the anchor from persistent storage. Note this does not destroy the current instance.
    ///
    /// This operation is asynchronous. Use the returned <see cref="OVRTask{TResult}"/> to track the result of the
    /// asynchronous operation.
    ///
    /// NOTE: When erasing multiple anchors, it is more efficient to erase them in a batch using
    /// <see cref="EraseAsync(IEnumerable{OVRAnchor},IEnumerable{Guid})"/>.
    /// </remarks>
    /// <returns>An awaitable <see cref="OVRTask{TResult}"/> representing the asynchronous request.</returns>
    /// <seealso cref="SaveAsync()"/>
    /// <seealso cref="EraseAsync(IEnumerable{OVRAnchor},IEnumerable{Guid})"/>
    public OVRTask<OVRResult<EraseResult>> EraseAsync()
    {
        var uuid = Uuid;
        unsafe
        {
            return EraseSpacesAsync(default, new(&uuid, 1));
        }
    }

    /// <summary>
    /// Erase a collection of anchors.
    /// </summary>
    /// <remarks>
    /// This method removes up to 32 anchors from persistent storage.
    ///
    /// This operation is asynchronous. Use the returned <see cref="OVRTask{TResult}"/> to track the result of the
    /// asynchronous operation.
    /// </remarks>
    /// <param name="anchors">(Optional) A collection of anchors to remove from persistent storage.</param>
    /// <param name="uuids">(Optional) A collection of uuids to remove from persistent storage.</param>
    /// <returns>An awaitable <see cref="OVRTask{TResult}"/> representing the asynchronous request.</returns>
    /// <exception cref="ArgumentException">Thrown if both <paramref name="anchors"/> and <paramref name="uuids"/> are `null`.</exception>
    /// <seealso cref="SaveAsync(IEnumerable{OVRAnchor})"/>
    /// <exception cref="ArgumentException">Thrown if the combined number of <paramref name="anchors"/> and
    /// <paramref name="uuids"/> is greater than 32.</exception>
    public static OVRTask<OVRResult<EraseResult>> EraseAsync(IEnumerable<OVRAnchor> anchors, IEnumerable<Guid> uuids)
    {
        if (anchors == null && uuids == null)
            throw new ArgumentException($"One of {nameof(anchors)} or {nameof(uuids)} must not be null.");

        using var spaces = OVRNativeList.WithSuggestedCapacityFrom(anchors).AllocateEmpty<ulong>(Allocator.Temp);
        foreach (var anchor in anchors.ToNonAlloc())
        {
            spaces.Add(anchor.Handle);
        }

        using var ids = uuids.ToNativeList(Allocator.Temp);

        if (spaces.Count + ids.Count > MaxPersistentAnchorBatchSize)
            throw new ArgumentException($"Cannot erase more than {MaxPersistentAnchorBatchSize} anchors at once ({spaces.Count + ids.Count} were provided).");

        if (spaces.Count == 0 && ids.Count == 0)
        {
            return OVRTask.FromResult(OVRResult.From(EraseResult.Success));
        }

        return EraseSpacesAsync(spaces, ids);
    }

    static unsafe OVRTask<OVRResult<EraseResult>> EraseSpacesAsync(ReadOnlySpan<ulong> spaces, ReadOnlySpan<Guid> uuids)
    {
        var telemetryMarker = OVRTelemetry
            .Start((int)Telemetry.MarkerId.EraseSpaces)
            .AddAnnotation(Telemetry.Annotation.SpaceCount, spaces.Length)
            .AddAnnotation(Telemetry.Annotation.UuidCount, uuids.Length);

        fixed (ulong* spacesPtr = spaces)
        fixed (Guid* uuidsPtr = uuids)
        {
            var result = EraseSpaces((uint)spaces.Length, spacesPtr, (uint)uuids.Length, uuidsPtr, out var requestId);
            Telemetry.SetSyncResult(telemetryMarker, requestId, result);

            return result.IsSuccess()
                ? OVRTask.FromRequest<OVRResult<EraseResult>>(requestId)
                : OVRTask.FromResult(OVRResult.From((EraseResult)result));
        }
    }

    internal static void OnEraseSpacesResult(OVRDeserialize.SpacesEraseResultData eventData)
        => Telemetry.SetAsyncResultAndSend(Telemetry.MarkerId.EraseSpaces, eventData.RequestId, (long)eventData.Result);

    /// <summary>
    /// Share this anchor with the specified users.
    /// </summary>
    /// <remarks>
    ///
    /// This method shares the anchor with a collection of <see cref="OVRSpaceUser"/>.
    ///
    /// This operation is asynchronous. Use the returned <see cref="OVRTask{TResult}"/> to track the result of the
    /// asynchronous operation.
    /// </remarks>
    /// <param name="users"> A collection of users with whom anchors will be shared.</param>
    /// <returns>An awaitable <see cref="OVRTask{TResult}"/> representing the asynchronous request.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="users"/> is `null`.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="users"/> count is less than one.</exception>
    /// <seealso cref="ShareAsync(IEnumerable{OVRAnchor})"/>
    public OVRTask<OVRResult<ShareResult>> ShareAsync(IEnumerable<OVRSpaceUser> users)
    {
        if (users == null)
            throw new ArgumentNullException(nameof(users));

        unsafe
        {
            using var userList = OVRNativeList.WithSuggestedCapacityFrom(users).AllocateEmpty<ulong>(Allocator.Temp);
            foreach (var user in users.ToNonAlloc())
            {
                userList.Add(user._handle);
            }

            if (userList.Count < 1)
                throw new ArgumentException($"{nameof(users)} must contain at least one user.");

            var handle = Handle;
            return ShareSpacesAsync(new(&handle, 1), userList);
        }
    }

    /// <summary>
    /// Share a collection of anchors with a collection of users.
    /// </summary>
    /// <remarks>
    ///
    /// This method shares a collection of anchors with a collection of users.
    ///
    /// This operation is asynchronous. Use the returned <see cref="OVRTask{TResult}"/> to track the result of the
    /// asynchronous operation.
    /// </remarks>
    /// <param name="anchors">A collection of anchors to share.</param>
    /// <param name="users"> A collection of users with whom anchors will be shared.</param>
    /// <returns>An awaitable <see cref="OVRTask{TResult}"/> representing the asynchronous request.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="anchors"/> or <paramref name="users"/> are `null`.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="users"/> count is less than one.</exception>
    /// <seealso cref="ShareAsync(IEnumerable{OVRSpaceUser})"/>
    public static OVRTask<OVRResult<ShareResult>> ShareAsync(
        IEnumerable<OVRAnchor> anchors,
        IEnumerable<OVRSpaceUser> users)
    {
        if (anchors == null)
            throw new ArgumentNullException(nameof(anchors));

        if (users == null)
            throw new ArgumentNullException(nameof(users));

        using var spaceList = OVRNativeList.WithSuggestedCapacityFrom(anchors).AllocateEmpty<ulong>(Allocator.Temp);
        foreach (var anchor in anchors.ToNonAlloc())
        {
            spaceList.Add(anchor.Handle);
        }

        using var userList = OVRNativeList.WithSuggestedCapacityFrom(users).AllocateEmpty<ulong>(Allocator.Temp);
        foreach (var user in users.ToNonAlloc())
        {
            userList.Add(user._handle);
        }

        if (userList.Count < 1)
            throw new ArgumentException($"{nameof(users)} must contain at least one user.");

        if (spaceList.Count == 0)
            return OVRTask.FromResult(OVRResult.From(ShareResult.Success));

        return ShareSpacesAsync(spaces: spaceList, users: userList);
    }

    static unsafe OVRTask<OVRResult<ShareResult>> ShareSpacesAsync(ReadOnlySpan<ulong> spaces, ReadOnlySpan<ulong> users)
    {
        fixed (ulong* spacePtr = spaces)
        fixed (ulong* userPtr = users)
        {
            var result = ShareSpaces(
                spaces: spacePtr,
                numSpaces: (uint)spaces.Length,
                userHandles: userPtr,
                numUsers: (uint)users.Length,
                out var requestId);

            return result.IsSuccess()
                ? OVRTask.FromRequest<OVRResult<ShareResult>>(requestId)
                : OVRTask.FromResult(OVRResult.From((ShareResult)result));
        }
    }


    internal ulong Handle { get; }

    /// <summary>
    /// Unique Identifier representing the anchor.
    /// </summary>
    public Guid Uuid { get; }

    internal OVRAnchor(ulong handle, Guid uuid)
    {
        Handle = handle;
        Uuid = uuid;
    }

    /// <summary>
    /// Gets the anchor's component of a specific type.
    /// </summary>
    /// <typeparam name="T">The type of the component.</typeparam>
    /// <returns>The requested component.</returns>
    /// <remarks>Make sure the anchor supports the specified type of component using <see cref="SupportsComponent{T}"/></remarks>
    /// <exception cref="InvalidOperationException">Thrown if the anchor doesn't support the specified type of component.</exception>
    /// <seealso cref="TryGetComponent{T}"/>
    /// <seealso cref="SupportsComponent{T}"/>
    public T GetComponent<T>() where T : struct, IOVRAnchorComponent<T>
    {
        if (!TryGetComponent<T>(out var component))
        {
            throw new InvalidOperationException($"Anchor {Uuid} does not have component {typeof(T).Name}");
        }

        return component;
    }

    /// <summary>
    /// Tries to get the anchor's component of a specific type.
    /// </summary>
    /// <param name="component">The requested component, as an <c>out</c> parameter.</param>
    /// <typeparam name="T">The type of the component.</typeparam>
    /// <returns>Whether or not the request succeeded. It may fail if the anchor doesn't support this type of component.</returns>
    /// <seealso cref="GetComponent{T}"/>
    public bool TryGetComponent<T>(out T component) where T : struct, IOVRAnchorComponent<T>
    {
        component = default;
        if (!GetSpaceComponentStatusInternal(Handle, component.Type, out _, out _).IsSuccess())
        {
            return false;
        }

        component = component.FromAnchor(this);
        return true;
    }

    /// <summary>
    /// Tests whether or not the anchor supports a specific type of component.
    /// </summary>
    /// <remarks>
    /// For performance reasons, we use xrGetSpaceComponentStatusFB, which can
    /// result in an error in the logs when the component is not available.
    ///
    /// This error does not have impact on the control flow. The alternative method,
    /// <seealso cref="GetSupportedComponents(List{SpaceComponentType})"/> avoids
    /// this error reporting, but does have performance constraints.
    /// </remarks>
    /// <typeparam name="T">The type of the component.</typeparam>
    /// <returns>Whether or not the specified type of component is supported.</returns>
    public bool SupportsComponent<T>() where T : struct, IOVRAnchorComponent<T>
        => GetSpaceComponentStatusInternal(Handle, default(T).Type, out _, out _).IsSuccess();

    /// <summary>
    /// Get all the supported components of an anchor.
    /// </summary>
    /// <param name="components">The list to populate with the supported components. The list is cleared first.</param>
    /// <returns>`True` if the supported components could be retrieved, otherwise `False`.</returns>
    public bool GetSupportedComponents(List<SpaceComponentType> components)
    {
        components.Clear();

        unsafe
        {
            if (!EnumerateSpaceSupportedComponents(Handle, 0, out var count, null).IsSuccess())
                return false;

            var buffer = stackalloc SpaceComponentType[(int)count];
            if (!EnumerateSpaceSupportedComponents(Handle, count, out count, buffer).IsSuccess())
                return false;

            for (uint i = 0; i < count; i++)
            {
                components.Add(buffer[i]);
            }

            return true;
        }
    }

    public bool Equals(OVRAnchor other) => Handle.Equals(other.Handle) && Uuid.Equals(other.Uuid);
    public override bool Equals(object obj) => obj is OVRAnchor other && Equals(other);
    public static bool operator ==(OVRAnchor lhs, OVRAnchor rhs) => lhs.Equals(rhs);
    public static bool operator !=(OVRAnchor lhs, OVRAnchor rhs) => !lhs.Equals(rhs);
    public override int GetHashCode() => unchecked(Handle.GetHashCode() * 486187739 + Uuid.GetHashCode());
    public override string ToString() => Uuid.ToString();

    /// <summary>
    /// Disposes of an anchor.
    /// </summary>
    /// <remarks>
    /// Calling this method will destroy the anchor so that it won't be managed by internal systems until
    /// the next time it is fetched again.
    /// </remarks>
    public void Dispose() => OVRPlugin.DestroySpace(Handle);

    [RuntimeInitializeOnLoadMethod]
    internal static void Init()
    {
        _deferredTasks.Clear();
        Telemetry.OnInit();
    }

    internal const int MaxPersistentAnchorBatchSize = 32;
}
