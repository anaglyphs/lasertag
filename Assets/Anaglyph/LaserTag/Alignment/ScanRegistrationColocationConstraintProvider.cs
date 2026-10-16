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

		public enum Phase { Stopped, Aligned, Searching, Registering }

		[Header("Attempts")]
		[SerializeField] private float secondsBetweenAttempts = 2f;
		[SerializeField] private int minReferenceChunks = 4;
		[SerializeField] private int minLocalChunks = 3;

		[Header("Acceptance")]
		// agreement among the samples that landed in the authority's observed space. two
		// independently scanned volumes of one room agree around 0.5-0.6 at the right pose
		[SerializeField] private float minAgreement = 0.4f;
		// how many of this scan's samples must land in the authority's observed space; an
		// absolute count, so scanning more than the authority never hurts
		[SerializeField] private int minOverlapSamples = 500;
		// the winner's lead over the best rival peak, as a fraction of its own score
		[SerializeField] private float minMarginFraction = 0.15f;
		[SerializeField] private float maxRefinedRms = 0.1f;

		// a pose is only committed once two consecutive attempts, with more scan between
		// them, land on it. False peaks wander between attempts; the true one holds still
		[SerializeField] private int confirmations = 2;
		[SerializeField] private float confirmTranslation = 0.15f;
		[SerializeField] private float confirmYawDegrees = 3f;

		private Pose4? candidatePose;
		private int candidateCount;

		public Phase CurrentPhase { get; private set; } = Phase.Stopped;
		public RegistrationResult LastResult { get; private set; }
		public int ReferenceChunkCount => referenceChunks.Count;
		public event Action<RegistrationResult> Registered = delegate { };

		/// <summary>
		/// True while a joiner needs its own scan to register with, before the map
		/// coordinator would otherwise let it integrate anything
		/// </summary>
		public static bool LocalScanRequested => Instance != null && Instance.CurrentPhase is Phase.Searching or Phase.Registering;

		/// <summary>True while incoming snapshot chunks belong in the reference volume, not the live scan.</summary>
		public bool WantsReferenceChunks => CurrentPhase is Phase.Searching or Phase.Registering;

		private readonly Dictionary<int, NativeArray<EnvScanner.Voxel>> referenceChunks = new();
		private TsdfRegistration registration;
		private RegistrationVolume reference;
		private float nextAttempt;

		// the one constraint: where the tracking space sat when the fit was found, and
		// where the registration says it belongs. Identical for the authority
		private Pose observedSpacePose;
		private Pose canonSpacePose;

		public bool IsAvailable => EnvScanner.Instance != null && MainXRRig.Instance != null;
		public bool IsRunning => CurrentPhase != Phase.Stopped;

		private void Awake()
		{
			Instance = this;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
		}

		private void OnDestroy()
		{
			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
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
			Transform space = MainXRRig.TrackingSpace;
			observedSpacePose = canonSpacePose = space ? new Pose(space.position, space.rotation) : Pose.identity;
			CurrentPhase = Phase.Aligned;
		}

		private void BeginSearch()
		{
			if (CurrentPhase is Phase.Searching or Phase.Registering) return;
			ClearReference();
			candidatePose = null;
			candidateCount = 0;
			nextAttempt = Time.unscaledTime + secondsBetweenAttempts;
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
				canonSpacePose, hasReliableRotation: true));
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
			if (CurrentPhase != Phase.Searching || Time.unscaledTime < nextAttempt) return;
			nextAttempt = Time.unscaledTime + secondsBetweenAttempts;

			EnvScanner scanner = EnvScanner.Instance;
			EnvMesher mesher = EnvMesher.Instance;
			if (!scanner || !mesher || !IsAvailable) return;
			if (referenceChunks.Count < minReferenceChunks || mesher.ChunksList.Count < minLocalChunks) return;

			Attempt(scanner, mesher);
		}

		private async void Attempt(EnvScanner scanner, EnvMesher mesher)
		{
			CurrentPhase = Phase.Registering;
			try
			{
				registration ??= new TsdfRegistration(TsdfRegistration.LoadShader(), scanner);
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
				RegistrationResult result = await registration.RegisterAsync();
				if (!this || CurrentPhase != Phase.Registering) return;

				LastResult = result;
				Registered.Invoke(result);
				int referenceObserved = 0;
				foreach (NativeArray<EnvScanner.Voxel> voxels in referenceChunks.Values)
					for (int i = 0; i < voxels.Length; i += 64) if (!voxels[i].IsEmpty) referenceObserved++;
				Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] {result}  (reference {referenceChunks.Count} chunks {min}..{max} bounds {reference.WorldBounds} observed≈{referenceObserved * 64}; local {local.Count} chunks, {registration.FineSampleCount} samples, bounds {registration.SourceBounds})");

				string rejection = !result.success ? result.failure :
					result.agreement < minAgreement ? $"agreement {result.agreement:F2} < {minAgreement}" :
					result.overlapSamples < minOverlapSamples ? $"overlap {result.overlapSamples} samples < {minOverlapSamples}" :
					result.margin < minMarginFraction * result.score ? $"margin {result.margin:F3} < {minMarginFraction:F2} × score {result.score:F3}" :
					result.refinedRms < 0 ? "refinement failed" :
					result.refinedRms > maxRefinedRms ? $"rms {result.refinedRms * 100:F1}cm > {maxRefinedRms * 100:F0}cm" : null;

				if (rejection != null)
				{
					Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] not accepted: {rejection}");
					candidatePose = null;
					candidateCount = 0;
					return;
				}

				bool sameAsBefore = candidatePose.HasValue &&
					math.distance(candidatePose.Value.translation, result.pose.translation) <= confirmTranslation &&
					math.abs(math.degrees(math.atan2(math.sin(candidatePose.Value.yaw - result.pose.yaw), math.cos(candidatePose.Value.yaw - result.pose.yaw)))) <= confirmYawDegrees;
				candidateCount = sameAsBefore ? candidateCount + 1 : 1;
				candidatePose = result.pose;

				if (candidateCount < confirmations)
				{
					Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] candidate {result.pose}, awaiting confirmation ({candidateCount}/{confirmations})");
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
			CurrentPhase = Phase.Aligned;

			scanner.Clear();
			foreach ((int chunkIndex, NativeArray<EnvScanner.Voxel> voxels) in referenceChunks)
				scanner.UploadChunk(chunkIndex, voxels);
			ClearReference();

			Debug.Log($"[{nameof(ScanRegistrationColocationConstraintProvider)}] aligned: {localToCanon}");
		}
	}
}
