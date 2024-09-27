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
using System.Diagnostics;
using Meta.XR.Util;
using System.Threading.Tasks;
using Unity.Collections;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
#pragma warning disable OVR004
using System.Linq;
#pragma warning restore
#endif
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Represents a spatial anchor.
/// </summary>
/// <remarks>
/// This component can be used in two ways: to create a new spatial anchor or to bind to an existing spatial anchor.
///
/// To create a new spatial anchor, simply add this component to any GameObject. The transform of the GameObject is used
/// to create a new spatial anchor in the Oculus Runtime. Afterwards, the GameObject's transform will be updated
/// automatically. The creation operation is asynchronous, and, if it fails, this component will be destroyed.
///
/// To load previously saved anchors and bind them to an <see cref="OVRSpatialAnchor"/>, see
/// <see cref="LoadUnboundAnchorsAsync(IEnumerable{Guid},List{UnboundAnchor},Action{List{UnboundAnchor},int})"/>.
/// </remarks>
[DisallowMultipleComponent]
[HelpURL("https://developer.oculus.com/documentation/unity/unity-spatial-anchors-persist-content/#ovrspatialanchor-component")]
[Feature(Feature.Anchors)]
public partial class OVRSpatialAnchor : MonoBehaviour
{
    private bool _startCalled;

    private ulong _requestId;

    private bool _creationFailed;

    private event Action<OperationResult> _onLocalize;

    internal OVRAnchor _anchor { get; private set; }

    /// <summary>
    /// Event that is dispatched when the anchor is localized.
    /// </summary>
    /// <remarks>
    /// If the anchor is already localized when a subscriber is added, the subscriber is invoked immediately.
    ///
    /// Consider <see cref="WhenLocalizedAsync"/> for a more flexible way to await anchor localization.
    /// </remarks>
    /// <seealso cref="Localized"/>
    /// <seealso cref="WhenLocalizedAsync"/>
    public event Action<OperationResult> OnLocalize
    {
        add
        {
            if (Created && OVRPlugin.GetSpaceComponentStatus(_anchor.Handle, OVRPlugin.SpaceComponentType.Locatable,
                    out var isEnabled, out var isPending) && !isPending)
            {
                // Since we always try to localize upon creation, if isPending is false, then it means the attempt to
                // localize has completed, and isEnabled determines whether it was successful.
                value(isEnabled ? OperationResult.Success : OperationResult.Failure);
            }
            else
            {
                _onLocalize += value;
            }
        }

        remove => _onLocalize -= value;
    }

    /// <summary>
    /// The UUID associated with the spatial anchor.
    /// </summary>
    /// <remarks>
    /// UUIDs persist across sessions. If you load a persisted anchor, you can use the UUID to identify
    /// it.
    /// </remarks>
    public Guid Uuid => _anchor.Uuid;

    /// <summary>
    /// Whether the spatial anchor is created.
    /// </summary>
    /// <remarks>
    /// Creation is asynchronous and may take several frames. If creation fails, the component is destroyed.
    /// </remarks>
    public bool Created => this && _anchor != OVRAnchor.Null;

    /// <summary>
    /// Checks whether the spatial anchor is pending creation.
    /// </summary>
    public bool PendingCreation => this && _requestId != 0;

    /// <summary>
    /// Creates an async task that completes when <see cref="Created"/> becomes `True`.
    /// </summary>
    /// <remarks>
    /// Anchor creation is asynchronous, and adding this component to a `GameObject` will start the creation operation.
    /// You can use this async method to `await` the creation from another `async` method.
    ///
    /// <example>
    /// This allows you to write code like this:
    /// <code><![CDATA[
    /// async Task<OVRSpatialAnchor> CreateAnchor(GameObject gameObject) {
    ///   var anchor = gameObject.AddComponent<OVRSpatialAnchor>();
    ///   await anchor.WhenCreatedAsync();
    ///   return anchor;
    /// }
    /// ]]></code>
    /// </example>
    /// </remarks>
    /// <returns>A task-like object that can be used to track the completion of the creation. If creation succeeds,
    /// the result of the task is `True`, otherwise `False`.</returns>
    public async OVRTask<bool> WhenCreatedAsync()
    {
        while (this && !Created && !_creationFailed)
        {
            await Task.Yield();
        }

        return this && Created;
    }

    /// <summary>
    /// Whether the spatial anchor is localized.
    /// </summary>
    /// <remarks>
    /// When you create a new spatial anchor, it may take a few frames before it is localized. Once localized,
    /// its transform updates automatically.
    /// </remarks>
    public bool Localized => Created &&
                             OVRPlugin.GetSpaceComponentStatus(_anchor.Handle, OVRPlugin.SpaceComponentType.Locatable,
                                 out var isEnabled, out _) && isEnabled;

    /// <summary>
    /// Async method that completes when localization completes.
    /// </summary>
    /// <remarks>
    /// Localization occurs automatically, but the process may take some time. After creating a new
    /// <see cref="OVRSpatialAnchor"/>, you can `await` this method from within another `async` method to pause
    /// execution until the anchor has been localized.
    ///
    /// If the localization operation has already completed, this method returns immediately.
    ///
    /// The `bool` result of the returned <see cref="OVRTask{TResult}"/> indicates whether localization was successful.
    ///
    /// <example>
    /// To create a new spatial anchor and wait until it is both created and localized, you can use code like this:
    /// <code><![CDATA[
    /// async Task<OVRSpatialAnchor> CreateAnchor(GameObject gameObject) {
    ///   var anchor = gameObject.AddComponent<OVRSpatialAnchor>();
    ///   await anchor.WhenLocalizedAsync();
    ///   return anchor; // anchor is localized and ready to use
    /// }
    /// ]]></code>
    /// </example>
    /// </remarks>
    /// <returns>A task-like object that can be used to track the completion of the asynchronous localization operation.</returns>
    public async OVRTask<bool> WhenLocalizedAsync()
    {
        if (!await WhenCreatedAsync())
        {
            return false;
        }

        while (OVRPlugin.GetSpaceComponentStatus(_anchor.Handle, OVRPlugin.SpaceComponentType.Locatable,
                   out var isEnabled, out var changePending))
        {
            if (!changePending)
            {
                return isEnabled;
            }

            await Task.Yield();
        }

        return false;
    }

    /// <summary>
    /// Shares the anchor to an <see cref="OVRSpaceUser"/>.
    /// The specified user will be able to download, track, and share specified anchors.
    /// </summary>
    /// <remarks>
    /// This method is asynchronous; use the returned <see cref="OVRTask{TResult}"/> to be notified of completion.
    /// </remarks>
    /// <param name="user">An Oculus user to share the anchor with.</param>
    /// <returns>
    /// An <see cref="OVRTask{TResult}"/> with a <see cref="OperationResult"/> type parameter indicating the success of the share operation.
    /// </returns>
    public OVRTask<OperationResult> ShareAsync(OVRSpaceUser user)
    {
        var userList = OVRObjectPool.List<OVRSpaceUser>();
        userList.Add(user);
        return ShareAsyncInternal(userList);
    }

    /// <summary>
    /// Shares the anchor to an <see cref="OVRSpaceUser"/>.
    /// The specified user will be able to download, track, and share specified anchors.
    /// </summary>
    /// <remarks>
    /// This method is asynchronous; use the returned <see cref="OVRTask{TResult}"/> to be notified of completion.
    /// </remarks>
    /// <param name="user1">An Oculus user to share the anchor with.</param>
    /// <param name="user2">An Oculus user to share the anchor with.</param>
    /// <returns>
    /// An <see cref="OVRTask{TResult}"/> with a <see cref="OperationResult"/> type parameter indicating the success of the share operation.
    /// </returns>
    public OVRTask<OperationResult> ShareAsync(OVRSpaceUser user1, OVRSpaceUser user2)
    {
        var userList = OVRObjectPool.List<OVRSpaceUser>();
        userList.Add(user1);
        userList.Add(user2);
        return ShareAsyncInternal(userList);
    }

    /// <summary>
    /// Shares the anchor to an <see cref="OVRSpaceUser"/>.
    /// The specified user will be able to download, track, and share specified anchors.
    /// </summary>
    /// <remarks>
    /// This method is asynchronous; use the returned <see cref="OVRTask{TResult}"/> to be notified of completion.
    /// </remarks>
    /// <param name="user1">An Oculus user to share the anchor with.</param>
    /// <param name="user2">An Oculus user to share the anchor with.</param>
    /// <param name="user3">An Oculus user to share the anchor with.</param>
    /// <returns>
    /// An <see cref="OVRTask{TResult}"/> with a <see cref="OperationResult"/> type parameter indicating the success of the share operation.
    /// </returns>
    public OVRTask<OperationResult> ShareAsync(OVRSpaceUser user1, OVRSpaceUser user2, OVRSpaceUser user3)
    {
        var userList = OVRObjectPool.List<OVRSpaceUser>();
        userList.Add(user1);
        userList.Add(user2);
        userList.Add(user3);
        return ShareAsyncInternal(userList);
    }

    /// <summary>
    /// Shares the anchor to an <see cref="OVRSpaceUser"/>.
    /// The specified user will be able to download, track, and share specified anchors.
    /// </summary>
    /// <remarks>
    /// This method is asynchronous; use the returned <see cref="OVRTask{TResult}"/> to be notified of completion.
    /// </remarks>
    /// <param name="user1">An Oculus user to share the anchor with.</param>
    /// <param name="user2">An Oculus user to share the anchor with.</param>
    /// <param name="user3">An Oculus user to share the anchor with.</param>
    /// <param name="user4">An Oculus user to share the anchor with.</param>
    /// <returns>
    /// An <see cref="OVRTask{TResult}"/> with a <see cref="OperationResult"/> type parameter indicating the success of the share operation.
    /// </returns>
    public OVRTask<OperationResult> ShareAsync(OVRSpaceUser user1, OVRSpaceUser user2, OVRSpaceUser user3,
        OVRSpaceUser user4)
    {
        var userList = OVRObjectPool.List<OVRSpaceUser>();
        userList.Add(user1);
        userList.Add(user2);
        userList.Add(user3);
        userList.Add(user4);
        return ShareAsyncInternal(userList);
    }

    /// <summary>
    /// Shares the anchor to an <see cref="OVRSpaceUser"/>.
    /// The specified user will be able to download, track, and share specified anchors.
    /// </summary>
    /// <remarks>
    /// This method is asynchronous; use the returned <see cref="OVRTask{TResult}"/> to be notified of completion.
    /// </remarks>
    /// <param name="users">A collection of Oculus users to share the anchor with.</param>
    /// <returns>
    /// An <see cref="OVRTask{TResult}"/> with a <see cref="OperationResult"/> type parameter indicating the success of the share operation.
    /// </returns>
    public OVRTask<OperationResult> ShareAsync(IEnumerable<OVRSpaceUser> users)
    {
        var userList = OVRObjectPool.List<OVRSpaceUser>();
        userList.AddRange(users);
        return ShareAsyncInternal(userList);
    }

    /// <summary>
    /// Shares a collection of anchors with a collection of users.
    /// </summary>
    /// <remarks>
    /// The <see cref="users"/> will be able to download, localize, and share the specified <see cref="anchors"/>.
    ///
    /// This method is asynchronous. Use the returned <see cref="OVRTask{TResult}"/> to monitor completion.
    ///
    /// This operation fully succeeds or fails, which means, either all anchors are successfully shared
    /// or the operation fails.
    /// </remarks>
    /// <param name="anchors">The collection of anchors to share.</param>
    /// <param name="users">An array of Oculus users to share these anchors with.</param>
    /// <returns>A task that can be used to track the completion of the sharing operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="anchors"/> is `null`.</exception>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="users"/> is `null`.</exception>
    public static OVRTask<OperationResult> ShareAsync(IEnumerable<OVRSpatialAnchor> anchors,
        IEnumerable<OVRSpaceUser> users)
    {
        if (anchors == null)
            throw new ArgumentNullException(nameof(anchors));

        if (users == null)
            throw new ArgumentNullException(nameof(users));

        unsafe
        {
            using var spaces = new OVRNativeList<ulong>(anchors.ToNonAlloc().Count, Allocator.Temp);
            foreach (var anchor in anchors.ToNonAlloc())
            {
                spaces.Add(anchor._anchor.Handle);
            }

            using var userHandles = new OVRNativeList<ulong>(users.ToNonAlloc().Count, Allocator.Temp);
            foreach (var user in users.ToNonAlloc())
            {
                userHandles.Add(user._handle);
            }

            var result = OVRPlugin.ShareSpaces(spaces, (uint)spaces.Count, userHandles,
                (uint)userHandles.Count, out var requestId);

            Development.LogRequestOrError(requestId, result,
                $"Sharing {spaces.Count} spatial anchors with {userHandles.Count} users.",
                $"xrShareSpacesFB failed with error {result}.");

            return result.IsSuccess()
                ? OVRTask.FromRequest<OperationResult>(requestId)
                : OVRTask.FromResult((OperationResult)result);
        }
    }

    private OVRTask<OperationResult> ShareAsyncInternal(List<OVRSpaceUser> users)
    {
        var shareRequestAnchors = GetListToStoreTheShareRequest(users);
        shareRequestAnchors.Add(this);
        var requestId = Guid.NewGuid();
        AsyncRequestTaskIds[this] = requestId;
        return OVRTask.FromGuid<OperationResult>(requestId);
    }

    private List<OVRSpatialAnchor> GetListToStoreTheShareRequest(List<OVRSpaceUser> users)
    {
        users.Sort((x, y) => x.Id.CompareTo(y.Id));
        foreach (var (shareRequestUsers, shareRequestAnchors) in ShareRequests)
        {
            if (!AreSortedUserListsEqual(users, shareRequestUsers))
            {
                continue;
            }

            // reuse the current request
            return shareRequestAnchors;
        }

        // add a new request
        var anchorList = OVRObjectPool.List<OVRSpatialAnchor>();
        ShareRequests.Add((users, anchorList));
        return anchorList;
    }

    private static bool AreSortedUserListsEqual(IReadOnlyList<OVRSpaceUser> sortedList1,
        IReadOnlyList<OVRSpaceUser> sortedList2)
    {
        if (sortedList1.Count != sortedList2.Count)
        {
            return false;
        }

        for (var i = 0; i < sortedList1.Count; i++)
        {
            if (sortedList1[i].Id != sortedList2[i].Id)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Saves a collection of anchors.
    /// </summary>
    /// <remarks>
    /// This method is asynchronous; use the returned <see cref="OVRTask{TResult}"/> to be notified of completion.
    /// When saved, the <see cref="OVRSpatialAnchor"/> can be loaded by a different session. Use
    /// <see cref="LoadUnboundAnchorsAsync(IEnumerable{Guid},List{UnboundAnchor},Action{List{UnboundAnchor},int})"/>
    /// to load some or all <see cref="OVRSpatialAnchor"/> at a future time.
    ///
    /// This operation fully succeeds or fails; that is, either all anchors are successfully saved, or the operation
    /// fails.
    /// </remarks>
    /// <param name="anchors">The anchors to save (up to 32).</param>
    /// <returns>An awaitable <see cref="OVRTask{TResult}"/> representing the asynchronous save operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="anchors"/> is `null`.</exception>
    /// <seealso cref="SaveAnchorAsync"/>
    /// <exception cref="ArgumentException">Thrown if <paramref name="anchors"/> contains more than 32 anchors.</exception>
    public static OVRTask<OVRResult<OVRAnchor.SaveResult>> SaveAnchorsAsync(IEnumerable<OVRSpatialAnchor> anchors)
    {
        if (anchors == null)
            throw new ArgumentNullException(nameof(anchors));

        using var spaces = new OVRNativeList<ulong>(Allocator.Temp);
        foreach (var anchor in anchors.ToNonAlloc())
        {
            spaces.Add(anchor._anchor.Handle);
        }

        if (spaces.Count > OVRAnchor.MaxPersistentAnchorBatchSize)
            throw new ArgumentException($"Cannot save more than {OVRAnchor.MaxPersistentAnchorBatchSize} anchors at once ({spaces.Count} were provided).", nameof(anchors));

        return OVRAnchor.SaveSpacesAsync(spaces);
    }

    /// <summary>
    /// Saves this anchor to persistent storage.
    /// </summary>
    /// <remarks>
    /// This operation is asynchronous. Use the returned <see cref="OVRTask{TResult}"/> to track the result of the
    /// asynchronous operation.
    ///
    /// When saved, an <see cref="OVRSpatialAnchor"/> can be loaded by a different session. Use the
    /// <see cref="Uuid"/> to identify the same <see cref="OVRSpatialAnchor"/> at a future time.
    ///
    /// NOTE: If you have a collection of anchors to save, it is more efficient to use <see cref="SaveAnchorsAsync"/>.
    /// </remarks>
    /// <returns>An awaitable <see cref="OVRTask{TResult}"/> representing the asynchronous save operation.</returns>
    /// <seealso cref="SaveAnchorsAsync"/>
    public OVRTask<OVRResult<OVRAnchor.SaveResult>> SaveAnchorAsync() => _anchor.SaveAsync();

    /// <summary>
    /// Erase the anchor from persistent storage.
    /// </summary>
    /// <remarks>
    /// This operation is asynchronous. Use the returned <see cref="OVRTask{TResult}"/> to track the result of the
    /// asynchronous operation.
    ///
    /// NOTE: If you have a collection of anchors to save, it is more efficient to use <see cref="EraseAnchorsAsync"/>.
    /// </remarks>
    /// <returns>An awaitable <see cref="OVRTask{TResult}"/> representing the asynchronous erase operation.</returns>
    /// <seealso cref="EraseAnchorsAsync"/>
    public OVRTask<OVRResult<OVRAnchor.EraseResult>> EraseAnchorAsync() => _anchor.EraseAsync();

    /// <summary>
    /// Erase a collection of anchors from persistent storage.
    /// </summary>
    /// <remarks>
    /// This operation is asynchronous. Use the returned <see cref="OVRTask{TResult}"/> to track the result of the
    /// asynchronous operation.
    ///
    /// The total number of anchors (<paramref name="anchors"/> and <paramref name="uuids"/>) may not exceed 32.
    /// </remarks>
    /// <param name="anchors">(Optional) The anchors to erase</param>
    /// <param name="uuids">(Optional) The UUIDs to erase</param>
    /// <returns>An awaitable <see cref="OVRTask{TResult}"/> representing the asynchronous erase operation.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="anchors"/> and <paramref name="uuids"/> are both `null`.</exception>
    /// <seealso cref="EraseAnchorAsync"/>
    /// <exception cref="ArgumentException">Thrown if the combined number of <paramref name="anchors"/> and
    /// <paramref name="uuids"/> is greater than 32.</exception>
    public static OVRTask<OVRResult<OVRAnchor.EraseResult>> EraseAnchorsAsync(
        IEnumerable<OVRSpatialAnchor> anchors,
        IEnumerable<Guid> uuids)
    {
        if (anchors == null && uuids == null)
            throw new ArgumentException($"One of {nameof(anchors)} or {nameof(uuids)} must not be null.");

        using (new OVRObjectPool.ListScope<OVRAnchor>(out var spaces))
        {
            foreach (var anchor in anchors.ToNonAlloc())
            {
                spaces.Add(anchor._anchor);
            }

            return OVRAnchor.EraseAsync(spaces, uuids);
        }
    }

    private static void ThrowIfBound(Guid uuid)
    {
        if (SpatialAnchors.ContainsKey(uuid))
            throw new InvalidOperationException(
                $"Spatial anchor with uuid {uuid} is already bound to an {nameof(OVRSpatialAnchor)}.");
    }

    // Initializes this component without checking preconditions
    private void InitializeUnchecked(OVRSpace space, Guid uuid)
    {
        SpatialAnchors.Add(uuid, this);
        _requestId = 0;
        _anchor = new OVRAnchor(space, uuid);

        if (_anchor.TryGetComponent<OVRLocatable>(out var locatable))
        {
            locatable.SetEnabledAsync(true);
        }

        if (_anchor.TryGetComponent<OVRStorable>(out var storable))
        {
            storable.SetEnabledAsync(true);
        }

        if (_anchor.TryGetComponent<OVRSharable>(out var sharable))
        {
            sharable.SetEnabledAsync(true);
        }

        // Try to update the pose as soon as we can.
        UpdateTransform();
    }

    private void Start()
    {
        _startCalled = true;

        if (Created)
        {
            Development.Log($"[{Uuid}] {nameof(OVRSpatialAnchor)} created from existing an existing anchor.");
        }
        else
        {
            CreateSpatialAnchor();
        }
    }

    private void Update()
    {
        if (Created)
        {
            UpdateTransform();
        }
    }

    private void LateUpdate()
    {
#pragma warning disable CS0612 // Type or member is obsolete
        SaveBatchAnchors();
#pragma warning restore
        ShareBatchAnchors();
    }

    private static void ShareBatchAnchors()
    {
        foreach (var (userList, anchorList) in ShareRequests)
        {
            if (userList.Count > 0 && anchorList.Count > 0)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                Share(anchorList, userList);
#pragma warning restore
            }

            OVRObjectPool.Return(userList);
            OVRObjectPool.Return(anchorList);
        }

        ShareRequests.Clear();
    }

    private void OnDestroy()
    {
        if (_anchor != OVRAnchor.Null)
        {
            _anchor.Dispose();
        }

        SpatialAnchors.Remove(Uuid);
    }

    private OVRPose GetTrackingSpacePose()
    {
        var mainCamera = Camera.main;
        if (mainCamera)
        {
            return transform.ToTrackingSpacePose(mainCamera);
        }

        Development.LogWarning($"No main camera found. Using world-space pose.");
        return transform.ToOVRPose(isLocal: false);
    }

    private void CreateSpatialAnchor()
    {
        var created = OVRPlugin.CreateSpatialAnchor(new OVRPlugin.SpatialAnchorCreateInfo
        {
            BaseTracking = OVRPlugin.GetTrackingOriginType(),
            PoseInSpace = GetTrackingSpacePose().ToPosef(),
            Time = OVRPlugin.GetTimeInSeconds(),
        }, out _requestId);

        if (created)
        {
            Development.LogRequest(_requestId, $"Creating spatial anchor...");
            CreationRequests[_requestId] = this;
        }
        else
        {
            Development.LogError(
                $"{nameof(OVRPlugin)}.{nameof(OVRPlugin.CreateSpatialAnchor)} failed. Destroying {nameof(OVRSpatialAnchor)} component.");
            Destroy(this);
        }
    }

    internal static bool TryGetPose(OVRSpace space, out OVRPose pose)
    {
        var tryLocateSpace = OVRPlugin.TryLocateSpace(space, OVRPlugin.GetTrackingOriginType(), out var posef, out var locationFlags);
        if (!tryLocateSpace || !locationFlags.IsOrientationValid() || !locationFlags.IsPositionValid())
        {
            pose = OVRPose.identity;
            return false;
        }

        pose = posef.ToOVRPose();
        var mainCamera = Camera.main;
        if (mainCamera)
        {
            pose = pose.ToWorldSpacePose(mainCamera);
        }

        return true;
    }

    private void UpdateTransform()
    {
        if (TryGetPose(_anchor.Handle, out var pose))
        {
            transform.SetPositionAndRotation(pose.position, pose.orientation);
        }
    }

    private struct MultiAnchorDelegatePair
    {
        public List<OVRSpatialAnchor> Anchors;
        public Action<ICollection<OVRSpatialAnchor>, OperationResult> Delegate;
    }

    internal static readonly Dictionary<Guid, OVRSpatialAnchor> SpatialAnchors =
        new Dictionary<Guid, OVRSpatialAnchor>();

    private static readonly Dictionary<ulong, OVRSpatialAnchor> CreationRequests =
        new Dictionary<ulong, OVRSpatialAnchor>();

    private static readonly Dictionary<OVRSpatialAnchor, Guid> AsyncRequestTaskIds =
        new Dictionary<OVRSpatialAnchor, Guid>();

    private static readonly List<(List<OVRSpaceUser>, List<OVRSpatialAnchor>)> ShareRequests =
        new List<(List<OVRSpaceUser>, List<OVRSpatialAnchor>)>();

    private static readonly Dictionary<ulong, MultiAnchorDelegatePair> MultiAnchorCompletionDelegates =
        new Dictionary<ulong, MultiAnchorDelegatePair>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void InitializeOnLoad()
    {
        CreationRequests.Clear();
        MultiAnchorCompletionDelegates.Clear();
        SpatialAnchors.Clear();
    }

    static OVRSpatialAnchor()
    {
        OVRManager.SpatialAnchorCreateComplete += OnSpatialAnchorCreateComplete;
        OVRManager.SpaceSaveComplete += OnSpaceSaveComplete;
        OVRManager.SpaceListSaveComplete += OnSpaceListSaveComplete;
        OVRManager.ShareSpacesComplete += OnShareSpacesComplete;
        OVRManager.SpaceEraseComplete += OnSpaceEraseComplete;
        OVRManager.SpaceQueryComplete += OnSpaceQueryComplete;
        OVRManager.SpaceSetComponentStatusComplete += OnSpaceSetComponentStatusComplete;
    }

    private static void InvokeMultiAnchorDelegate(ulong requestId, OperationResult result,
        MultiAnchorActionType actionType)
    {
        if (!MultiAnchorCompletionDelegates.Remove(requestId, out var value))
        {
            return;
        }

        value.Delegate?.Invoke(value.Anchors, result);

        try
        {
            foreach (var anchor in value.Anchors)
            {
                switch (actionType)
                {
                    case MultiAnchorActionType.Save:
                    {
                        if (result != OperationResult.Success)
                        {
                            Development.LogError(
                                $"[{anchor.Uuid}] {nameof(OVRPlugin)}.{nameof(OVRPlugin.SaveSpaceList)} failed with result: {result}.");
                        }

                        if (AsyncRequestTaskIds.TryGetValue(anchor, out var taskId))
                        {
                            AsyncRequestTaskIds.Remove(anchor);
                            OVRTask.GetExisting<bool>(taskId).SetResult(result == OperationResult.Success);
                        }

                        break;
                    }
                    case MultiAnchorActionType.Share:
                    {
                        if (result != OperationResult.Success)
                        {
                            Development.LogError(
                                $"[{anchor.Uuid}] {nameof(OVRPlugin)}.{nameof(OVRPlugin.ShareSpaces)} failed with result: {result}.");
                        }

                        if (AsyncRequestTaskIds.TryGetValue(anchor, out var taskId))
                        {
                            AsyncRequestTaskIds.Remove(anchor);
                            OVRTask.GetExisting<OperationResult>(taskId).SetResult(result);
                        }

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(actionType), actionType, null);
                }
            }
        }
        finally
        {
            OVRObjectPool.Return(value.Anchors);
        }
    }

    private static void OnSpatialAnchorCreateComplete(ulong requestId, bool success, OVRSpace space, Guid uuid)
    {
        Development.LogRequestResult(requestId, success,
            $"[{uuid}] Spatial anchor created.",
            $"Failed to create spatial anchor. Destroying {nameof(OVRSpatialAnchor)} component.");

        if (!CreationRequests.Remove(requestId, out var anchor)) return;

        if (anchor)
        {
            anchor._creationFailed = !success;
        }

        if (success && anchor)
        {
            // All good; complete setup of OVRSpatialAnchor component.
            anchor.InitializeUnchecked(space, uuid);
            return;
        }

        if (success && !anchor)
        {
            // Creation succeeded, but the OVRSpatialAnchor component was destroyed before the callback completed.
            OVRPlugin.DestroySpace(space);
        }
        else if (!success && anchor)
        {
            // The OVRSpatialAnchor component exists but creation failed.
            Destroy(anchor);
        }
        // else if creation failed and the OVRSpatialAnchor component was destroyed, nothing to do.
    }

    /// <summary>
    /// A spatial anchor that has not been bound to an <see cref="OVRSpatialAnchor"/>.
    /// </summary>
    /// <remarks>
    /// Use this object to bind an unbound spatial anchor to an <see cref="OVRSpatialAnchor"/>.
    /// </remarks>
    public readonly partial struct UnboundAnchor
    {
        internal readonly OVRSpace _space;

        /// <summary>
        /// The universally unique identifier associated with this anchor.
        /// </summary>
        public Guid Uuid { get; }

        /// <summary>
        /// Whether the anchor has been localized.
        /// </summary>
        /// <remarks>
        /// Prior to localization, the anchor's <see cref="Pose"/> cannot be determined.
        /// </remarks>
        /// <seealso cref="WhenLocalizedAsync"/>
        /// <seealso cref="Localizing"/>
        public bool Localized => OVRPlugin.GetSpaceComponentStatus(_space, OVRPlugin.SpaceComponentType.Locatable,
            out var enabled, out _) && enabled;

        /// <summary>
        /// Whether the anchor is in the process of being localized.
        /// </summary>
        /// <seealso cref="WhenLocalizedAsync"/>
        /// <seealso cref="Localized"/>
        public bool Localizing => OVRPlugin.GetSpaceComponentStatus(_space, OVRPlugin.SpaceComponentType.Locatable,
            out var enabled, out var pending) && !enabled && pending;

        /// <summary>
        /// The world space pose of the spatial anchor.
        /// </summary>
        /// <remarks>
        /// Use this method to get the pose of the anchor after it has been localized with <see cref="LocalizeAsync"/>.
        ///
        /// If the anchor is not localized, this method throws `InvalidOperationException`. If the anchor has been
        /// localized, but the pose cannot be retrieved (for example, because of a temporary tracking issue), then this
        /// method returns `False`, and you should try again later.
        /// </remarks>
        /// <param name="pose">If this method returns `True`, then <paramref name="pose"/> will contain the world space
        /// pose of the anchor. If this method returns `False`, <paramref name="pose"/> is set to identity.</param>
        /// <returns>Returns `True` if the pose could be obtained, otherwise `False`.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the anchor is invalid, e.g., because it has been destroyed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the anchor has not been localized.</exception>
        public bool TryGetPose(out Pose pose)
        {
            var anchor = new OVRAnchor(_space, Uuid);
            if (anchor == OVRAnchor.Null)
                throw new InvalidOperationException($"The {nameof(UnboundAnchor)} is not valid. Was it default (zero) initialized?");

            if (!anchor.TryGetComponent<OVRLocatable>(out var locatable))
                throw new InvalidOperationException($"Anchor {Uuid} is not localizable.");

            if (!locatable.IsEnabled)
                throw new InvalidOperationException($"The anchor {Uuid} is not localized. An anchor must be localized before getting the pose.");

            if (OVRSpatialAnchor.TryGetPose(_space, out var ovrPose))
            {
                pose = new Pose(ovrPose.position, ovrPose.orientation);
                return true;
            }

            pose = Pose.identity;
            return false;
        }

        /// <summary>
        /// Localizes an anchor.
        /// </summary>
        /// <remarks>
        /// The delegate supplied to
        /// <see cref="LoadUnboundAnchorsAsync(IEnumerable{Guid},List{UnboundAnchor},Action{List{UnboundAnchor},int})"/>
        /// receives an array of unbound spatial anchors. You can choose whether to localize each one and be notified
        /// when localization completes.
        ///
        /// Upon successful localization, your delegate should instantiate an <see cref="OVRSpatialAnchor"/>, then bind
        /// the <see cref="UnboundAnchor"/> to the <see cref="OVRSpatialAnchor"/> by calling
        /// <see cref="UnboundAnchor.BindTo"/>. Once an <see cref="UnboundAnchor"/> is bound to an
        /// <see cref="OVRSpatialAnchor"/>, it cannot be used again; that is, it cannot be bound to multiple
        /// <see cref="OVRSpatialAnchor"/> components.
        /// </remarks>
        /// <param name="timeout">The timeout, in seconds, to attempt localization, or zero to indicate no timeout.</param>
        /// <returns>
        /// An <see cref="OVRTask{TResult}"/> with a boolean type parameter indicating the success of the localization.
        /// </returns>
        /// <exception cref="InvalidOperationException">Thrown if the anchor does not support localization, e.g.,
        /// because it is invalid.</exception>
        public OVRTask<bool> LocalizeAsync(double timeout = 0)
        {
            var anchor = new OVRAnchor(_space, Uuid);
            if (anchor.TryGetComponent<OVRStorable>(out var storable))
            {
                storable.SetEnabledAsync(true);
            }

            if (anchor.TryGetComponent<OVRSharable>(out var sharable))
            {
                sharable.SetEnabledAsync(true);
            }

            return anchor.GetComponent<OVRLocatable>().SetEnabledAsync(true, timeout);
        }

        /// <summary>
        /// Binds an unbound anchor to an <see cref="OVRSpatialAnchor"/> component.
        /// </summary>
        /// <remarks>
        /// Use this to bind an unbound anchor to an <see cref="OVRSpatialAnchor"/>. After <see cref="BindTo"/> is used
        /// to bind an <see cref="UnboundAnchor"/> to an <see cref="OVRSpatialAnchor"/>, the
        /// <see cref="UnboundAnchor"/> is no longer valid; that is, it cannot be bound to another
        /// <see cref="OVRSpatialAnchor"/>.
        /// </remarks>
        /// <param name="spatialAnchor">The component to which this unbound anchor should be bound.</param>
        /// <exception cref="InvalidOperationException">Thrown if this <see cref="UnboundAnchor"/> does not refer to a valid anchor.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="spatialAnchor"/> is `null`.</exception>
        /// <exception cref="ArgumentException">Thrown if an anchor is already bound to <paramref name="spatialAnchor"/>.</exception>
        /// <exception cref="ArgumentException">Thrown if <paramref name="spatialAnchor"/> is pending creation (see <see cref="OVRSpatialAnchor.PendingCreation"/>).</exception>
        /// <exception cref="InvalidOperationException">Thrown if this <see cref="UnboundAnchor"/> is already bound to an <see cref="OVRSpatialAnchor"/>.</exception>
        public void BindTo(OVRSpatialAnchor spatialAnchor)
        {
            if (!_space.Valid)
                throw new InvalidOperationException($"{nameof(UnboundAnchor)} does not refer to a valid anchor.");

            if (spatialAnchor == null)
                throw new ArgumentNullException(nameof(spatialAnchor));

            if (spatialAnchor.Created)
                throw new ArgumentException(
                    $"Cannot bind {Uuid} to {nameof(spatialAnchor)} because {nameof(spatialAnchor)} is already bound to {spatialAnchor.Uuid}.",
                    nameof(spatialAnchor));

            if (spatialAnchor.PendingCreation)
                throw new ArgumentException(
                    $"Cannot bind {Uuid} to {nameof(spatialAnchor)} because {nameof(spatialAnchor)} is being used to create a new spatial anchor.",
                    nameof(spatialAnchor));

            ThrowIfBound(Uuid);

            spatialAnchor.InitializeUnchecked(_space, Uuid);
        }

        internal UnboundAnchor(OVRSpace space, Guid uuid)
        {
            _space = space;
            Uuid = uuid;
        }
    }

    /// <summary>
    /// Loads up to 50 unbound anchors with specified UUIDs.
    /// </summary>
    /// <remarks>
    /// An <see cref="UnboundAnchor"/> is an anchor that exists, but that isn't managed by an
    /// <see cref="OVRSpatialAnchor"/>. Use this method to load a collection of anchors by UUID that haven't already
    /// been bound to an <see cref="OVRSpatialAnchor"/>.
    ///
    /// In order to be loaded, the anchor must have previously been persisted, e.g., with
    /// <see cref="SaveAnchorsAsync"/>.
    ///
    /// NOTE: This method will only process the first 50 UUIDs provided by <paramref name="uuids"/>.
    ///
    /// This method is asynchronous. The returned <see cref="OVRTask{TResult}"/> completes when all results are
    /// available. However, anchors may be loaded in batches. To be notified as the results of individual batches are
    /// loaded, provide an <paramref name="onIncrementalResultsAvailable"/> callback. The callback accepts two
    /// parameters: <code><![CDATA[(List<UnboundAnchor> unboundAnchors, int startingIndex)]]></code>
    /// - `unboundAnchors` is a reference to the <paramref name="unboundAnchors"/> parameter.
    /// - `startingIndex` is the index into `unboundAnchors` which contains the first newly loaded anchor in this batch.
    ///
    /// Before the task completes, it is undefined behavior to access <paramref name="unboundAnchors"/> outside of
    /// <see cref="onIncrementalResultsAvailable"/> invocations.
    ///
    /// Note that if you bind an <see cref="UnboundAnchor"/> in the <see cref="onIncrementalResultsAvailable"/> callback,
    /// that same anchor will no longer be present in the <paramref name="unboundAnchors"/> provided to future
    /// incremental results, or after the task completes. That is, an <see cref="UnboundAnchor"/> is removed from the
    /// list of <paramref name="unboundAnchors"/> once it is bound.
    ///
    /// <paramref name="unboundAnchors"/> is the buffer used to store the results. This allows you to reuse the same
    /// buffer between subsequent calls. The list is cleared before any anchors are added to it.
    /// </remarks>
    /// <param name="uuids">The UUIDs of the anchors to load.</param>
    /// <param name="unboundAnchors">The buffer to store the resulting unbound anchors into.</param>
    /// <param name="onIncrementalResultsAvailable">An optional callback that is invoked whenever a new batch of unbound
    /// anchors has been loaded.</param>
    /// <returns>A new <see cref="OVRTask{TResult}"/> that completes when the loading operation completes.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="uuids"/> is `null`.</exception>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="unboundAnchors"/> is `null`.</exception>
    public static OVRTask<OVRResult<List<UnboundAnchor>, OVRAnchor.FetchResult>> LoadUnboundAnchorsAsync(
        IEnumerable<Guid> uuids,
        List<UnboundAnchor> unboundAnchors,
        Action<List<UnboundAnchor>, int> onIncrementalResultsAvailable = null)
    {
        if (uuids == null)
            throw new ArgumentNullException(nameof(uuids));

        if (unboundAnchors == null)
            throw new ArgumentNullException(nameof(unboundAnchors));

        return LoadUnboundAnchorsAsync(new OVRAnchor.FetchOptions { Uuids = uuids }, unboundAnchors, onIncrementalResultsAvailable);
    }

    /// <summary>
    /// Load anchors that have been shared with you.
    /// </summary>
    /// <remarks>
    /// Use this method to load spatial anchors that have been shared with you by another user.
    ///
    /// This method requires access to Meta servers to query for shared anchors. This method can fail in a few ways:
    /// - The device cannot reach Meta servers, e.g., because there is no internet access (see <see cref="OperationResult.Failure_SpaceNetworkRequestFailed"/>)
    /// - The user has denied the "Share Point Cloud Data" device permission (see <see cref="OperationResult.Failure_SpaceCloudStorageDisabled"/>)
    /// - The application has not declared "Shared Spatial Anchor Support" (OVRManager > Quest Features > General > Shared Spatial Anchor Support)
    ///
    /// To load other types of anchors, use
    /// <see cref="LoadUnboundAnchorsAsync(IEnumerable{Guid},List{UnboundAnchor},Action{List{UnboundAnchor},int})"/>.
    /// </remarks>
    /// <param name="uuids">The set of anchor UUIDs to load</param>
    /// <param name="unboundAnchors">A buffer to store the loaded anchors. This container is cleared before being
    /// populated.</param>
    /// <returns>Returns an awaitable task-like object that can be used to track completion of the load operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="uuids"/> is `null`.</exception>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="unboundAnchors"/> is `null`.</exception>
    /// <seealso cref="LoadUnboundAnchorsAsync(IEnumerable{Guid},List{UnboundAnchor},Action{List{UnboundAnchor},int})"/>
    public static OVRTask<OVRResult<List<UnboundAnchor>, OperationResult>> LoadUnboundSharedAnchorsAsync(
        IEnumerable<Guid> uuids,
        List<UnboundAnchor> unboundAnchors)
    {
        if (uuids == null)
            throw new ArgumentNullException(nameof(uuids));

        if (unboundAnchors == null)
            throw new ArgumentNullException(nameof(unboundAnchors));

        async OVRTask<OVRResult<List<UnboundAnchor>, OperationResult>> Execute(IEnumerable<Guid> uuids,
            List<UnboundAnchor> unboundAnchors)
        {
            using (new OVRObjectPool.ListScope<OVRAnchor>(out var anchors))
            {
#pragma warning disable CS0618, CS0612 // Obsolete
                var result = await OVRAnchor.FetchAnchors(anchors,
                    OVRAnchor.GetQueryInfo(uuids, OVRSpace.StorageLocation.Cloud, 0));
#pragma warning restore CS0618, CS0612

                unboundAnchors.Clear();
                if (result.IsSuccess())
                {
                    foreach (var anchor in anchors)
                    {
                        if (TryGetUnbound(anchor, out var unboundAnchor))
                        {
                            unboundAnchors.Add(unboundAnchor);
                        }
                    }
                }

                return OVRResult.From(unboundAnchors, (OperationResult)result);
            }
        }

        return Execute(uuids, unboundAnchors);
    }


    static async OVRTask<OVRResult<List<UnboundAnchor>, OVRAnchor.FetchResult>> LoadUnboundAnchorsAsync(
        OVRAnchor.FetchOptions fetchOptions, List<UnboundAnchor> unboundAnchors,
        Action<List<UnboundAnchor>, int> resultsHandler)
    {
        unboundAnchors.Clear();

        OVRAnchor.FetchResult fetchResult;
        using (new OVRObjectPool.ListScope<OVRAnchor>(out var anchors))
        {
            var result = await OVRAnchor.FetchAnchorsAsync(anchors, fetchOptions, resultsHandler == null
                ? null
                : (incrementalResults, staringIndex) =>
                {
                    int? unboundAnchorStartingIndex = null;

                    // We repopulate the entire list because some anchors may have been bound since the last batch
                    unboundAnchors.Clear();
                    for (var i = 0; i < incrementalResults.Count; i++)
                    {
                        if (TryGetUnbound(incrementalResults[i], out var unboundAnchor))
                        {
                            if (i >= staringIndex && unboundAnchorStartingIndex == null)
                            {
                                // This is a new unbound anchor that we haven't yet reported
                                unboundAnchorStartingIndex = unboundAnchors.Count;
                            }

                            unboundAnchors.Add(unboundAnchor);
                        }
                    }

                    // Fire callback if there are any new ones
                    if (unboundAnchorStartingIndex.HasValue)
                    {
                        resultsHandler(unboundAnchors, unboundAnchorStartingIndex.Value);
                    }
                });

            fetchResult = result.Status;

            unboundAnchors.Clear();
            if (result.Success)
            {
                foreach (var anchor in result.Value)
                {
                    if (TryGetUnbound(anchor, out var unboundAnchor))
                    {
                        unboundAnchors.Add(unboundAnchor);
                    }
                }
            }
        }

        return OVRResult.From(unboundAnchors, fetchResult);
    }

    /// <summary>
    /// Create an unbound spatial anchor from an <see cref="OVRAnchor"/>.
    /// </summary>
    /// <remarks>
    /// Only spatial anchors retrieved as <see cref="OVRAnchor"/>s should use
    /// this method. Using this function on system-managed scene anchors will
    /// succeed, but certain functions will not work.
    /// </remarks>
    /// <param name="anchor">The <see cref="OVRAnchor"/> to create the unbound anchor for.</param>
    /// <param name="unboundAnchor">The created unboundAnchor.</param>
    /// <returns>True if <paramref name="anchor"/> is localizable and is not already bound to an
    /// <see cref="OVRSpatialAnchor"/>, otherwise false.</returns>
    public static bool FromOVRAnchor(OVRAnchor anchor, out UnboundAnchor unboundAnchor)
    {
        if (anchor == OVRAnchor.Null) throw new ArgumentNullException(nameof(anchor));

        return TryGetUnbound(anchor, out unboundAnchor);
    }

    private static bool TryGetUnbound(OVRAnchor anchor, out UnboundAnchor unboundAnchor)
    {
        unboundAnchor = default;
        if (SpatialAnchors.ContainsKey(anchor.Uuid))
        {
            Development.Log($"[{anchor}] Anchor is already bound to an {nameof(OVRSpatialAnchor)}. Ignoring.");
            return false;
        }

        // See if it supports localization
        var supportsLocatable = anchor.TryGetComponent<OVRLocatable>(out var locatable);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        using var _ = new OVRObjectPool.ListScope<OVRPlugin.SpaceComponentType>(out var supportedComponents);
        anchor.GetSupportedComponents(supportedComponents);
#pragma warning disable OVR004
        var debugComponentList = supportedComponents.Count == 0
            ? "(no components)"
            : string.Join(", ", supportedComponents.Select(c => c.ToString()));
#pragma warning restore
#else
        var debugComponentList = string.Empty;
#endif

        Development.Log(
            $"[{anchor}] {(locatable.IsEnabled ? "is localized" : "not yet localized")}. Supported components: {debugComponentList}");

        if (!supportsLocatable)
        {
            Debug.LogError($"Ignoring {anchor} because it does not support localization.");
            return false;
        }

        unboundAnchor = new UnboundAnchor(anchor.Handle, anchor.Uuid);
        return true;
    }

    private static void OnSpaceSetComponentStatusComplete(ulong requestId, bool result, OVRSpace space, Guid uuid,
        OVRPlugin.SpaceComponentType componentType, bool enabled)
    {
        Development.LogRequestResult(requestId, result,
            $"[{uuid}] {componentType} {(enabled ? "enabled" : "disabled")}.",
            $"[{uuid}] Failed to set {componentType} status.");

        if (componentType == OVRPlugin.SpaceComponentType.Locatable && SpatialAnchors.TryGetValue(uuid, out var anchor))
        {
            anchor._onLocalize?.Invoke(enabled ? OperationResult.Success : OperationResult.Failure);
        }
    }

    private enum MultiAnchorActionType
    {
        Save,
        Share
    }

    private static void OnShareSpacesComplete(ulong requestId, OperationResult result)
    {
        Development.LogRequestResult(requestId, result >= 0,
            $"Spaces shared.",
            $"Spaces share failed with error {result}.");

        OVRTask.SetResult(requestId, result);
        InvokeMultiAnchorDelegate(requestId, result, MultiAnchorActionType.Share);
    }

    private static class Development
    {
        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Log(string message) => Debug.Log($"[{nameof(OVRSpatialAnchor)}] {message}");

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void LogWarning(string message) => Debug.LogWarning($"[{nameof(OVRSpatialAnchor)}] {message}");

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void LogError(string message) => Debug.LogError($"[{nameof(OVRSpatialAnchor)}] {message}");

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void LogRequestOrError(ulong requestId, OVRPlugin.Result result, string successMessage,
            string failureMessage)
        {
            if (result.IsSuccess())
            {
                LogRequest(requestId, successMessage);
            }
            else
            {
                LogError(failureMessage);
            }
        }

#if DEVELOPMENT_BUILD
        private static readonly HashSet<ulong> _requests = new HashSet<ulong>();
#endif // DEVELOPMENT_BUILD

        [Conditional("DEVELOPMENT_BUILD")]
        public static void LogRequest(ulong requestId, string message)
        {
#if DEVELOPMENT_BUILD
            _requests.Add(requestId);
#endif // DEVELOPMENT_BUILD
            Log($"({requestId}) {message}");
        }

        [Conditional("DEVELOPMENT_BUILD")]
        public static void LogRequestResult(ulong requestId, bool result, string successMessage, string failureMessage)
        {
#if DEVELOPMENT_BUILD
            // Not a request we're tracking
            if (!_requests.Remove(requestId)) return;
#endif // DEVELOPMENT_BUILD
            if (result)
            {
                Log($"({requestId}) {successMessage}");
            }
            else
            {
                LogError($"({requestId}) {failureMessage}");
            }
        }
    }

    /// <summary>
    /// Possible results of operations related to anchor sharing.
    /// </summary>
    [OVRResultStatus]
    public enum OperationResult
    {
        /// <summary>Operation succeeded.</summary>
        Success = OVRPlugin.Result.Success,

        /// <summary>Operation failed.</summary>
        Failure = OVRPlugin.Result.Failure,

        /// <summary>Invalid data.</summary>
        Failure_DataIsInvalid = OVRPlugin.Result.Failure_DataIsInvalid,

        /// <summary>Saving anchors to cloud storage is not permitted by the user.</summary>
        Failure_SpaceCloudStorageDisabled = OVRPlugin.Result.Failure_SpaceCloudStorageDisabled,

        /// <summary>
        /// The user was able to download the anchors, but the device was unable to localize
        /// itself in the spatial data received from the sharing device.
        /// </summary>
        Failure_SpaceMappingInsufficient = OVRPlugin.Result.Failure_SpaceMappingInsufficient,

        /// <summary>
        /// The user was able to download the anchors, but the device was unable to localize them.
        /// </summary>
        Failure_SpaceLocalizationFailed = OVRPlugin.Result.Failure_SpaceLocalizationFailed,

        /// <summary>Network operation timed out.</summary>
        Failure_SpaceNetworkTimeout = OVRPlugin.Result.Failure_SpaceNetworkTimeout,

        /// <summary>Network operation failed.</summary>
        Failure_SpaceNetworkRequestFailed = OVRPlugin.Result.Failure_SpaceNetworkRequestFailed,
    }

    /// <summary>
    /// This struct helped inverting callback signature
    /// when using OVRTasks. OVRTasks expect <c>Action{TResult, TCapture}</c> signature
    /// but public API requires <c>Action{TCapture, TResult}</c> signature.
    /// </summary>
    private readonly struct InvertedCapture<TResult, TCapture>
    {
        private static readonly Action<TResult, InvertedCapture<TResult, TCapture>> Delegate = Invoke;

        private readonly TCapture _capture;
        private readonly Action<TCapture, TResult> _callback;

        private InvertedCapture(Action<TCapture, TResult> callback, TCapture capture)
        {
            _callback = callback;
            _capture = capture;
        }

        private static void Invoke(TResult result, InvertedCapture<TResult, TCapture> invertedCapture)
        {
            invertedCapture._callback?.Invoke(invertedCapture._capture, result);
        }

        public static void ContinueTaskWith(OVRTask<TResult> task, Action<TCapture, TResult> onCompleted,
            TCapture state)
        {
            task.ContinueWith(Delegate, new InvertedCapture<TResult, TCapture>(onCompleted, state));
        }
    }
}

public static class OperationResultExtensions
{
    public static bool IsSuccess(this OVRSpatialAnchor.OperationResult res) => res == OVRSpatialAnchor.OperationResult.Success;
    public static bool IsError(this OVRSpatialAnchor.OperationResult res) => res < 0;
    public static bool IsWarning(this OVRSpatialAnchor.OperationResult res) => res > 0;
}

/// <summary>
/// Represents a user for purposes of sharing scene anchors
/// </summary>
public struct OVRSpaceUser : IDisposable
{
    internal ulong _handle;

    /// <summary>
    /// Checks if the user is valid
    /// </summary>
    public bool Valid => _handle != 0 && Id != 0;

    /// <summary>
    /// Creates a space user handle for given Facebook user ID
    /// </summary>
    /// <param name="spaceUserId">The Facebook user ID obtained from the other party over the network</param>
    public OVRSpaceUser(ulong spaceUserId)
    {
        OVRPlugin.CreateSpaceUser(spaceUserId, out _handle);
    }

    /// <summary>
    /// The user ID associated with this <see cref="OVRSpaceUser"/>.
    /// </summary>
    public ulong Id => OVRPlugin.GetSpaceUserId(_handle, out var userId) ? userId : 0;

    /// <summary>
    /// Disposes of the <see cref="OVRSpaceUser"/>.
    /// </summary>
    /// <remarks>
    /// This method does not destroy the user account. It disposes the handle used to reference it.
    /// </remarks>
    public void Dispose()
    {
        OVRPlugin.DestroySpaceUser(_handle);
        _handle = 0;
    }
}
