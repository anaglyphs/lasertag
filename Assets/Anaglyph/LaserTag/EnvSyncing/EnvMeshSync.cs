using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Netcode;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.DepthKit.EnvScanning;
using Draco;
using Draco.Encode;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.LaserTag.EnvSyncing
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

		// How many voxels the surface must sweep past, relative to the state peers already
		// have, before a chunk is worth re-sending. Measured against the last transmission
		// rather than accumulated over time, so depth noise around a stationary surface
		// cancels out instead of eventually crossing any threshold on its own.
		[SerializeField] private int syncFlippedVoxelsThreshold = 32;

		// Floor on how often a single chunk may occupy the encoder and the wire. A chunk
		// held back here keeps accumulating divergence and sends as soon as it expires.
		[SerializeField] private float minSecondsBetweenChunkSends = 2f;

		// A below-threshold divergence is still sent after this long without another
		// completed mesh update, so the last small changes converge once a chunk settles.
		[SerializeField, Min(0f)] private float secondsUntilChunkStable = 2f;

		[Header("Draco compression")]
		[SerializeField, Range(1, 30)] private int positionQuantizationBits = 10;
		[SerializeField, Range(0, 10)] private int encodingSpeed;
		[SerializeField, Range(0, 10)] private int decodingSpeed = 4;

		// Chunk.voxelSignBits as of each chunk's last transmission. Absence means peers have
		// never seen the chunk at all.
		private readonly Dictionary<int, NativeArray<uint>> lastSentSignBits = new();
		private readonly Dictionary<int, float> lastSendTimes = new();

		// Chunks whose mesh changed since they were last weighed against the threshold.
		// Below-threshold chunks stay pending until another update or the stability timeout.
		private readonly HashSet<int> pendingSync = new();
		private readonly List<int> pendingSyncDrain = new();
		private readonly Dictionary<int, float> lastMeshUpdateTimes = new();
		private readonly HashSet<int> waitingForStability = new();

		// Draco encodes every vertex attribute a mesh declares and offers no way to skip one,
		// so positions are restaged through a mesh carrying no normal stream — normals are
		// roughly half the vertex payload and peers recalculate them on decode anyway.
		private static readonly VertexAttributeDescriptor[] PositionOnlyLayout = { new(VertexAttribute.Position) };
		private Mesh positionOnlyMesh;

		// Draco encoding is relatively expensive. Keep one sequential worker and
		// coalesce repeated requests for the same chunk while it waits in the queue.
		private readonly Queue<int> encodeQueue = new();
		private readonly HashSet<int> queuedEncodes = new();
		private bool encodeWorkerRunning;

		// Revisions prevent asynchronous Draco decodes from applying out of order.
		private readonly Dictionary<int, uint> sentRevisions = new();
		private readonly Dictionary<(ulong sender, int chunkIndex), uint> receivedRevisions = new();

		// Invalidates encoding/decoding work that outlives a network session.
		private int syncGeneration;

		private void Awake()
		{
			Instance = this;

			positionOnlyMesh = new Mesh { name = "Draco encode staging" };
			positionOnlyMesh.MarkDynamic();

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
			MapManager.WorldFrameRebased += OnWorldFrameRebased;

			if (EnvScanner.Instance != null)
				EnvScanner.Instance.Cleared += OnScanCleared;
		}

		/// <summary>
		/// A cleared scan destroys and rebuilds chunks, so anything remembered
		/// about them describes voxels that no longer exist
		/// </summary>
		private void OnScanCleared()
		{
			ClearPendingSync();
			lastSendTimes.Clear();
			ClearSentSignBits();
		}

		private void OnDestroy()
		{
			MapManager.WorldFrameRebased -= OnWorldFrameRebased;

			if (EnvScanner.Instance != null)
				EnvScanner.Instance.Cleared -= OnScanCleared;

			syncGeneration++;
			encodeQueue.Clear();
			queuedEncodes.Clear();
			ClearSentSignBits();

			Destroy(positionOnlyMesh);

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

			ClearPendingSync();
			lastSendTimes.Clear();
			ClearSentSignBits();
			encodeQueue.Clear();
			queuedEncodes.Clear();

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

			EnvMesher.Instance.ChunkMeshUpdated += OnChunkMeshUpdated;

			// Don't reset if authority, because the authority determines the coordinate system
			// so their scan will be aligned with the environment
			if(!SyncBus.IsAuthority)
				EnvScanner.Instance.Clear();
		}

		private void OnBusDeactivated()
		{
			syncGeneration++;

			if (EnvMesher.Instance)
				EnvMesher.Instance.ChunkMeshUpdated -= OnChunkMeshUpdated;

			ClearPendingSync();
			lastSendTimes.Clear();
			ClearSentSignBits();
			encodeQueue.Clear();
			queuedEncodes.Clear();
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

		private void OnChunkMeshUpdated(Chunk chunk)
		{
			pendingSync.Add(chunk.chunkIndex);
			lastMeshUpdateTimes[chunk.chunkIndex] = Time.time;
			waitingForStability.Remove(chunk.chunkIndex);
		}

		private void Update()
		{
			if (pendingSync.Count == 0) return;

			// don't send scans until they're in the shared reference space
			if (!SyncBus.Active || !ColocationManager.IsColocated) return;

			pendingSyncDrain.Clear();
			pendingSyncDrain.AddRange(pendingSync);

			foreach (int chunkIndex in pendingSyncDrain)
			{
				if (!EnvMesher.Instance.TryGetChunk(chunkIndex, out Chunk chunk) ||
				    !chunk.voxelSignBits.IsCreated)
				{
					RemovePendingSync(chunkIndex);
					continue;
				}

				// leave it pending: re-meshing will fire ChunkMeshUpdated again
				if (chunk.dirty) continue;

				if (lastSendTimes.TryGetValue(chunkIndex, out float lastSendTime) &&
				    Time.time - lastSendTime < minSecondsBetweenChunkSends)
					continue;

				bool hasStabilized = !lastMeshUpdateTimes.TryGetValue(chunkIndex, out float lastMeshUpdateTime) ||
				                     Time.time - lastMeshUpdateTime >= secondsUntilChunkStable;

				// The threshold was already checked for this exact mesh. Avoid recounting its
				// voxel bits every frame while only the stability timer is advancing.
				if (waitingForStability.Contains(chunkIndex) && !hasStabilized)
					continue;

				int flippedVoxelsThreshold = hasStabilized ? 1 : Math.Max(1, syncFlippedVoxelsThreshold);
				if (!HasDivergedFromPeers(chunk, flippedVoxelsThreshold))
				{
					if (hasStabilized)
						RemovePendingSync(chunkIndex);
					else
						waitingForStability.Add(chunkIndex);

					continue;
				}

				RemovePendingSync(chunkIndex);
				lastSendTimes[chunkIndex] = Time.time;
				QueueChunkMesh(chunk);
			}
		}

		private bool HasDivergedFromPeers(Chunk chunk, int flippedVoxelsThreshold)
		{
			// peers have never seen this chunk, so anything in it is news
			if (!lastSentSignBits.TryGetValue(chunk.chunkIndex, out NativeArray<uint> sentSignBits))
				return chunk.meshIsPopulated;

			NativeArray<uint> signBits = chunk.voxelSignBits;
			int flippedVoxels = 0;

			for (int i = 0; i < signBits.Length; i++)
			{
				flippedVoxels += math.countbits(signBits[i] ^ sentSignBits[i]);
				if (flippedVoxels >= flippedVoxelsThreshold) return true;
			}

			return false;
		}

		private void RemovePendingSync(int chunkIndex)
		{
			pendingSync.Remove(chunkIndex);
			lastMeshUpdateTimes.Remove(chunkIndex);
			waitingForStability.Remove(chunkIndex);
		}

		private void ClearPendingSync()
		{
			pendingSync.Clear();
			lastMeshUpdateTimes.Clear();
			waitingForStability.Clear();
		}

		/// <summary>
		/// Records the surface peers are about to receive, so the next threshold
		/// test measures divergence from what they hold rather than from scratch
		/// </summary>
		private void RememberSentSurface(Chunk chunk)
		{
			if (!chunk.voxelSignBits.IsCreated) return;

			if (!lastSentSignBits.TryGetValue(chunk.chunkIndex, out NativeArray<uint> sentSignBits))
			{
				sentSignBits = new NativeArray<uint>(chunk.voxelSignBits.Length, Allocator.Persistent);
				lastSentSignBits[chunk.chunkIndex] = sentSignBits;
			}

			sentSignBits.CopyFrom(chunk.voxelSignBits);
		}

		/// <summary>
		/// The send never landed, so peers hold nothing for this chunk
		/// and its next mesh update should go out unconditionally
		/// </summary>
		private void ForgetSentSurface(int chunkIndex)
		{
			if (!lastSentSignBits.Remove(chunkIndex, out NativeArray<uint> sentSignBits)) return;

			sentSignBits.Dispose();
		}

		private void ClearSentSignBits()
		{
			foreach (NativeArray<uint> sentSignBits in lastSentSignBits.Values)
				sentSignBits.Dispose();

			lastSentSignBits.Clear();
		}

		private void QueueChunkMesh(Chunk chunk)
		{
			if (!SyncBus.Active) return;

			if (queuedEncodes.Add(chunk.chunkIndex))
				encodeQueue.Enqueue(chunk.chunkIndex);

			StartEncodeWorkerIfNeeded();
		}

		private void StartEncodeWorkerIfNeeded()
		{
			if (encodeWorkerRunning || encodeQueue.Count == 0 || !SyncBus.Active)
				return;

			encodeWorkerRunning = true;
			ProcessEncodeQueue(syncGeneration);
		}

		private async void ProcessEncodeQueue(int generation)
		{
			try
			{
				while (generation == syncGeneration && SyncBus.Active && encodeQueue.Count > 0)
				{
					int chunkIndex = encodeQueue.Dequeue();
					queuedEncodes.Remove(chunkIndex);

					if (EnvMesher.Instance.TryGetChunk(chunkIndex, out Chunk chunk))
						await EncodeAndSendChunk(chunk, generation);
				}
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
			}
			finally
			{
				encodeWorkerRunning = false;
				StartEncodeWorkerIfNeeded();
			}
		}

		private async Task EncodeAndSendChunk(Chunk chunk, int generation)
		{
			if (generation != syncGeneration || !SyncBus.Active)
				return;

			int chunkIndex = chunk.chunkIndex;
			Mesh mesh = chunk.mesh;

			bool isPopulated = mesh != null &&
			                   mesh.vertexCount > 0 &&
			                   mesh.subMeshCount == 1 &&
			                   mesh.GetIndexCount(0) >= 3;

			if (!isPopulated)
			{
				RememberSentSurface(chunk);
				RaiseChunkPayload(chunkIndex, NextRevision(chunkIndex), default);
				return;
			}

			CopyPositionsOnly(mesh, positionOnlyMesh);

			// snapshot alongside the geometry being staged, not after the encode —
			// the chunk may re-mesh while Draco is working
			RememberSentSurface(chunk);

			EncodeResult[] results = null;

			try
			{
				using Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(positionOnlyMesh);

				QuantizationSettings quantization = new(positionQuantizationBits);
				SpeedSettings speed = new(encodingSpeed, decodingSpeed);
				results = await DracoEncoder.EncodeMesh(positionOnlyMesh, meshDataArray[0], quantization, speed);

				if (!this || generation != syncGeneration || !SyncBus.Active)
					return;

				if (results == null || results.Length != 1)
				{
					Debug.LogWarning(
						$"[{nameof(EnvMeshSync)}] Draco failed to encode chunk {chunkIndex}");
					ForgetSentSurface(chunkIndex);
					return;
				}

				NativeArray<byte> encodedData = results[0].data;
				int payloadSize = ChunkPayloadHeaderBytes + encodedData.Length;

				if (payloadSize > MaxPayloadBytes)
				{
					Debug.LogWarning(
						$"[{nameof(EnvMeshSync)}] Chunk {chunkIndex} Draco payload ({payloadSize}B) exceeds the fragmented message cap");
					ForgetSentSurface(chunkIndex);
					return;
				}

				RaiseChunkPayload(chunkIndex, NextRevision(chunkIndex), encodedData);
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

		private const MeshUpdateFlags EncodeStagingFlags = MeshUpdateFlags.DontNotifyMeshUsers |
		                                                   MeshUpdateFlags.DontRecalculateBounds |
		                                                   MeshUpdateFlags.DontValidateIndices;

		private static void CopyPositionsOnly(Mesh source, Mesh destination)
		{
			using Mesh.MeshDataArray sourceArray = Mesh.AcquireReadOnlyMeshData(source);
			Mesh.MeshData sourceData = sourceArray[0];

			int vertexCount = sourceData.vertexCount;
			int indexCount = (int)source.GetIndexCount(0);

			Mesh.MeshDataArray destinationArray = Mesh.AllocateWritableMeshData(1);
			Mesh.MeshData destinationData = destinationArray[0];

			destinationData.SetVertexBufferParams(vertexCount, PositionOnlyLayout);
			destinationData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);

			sourceData.GetVertices(destinationData.GetVertexData<Vector3>());
			sourceData.GetIndices(destinationData.GetIndexData<int>(), 0);

			destinationData.subMeshCount = 1;
			destinationData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), EncodeStagingFlags);

			Mesh.ApplyAndDisposeWritableMeshData(destinationArray, destination, EncodeStagingFlags);
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

				// a MeshCollider only recooks when sharedMesh changes value, so editing the
				// mesh it already holds needs the reference cleared and reassigned. the two
				// stay adjacent so nothing failing in between can strand the collider empty
				chunk.meshCollider.sharedMesh = null;
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

			// nothing to clear if the chunk was never built here
			if (!EnvMesher.Instance.TryGetChunk(chunkIndex, out Chunk chunk)) return;

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
