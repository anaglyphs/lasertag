using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Anaglyph.DepthKit.EnvScanning;
using Anaglyph.Netcode;
using Draco;
using Draco.Encode;
using Unity.Collections;
using UnityEngine;

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

		// a swept voxel is one voxel fully flipping sign (-127 to 127)
		[SerializeField] private int syncSweptVoxelsThreshold = 100;

		[Header("Draco compression")]
		[SerializeField, Range(1, 30)] private int positionQuantizationBits = 10;
		[SerializeField, Range(0, 10)] private int encodingSpeed;
		[SerializeField, Range(0, 10)] private int decodingSpeed = 4;

		// per-chunk change sum at last sync; chunks re-sync every
		// time they accumulate a threshold's worth of new change
		private readonly Dictionary<int, uint> lastSyncedChangeSums = new();

		// chunks that crossed the threshold mid-mesh, waiting
		// to send until meshing and decimation finish
		private readonly HashSet<int> pendingSync = new();

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

			chunkEvent.Register();
			visibleEvent.Register();
			chunkEvent.Received += OnChunkReceived;
			visibleEvent.Received += OnVisibleReceived;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			ColocationManager.Colocated += OnColocated;
		}

		private void OnDestroy()
		{
			syncGeneration++;
			encodeQueue.Clear();
			queuedEncodes.Clear();

			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			chunkEvent.Received -= OnChunkReceived;
			visibleEvent.Received -= OnVisibleReceived;
			visibleEvent.Unregister();
			chunkEvent.Unregister();

			ColocationManager.Colocated -= OnColocated;
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

			EnvMesher.Instance.VisibleChunkPolled += OnVisibleChunkPolled;
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
			{
				EnvMesher.Instance.VisibleChunkPolled -= OnVisibleChunkPolled;
				EnvMesher.Instance.ChunkMeshUpdated -= OnChunkMeshUpdated;
			}

			pendingSync.Clear();
			lastSyncedChangeSums.Clear();
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

		private void OnVisibleChunkPolled(int chunkIndex, uint changeSum)
		{
			// don't send scans until they're in the shared reference space

			if (!ColocationManager.IsColocated) return;

			lastSyncedChangeSums.TryGetValue(chunkIndex, out uint lastSynced);

			// subtraction guards against overflow, like the meshing threshold
			if (changeSum - lastSynced < (uint)(syncSweptVoxelsThreshold * 254)) return;

			lastSyncedChangeSums[chunkIndex] = changeSum;

			Chunk chunk = EnvMesher.Instance.GetOrCreateChunk(chunkIndex);

			if (chunk.dirty)
			{
				pendingSync.Add(chunkIndex); // send once it finishes meshing
			}
			else
			{
				pendingSync.Remove(chunkIndex);
				QueueChunkMesh(chunk);
			}
		}

		private void OnChunkMeshUpdated(Chunk chunk)
		{
			if (pendingSync.Remove(chunk.chunkIndex))
				QueueChunkMesh(chunk);
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
				RaiseChunkPayload(chunkIndex, NextRevision(chunkIndex), default);
				return;
			}

			EncodeResult[] results = null;

			try
			{
				using Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);

				QuantizationSettings quantization = new(positionQuantizationBits);
				SpeedSettings speed = new(encodingSpeed, decodingSpeed);
				results = await DracoEncoder.EncodeMesh(mesh, meshDataArray[0], quantization, speed);

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
