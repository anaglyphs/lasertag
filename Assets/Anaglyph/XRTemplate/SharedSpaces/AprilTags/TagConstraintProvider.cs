using Anaglyph.Netcode;
using Anaglyph.XRTemplate.AprilTags;
using AprilTag;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

namespace Anaglyph.XRTemplate.SharedSpaces
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
	public class TagConstraintProvider : MonoBehaviour, IColocationConstraintProvider
	{
		public static TagConstraintProvider Instance { get; private set; }

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

		[SerializeField] private float tagSizeCmHostSetting;
		public float HostTagSizeCm
		{
			get => tagSizeCmHostSetting;
			set
			{
				tagSizeCmHostSetting = Mathf.Max(0f, value);

				// A host may change this after the session is already running. Keep the
				// canonical session value and the local tracker in step immediately.
				if (SyncBus.Active && SyncBus.IsAuthority)
					tagSizeSync.Value = tagSizeCmHostSetting;

				UpdateTrackerEnabled();
			}
		}
		private readonly SyncVariable<float> tagSizeSync = new("colocation.tags.size");
		public float TagSizeCm => tagSizeSync.Value > 0f
			? tagSizeSync.Value
			: tagSizeCmHostSetting;

		private readonly SyncDictionary<int, Pose> registeredTags =
			new("colocation.tags.canon");
		public IReadOnlyDictionary<int, Pose> RegisteredTags => registeredTags;

		public event Action TagsChanged = delegate { };
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
		private readonly List<TagConstraintData> tagScratch = new();
		private readonly List<TaggedAnchorConstraintData> anchorScratch = new();

		private AnchorRegistry registry;
		private CancellationTokenSource lifetimeCtknSrc;
		private int stateGeneration;
		private bool detectionOverride;

		public bool IsAvailable => registry != null && registry.IsAvailable;
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
			registeredTags.Register();
			registeredTags.Changed += OnTagsChanged;

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
				tagSizeSync.Value = tagSizeCmHostSetting;
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

		public void SetRegisteredTags(IEnumerable<TagConstraintData> tags)
		{
			if (!SyncBus.IsAuthority)
			{
				Debug.LogWarning("Trying to set tag constraints while not the authority!");
				return;
			}

			tagScratch.Clear();
			tagScratch.AddRange(tags);

			tagIdScratch.Clear();
			foreach (int tagId in registeredTags.Keys)
			{
				bool retained = false;
				foreach (TagConstraintData entry in tagScratch)
					if (entry.tagId == tagId)
					{
						retained = true;
						break;
					}

				if (!retained)
					tagIdScratch.Add(tagId);
			}

			foreach (int tagId in tagIdScratch)
				registeredTags.Remove(tagId);

			foreach (TagConstraintData entry in tagScratch)
				if (!registeredTags.TryGetValue(entry.tagId, out Pose existing) ||
				    existing != entry.canonPose)
					registeredTags.Set(entry.tagId, entry.canonPose);
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

			if (IsAvailable)
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

			bool headIsStable = !gotVelocity ||
				(headVelocity.magnitude < maxHeadSpeed &&
				 headAngularVelocity.magnitude < maxHeadAngSpeed);

			#if UNITY_EDITOR
			headIsStable = true;
			#endif

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

				if (!localAnchors.TryGetValue(observed.ID, out LocalAnchor anchor))
					MintTagAnchor(observed.ID, observedPose, canonTag);
				else
					CorrectAnchor(anchor, observedPose, canonTag);
			}
		}

		private async void MintTagAnchor(int tagId, Pose observedTag, Pose canonTag)
		{
			if (!IsAvailable || !mintsInFlight.Add(tagId))
				return;

			int generation = stateGeneration;
			AnchorLease minted = null;
			Guid guid = Guid.Empty;
			bool saved = false;
			bool established = false;

			try
			{
				CancellationToken ctkn = lifetimeCtknSrc.Token;
				minted = await registry.TryMintAsync(observedTag, ctkn);
				if (minted == null)
					return;

				guid = minted.Handle.guid.guid;
				saved = await registry.TrySaveAsync(minted.Handle.anchor, ctkn);
				if (!saved)
					return;

				if (!IsRunning || generation != stateGeneration ||
				    !registeredTags.ContainsKey(tagId) || localAnchors.ContainsKey(tagId))
					return;

				localAnchors.Add(tagId, new LocalAnchor
				{
					guid = guid,
					tagId = tagId,
					canon = canonTag,
					lease = minted,
				});
				minted = null;
				established = true;
				stateGeneration++;
				AnchorsChanged.Invoke();
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
				minted?.Dispose();
				mintsInFlight.Remove(tagId);

				if (saved && !established && registry != null && registry.IsAvailable)
				{
					try
					{
						await registry.TryEraseSavedAsync(
							new SerializableGuid(guid), CancellationToken.None);
					}
					catch (ObjectDisposedException)
					{
					}
				}
			}
		}

		/// <summary>
		/// canonAnchor := canonTag * inverse(observedTag) * observedAnchor. The relative
		/// observation is alignment-invariant, so the current rig correction cancels out.
		/// </summary>
		private void CorrectAnchor(LocalAnchor anchor, Pose observedTag, Pose canonTag)
		{
			AnchorHandle handle = anchor.lease?.Handle;
			if (handle == null || handle.state != AnchorHandle.State.Active ||
			    handle.anchor.trackingState != TrackingState.Tracking)
				return;

			Transform anchorTransform = handle.anchor.transform;
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
			return IsAvailable &&
			       await registry.TryEraseSavedAsync(new SerializableGuid(guid), ctkn);
		}
	}
}
