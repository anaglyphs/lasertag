using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using AprilTag;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

namespace Anaglyph.XR.SharedSpaces.AprilTags
{
	public readonly struct TagConstraintData
	{
		public TagConstraintData(int tagId, Pose canonPose)
		{
			this.tagId = tagId;
			this.canonPose = canonPose;
		}

		public readonly int tagId;
		public readonly Pose canonPose;
	}

	public readonly struct TaggedAnchorConstraintData
	{
		public TaggedAnchorConstraintData(Guid guid, int tagId, Pose canonPose)
		{
			this.guid = guid;
			this.tagId = tagId;
			this.canonPose = canonPose;
		}

		public readonly Guid guid;
		public readonly int tagId;
		public readonly Pose canonPose;
	}

	/// <summary>
	/// A complete AprilTag colocation strategy. The authority synchronizes registered
	/// tag/canon-pose pairs; each peer creates and persists its own local anchor at each tag it
	/// sees. The colocator receives only those anchor/canon-pose pairs, never raw tag readings,
	/// so alignment roughly survives when the device loses & regains tracking.
	/// Later readings correct for anchor drift
	/// by preserving its currently observed offset from the physical tag.
	/// </summary>
	[DefaultExecutionOrder(-200)]
	public class AprilTagColocationConstraintProvider : MonoBehaviour, IColocationConstraintProvider
	{
		public static AprilTagColocationConstraintProvider Instance { get; private set; }

		private sealed class LocalAnchor
		{
			public Guid guid;
			public int tagId;
			public Pose canon;
			public AnchorLease lease;
		}

		private sealed class TagCorrection
		{
			public Vector3 positionSum;
			public Vector4 rotationSum;
			public int samples;
		}

		// Whatever registered the tags owns their size, so this is only the last size handed to
		// this device. It stands in until a session publishes one, and is what an authority
		// publishes when it opens the session.
		private float adoptedTagSizeCm;
		private readonly SyncVariable<float> tagSizeSync = new("colocation.tags.size");
		public float TagSizeCm => tagSizeSync.Value > 0f
			? tagSizeSync.Value
			: adoptedTagSizeCm;

		/// <summary>
		/// Adopts the size the tags being loaded were registered at. The session authority also
		/// publishes it, so every peer solves at the size the session's references were solved at.
		/// </summary>
		public void AdoptTagSize(float centimeters)
		{
			float next = Mathf.Max(0f, centimeters);
			if (Mathf.Approximately(next, adoptedTagSizeCm))
				return;

			adoptedTagSizeCm = next;

			// References may be swapped after the session is already running. Keep the canonical
			// session value and the local tracker in step immediately.
			if (SyncBus.Active && SyncBus.IsAuthority)
				tagSizeSync.Value = adoptedTagSizeCm;

			UpdateTrackerEnabled();
			TagSizeChanged.Invoke();
		}

		private readonly SyncDictionary<int, Pose> registeredTags =
			new("colocation.tags.canon");
		public IReadOnlyDictionary<int, Pose> RegisteredTags => registeredTags;
		public int LocalAnchorCount => localAnchors.Count;
		public Func<ulong, int, Pose, bool> RegistrationGate { get; set; }

		public event Action TagsChanged = delegate { };

		/// <summary>
		/// The size tags are being solved at has changed, from either half of
		/// <see cref="TagSizeCm"/>. Registered poses are only valid at the size they were solved
		/// at, so whatever persists them has to hear about this.
		/// </summary>
		public event Action TagSizeChanged = delegate { };
		public event Action AnchorsChanged = delegate { };
		public event Action<int, Pose> TagObserved = delegate { };

		[Tooltip("How near the tag must be to trust an observation, as a multiple of tag size")]
		[SerializeField] private float lockDistanceScale = 10f;

		[Tooltip("In meters/second")]
		[SerializeField] private float maxHeadSpeed = 2f;

		[Tooltip("In radians/second")]
		[SerializeField] private float maxHeadAngSpeed = 2f;

		[Tooltip("Tag observations averaged before an anchor's canon pose is rewritten")]
		[SerializeField] private int correctionSamples = 30;

		[SerializeField] private AprilTagTracker tagTracker;
		public AprilTagTracker TagTracker => tagTracker;

		private readonly Dictionary<int, LocalAnchor> localAnchors = new();
		private readonly Dictionary<int, TagCorrection> corrections = new();
		private readonly HashSet<int> mintsInFlight = new();
		private readonly List<int> tagIdScratch = new();
		// Reconciliation runs reentrantly from tag removals, so it cannot share tagIdScratch
		// with the import operations that trigger it.
		private readonly List<int> anchorRemovalScratch = new();
		private readonly List<TaggedAnchorConstraintData> anchorScratch = new();

		private AnchorRegistry registry;
		private CancellationTokenSource lifetimeCtknSrc;
		private int stateGeneration;
		private bool detectionOverride;

		/// <summary>
		/// Tag colocation needs both halves: a detector to see the tags with, and an anchor
		/// runtime to realize what it sees. Without either, no reference can ever be observed.
		/// </summary>
		public bool IsAvailable => AnchorsAvailable && tagTracker != null;

		private bool AnchorsAvailable => registry != null && registry.IsAvailable;
		public bool IsRunning { get; private set; }
		public bool IsDetecting => tagTracker != null && tagTracker.enabled;

		private void Awake()
		{
			Instance = this;
			registry = AnchorRegistry.Instance ?? FindFirstObjectByType<AnchorRegistry>();
			if (registry == null)
				Debug.LogError("TagConstraintProvider requires an AnchorRegistry in the scene.", this);

			lifetimeCtknSrc = new CancellationTokenSource();

			if (!tagTracker)
				tagTracker = FindAnyObjectByType<AprilTagTracker>();

			registeredTags.ResetOnDeactivate = false;
			registeredTags.ValidateSet = ValidateTagRegistration;
			registeredTags.Register();
			registeredTags.Changed += OnTagsChanged;

			tagSizeSync.Validate = ValidateTagSize;
			tagSizeSync.Register();
			tagSizeSync.Changed += OnTagSizeChanged;
			SyncBus.Activated += OnBusActivated;

			if (tagTracker != null)
				tagTracker.OnDetectTags += OnDetectTags;
		}

		private void Start()
		{
			UpdateTrackerEnabled();
		}

		private void OnDestroy()
		{
			StopProviding();
			lifetimeCtknSrc?.Cancel();

			if (tagTracker != null)
				tagTracker.OnDetectTags -= OnDetectTags;

			SyncBus.Activated -= OnBusActivated;
			tagSizeSync.Changed -= OnTagSizeChanged;
			tagSizeSync.Unregister();

			registeredTags.Changed -= OnTagsChanged;
			registeredTags.Unregister();

			if (Instance == this)
				Instance = null;
		}

		// ------- provider and detector lifecycle -----------------

		public void StartProviding()
		{
			if (IsRunning)
				return;

			IsRunning = true;
			stateGeneration++;
			ReconcileAnchors();
			UpdateTrackerEnabled();
		}

		public void StopProviding()
		{
			if (!IsRunning)
				return;

			IsRunning = false;
			stateGeneration++;
			corrections.Clear();

			foreach (LocalAnchor anchor in localAnchors.Values)
			{
				anchor.lease?.Dispose();
				anchor.lease = null;
			}

			UpdateTrackerEnabled();
		}

		/// <summary>
		/// Enables tag detection for map authoring without activating this colocation provider.
		/// Observations are reported, but no anchors are created or corrected.
		/// </summary>
		public void SetDetectionOverride(bool enabled)
		{
			detectionOverride = enabled;
			UpdateTrackerEnabled();
		}

		private void UpdateTrackerEnabled()
		{
			if (tagTracker == null)
				return;

			tagTracker.tagSizeMeters = EffectiveTagSizeMeters();
			tagTracker.enabled = IsRunning || detectionOverride;
		}

		private float EffectiveTagSizeMeters()
		{
			return TagSizeCm / 100f;
		}

		private void OnBusActivated()
		{
			if (SyncBus.IsAuthority)
			{
				if (adoptedTagSizeCm > 0f)
					tagSizeSync.Value = adoptedTagSizeCm;
			}
			else
			{
				// Tag anchors are private realizations of one map. A joining peer must not
				// carry an unrelated offline map's same-numbered tags into the new session;
				// a persistence adapter may restore matching local anchors after map identity.
				SetLocalAnchors(Array.Empty<TaggedAnchorConstraintData>());
			}
		}

		private void OnTagSizeChanged(float _, float __)
		{
			UpdateTrackerEnabled();
			TagSizeChanged.Invoke();
		}

		private void OnApplicationFocus(bool focused)
		{
			if (!focused)
				corrections.Clear();
		}

		// ------- state import/export ------------------------------

		/// <summary>
		/// Replaces registered tags and this device's private tag anchors. Only an offline peer
		/// or session authority may inject registered tags; clients receive them from provider sync.
		/// </summary>
		public void SetConstraints(IEnumerable<TagConstraintData> tags,
			IEnumerable<TaggedAnchorConstraintData> anchors)
		{
			SetRegisteredTags(tags);
			SetLocalAnchors(anchors);
		}

		/// <summary>
		/// Proposes one tag registration from any peer. The authority validates it, applies it and
		/// broadcasts, so every peer's registered set stays the authority's — including the
		/// requester's, which is written by the broadcast rather than optimistically.
		/// </summary>
		public void RequestRegisterTag(int tagId, Pose canonPose)
		{
			registeredTags.RequestSet(tagId, canonPose);
		}

		/// <summary>
		/// Proposes removing one registered tag, from any peer. Every peer then drops the anchor it
		/// had realized for it — see <see cref="DropUnregisteredAnchors"/> — so no peer keeps
		/// aligning to a reference the session no longer has.
		/// </summary>
		public void RequestUnregisterTag(int tagId)
		{
			registeredTags.RequestRemove(tagId);
		}

		/// <summary>
		/// Proposes the size tags are solved at, from any peer. Validated like any other request,
		/// including on the authority: this is a deliberate change to what the references mean,
		/// unlike <see cref="AdoptTagSize"/> restoring the size they were already solved at.
		/// </summary>
		public void RequestTagSize(float centimeters)
		{
			tagSizeSync.Request(Mathf.Max(0f, centimeters));
		}

		/// <summary>
		/// Every registered pose was solved at the session's current size, so changing it would
		/// move all of them at once. Adoption is exempt: loading references has to restore the
		/// size they were solved at.
		/// </summary>
		private bool ValidateTagSize(ulong sender, float centimeters)
		{
			return centimeters > 0f && IsFinite(centimeters) && registeredTags.Count == 0;
		}

		/// <summary>
		/// A registered pose becomes every peer's alignment target and is written into their maps,
		/// so a garbage one would take the whole session's frame with it.
		/// </summary>
		private bool ValidateTagRegistration(ulong sender, int tagId, Pose canonPose)
		{
			Quaternion rotation = canonPose.rotation;

			return (RegistrationGate == null || RegistrationGate(sender, tagId, canonPose)) && tagId >= 0 &&
			       IsFinite(canonPose.position.x) && IsFinite(canonPose.position.y) &&
			       IsFinite(canonPose.position.z) &&
			       IsFinite(rotation.x) && IsFinite(rotation.y) &&
			       IsFinite(rotation.z) && IsFinite(rotation.w);
		}

		private static bool IsFinite(float value) =>
			!float.IsNaN(value) && !float.IsInfinity(value);

		public void SetRegisteredTags(IEnumerable<TagConstraintData> tags)
		{
			if (!SyncBus.IsAuthority)
			{
				Debug.LogWarning("Trying to set tag constraints while not the authority!");
				return;
			}

			List<KeyValuePair<int, Pose>> next = new();
			foreach (TagConstraintData entry in tags)
				next.Add(new KeyValuePair<int, Pose>(entry.tagId, entry.canonPose));

			registeredTags.ReplaceAll(next, (a, b) => a == b);
		}

		/// <summary>Replaces only this device's private tag-to-anchor realizations.</summary>
		public void SetLocalAnchors(IEnumerable<TaggedAnchorConstraintData> anchors)
		{
			anchorScratch.Clear();
			anchorScratch.AddRange(anchors);
			stateGeneration++;
			corrections.Clear();

			tagIdScratch.Clear();
			foreach ((int tagId, LocalAnchor existing) in localAnchors)
			{
				bool retained = false;
				foreach (TaggedAnchorConstraintData entry in anchorScratch)
					if (entry.tagId == tagId && entry.guid == existing.guid)
					{
						retained = true;
						break;
					}

				if (!retained)
					tagIdScratch.Add(tagId);
			}

			foreach (int tagId in tagIdScratch)
			{
				localAnchors[tagId].lease?.Dispose();
				localAnchors.Remove(tagId);
			}

			foreach (TaggedAnchorConstraintData entry in anchorScratch)
			{
				if (entry.tagId < 0)
					continue;

				if (localAnchors.TryGetValue(entry.tagId, out LocalAnchor existing))
				{
					existing.canon = entry.canonPose;
					continue;
				}

				localAnchors.Add(entry.tagId, new LocalAnchor
				{
					guid = entry.guid,
					tagId = entry.tagId,
					canon = entry.canonPose,
				});
			}

			if (IsRunning)
				ReconcileAnchors();

			AnchorsChanged.Invoke();
		}

		public void GetLocalAnchorConstraints(List<TaggedAnchorConstraintData> results)
		{
			foreach (LocalAnchor anchor in localAnchors.Values)
				results.Add(new TaggedAnchorConstraintData(
					anchor.guid, anchor.tagId, anchor.canon));
		}

		private void OnTagsChanged(SyncDictionary<int, Pose>.EventData _)
		{
			stateGeneration++;
			if (IsRunning)
				ReconcileAnchors();

			TagsChanged.Invoke();
		}

		// ------- anchor constraints -------------------------------

		private void ReconcileAnchors()
		{
			if (!IsRunning)
				return;

			bool dropped = DropUnregisteredAnchors();

			if (AnchorsAvailable)
				foreach (LocalAnchor anchor in localAnchors.Values)
					anchor.lease ??= registry.Acquire(
						new SerializableGuid(anchor.guid), AnchorSource.Local);

			if (dropped)
				AnchorsChanged.Invoke();
		}

		/// <summary>
		/// Forgets the anchors of tags that are no longer registered — bookkeeping, so it happens
		/// with or without an anchor runtime. An unregistered tag's anchor realizes nothing:
		/// keeping the entry would keep exporting a constraint whose tag is gone, and would hold
		/// the tag id against a fresh mint if the tag comes back. The device's saved anchor is
		/// deliberately left alone; only the embedding map layer knows whether some other map
		/// still needs it.
		/// </summary>
		private bool DropUnregisteredAnchors()
		{
			anchorRemovalScratch.Clear();
			foreach (LocalAnchor anchor in localAnchors.Values)
				if (!registeredTags.ContainsKey(anchor.tagId))
					anchorRemovalScratch.Add(anchor.tagId);

			foreach (int tagId in anchorRemovalScratch)
			{
				if (!localAnchors.Remove(tagId, out LocalAnchor dropped))
					continue;

				dropped.lease?.Dispose();
				// A half-averaged correction must not carry over onto a later anchor for the
				// same tag.
				corrections.Remove(tagId);
			}

			return anchorRemovalScratch.Count > 0;
		}

		public void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (!IsRunning)
				return;

			foreach (LocalAnchor entry in localAnchors.Values)
			{
				AnchorHandle handle = entry.lease?.Handle;
				if (handle == null || handle.state != AnchorHandle.State.Active) continue;
				if (handle.anchor.trackingState != TrackingState.Tracking) continue;

				Transform t = handle.anchor.transform;
				results.Add(new ColocationConstraint(
					new Pose(t.position, t.rotation), entry.canon, hasReliableRotation: true));
			}
		}

		// ------- tag observations --------------------------------

		private void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			if (!IsDetecting)
				return;

			Vector3 headVelocity = default;
			Vector3 headAngularVelocity = default;
			bool gotVelocity = HeadPoseHistory.Instance != null &&
				HeadPoseHistory.Instance.TryGetVelocity(tagTracker.FrameTimestampNs,
					out headVelocity, out headAngularVelocity);

			// The speed limits exist because a moving head smears the camera image and shifts
			// the pose the reading is paired with. A rendered simulator frame has neither
			// problem, so gating on it would only make authoring against a simulator harder.
			bool headIsStable = !gotVelocity || tagTracker.FrameIsRendered ||
				(headVelocity.magnitude < maxHeadSpeed &&
				 headAngularVelocity.magnitude < maxHeadAngSpeed);

			if (!headIsStable || MainXRRig.Camera == null)
				return;

			Vector3 headPosition = MainXRRig.Camera.transform.position;
			float lockDistance = EffectiveTagSizeMeters() * lockDistanceScale;

			foreach (TagPose observed in results)
			{
				if (Vector3.Distance(headPosition, observed.Position) >= lockDistance)
					continue;

				Pose observedPose = new(observed.Position, observed.Rotation);
				TagObserved.Invoke(observed.ID, observedPose);

				if (!IsRunning ||
				    !registeredTags.TryGetValue(observed.ID, out Pose canonTag))
					continue;

				localAnchors.TryGetValue(observed.ID, out LocalAnchor anchor);
				if (!IsTracking(anchor))
					MintTagAnchor(observed.ID, observedPose, canonTag, anchor);
				else
					CorrectAnchor(anchor, observedPose, canonTag);
			}
		}

		private async void MintTagAnchor(int tagId, Pose observedTag, Pose canonTag, LocalAnchor replacing)
		{
			if (!AnchorsAvailable || !mintsInFlight.Add(tagId))
				return;

			int generation = stateGeneration;

			try
			{
				await AnchorMinting.TryMintAsync(registry, observedTag,
					minted => CommitTagAnchor(tagId, canonTag, generation, minted, replacing),
					commitTakesLease: true, lifetimeCtknSrc.Token);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			finally
			{
				mintsInFlight.Remove(tagId);
			}
		}

		/// <summary>
		/// Takes a minted anchor as this tag's realization, unless the state it was minted for has
		/// moved on. Refusing it erases the save, so the tag is minted again next time it is seen.
		/// </summary>
		private bool CommitTagAnchor(int tagId, Pose canonTag, int generation, MintedAnchor minted, LocalAnchor replacing)
		{
			if (!IsRunning || generation != stateGeneration ||
			    !registeredTags.ContainsKey(tagId))
				return false;

			localAnchors.TryGetValue(tagId, out LocalAnchor current);
			if (!ReferenceEquals(current, replacing) || IsTracking(current))
				return false;

			// A saved UUID can fail to restore indefinitely. Keep it until a fresh observation
			// has produced a replacement, and reject that replacement if the old anchor
			// recovered while minting. The map layer owns cleanup of orphaned saves.
			localAnchors[tagId] = new LocalAnchor
			{
				guid = minted.guid,
				tagId = tagId,
				canon = canonTag,
				lease = minted.lease,
			};
			replacing?.lease?.Dispose();
			corrections.Remove(tagId);

			stateGeneration++;
			AnchorsChanged.Invoke();
			return true;
		}

		private static bool IsTracking(LocalAnchor anchor)
		{
			AnchorHandle handle = anchor?.lease?.Handle;
			return handle != null && handle.state == AnchorHandle.State.Active &&
				handle.anchor != null && handle.anchor.trackingState == TrackingState.Tracking;
		}

		/// <summary>
		/// canonAnchor := canonTag * inverse(observedTag) * observedAnchor. The relative
		/// observation is alignment-invariant, so the current rig correction cancels out.
		/// </summary>
		private void CorrectAnchor(LocalAnchor anchor, Pose observedTag, Pose canonTag)
		{
			if (!IsTracking(anchor))
				return;

			Transform anchorTransform = anchor.lease.Handle.anchor.transform;
			Matrix4x4 observedTagMatrix = Matrix4x4.TRS(
				observedTag.position, observedTag.rotation, Vector3.one);
			Matrix4x4 observedAnchorMatrix = Matrix4x4.TRS(
				anchorTransform.position, anchorTransform.rotation, Vector3.one);
			Matrix4x4 canonTagMatrix = Matrix4x4.TRS(
				canonTag.position, canonTag.rotation, Vector3.one);
			Matrix4x4 correctedMatrix =
				canonTagMatrix * (observedTagMatrix.inverse * observedAnchorMatrix);

			Quaternion correctedRotation = correctedMatrix.rotation;
			if (!corrections.TryGetValue(anchor.tagId, out TagCorrection correction))
			{
				correction = new TagCorrection();
				corrections.Add(anchor.tagId, correction);
			}

			Vector4 rotationVector = new(correctedRotation.x, correctedRotation.y,
				correctedRotation.z, correctedRotation.w);
			if (correction.samples > 0 && Vector4.Dot(correction.rotationSum, rotationVector) < 0f)
				rotationVector = -rotationVector;

			correction.positionSum += correctedMatrix.GetPosition();
			correction.rotationSum += rotationVector;
			correction.samples++;

			if (correction.samples < Mathf.Max(1, correctionSamples))
				return;

			Vector4 averageRotation = correction.rotationSum.normalized;
			anchor.canon = new Pose(
				correction.positionSum / correction.samples,
				new Quaternion(averageRotation.x, averageRotation.y,
					averageRotation.z, averageRotation.w));

			corrections.Remove(anchor.tagId);
			AnchorsChanged.Invoke();
		}

		// ------- persistence utilities ---------------------------

		/// <summary>
		/// Deletes an anchor's local save. Dropping a tag anchor never does this on its own,
		/// because whether the anchor is really unwanted is a question about maps, which this
		/// provider knows nothing about.
		/// </summary>
		public async Awaitable<bool> EraseAsync(Guid guid, CancellationToken ctkn = default)
		{
			return AnchorsAvailable &&
			       await registry.TryEraseSavedAsync(new SerializableGuid(guid), ctkn);
		}
	}
}
