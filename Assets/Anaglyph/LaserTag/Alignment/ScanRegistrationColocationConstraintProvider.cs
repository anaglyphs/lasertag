using System;
using System.Collections.Generic;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.SharedSpaces;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Colocation by registering this headset's environment scan against the session
	/// authority's. No anchors, tags or platform service: the authority's frame is the
	/// canon frame, a joiner scans its surroundings, receives the authority's chunk
	/// snapshot into a reference volume, and searches for the gravity-aligned
	/// transform that lays its scan onto the reference. Once found, that transform is
	/// the single constraint the <see cref="Colocator"/> aligns the rig with, exactly
	/// as an anchor would be. The authority, and anyone offline, is aligned at once.
	/// </summary>
	public class ScanRegistrationColocationConstraintProvider : MonoBehaviour, IColocationConstraintProvider
	{
		public static ScanRegistrationColocationConstraintProvider Instance { get; private set; }

		public enum Phase { Stopped, Aligned, Searching, Registering, Relocalizing }

		[Header("Attempts")]
		[SerializeField] private int minReferenceChunks = 4;
		[SerializeField] private int minLocalChunks = 3;
		private float nextCoverageLog;

		[Header("Acceptance")]
		// shared with loop closure (SharedSlam) and ScanRegistrationBench
		[SerializeField] private RegistrationGates gates = new();

		/// <summary>When a registration may be attempted and trusted, for everything that registers scans.</summary>
		public RegistrationGates Gates => gates;

		public Phase CurrentPhase
		{
			get => currentPhase;
			private set
			{
				if (currentPhase == value) return;
				currentPhase = value;
				PhaseChanged.Invoke(value);
			}
		}

		private Phase currentPhase = Phase.Stopped;

		/// <summary>Raised after <see cref="CurrentPhase"/> changes, with the new phase.</summary>
		public event Action<Phase> PhaseChanged = delegate { };

		/// <summary>Forwarded from the registration: a search began.</summary>
		public event Action SearchStarted = delegate { };

		/// <summary>Forwarded from the registration: the best pose after each level and after refinement.</summary>
		public event Action<SearchStep> SearchStep = delegate { };

		private void EnsureRegistration(EnvScanner scanner)
		{
			if (registration != null) return;
			registration = new TsdfRegistration(TsdfRegistration.LoadShader(), scanner);
			registration.SearchStarted += () => SearchStarted.Invoke();
			registration.StepCompleted += step => SearchStep.Invoke(step);
		}
		public RegistrationResult LastResult { get; private set; }
		public int ReferenceChunkCount => referenceChunks.Count;
		public event Action<RegistrationResult> Registered = delegate { };

		/// <summary>
		/// True while a joiner needs its own scan to register with, before the map
		/// coordinator would otherwise let it integrate anything
		/// </summary>
		public static bool LocalScanRequested => Instance != null && Instance.CurrentPhase is Phase.Searching or Phase.Registering;

		[Header("Relocalization")]
		// after sleep, tracking loss or a recenter the rig's pose means something else;
		// the sensor is scanned into a scratch volume, in the stale frame, and that scan
		// is registered against the map already held — the same source the initial
		// colocation uses, fused and growing between attempts. The whole map is searched,
		// never a window about the stale alignment: a recenter can turn the frame by any
		// angle, and a window that excludes the true pose returns the room's look-alike
		// (its 180° twin, another corner) with nothing in the window to rival it, so the
		// margin can't object. Measured on saved scans: a ±90° window with the truth 150°
		// away accepted the 180° pose; the global search ranks the truth first
		[SerializeField] private int3 relocalizeScratchChunks = new(4, 4, 4);
		// the winner's lead over the best distinct rival, as a fraction of its own score
		[SerializeField] private float relocalizeMinMarginFraction = 0.15f;

		private EnvVolume scratch;
		private bool alignmentApplied;

		/// <summary>The scratch volume relocalization registers against the map; null unless relocalizing.</summary>
		public EnvVolume ScratchVolume => scratch;
		private bool relocalizeInFlight;

		// bumped by every recenter: the sensor's world frame moved, so any scan fused before
		// it, and any registration still running on one, describes a frame that is gone
		private int frameEpoch;

		/// <summary>True while incoming snapshot chunks belong in the reference volume, not the live scan.</summary>
		public bool WantsReferenceChunks => CurrentPhase is Phase.Searching or Phase.Registering;

		private readonly Dictionary<int, NativeArray<EnvScanner.Voxel>> referenceChunks = new();

		/// <summary>The authority's snapshot chunks received while searching, in the authority's frame.</summary>
		public IReadOnlyDictionary<int, NativeArray<EnvScanner.Voxel>> ReferenceChunks => referenceChunks;
		private TsdfRegistration registration;
		private RegistrationVolume reference;

		// the one constraint: where the tracking space sat when the fit was found, and
		// where the registration says it belongs. Identical for the authority
		private Pose observedSpacePose;
		private Pose canonSpacePose;

		/// <summary>Where the registration put the tracking space: the alignment before any drift correction.</summary>
		public Pose CanonSpacePose => canonSpacePose;

		/// <summary>Bumped whenever a new alignment is found (colocation, relocalization, becoming the authority).</summary>
		public int AlignmentGeneration { get; private set; }

		/// <summary>
		/// Pose-graph SLAM's drift correction, applied on top of the registered alignment:
		/// the tracking space is aligned to SlamCorrection ∘ <see cref="CanonSpacePose"/>.
		/// Reset whenever a new alignment is found, since that alignment was registered
		/// against the map as already corrected
		/// </summary>
		public Pose4 SlamCorrection { get; set; } = Pose4.Identity;

		private void NewAlignment()
		{
			AlignmentGeneration++;
			SlamCorrection = Pose4.Identity;
		}

		public bool IsAvailable => EnvScanner.Instance != null && MainXRRig.Instance != null;
		public bool IsRunning => CurrentPhase != Phase.Stopped;

		private void Awake()
		{
			Instance = this;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
			MainXRRig.Recentered += OnRecentered;
		}

		private void OnDestroy()
		{
			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			MainXRRig.Recentered -= OnRecentered;
			StopProviding();
			registration?.Dispose();
			reference?.Dispose();
			if (Instance == this) Instance = null;
		}

		// ---- provider lifecycle ------------------------------------------------

		public void StartProviding()
		{
			if (IsRunning) return;
			EnterRole();
		}

		public void StopProviding()
		{
			if (CurrentPhase != Phase.Stopped)
				Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] stopped (was {CurrentPhase})");
			CurrentPhase = Phase.Stopped;
			ClearReference();
			ReleaseScratch();
		}

		private void OnRecentered()
		{
			frameEpoch++;
			switch (CurrentPhase)
			{
				case Phase.Aligned:
					BeginRelocalize("recenter");
					break;
				case Phase.Relocalizing:
					// the scratch scan so far is in the frame before this recenter; fusing the next
					// frames on top would register a blend of two frames against the map
					RestartScratch();
					Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] recentered while relocalizing: restarting the scratch scan");
					break;
				case Phase.Searching:
				case Phase.Registering:
					// not colocated yet, so the live map is this headset's own scan, fused in the
					// frame before this recenter; the authority's snapshot is unaffected
					if (EnvScanner.Instance != null) EnvScanner.Instance.Clear();
					Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] recentered while searching: restarting the local scan");
					break;
			}
		}

		// the colocator drops to Lost on tracking loss, sleep and recenter; the alignment
		// it keeps applied is stale, and only a fresh registration can say by how much
		private void BeginRelocalize(string why)
		{
			if (CurrentPhase != Phase.Aligned) return;
			EnvScanner scanner = EnvScanner.Instance;
			if (scanner == null) return;

			// nothing mapped yet to register against (XROrigin setting the tracking origin
			// mode at startup raises a recenter): relocalizing would wait forever with the
			// live scan shut. The authority's frame is canon by definition, so it re-anchors
			// on the new one; anyone else looks for the authority again
			EnvMesher mesher = EnvMesher.Instance;
			if (mesher == null || mesher.ChunksList.Count < minLocalChunks)
			{
				Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] {why} with no map to relocalize against: {(SyncBus.IsAuthority ? "re-anchoring" : "searching again")}");
				scanner.Clear();
				if (SyncBus.IsAuthority) AlignIdentity();
				else BeginSearch();
				return;
			}

			CurrentPhase = Phase.Relocalizing;
			RestartScratch();
			Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] relocalizing ({why}): scanning into a scratch volume and registering it against the map");
		}

		// the live integration gate is shut while relocalizing (LocalScanRequested is false),
		// so the sensor goes into a fresh scratch volume on the same chunk grid
		private void RestartScratch()
		{
			EnvScanner scanner = EnvScanner.Instance;
			if (scanner == null) return;
			ReleaseScratch();
			scratch = scanner.CreateVolume(relocalizeScratchChunks, "Relocalization Scratch");
			scanner.GatedVolume = scratch;
		}

		// the scratch scan only means something while relocalizing
		private void ReleaseScratch()
		{
			EnvScanner scanner = EnvScanner.Instance;
			if (scanner != null && scanner.GatedVolume == scratch) scanner.GatedVolume = null;
			scratch?.Dispose();
			scratch = null;
		}

		private void OnBusActivated() { if (IsRunning) EnterRole(); }
		private void OnBusDeactivated() { if (IsRunning) EnterRole(); }
		private void OnAuthorityChanged(bool _) { if (IsRunning) EnterRole(); }

		// the authority's frame is canon by definition; everyone else has to find it
		private void EnterRole()
		{
			if (SyncBus.IsAuthority) AlignIdentity();
			else BeginSearch();
		}

		private void AlignIdentity()
		{
			ClearReference();
			ReleaseScratch();
			Transform space = MainXRRig.TrackingSpace;
			observedSpacePose = canonSpacePose = space ? new Pose(space.position, space.rotation) : Pose.identity;
			NewAlignment();
			alignmentApplied = false;
			CurrentPhase = Phase.Aligned;
		}

		private void BeginSearch()
		{
			if (CurrentPhase is Phase.Searching or Phase.Registering) return;
			ClearReference();
			ReleaseScratch();
			CurrentPhase = Phase.Searching;
			Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] searching: scanning locally and waiting for the authority's snapshot");
		}

		public void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (CurrentPhase != Phase.Aligned || !IsAvailable) return;
			Transform space = MainXRRig.TrackingSpace;
			if (!space) return;

			// observed drifts toward canon as the colocator moves the rig, then the two
			// coincide and the fit holds still
			results.Add(new ColocationConstraint(new Pose(space.position, space.rotation),
				SlamCorrection.Apply(canonSpacePose), hasReliableRotation: true));
		}

		// ---- reference volume --------------------------------------------------

		/// <summary>A snapshot chunk from the authority, in the authority's frame.</summary>
		public void AcceptReferenceChunk(int chunkIndex, NativeArray<EnvScanner.Voxel> voxels)
		{
			if (!WantsReferenceChunks) return;
			if (!referenceChunks.TryGetValue(chunkIndex, out NativeArray<EnvScanner.Voxel> copy))
			{
				copy = new NativeArray<EnvScanner.Voxel>(voxels.Length, Allocator.Persistent);
				referenceChunks[chunkIndex] = copy;
			}

			copy.CopyFrom(voxels);
		}

		private void ClearReference()
		{
			foreach (NativeArray<EnvScanner.Voxel> voxels in referenceChunks.Values) voxels.Dispose();
			referenceChunks.Clear();
		}

		// ---- attempts ----------------------------------------------------------

		private void Update()
		{
			// the colocator reports Lost until its next fit applies a fresh alignment,
			// so a loss only counts once the alignment we last produced was in use
			if (ColocationManager.Instance != null)
			{
				ColocationAlignmentState state = ColocationManager.Instance.AlignmentState;
				if (state == ColocationAlignmentState.Localized) alignmentApplied = true;
				else if (state == ColocationAlignmentState.Lost && alignmentApplied && CurrentPhase == Phase.Aligned)
				{
					alignmentApplied = false;
					BeginRelocalize("tracking lost");
				}
			}

			// attempts run back to back: one in flight is never overlapped, and its own
			// readbacks spread it over frames
			if (CurrentPhase is not (Phase.Searching or Phase.Relocalizing)) return;

			EnvScanner scanner = EnvScanner.Instance;
			EnvMesher mesher = EnvMesher.Instance;
			if (!scanner || !mesher || !IsAvailable) return;

			if (CurrentPhase == Phase.Relocalizing)
			{
				if (!relocalizeInFlight && scratch != null && scratch.Chunks.Count > 0 && mesher.ChunksList.Count >= minLocalChunks) Relocalize(scanner, mesher);
				return;
			}

			if (referenceChunks.Count < minReferenceChunks || mesher.ChunksList.Count < minLocalChunks) return;

			Attempt(scanner, mesher);
		}

		// ---- relocalization ----------------------------------------------------

		/// <summary>
		/// The map this headset holds is the target; the scratch volume the sensor has
		/// been scanning into since the loss, in the stale frame, is the source. The
		/// transform found moves the stale frame onto the map's
		/// </summary>
		private async void Relocalize(EnvScanner scanner, EnvMesher mesher)
		{
			// stays in Relocalizing rather than Registering: Registering opens the local
			// integration gate (a joiner needs its own scan), which here would bake the
			// stale scan into the very map it is being matched against
			relocalizeInFlight = true;
			int epoch = frameEpoch;
			try
			{
				EnsureRegistration(scanner);
				reference ??= new RegistrationVolume(TsdfRegistration.LoadShader(), scanner);
				registration.Target = reference;

				List<int> mapChunks = new(mesher.ChunksList.Count);
				int3 min = int.MaxValue, max = int.MinValue;
				foreach (Chunk chunk in mesher.ChunksList)
				{
					mapChunks.Add(chunk.chunkIndex);
					int3 coord = scanner.ChunkIndexToChunkCoord(chunk.chunkIndex);
					min = math.min(min, coord);
					max = math.max(max, coord);
				}

				if (!reference.Allocate(min, max))
				{
					Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] not attempted: the map spans {max - min + 1} chunks, more than the reference volume holds");
					return;
				}

				reference.FillFromAtlas(mapChunks);

				List<int> scratchChunks = new(scratch.Chunks);
				if (!await registration.SetSourceChunksAsync(scratchChunks, scratch) || !this || CurrentPhase != Phase.Relocalizing)
				{
					if (this && CurrentPhase == Phase.Relocalizing) Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] relocalize: no usable samples in the scratch scan yet ({scratchChunks.Count} chunks)");
					return;
				}

				if (epoch != frameEpoch || !HasCoverage("relocalize", scratchChunks.Count)) return;

				RegistrationResult result = await RegisterGlobalAsync();
				if (!this || CurrentPhase != Phase.Relocalizing) return;
				if (epoch != frameEpoch)
				{
					Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] relocalize: discarded, recentered mid-registration");
					return;
				}
				string rejection = Rejection(result, relocalizeMinMarginFraction);

				LastResult = result;
				Registered.Invoke(result);
				Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] relocalize: {result}  (scratch {scratchChunks.Count} chunks, {registration.FineSampleCount} samples; map {mapChunks.Count} chunks)");

				if (rejection != null)
				{
					Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] not accepted: {rejection}");
					return;
				}

				Transform space = MainXRRig.TrackingSpace;
				observedSpacePose = new Pose(space.position, space.rotation);
				canonSpacePose = new Pose(result.pose.Apply(space.position), math.mul(result.pose.Rotation, (quaternion)space.rotation));
				NewAlignment();
				ReleaseScratch();
				alignmentApplied = false;
				CurrentPhase = Phase.Aligned;
				Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] relocalized: {result.pose}");
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
			}
			finally
			{
				relocalizeInFlight = false;
			}
		}

		private async void Attempt(EnvScanner scanner, EnvMesher mesher)
		{
			CurrentPhase = Phase.Registering;
			int epoch = frameEpoch;
			try
			{
				EnsureRegistration(scanner);
				reference ??= new RegistrationVolume(TsdfRegistration.LoadShader(), scanner);
				registration.Target = reference;

				// the reference: every snapshot chunk, over their bounding box
				int3 min = int.MaxValue, max = int.MinValue;
				foreach (int chunkIndex in referenceChunks.Keys)
				{
					int3 coord = scanner.ChunkIndexToChunkCoord(chunkIndex);
					min = math.min(min, coord);
					max = math.max(max, coord);
				}

				if (!reference.Allocate(min, max))
				{
					Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] not attempted: the authority's {referenceChunks.Count} chunks span {max - min + 1} chunks, more than the reference volume holds ({RegistrationVolume.MaxDims} voxels)");
					return;
				}

				foreach ((int chunkIndex, NativeArray<EnvScanner.Voxel> voxels) in referenceChunks)
					reference.FillFromVoxels(chunkIndex, voxels);

				// the source: everything scanned locally so far
				List<int> local = new(mesher.ChunksList.Count);
				foreach (Chunk chunk in mesher.ChunksList) local.Add(chunk.chunkIndex);

				if (!await registration.SetSourceChunksAsync(local) || !this) return;
				if (epoch != frameEpoch || !HasCoverage("colocate", local.Count)) return;
				RegistrationResult result = await RegisterGlobalAsync();
				if (!this || CurrentPhase != Phase.Registering) return;
				if (epoch != frameEpoch)
				{
					Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] discarded, recentered mid-registration");
					return;
				}

				LastResult = result;
				Registered.Invoke(result);
				int referenceObserved = 0;
				foreach (NativeArray<EnvScanner.Voxel> voxels in referenceChunks.Values)
					for (int i = 0; i < voxels.Length; i += 64) if (!voxels[i].IsEmpty) referenceObserved++;
				Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] {result}  (reference {referenceChunks.Count} chunks {min}..{max} bounds {reference.WorldBounds} observed≈{referenceObserved * 64}; local {local.Count} chunks, {registration.FineSampleCount} samples, bounds {registration.SourceBounds})");

				string rejection = Rejection(result);
				if (rejection != null)
				{
					Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] not accepted: {rejection}");
					return;
				}

				Align(scanner, result.pose);
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
			}
			finally
			{
				if (this && CurrentPhase == Phase.Registering) CurrentPhase = Phase.Searching;
			}
		}

		// at the yaws the maps' wall directions allow first; at every yaw if nothing fits
		// there (too few walls, or walls that mislead). A fit that only failed the margin
		// or rms gate is not improved on by a wider search
		private async Awaitable<RegistrationResult> RegisterGlobalAsync()
		{
			RegistrationResult result = await registration.RegisterAsync(yawHypotheses: true);
			if (!this || result.yawHypotheses == 0 || Fits(result)) return result;

			Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] {result.yawHypotheses} wall yaw hypotheses: {result} — {Rejection(result)}; searching every yaw");
			return await registration.RegisterAsync(yawHypotheses: false);
		}

		// true once the extracted source constrains every degree of freedom; logs why not, at most every 2 s
		private bool HasCoverage(string what, int chunks)
		{
			SourceConstraint constraint = registration.Constraint;
			if (gates.HasCoverage(constraint, EnvScanner.Instance)) return true;
			float minConstraint = gates.MinSourceConstraint(EnvScanner.Instance);
			if (Time.unscaledTime >= nextCoverageLog)
			{
				nextCoverageLog = Time.unscaledTime + 2f;
				Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] {what}: waiting for coverage, {constraint} < {minConstraint:F0} ({chunks} chunks, {registration.FineSampleCount} samples)");
			}

			return false;
		}

		private bool Fits(RegistrationResult result) => gates.Fits(result, EnvScanner.Instance);

		private string Rejection(RegistrationResult result) => gates.Rejection(result, EnvScanner.Instance);

		private string Rejection(RegistrationResult result, float marginFraction) => gates.Rejection(result, EnvScanner.Instance, marginFraction);

		/// <summary>
		/// <paramref name="localToCanon"/> maps this headset's current world frame onto the
		/// authority's. The tracking space's pose in that frame becomes the constraint's
		/// canon; the local scan, made in the old frame, is replaced by the reference
		/// </summary>
		private void Align(EnvScanner scanner, Pose4 localToCanon)
		{
			Transform space = MainXRRig.TrackingSpace;
			observedSpacePose = new Pose(space.position, space.rotation);
			canonSpacePose = new Pose(localToCanon.Apply(space.position), math.mul(localToCanon.Rotation, (quaternion)space.rotation));
			NewAlignment();
			alignmentApplied = false;
			CurrentPhase = Phase.Aligned;

			scanner.Clear();
			foreach ((int chunkIndex, NativeArray<EnvScanner.Voxel> voxels) in referenceChunks)
				scanner.UploadChunk(chunkIndex, voxels);
			ClearReference();

			Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] aligned: {localToCanon}");
		}
	}
}
