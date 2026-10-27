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
	/// A complete AprilTag colocation strategy with two modes.
	/// <para>Registered tags: the authority synchronizes registered
	/// tag/canon-pose pairs; each peer creates and persists its own local anchor at each tag it
	/// sees. The colocator receives only those anchor/canon-pose pairs, never raw tag readings,
	/// so alignment roughly survives when the device loses & regains tracking.
	/// Later readings correct for anchor drift
	/// by preserving its currently observed offset from the physical tag.</para>
	/// <para>Two tags (<see cref="TwoTagMode"/>), for spaces with no registered tags: the lower
	/// of two detected IDs defines world zero; the line to the higher defines +Z. Gravity defines
	/// +Y. Tag rotations, registrations, and saved map references are unused. Observations live
	/// only in the current tracking frame. Invalidate them when that frame or alignment is lost;
	/// both tags must then be seen again. No anchors are created or used.</para>
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

		private sealed class PairReference
		{
			public readonly int id;
			public bool observed;
			public Vector3 position;
			public Vector3 positionSum;
			public int samples;

			public PairReference(int id) => this.id = id;
		}

		public const float MinimumHorizontalSeparation = 0.1f;

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
			ResetPairReferences();

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

		public event Action TagsChanged = delegate { };

		/// <summary>
		/// The size tags are being solved at has changed, from either half of
		/// <see cref="TagSizeCm"/>. Registered poses are only valid at the size they were solved
		/// at, so whatever persists them has to hear about this.
		/// </summary>
		public event Action TagSizeChanged = delegate { };
		public event Action AnchorsChanged = delegate { };
		public event Action<int, Pose> TagObserved = delegate { };

		/// <summary>Two-tag mode chose its pair from the first two IDs it saw.</summary>
		public event Action<int, int> PairSelected = delegate { };

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

		private int configuredFirst = -1;
		private int configuredSecond = -1;
		private PairReference pairOrigin;
		private PairReference pairForward;
		private long ignorePairFramesThrough;
		private bool twoTagMode;

		public int? OriginTagId => pairOrigin?.id;
		public int? ForwardTagId => pairForward?.id;

		/// <summary>
		/// Whether this provider's own constraints come from a tag pair rather than registered
		/// tags' anchors. Registered tags are still realized as anchors in either mode.
		/// </summary>
		public bool TwoTagMode
		{
			get => twoTagMode;
			set
			{
				if (twoTagMode == value) return;
				twoTagMode = value;
				ResetPairReferences();
			}
		}

		/// <summary>Two-tag observations are dropped, and the pair forgotten, while this is false.</summary>
		public Func<bool> TrackingReady { get; set; } = () => true;

		/// <summary>The frame pair observations are kept in, resolved on use.</summary>
		public Func<Transform> TrackingSpaceSource { get; set; } =
			() => MainXRRig.Instance != null ? MainXRRig.TrackingSpace : null;

		private Transform PairTrackingSpace => TrackingSpaceSource?.Invoke();

		private AnchorRegistry registry;
		private CancellationTokenSource lifetimeCtknSrc;
		private int stateGeneration;
		private bool detectionOverride;

		/// <summary>
		/// Tag colocation needs both halves: a detector to see the tags with, and an anchor
		/// runtime to realize what it sees. Without either, no reference can ever be observed.
		/// Two-tag mode needs only the detector and a tracking space.
		/// </summary>
		public bool IsAvailable => tagTracker != null && (twoTagMode ? PairTrackingSpace != null : AnchorsAvailable);

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
			registeredTags.ValidateSet = (_, _, _) => false;
			registeredTags.ValidateRemove = (_, _) => false;
			registeredTags.ValidateClear = _ => false;
			registeredTags.Register();
			registeredTags.Changed += OnTagsChanged;

			tagSizeSync.Validate = (_, _) => false;
			tagSizeSync.Register();
			tagSizeSync.Changed += OnTagSizeChanged;
			SyncBus.Activated += OnBusActivated;
			MainXRRig.Recentered += InvalidatePendingObservations;

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
			MainXRRig.Recentered -= InvalidatePendingObservations;
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
			ResetPairReferences();
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
			ResetPairReferences();

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
			ResetPairReferences();
			TagSizeChanged.Invoke();
		}

		private void OnApplicationFocus(bool focused)
		{
			if (AppFocus.IsLoss(focused))
				InvalidatePendingObservations();
		}
		private void OnApplicationPause(bool paused) { if (paused) InvalidatePendingObservations(); }
		private void InvalidatePendingObservations() { stateGeneration++; corrections.Clear(); ResetPairReferences(); }

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

			if (twoTagMode)
			{
				GetPairConstraints(results);
				return;
			}

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
				ObservePair(observed.ID, observedPose);

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
			if (!AnchorsAvailable || !MainXRRig.Instance || !MainXRRig.TrackingSpace || !mintsInFlight.Add(tagId))
				return;

			int generation = stateGeneration;
			Transform trackingSpace = MainXRRig.TrackingSpace;
			Pose trackingTag = new(trackingSpace.InverseTransformPoint(observedTag.position),
				Quaternion.Inverse(trackingSpace.rotation) * observedTag.rotation);

			try
			{
				await AnchorMinting.TryMintAsync(registry, observedTag,
					minted => CommitTagAnchor(tagId, canonTag, trackingTag, trackingSpace, generation, minted, replacing),
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
		private bool CommitTagAnchor(int tagId, Pose canonTag, Pose trackingTag, Transform trackingSpace,
			int generation, MintedAnchor minted, LocalAnchor replacing)
		{
			if (!IsRunning || generation != stateGeneration ||
			    !registeredTags.ContainsKey(tagId) || !trackingSpace)
				return false;

			localAnchors.TryGetValue(tagId, out LocalAnchor current);
			if (!ReferenceEquals(current, replacing) || IsTracking(current))
				return false;
			AnchorHandle handle = minted.lease?.Handle;
			if (handle == null || handle.state != AnchorHandle.State.Active || !handle.anchor || handle.anchor.trackingState != TrackingState.Tracking)
				return false;

			// Native creation need not preserve the requested tag pose. Relate the actual
			// anchor to the tag in one current frame, even if the rig moved during the await.
			Pose observedTag = new(trackingSpace.TransformPoint(trackingTag.position), trackingSpace.rotation * trackingTag.rotation);
			Pose canonAnchor = CanonicalAnchorPose(canonTag, observedTag, handle.anchor.transform);

			// A saved UUID can fail to restore indefinitely. Keep it until a fresh observation
			// has produced a replacement, and reject that replacement if the old anchor
			// recovered while minting. The map layer owns cleanup of orphaned saves.
			localAnchors[tagId] = new LocalAnchor
			{
				guid = minted.guid,
				tagId = tagId,
				canon = canonAnchor,
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
		private static Pose CanonicalAnchorPose(Pose canonTag, Pose observedTag, Transform observedAnchor)
		{
			Quaternion rotation = canonTag.rotation * Quaternion.Inverse(observedTag.rotation);
			return new Pose(canonTag.position + rotation * (observedAnchor.position - observedTag.position),
				rotation * observedAnchor.rotation);
		}

		/// <summary>
		/// canonAnchor := canonTag * inverse(observedTag) * observedAnchor. The relative
		/// observation is alignment-invariant, so the current rig correction cancels out.
		/// </summary>
		private void CorrectAnchor(LocalAnchor anchor, Pose observedTag, Pose canonTag)
		{
			if (!IsTracking(anchor))
				return;

			Pose corrected = CanonicalAnchorPose(canonTag, observedTag, anchor.lease.Handle.anchor.transform);
			Quaternion correctedRotation = corrected.rotation;
			if (!corrections.TryGetValue(anchor.tagId, out TagCorrection correction))
			{
				correction = new TagCorrection();
				corrections.Add(anchor.tagId, correction);
			}

			Vector4 rotationVector = new(correctedRotation.x, correctedRotation.y,
				correctedRotation.z, correctedRotation.w);
			if (correction.samples > 0 && Vector4.Dot(correction.rotationSum, rotationVector) < 0f)
				rotationVector = -rotationVector;

			correction.positionSum += corrected.position;
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

		// ------- two-tag mode ------------------------------------

		/// <summary>Reacquires the configured pair, discarding only its transient observations.</summary>
		public void ResetPairReferences()
		{
			pairOrigin = pairForward = null;
			// Detection is asynchronous. A frame already being processed at invalidation
			// must not supply observations in the replacement tracking frame.
			ignorePairFramesThrough = tagTracker != null ? tagTracker.FrameTimestampNs : 0;
		}

		/// <summary>Restricts two-tag mode to this pair, or with no valid pair, to the first two IDs seen.</summary>
		public void ConfigurePair(int first, int second)
		{
			if (first >= 0 && second >= 0 && first != second)
			{ int low = Math.Min(first, second); second = Math.Max(first, second); first = low; }
			else first = second = -1;
			if (configuredFirst == first && configuredSecond == second) return;
			configuredFirst = first; configuredSecond = second; ResetPairReferences();
		}

		private bool CanObservePair()
		{
			if (!IsRunning || !twoTagMode) return false;
			if (IsAvailable && (TrackingReady?.Invoke() ?? true)) return true;
			ResetPairReferences();
			return false;
		}

		private void ObservePair(int id, Pose pose)
		{
			if (!CanObservePair() || tagTracker.FrameTimestampNs <= ignorePairFramesThrough ||
				!IsFinite(pose.position)) return;
			Vector3 position = PairTrackingSpace.InverseTransformPoint(pose.position);
			if (!IsFinite(position)) return;
			PairReference reference = SelectPairReference(id);
			if (reference == null) return;

			if (!reference.observed)
			{
				reference.position = position;
				reference.observed = true;
				return;
			}

			// Rig corrections move world space; averaging in tracking space avoids feeding
			// those corrections back into the observations.
			reference.positionSum += position;
			if (++reference.samples < Mathf.Max(1, correctionSamples)) return;
			reference.position = reference.positionSum / reference.samples;
			reference.positionSum = Vector3.zero;
			reference.samples = 0;
		}

		private PairReference SelectPairReference(int id)
		{
			if (id < 0) return null;
			if (configuredFirst >= 0)
			{
				if (id == configuredFirst) return pairOrigin ??= new PairReference(id);
				if (id == configuredSecond) return pairForward ??= new PairReference(id);
				return null;
			}
			if (pairOrigin == null) return pairOrigin = new PairReference(id);
			if (id == pairOrigin.id) return pairOrigin;
			if (pairForward == null)
			{
				pairForward = new PairReference(id);
				if (pairOrigin.id > pairForward.id) (pairOrigin, pairForward) = (pairForward, pairOrigin);
			}
			return id == pairOrigin.id ? pairOrigin : id == pairForward.id ? pairForward : null;
		}

		private void GetPairConstraints(List<ColocationConstraint> results)
		{
			if (!CanObservePair() || pairOrigin?.observed != true || pairForward?.observed != true) return;

			Transform trackingSpace = PairTrackingSpace;
			if (TryCreatePairConstraints(trackingSpace.TransformPoint(pairOrigin.position),
				trackingSpace.TransformPoint(pairForward.position), out var first, out var second))
			{
				if (configuredFirst < 0)
				{
					var selectedOrigin = pairOrigin;
					var selectedForward = pairForward;
					configuredFirst = pairOrigin.id; configuredSecond = pairForward.id;
					PairSelected.Invoke(configuredFirst, configuredSecond);
					// The session may reject this pair or replace its context synchronously.
					if (!IsRunning || !twoTagMode || pairOrigin != selectedOrigin || pairForward != selectedForward) return;
				}
				results.Add(first);
				results.Add(second);
			}
		}

		/// <summary>Preserves measured scale and height difference while constraining yaw.</summary>
		public static bool TryCreatePairConstraints(Vector3 originPosition, Vector3 forwardPosition,
			out ColocationConstraint first, out ColocationConstraint second)
		{
			first = second = default;
			Vector3 delta = forwardPosition - originPosition;
			if (!IsFinite(originPosition) || !IsFinite(forwardPosition) || !IsFinite(delta)) return false;
			float distance = new Vector2(delta.x, delta.z).magnitude;
			if (float.IsInfinity(distance) || distance < MinimumHorizontalSeparation) return false;

			first = new ColocationConstraint(new Pose(originPosition, Quaternion.identity), Pose.identity);
			second = new ColocationConstraint(new Pose(forwardPosition, Quaternion.identity),
				new Pose(new Vector3(0, delta.y, distance), Quaternion.identity));
			return true;
		}

		private static bool IsFinite(Vector3 value) =>
			IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

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
