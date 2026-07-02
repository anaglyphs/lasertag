using System;
using System.Collections.Generic;
using Anaglyph.DepthKit.EnvScanning;
using Anaglyph.Netcode;
using Unity.Collections;
using Unity.Mathematics;
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

		/// <summary>
		/// Invoked after a populated chunk mesh received from another
		/// player is applied to a chunk's remote mesh
		/// </summary>
		public static event Action<Chunk> RemoteMeshApplied = delegate { };

		// a swept voxel is one voxel fully flipping sign (-127 to 127)
		[SerializeField] private int syncSweptVoxelsThreshold = 100;

		// per-chunk change sum at last sync; chunks re-sync every
		// time they accumulate a threshold's worth of new change
		private readonly Dictionary<int, uint> lastSyncedChangeSums = new();

		// chunks that crossed the threshold mid-mesh, waiting
		// to send until meshing and decimation finish
		private readonly HashSet<int> pendingSync = new();

		// world size of the full chunk volume, for position quantization
		private float chunkSpan;

		private void Awake()
		{
			Instance = this;

			chunkEvent.Register();
			visibleEvent.Register();
			chunkEvent.Received += OnChunkReceived;
			visibleEvent.Received += OnVisibleReceived;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
		}

		private void OnDestroy()
		{
			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			chunkEvent.Received -= OnChunkReceived;
			visibleEvent.Received -= OnVisibleReceived;
			visibleEvent.Unregister();
			chunkEvent.Unregister();
		}

		private void OnBusActivated()
		{
			chunkSpan = EnvScanner.Instance.VoxPerChunkDim * EnvScanner.Instance.VoxSize;

			EnvMesher.Instance.VisibleChunkPolled += OnVisibleChunkPolled;
			EnvMesher.Instance.ChunkMeshUpdated += OnChunkMeshUpdated;
		}

		private void OnBusDeactivated()
		{
			if (EnvMesher.Instance)
			{
				EnvMesher.Instance.VisibleChunkPolled -= OnVisibleChunkPolled;
				EnvMesher.Instance.ChunkMeshUpdated -= OnChunkMeshUpdated;
			}

			pendingSync.Clear();
			lastSyncedChangeSums.Clear();
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
				SendChunkMesh(chunk);
			}
		}

		private void OnChunkMeshUpdated(Chunk chunk)
		{
			if (pendingSync.Remove(chunk.chunkIndex))
				SendChunkMesh(chunk);
		}

		private void SendChunkMesh(Chunk chunk)
		{
			if (!SyncBus.Active) return;

			using Mesh.MeshDataArray dataArray = Mesh.AcquireReadOnlyMeshData(chunk.mesh);
			Mesh.MeshData data = dataArray[0];

			int vertCount = data.vertexCount;
			int indexCount = data.subMeshCount > 0 ? data.GetSubMesh(0).indexCount : 0;

			if (vertCount > ushort.MaxValue)
			{
				Debug.LogWarning($"[{nameof(EnvMeshSync)}] Chunk {chunk.chunkIndex} mesh too large to sync");
				return;
			}

			int payloadSize = sizeof(int) * 3 + (vertCount * 3 + indexCount) * sizeof(ushort);

			if (payloadSize > MaxPayloadBytes)
			{
				Debug.LogWarning(
					$"[{nameof(EnvMeshSync)}] Chunk {chunk.chunkIndex} payload ({payloadSize}B) exceeds the fragmented message cap");
				return;
			}

			NativeArray<Vector3> positions = new(vertCount, Allocator.Temp);
			NativeArray<int> indices = new(indexCount, Allocator.Temp);

			if (vertCount > 0) data.GetVertices(positions);
			if (indexCount > 0) data.GetIndices(indices, 0);

			// quantize positions to 16 bits per axis in chunk-local space
			float quantScale = ushort.MaxValue / chunkSpan;

			byte[] payload = new byte[payloadSize];
			int offset = 0;

			SyncBytes.Write(payload, offset, chunk.chunkIndex);
			offset += sizeof(int);

			SyncBytes.Write(payload, offset, vertCount);
			offset += sizeof(int);

			for (int v = 0; v < vertCount; v++)
			{
				Vector3 p = positions[v];
				SyncBytes.Write(payload, offset,
					(ushort)math.clamp(math.round(p.x * quantScale), 0, ushort.MaxValue));
				SyncBytes.Write(payload, offset + sizeof(ushort),
					(ushort)math.clamp(math.round(p.y * quantScale), 0, ushort.MaxValue));
				SyncBytes.Write(payload, offset + sizeof(ushort) * 2,
					(ushort)math.clamp(math.round(p.z * quantScale), 0, ushort.MaxValue));
				offset += sizeof(ushort) * 3;
			}

			SyncBytes.Write(payload, offset, indexCount);
			offset += sizeof(int);

			for (int i = 0; i < indexCount; i++)
			{
				SyncBytes.Write(payload, offset, (ushort)indices[i]);
				offset += sizeof(ushort);
			}

			chunkEvent.Raise(payload);
		}

		private void OnChunkReceived(ulong sender, byte[] payload)
		{
			// Direct events also invoke locally; the sender already has this mesh.
			if (sender == SyncBus.LocalClientId) return;

			// validate malformed or corrupt payloads
			if (payload.Length < sizeof(int) * 2) return;

			int offset = 0;

			int chunkIndex = SyncBytes.Read<int>(payload, offset);
			offset += sizeof(int);

			if (chunkIndex < 0 || chunkIndex >= EnvScanner.Instance.ChunkTableLength)
				return;

			int vertCount = SyncBytes.Read<int>(payload, offset);
			offset += sizeof(int);

			if (vertCount < 0 || payload.Length < offset + vertCount * 3 * sizeof(ushort) + sizeof(int))
				return;

			int positionsOffset = offset;
			offset += vertCount * 3 * sizeof(ushort);

			int indexCount = SyncBytes.Read<int>(payload, offset);
			offset += sizeof(int);

			if (indexCount < 0 || indexCount % 3 != 0 ||
			    payload.Length < offset + indexCount * sizeof(ushort))
				return;

			NativeArray<Vector3> positions = new(vertCount, Allocator.Temp);
			NativeArray<int> indices = new(indexCount, Allocator.Temp);

			float dequantScale = chunkSpan / ushort.MaxValue;

			for (int v = 0; v < vertCount; v++)
				positions[v] = new Vector3(
					SyncBytes.Read<ushort>(payload, positionsOffset + (v * 3 + 0) * sizeof(ushort)) * dequantScale,
					SyncBytes.Read<ushort>(payload, positionsOffset + (v * 3 + 1) * sizeof(ushort)) * dequantScale,
					SyncBytes.Read<ushort>(payload, positionsOffset + (v * 3 + 2) * sizeof(ushort)) * dequantScale);

			for (int i = 0; i < indexCount; i++)
			{
				int index = SyncBytes.Read<ushort>(payload, offset + i * sizeof(ushort));
				if (index >= vertCount) return;
				indices[i] = index;
			}

			Chunk chunk = EnvMesher.Instance.GetOrCreateChunk(chunkIndex);
			chunk.ApplyMeshData(positions, indices);

			if (indices.Length > 0)
				RemoteMeshApplied.Invoke(chunk);
		}
	}
}
