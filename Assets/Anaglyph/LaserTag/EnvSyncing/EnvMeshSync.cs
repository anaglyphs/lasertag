using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Anaglyph.DepthKit.EnvScanning;
using Anaglyph.Lasertag.Networking;
using Anaglyph.Netcode;
using Draco;
using Draco.Encode;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Broadcasts locally scanned chunk meshes to other players when chunks
	/// pass a change threshold, and applies meshes received from other players
	/// </summary>
	public class EnvMeshSync : MonoBehaviour
	{
		public static EnvMeshSync Instance { get; private set; }

		// Direct events: chunk payloads are fire-and-forget and need no ordering
		// against other synced state. NGO's proxy path fans them out server-side on
		// both LAN (DAHost) and the CMB service — unlike the old named messages,
		// which the CMB service cannot relay at all.
		private readonly SyncEventBytes chunkEvent = new("env.chunks", EventRoute.Direct);
		private readonly SyncEvent<bool> visibleEvent = new("env.visible", EventRoute.Direct);

		// NGO caps fragmented messages at 64000 bytes; leave headroom for headers.
		private const int MaxPayloadBytes = 60000;

		// "DRAC" in little-endian byte order, followed by chunk index and revision.
		private const uint ChunkPayloadMagic = 0x43415244;
		private const int ChunkPayloadHeaderBytes = sizeof(uint) + sizeof(int) + sizeof(uint);

		/// <summary>
		/// Invoked after a populated chunk mesh received from another
		/// player is applied to a chunk's remote mesh
		/// </summary>
		public static event Action<Chunk> RemoteMeshApplied = delegate { };

		// Change is counted in voxels the isosurface crossed, as a fraction of one
		// chunk face - roughly what a flat wall spanning the chunk covers. Keeps these
		// meaningful if the voxel size or chunk resolution is retuned.
		// No FormerlySerializedAs here on purpose: the old fields of these names held
		// absolute voxel counts, and importing one as a fraction would silently mute
		// sending entirely. Defaults below are the old tuned values converted.
		[Header("Send thresholds (fraction of a chunk face)")]
		[SerializeField, Range(0f, 2f)] private float initialSurfaceSendThreshold = 0.33f;
		[SerializeField, Range(0f, 2f)] private float updatedSurfaceSendThreshold = 1.1f;

		// newly discovered geometry is worth more than re-measured geometry
		[FormerlySerializedAs("newCoverageWeight")] [SerializeField] private float newSurfaceWeight = 2f;

		[FormerlySerializedAs("firstSendSettleSeconds")]
		[Header("Settling")]
		// how long a chunk must go unobserved before a resend; a chunk mid-scan is
		// about to change again, so waiting collapses several partial sends into one
		[SerializeField] private float initialSendSettleSeconds = 0.5f;
		[SerializeField] private float settleSeconds = 1.5f;

		// someone standing and staring can't withhold a chunk forever
		[SerializeField] private float maxDeferSeconds = 8f;

		// no peer sent a chunk this long after it was ready, so the derived owner is
		// wrong (usually the cone test can't see an occluder). take it over.
		[SerializeField] private float ownerTakeoverSeconds = 15f;

		[SerializeField] private float selectInterval = 0.25f;
		[SerializeField] private float firstSendPriority = 4f;
		[SerializeField, Range(0f, 8f)] private float maxSettleBoost = 3f;

		[Header("Authority")]
		// Frustum approximating what a headset's depth sensor covers. Kept a little
		// narrower than the real sensor: wrongly counting a peer as an observer hands
		// them a chunk they'll never send, which costs more than a duplicate payload.
		[SerializeField, Range(1f, 179f)] private float observeVerticalFov = 60f;
		[SerializeField] private float observeAspect = 1.2f;

		[Header("Send rate")]
		// chunk payloads share a reliable fragmented pipe with gameplay traffic,
		// so they get a hard ceiling rather than whatever the queue can push
		[SerializeField] private float sendBytesPerSecond = 40000;
		[SerializeField] private float sendBurstBytes = 90000;
		[SerializeField] private int minSendBudgetBytes = 8000;

		[Header("Draco compression")]
		// Position precision as a fraction of a voxel. Quantizing finer than the grid
		// that produced the mesh only spends bits on marching cubes' own rounding.
		[SerializeField, Range(0.05f, 2f)] private float positionPrecisionVoxelFraction = 0.25f;
		[SerializeField, Range(0, 10)] private int encodingSpeed;
		[SerializeField, Range(0, 10)] private int decodingSpeed = 4;

		/// <summary>
		/// Per chunk send bookkeeping. Odometer fields wrap, so they are
		/// only ever compared by unsigned subtraction.
		/// </summary>
		private class ChunkSyncState
		{
			public Bounds worldBounds;

			public uint surfaceChange;
			public uint newCoverage;

			public uint sentSurfaceChange;
			public uint sentNewCoverage;
			public uint sentMeshingChangeSum;
			public bool everSent;

			public float lastObservedTime;

			// when this chunk's accumulated change first crossed its
			// threshold; zero means it has nothing worth sending
			public float readyTime;

			// last peer that was derived as owner while the chunk was observed
			public ulong owner;
			public bool hasOwner;
		}

		private readonly Dictionary<int, ChunkSyncState> syncStates = new();

		// Draco encoding is expensive and payloads are large, so exactly one chunk
		// is in flight at a time and the best candidate is picked when it frees up.
		private bool encodeInFlight;
		private float nextSelectTime;
		private float sendBudgetBytes;

		// Revisions prevent asynchronous Draco decodes from applying out of order.
		private readonly Dictionary<int, uint> sentRevisions = new();
		private readonly Dictionary<(ulong sender, int chunkIndex), uint> receivedRevisions = new();

		// Invalidates encoding/decoding work that outlives a network session.
		private int syncGeneration;

		private void Awake()
		{
			Instance = this;

			chunkEvent.Register();
			visibleEvent.Register();
			chunkEvent.Received += OnChunkReceived;
			visibleEvent.Received += OnVisibleReceived;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			ColocationManager.Colocated += OnColocated;
		}

		private void Start()
		{
			if (MapManager.Instance != null)
				MapManager.Instance.WorldFrameRebased += OnWorldFrameRebased;
		}

		private void OnDestroy()
		{
			if (MapManager.Instance != null)
				MapManager.Instance.WorldFrameRebased -= OnWorldFrameRebased;

			syncGeneration++;
			syncStates.Clear();

			if (strippedMesh != null)
				Destroy(strippedMesh);

			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			chunkEvent.Received -= OnChunkReceived;
			visibleEvent.Received -= OnVisibleReceived;
			visibleEvent.Unregister();
			chunkEvent.Unregister();

			ColocationManager.Colocated -= OnColocated;
		}

		/// <summary>
		/// The map changed under us, so world space now sits somewhere else and the scan
		/// describes the room in the wrong place. Every peer drops its own — including the
		/// meshes it received, which were measured in that same outgoing frame.
		/// </summary>
		private void OnWorldFrameRebased()
		{
			// Invalidates encodes and decodes of pre-change meshes still in flight.
			syncGeneration++;

			syncStates.Clear();

			// Revisions deliberately survive. They order a chunk's payloads rather than
			// describing its coordinates, and IsNewerRevision only ever moves forward: a peer
			// that restarted its numbering would look stale to one that had not cleared yet,
			// and everything it sent next would be dropped.

			if (EnvScanner.Instance != null)
				EnvScanner.Instance.Clear();
		}

		private void OnColocated(bool isColocated)
		{
			HandleScannerActivity();
		}
		
		private void OnNetcodeStateChanged(NetcodeState state)
		{
			HandleScannerActivity();
		}

		private void HandleScannerActivity()
		{
			// Scanner is only enabled if connected & collocated OR disconnected
			EnvMesher.Instance.enabled = ColocationManager.IsColocated || NetcodeManagement.State == NetcodeState.Disconnected;
		}

		private void OnBusActivated()
		{
			syncGeneration++;
			syncStates.Clear();
			sendBudgetBytes = sendBurstBytes;

			EnvMesher.Instance.VisibleChunkPolled += OnVisibleChunkPolled;

			// Don't reset if authority, because the authority determines the coordinate system
			// so their scan will be aligned with the environment
			if(!SyncBus.IsAuthority)
				EnvScanner.Instance.Clear();
		}

		private void OnBusDeactivated()
		{
			syncGeneration++;

			if (EnvMesher.Instance)
				EnvMesher.Instance.VisibleChunkPolled -= OnVisibleChunkPolled;

			syncStates.Clear();
			sentRevisions.Clear();
			receivedRevisions.Clear();
		}

		public void SetEnvMeshVisibleEveryone(bool visible)
		{
			visibleEvent.Raise(visible);
		}

		private void OnVisibleReceived(ulong sender, bool visible)
		{
			EnvMesher.Instance.SetChunksVisible(visible);
		}

		private void OnVisibleChunkPolled(int chunkIndex, EnvScanner.ChunkStats stats)
		{
			// don't send scans until they're in the shared reference space

			if (!ColocationManager.IsColocated) return;

			if (!syncStates.TryGetValue(chunkIndex, out ChunkSyncState state))
			{
				EnvScanner scanner = EnvScanner.Instance;
				// sent counters start at zero, so a chunk that was already scanned before
				// this peer started tracking it counts as fully unsent - which it is
				Vector3 corner = (Vector3)scanner.ChunkCoordToCornerWorldPos(
					scanner.ChunkIndexToChunkCoord(chunkIndex));

				state = new ChunkSyncState
				{
					worldBounds = new Bounds(
						corner + EnvMesher.ChunkWorldSizeHalf, EnvMesher.ChunkWorldSize),
				};
				syncStates.Add(chunkIndex, state);
			}

			state.surfaceChange = stats.surfaceChange;
			state.newCoverage = stats.newCoverage;
			state.lastObservedTime = Time.time;

			if (state.readyTime == 0f && PendingChangeWeight(state) >= ChangeThreshold(state))
				state.readyTime = Time.time;

			// Ownership is only recorded while somebody is looking. Everyone derives it
			// from the same replicated head poses, so no claim traffic is needed - and
			// by the time a settled chunk sends, its owner is already on record.
			RecordOwner(chunkIndex, state);
		}

		// unsigned subtraction absorbs odometer wraparound
		private float PendingChangeWeight(ChunkSyncState state) =>
			(state.surfaceChange - state.sentSurfaceChange) +
			newSurfaceWeight * (state.newCoverage - state.sentNewCoverage);

		private float ChangeThreshold(ChunkSyncState state) =>
			(state.everSent ? updatedSurfaceSendThreshold : initialSurfaceSendThreshold) *
			ChunkFaceVoxels;

		private float chunkFaceVoxels;

		/// <summary>Voxels on one face of a chunk, excluding the one voxel apron.</summary>
		private float ChunkFaceVoxels
		{
			get
			{
				if (chunkFaceVoxels == 0f)
				{
					int interior = EnvScanner.Instance.VoxPerChunkDim - 2;
					chunkFaceVoxels = interior * interior;
				}

				return chunkFaceVoxels;
			}
		}

		/// <summary>
		/// Draco quantizes over the mesh's own bounds, which never exceed the chunk,
		/// so deriving the bit count from the whole chunk lands at or below the
		/// requested precision.
		/// </summary>
		private int PositionQuantizationBits
		{
			get
			{
				EnvScanner scanner = EnvScanner.Instance;
				float precision = scanner.VoxSize * positionPrecisionVoxelFraction;
				int bits = Mathf.CeilToInt(Mathf.Log(scanner.ChunkWorldSizeDim / precision, 2f));

				return Mathf.Clamp(bits, QuantizationSettings.minQuantization,
					QuantizationSettings.maxQuantization);
			}
		}

		private void RecordOwner(int chunkIndex, ChunkSyncState state)
		{
			RefreshObserverFrusta();

			ulong owner = 0;
			uint ownerKey = 0;
			bool found = false;

			for (int i = 0; i < observerIds.Count; i++)
			{
				if (!GeometryUtility.TestPlanesAABB(observerFrusta[i], state.worldBounds))
					continue;

				// hashing the pair spreads chunks across observers instead
				// of piling every chunk onto the lowest client id
				uint key = OwnerKey(chunkIndex, observerIds[i]);
				if (found && key >= ownerKey) continue;

				owner = observerIds[i];
				ownerKey = key;
				found = true;
			}

			if (!found) return;

			state.owner = owner;
			state.hasOwner = true;
		}

		// One frustum per player, rebuilt once a frame and reused across
		// every chunk polled that frame. Plane arrays are pooled by slot.
		private readonly List<ulong> observerIds = new();
		private readonly List<Plane[]> observerFrusta = new();
		private int observerFrustaFrame = -1;

		private void RefreshObserverFrusta()
		{
			if (observerFrustaFrame == Time.frameCount) return;
			observerFrustaFrame = Time.frameCount;

			observerIds.Clear();

			EnvScanner scanner = EnvScanner.Instance;
			Matrix4x4 projection = Matrix4x4.Perspective(
				observeVerticalFov, observeAspect,
				scanner.MinScanDistance, scanner.MaxScanDistance);

			foreach (KeyValuePair<ulong, PlayerAvatar> pair in PlayerAvatar.All)
			{
				Transform head = pair.Value.HeadTransform;
				if (head == null) continue;

				int slot = observerIds.Count;
				if (slot == observerFrusta.Count)
					observerFrusta.Add(new Plane[6]);

				// Unity's view space looks down -Z, the head transform looks down +Z
				Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * head.worldToLocalMatrix;
				GeometryUtility.CalculateFrustumPlanes(projection * view, observerFrusta[slot]);

				observerIds.Add(pair.Key);
			}
		}

		private static uint OwnerKey(int chunkIndex, ulong clientId)
		{
			unchecked
			{
				uint h = (uint)chunkIndex * 2654435761u ^ (uint)clientId * 2246822519u;
				h ^= h >> 15;
				h *= 2654435761u;
				return h ^ (h >> 13);
			}
		}

		private void Update()
		{
			sendBudgetBytes = Mathf.Min(
				sendBudgetBytes + sendBytesPerSecond * Time.deltaTime, sendBurstBytes);

			if (encodeInFlight || !SyncBus.Active || !ColocationManager.IsColocated) return;
			if (Time.time < nextSelectTime) return;

			nextSelectTime = Time.time + selectInterval;

			if (sendBudgetBytes < minSendBudgetBytes) return;

			int chunkIndex = SelectChunkToSend();
			if (chunkIndex >= 0)
				SendChunk(chunkIndex, syncStates[chunkIndex], syncGeneration);
		}

		private int SelectChunkToSend()
		{
			float now = Time.time;
			int best = -1;
			float bestScore = 0f;

			foreach (KeyValuePair<int, ChunkSyncState> pair in syncStates)
			{
				float score = ScoreChunk(pair.Key, pair.Value, now);

				if (score <= bestScore) continue;

				bestScore = score;
				best = pair.Key;
			}

			return best;
		}

		/// <summary>
		/// Zero for anything not worth sending right now; higher is more urgent.
		/// </summary>
		private float ScoreChunk(int chunkIndex, ChunkSyncState state, float now)
		{
			if (state.readyTime == 0f) return 0f;

			float weight = PendingChangeWeight(state);
			if (weight < ChangeThreshold(state)) return 0f;

			if (!EnvMesher.Instance.TryGetChunk(chunkIndex, out Chunk chunk)) return 0f;

			// mid-mesh; it gets picked up on a later pass with a finished mesh
			if (chunk.dirty) return 0f;

			// nothing new to say if the local mesh hasn't been rebuilt since the last send
			if (state.everSent && chunk.lastMeshingChangeSum == state.sentMeshingChangeSum)
				return 0f;

			float waited = now - state.readyTime;

			if (!IsLocalOwner(state) && waited < ownerTakeoverSeconds) return 0f;

			float settled = now - state.lastObservedTime;
			float requiredSettle = state.everSent ? settleSeconds : initialSendSettleSeconds;

			if (settled < requiredSettle && waited < maxDeferSeconds) return 0f;

			float score = weight;

			// a chunk nobody has is a hole in the world; a stale one is only stale
			if (!state.everSent) score *= firstSendPriority;

			// among ready chunks, the ones that have been left alone longest are
			// the ones least likely to be superseded a moment later
			score *= 1f + Mathf.Min(settled / Mathf.Max(settleSeconds, 0.01f), maxSettleBoost);

			return score;
		}

		private bool IsLocalOwner(ChunkSyncState state)
		{
			// Nothing on record, or the owner left mid-scan. Erring toward sending
			// risks a duplicate payload; erring the other way leaves a hole.
			if (!state.hasOwner || !PlayerAvatar.All.ContainsKey(state.owner))
				return true;

			return state.owner == SyncBus.LocalClientId;
		}

		private async void SendChunk(int chunkIndex, ChunkSyncState state, int generation)
		{
			encodeInFlight = true;

			try
			{
				if (EnvMesher.Instance.TryGetChunk(chunkIndex, out Chunk chunk))
					await EncodeAndSendChunk(chunk, state, generation);
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
			}
			finally
			{
				encodeInFlight = false;
			}
		}

		private async Task EncodeAndSendChunk(Chunk chunk, ChunkSyncState state, int generation)
		{
			if (generation != syncGeneration || !SyncBus.Active)
				return;

			int chunkIndex = chunk.chunkIndex;
			Mesh mesh = chunk.mesh;

			// Change accumulated during the encode belongs to the next send, so
			// what counts as sent is snapshotted against the mesh going out now.
			uint surfaceChange = state.surfaceChange;
			uint newCoverage = state.newCoverage;
			uint meshingChangeSum = chunk.lastMeshingChangeSum;

			bool isPopulated = mesh != null &&
			                   mesh.vertexCount > 0 &&
			                   mesh.subMeshCount == 1 &&
			                   mesh.GetIndexCount(0) >= 3;

			if (!isPopulated)
			{
				// nobody has this chunk yet, so there is nothing to clear
				if (!state.everSent)
				{
					MarkSent(state, surfaceChange, newCoverage, meshingChangeSum, false);
					return;
				}

				RaiseChunkPayload(chunkIndex, NextRevision(chunkIndex), default);
				MarkSent(state, surfaceChange, newCoverage, meshingChangeSum, true);
				return;
			}

			EncodeResult[] results = null;

			try
			{
				// Draco encodes whatever attributes the mesh declares and offers no way
				// to exclude any, so normals are dropped by encoding a positions-only
				// copy. Receivers recalculate them.
				Mesh strippedMesh = StripToPositions(mesh);

				using Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(strippedMesh);

				QuantizationSettings quantization = new(PositionQuantizationBits);
				SpeedSettings speed = new(encodingSpeed, decodingSpeed);
				results = await DracoEncoder.EncodeMesh(
					strippedMesh, meshDataArray[0], quantization, speed);

				if (!this || generation != syncGeneration || !SyncBus.Active)
					return;

				if (results == null || results.Length != 1)
				{
					Debug.LogWarning(
						$"[{nameof(EnvMeshSync)}] Draco failed to encode chunk {chunkIndex}");
					return;
				}

				NativeArray<byte> encodedData = results[0].data;
				int payloadSize = ChunkPayloadHeaderBytes + encodedData.Length;

				if (payloadSize > MaxPayloadBytes)
				{
					Debug.LogWarning(
						$"[{nameof(EnvMeshSync)}] Chunk {chunkIndex} Draco payload ({payloadSize}B) exceeds the fragmented message cap");
					return;
				}

				RaiseChunkPayload(chunkIndex, NextRevision(chunkIndex), encodedData);
				MarkSent(state, surfaceChange, newCoverage, meshingChangeSum, true);
			}
			finally
			{
				if (results != null)
				{
					for (int i = 0; i < results.Length; i++)
						results[i].Dispose();
				}
			}
		}

		// reused across sends; only ever holds the mesh currently being encoded
		private Mesh strippedMesh;

		/// <summary>
		/// Copies positions and triangles into a scratch mesh with no other vertex
		/// attributes, so Draco has nothing but positions to encode.
		/// </summary>
		private Mesh StripToPositions(Mesh source)
		{
			if (strippedMesh == null)
			{
				strippedMesh = new Mesh { name = "Chunk encode scratch" };
				strippedMesh.MarkDynamic();
				strippedMesh.indexFormat = IndexFormat.UInt32;
			}

			using Mesh.MeshDataArray sourceArray = Mesh.AcquireReadOnlyMeshData(source);
			Mesh.MeshData sourceData = sourceArray[0];

			int indexCount = (int)source.GetIndexCount(0);

			NativeArray<Vector3> positions = new(sourceData.vertexCount, Allocator.Temp,
				NativeArrayOptions.UninitializedMemory);
			NativeArray<int> indices = new(indexCount, Allocator.Temp,
				NativeArrayOptions.UninitializedMemory);

			try
			{
				sourceData.GetVertices(positions);
				sourceData.GetIndices(indices, 0);

				strippedMesh.Clear();
				strippedMesh.SetVertices(positions);
				strippedMesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
				strippedMesh.bounds = source.bounds;
			}
			finally
			{
				positions.Dispose();
				indices.Dispose();
			}

			return strippedMesh;
		}

		private void MarkSent(ChunkSyncState state, uint surfaceChange, uint newCoverage,
			uint meshingChangeSum, bool wasTransmitted)
		{
			state.sentSurfaceChange = surfaceChange;
			state.sentNewCoverage = newCoverage;
			state.sentMeshingChangeSum = meshingChangeSum;
			state.everSent |= wasTransmitted;

			// change that landed while this was encoding isn't covered by the mesh that
			// just went out, and the chunk may not be observed again to notice it
			state.readyTime = PendingChangeWeight(state) >= ChangeThreshold(state) ? Time.time : 0f;
		}

		private void ChargeSendBudget(int payloadBytes)
		{
			// the host's uplink carries one copy per peer; a client sends one and
			// lets the host fan it out
			int copies = SyncBus.IsAuthority ? Mathf.Max(1, PlayerAvatar.OtherPlayers.Count) : 1;
			sendBudgetBytes -= payloadBytes * copies;
		}

		private uint NextRevision(int chunkIndex)
		{
			sentRevisions.TryGetValue(chunkIndex, out uint revision);
			revision++;

			// Reserve zero for malformed/uninitialized packets.
			if (revision == 0) revision = 1;

			sentRevisions[chunkIndex] = revision;
			return revision;
		}

		private void RaiseChunkPayload(int chunkIndex, uint revision, NativeArray<byte> encodedData)
		{
			int payloadSize = ChunkPayloadHeaderBytes + (encodedData.IsCreated ? encodedData.Length : 0);
			byte[] payload = new byte[payloadSize];

			SyncBytes.Write(payload, 0, ChunkPayloadMagic);
			SyncBytes.Write(payload, sizeof(uint), chunkIndex);
			SyncBytes.Write(payload, sizeof(uint) + sizeof(int), revision);

			if (encodedData.IsCreated)
				NativeArray<byte>.Copy(encodedData, 0, payload, ChunkPayloadHeaderBytes, encodedData.Length);

			chunkEvent.Raise(payload);
			ChargeSendBudget(payloadSize);
		}

		private async void OnChunkReceived(ulong sender, byte[] payload)
		{
			// Direct events also invoke locally; the sender already has this mesh.
			if (sender == SyncBus.LocalClientId) return;

			// validate malformed or corrupt payloads
			if (payload.Length < ChunkPayloadHeaderBytes || payload.Length > MaxPayloadBytes)
				return;

			uint magic = SyncBytes.Read<uint>(payload, 0);
			if (magic != ChunkPayloadMagic) return;

			int chunkIndex = SyncBytes.Read<int>(payload, sizeof(uint));

			if (chunkIndex < 0 || chunkIndex >= EnvScanner.Instance.ChunkTableLength)
				return;

			uint revision = SyncBytes.Read<uint>(payload, sizeof(uint) + sizeof(int));
			if (revision == 0) return;

			(ulong sender, int chunkIndex) revisionKey = (sender, chunkIndex);
			if (receivedRevisions.TryGetValue(revisionKey, out uint latestRevision) &&
			    !IsNewerRevision(revision, latestRevision))
				return;

			receivedRevisions[revisionKey] = revision;
			int generation = syncGeneration;

			// A header-only packet clears the chunk without invoking Draco.
			if (payload.Length == ChunkPayloadHeaderBytes)
			{
				ClearRemoteChunk(chunkIndex, revisionKey, revision, generation);
				return;
			}

			NativeArray<byte> encodedData = new(
				payload.Length - ChunkPayloadHeaderBytes,
				Allocator.Persistent,
				NativeArrayOptions.UninitializedMemory);
			NativeArray<byte>.Copy(
				payload,
				ChunkPayloadHeaderBytes,
				encodedData,
				0,
				encodedData.Length);

			Mesh.MeshDataArray meshDataArray = default;
			bool meshDataAllocated = false;
			BoneWeightData boneWeightData = null;

			try
			{
				meshDataArray = Mesh.AllocateWritableMeshData(1);
				meshDataAllocated = true;

				DecodeSettings settings = DecodeSettings.Default | DecodeSettings.RequireNormals;
				DecodeResult result = await DracoDecoder.DecodeMesh(
					meshDataArray[0],
					encodedData.AsReadOnly(),
					settings);
				boneWeightData = result.boneWeightData;

				if (!result.success)
				{
					Debug.LogWarning(
						$"[{nameof(EnvMeshSync)}] Draco failed to decode chunk {chunkIndex}");
					return;
				}

				if (!this ||
				    generation != syncGeneration ||
				    !SyncBus.Active ||
				    !receivedRevisions.TryGetValue(revisionKey, out uint currentRevision) ||
				    currentRevision != revision)
					return;

				Chunk chunk = EnvMesher.Instance.GetOrCreateChunk(chunkIndex);

				if (chunk.mesh == null)
				{
					chunk.mesh = new Mesh();
					chunk.mesh.MarkDynamic();
				}

				chunk.meshCollider.enabled = false;
				chunk.meshCollider.sharedMesh = null;

				Mesh.ApplyAndDisposeWritableMeshData(
					meshDataArray,
					chunk.mesh,
					DracoDecoder.defaultMeshUpdateFlags);
				meshDataAllocated = false;

				chunk.mesh.bounds = result.bounds;
				if (result.calculateNormals)
					chunk.mesh.RecalculateNormals();
				chunk.mesh.MarkModified();

				chunk.meshFilter.sharedMesh = chunk.mesh;
				chunk.meshIsPopulated = chunk.mesh.vertexCount > 0 &&
				                        chunk.mesh.subMeshCount > 0 &&
				                        chunk.mesh.GetIndexCount(0) >= 3;
				chunk.meshCollider.sharedMesh = chunk.mesh;
				chunk.meshCollider.enabled = chunk.meshIsPopulated;

				if (chunk.meshIsPopulated)
					RemoteMeshApplied.Invoke(chunk);
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
			}
			finally
			{
				boneWeightData?.Dispose();
				if (meshDataAllocated) meshDataArray.Dispose();
				encodedData.Dispose();
			}
		}

		private void ClearRemoteChunk(
			int chunkIndex,
			(ulong sender, int chunkIndex) revisionKey,
			uint revision,
			int generation)
		{
			if (generation != syncGeneration ||
			    !SyncBus.Active ||
			    !receivedRevisions.TryGetValue(revisionKey, out uint currentRevision) ||
			    currentRevision != revision)
				return;

			Chunk chunk = EnvMesher.Instance.GetOrCreateChunk(chunkIndex);
			chunk.mesh.Clear();
			chunk.meshIsPopulated = false;
			chunk.meshCollider.sharedMesh = null;
			chunk.meshCollider.enabled = false;
		}

		private static bool IsNewerRevision(uint candidate, uint current)
		{
			return unchecked((int)(candidate - current)) > 0;
		}
	}
}
