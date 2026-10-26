using System;
using System.Collections.Generic;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Colocation by registering this headset's environment scan against the session
	/// authority's: a joiner scans its surroundings, receives the authority's chunk
	/// snapshot into a reference volume, and searches for the gravity-aligned transform
	/// that lays its scan onto the reference. Relocalization scans into a scratch volume
	/// and registers that against the map already held.
	/// </summary>
	public class ScanRegistrationColocationConstraintProvider : CanonColocationProvider
	{
		public static ScanRegistrationColocationConstraintProvider Instance { get; private set; }

		[Header("Attempts")]
		[SerializeField] private int minReferenceChunks = 4;
		[SerializeField] private int minLocalChunks = 3;
		private float nextCoverageLog;

		[Header("Acceptance")]
		// shared with ScanRegistrationBench
		[SerializeField] private RegistrationGates gates = new();

		/// <summary>When a registration may be attempted and trusted, for everything that registers scans.</summary>
		public RegistrationGates Gates => gates;

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

		/// <summary>The scratch volume relocalization registers against the map; null unless relocalizing.</summary>
		public EnvVolume ScratchVolume => scratch;
		private bool relocalizeInFlight;

		/// <summary>True while incoming snapshot chunks belong in the reference volume, not the live scan.</summary>
		public bool WantsReferenceChunks => CurrentPhase is Phase.Searching or Phase.Registering;

		private readonly Dictionary<int, NativeArray<EnvScanner.Voxel>> referenceChunks = new();

		/// <summary>The authority's snapshot chunks received while searching, in the authority's frame.</summary>
		public IReadOnlyDictionary<int, NativeArray<EnvScanner.Voxel>> ReferenceChunks => referenceChunks;
		private TsdfRegistration registration;
		private RegistrationVolume reference;

		public override string LastAttemptDescription => LastResult.ToString();

		protected override void Awake()
		{
			Instance = this;
			base.Awake();
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			registration?.Dispose();
			reference?.Dispose();
			if (Instance == this) Instance = null;
		}

		protected override void ResetWorkingState()
		{
			ClearReference();
			ReleaseScratch();
		}

		protected override bool HasMapToRelocalizeAgainst =>
			EnvMesher.Instance != null && EnvMesher.Instance.ChunksList.Count >= minLocalChunks;

		protected override void OnRelocalizeBegun() => RestartScratch();

		// the scratch scan so far is in the frame before this recenter; fusing the next
		// frames on top would register a blend of two frames against the map
		protected override void OnRecenteredWhileRelocalizing()
		{
			RestartScratch();
			Debug.Log($"{LogTag} recentered while relocalizing: restarting the scratch scan");
		}

		// not colocated yet, so the live map is this headset's own scan, fused in the
		// frame before this recenter; the authority's snapshot is unaffected
		protected override void OnRecenteredWhileSearching()
		{
			if (EnvScanner.Instance != null) EnvScanner.Instance.Clear();
			Debug.Log($"{LogTag} recentered while searching: restarting the local scan");
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

		protected override void Update()
		{
			base.Update();

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
			int epoch = FrameEpoch;
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
					Debug.Log($"{LogTag} not attempted: the map spans {max - min + 1} chunks, more than the reference volume holds");
					return;
				}

				reference.FillFromAtlas(mapChunks);

				List<int> scratchChunks = new(scratch.Chunks);
				if (!await registration.SetSourceChunksAsync(scratchChunks, scratch) || !this || CurrentPhase != Phase.Relocalizing)
				{
					if (this && CurrentPhase == Phase.Relocalizing) Debug.Log($"{LogTag} relocalize: no usable samples in the scratch scan yet ({scratchChunks.Count} chunks)");
					return;
				}

				if (epoch != FrameEpoch || !HasCoverage("relocalize", scratchChunks.Count)) return;

				RegistrationResult result = await RegisterGlobalAsync();
				if (!this || CurrentPhase != Phase.Relocalizing) return;
				if (epoch != FrameEpoch)
				{
					Debug.Log($"{LogTag} relocalize: discarded, recentered mid-registration");
					return;
				}
				string rejection = Rejection(result, relocalizeMinMarginFraction);

				LastResult = result;
				Registered.Invoke(result);
				Debug.Log($"{LogTag} relocalize: {result}  (scratch {scratchChunks.Count} chunks, {registration.FineSampleCount} samples; map {mapChunks.Count} chunks)");

				if (rejection != null)
				{
					Debug.Log($"{LogTag} not accepted: {rejection}");
					return;
				}

				Transform space = MainXRRig.TrackingSpace;
				ReleaseScratch();
				ApplyAlignment(result.pose, new Pose(space.position, space.rotation));
				Debug.Log($"{LogTag} relocalized: {result.pose}");
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
			int epoch = FrameEpoch;
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
					Debug.Log($"{LogTag} not attempted: the authority's {referenceChunks.Count} chunks span {max - min + 1} chunks, more than the reference volume holds ({RegistrationVolume.MaxDims} voxels)");
					return;
				}

				foreach ((int chunkIndex, NativeArray<EnvScanner.Voxel> voxels) in referenceChunks)
					reference.FillFromVoxels(chunkIndex, voxels);

				// the source: everything scanned locally so far
				List<int> local = new(mesher.ChunksList.Count);
				foreach (Chunk chunk in mesher.ChunksList) local.Add(chunk.chunkIndex);

				if (!await registration.SetSourceChunksAsync(local) || !this) return;
				if (epoch != FrameEpoch || !HasCoverage("colocate", local.Count)) return;
				RegistrationResult result = await RegisterGlobalAsync();
				if (!this || CurrentPhase != Phase.Registering) return;
				if (epoch != FrameEpoch)
				{
					Debug.Log($"{LogTag} discarded, recentered mid-registration");
					return;
				}

				LastResult = result;
				Registered.Invoke(result);
				int referenceObserved = 0;
				foreach (NativeArray<EnvScanner.Voxel> voxels in referenceChunks.Values)
					for (int i = 0; i < voxels.Length; i += 64) if (!voxels[i].IsEmpty) referenceObserved++;
				Debug.Log($"{LogTag} {result}  (reference {referenceChunks.Count} chunks {min}..{max} bounds {reference.WorldBounds} observed≈{referenceObserved * 64}; local {local.Count} chunks, {registration.FineSampleCount} samples, bounds {registration.SourceBounds})");

				string rejection = Rejection(result);
				if (rejection != null)
				{
					Debug.Log($"{LogTag} not accepted: {rejection}");
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

			Debug.Log($"{LogTag} {result.yawHypotheses} wall yaw hypotheses: {result} — {Rejection(result)}; searching every yaw");
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
				Debug.Log($"{LogTag} {what}: waiting for coverage, {constraint} < {minConstraint:F0} ({chunks} chunks, {registration.FineSampleCount} samples)");
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
			ApplyAlignment(localToCanon, new Pose(space.position, space.rotation));

			scanner.Clear();
			foreach ((int chunkIndex, NativeArray<EnvScanner.Voxel> voxels) in referenceChunks)
				scanner.UploadChunk(chunkIndex, voxels);
			ClearReference();

			Debug.Log($"{LogTag} aligned: {localToCanon}");
		}
	}
}
