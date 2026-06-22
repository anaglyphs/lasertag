using System;
using System.Collections.Generic;
using Anaglyph.DepthKit.EnvScanning;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Broadcasts locally scanned chunk meshes to other players when chunks
	/// pass a change threshold, and applies meshes received from other players
	/// </summary>
	public class EnvMeshSync : NetworkBehaviour
	{
		public static EnvMeshSync Instance { get; private set; }

		private const string MessageName = "EnvChunkSync";
		private const NetworkDelivery Delivery = NetworkDelivery.ReliableFragmentedSequenced;

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
		}

		public override void OnNetworkSpawn()
		{
			chunkSpan = EnvScanner.Instance.VoxPerChunkDim * EnvScanner.Instance.VoxSize;

			NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnChunkMessage);
			EnvMesher.Instance.VisibleChunkPolled += OnVisibleChunkPolled;
			EnvMesher.Instance.ChunkMeshUpdated += OnChunkMeshUpdated;
		}

		public override void OnNetworkDespawn()
		{
			if (EnvMesher.Instance)
			{
				EnvMesher.Instance.VisibleChunkPolled -= OnVisibleChunkPolled;
				EnvMesher.Instance.ChunkMeshUpdated -= OnChunkMeshUpdated;
			}

			NetworkManager?.CustomMessagingManager?.UnregisterNamedMessageHandler(MessageName);

			pendingSync.Clear();
			lastSyncedChangeSums.Clear();
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
			if (!IsSpawned) return;

			using Mesh.MeshDataArray dataArray = Mesh.AcquireReadOnlyMeshData(chunk.mesh);
			Mesh.MeshData data = dataArray[0];

			int vertCount = data.vertexCount;
			int indexCount = data.subMeshCount > 0 ? data.GetSubMesh(0).indexCount : 0;

			if (vertCount > ushort.MaxValue)
			{
				Debug.LogWarning($"[{nameof(EnvMeshSync)}] Chunk {chunk.chunkIndex} mesh too large to sync");
				return;
			}

			NativeArray<Vector3> positions = new(vertCount, Allocator.Temp);
			NativeArray<int> indices = new(indexCount, Allocator.Temp);

			if (vertCount > 0) data.GetVertices(positions);
			if (indexCount > 0) data.GetIndices(indices, 0);

			// quantize positions to 16 bits per axis in chunk-local space
			NativeArray<ushort> qPositions = new(vertCount * 3, Allocator.Temp);
			float quantScale = ushort.MaxValue / chunkSpan;

			for (int v = 0; v < vertCount; v++)
			{
				Vector3 p = positions[v];
				qPositions[v * 3 + 0] = (ushort)math.clamp(math.round(p.x * quantScale), 0, ushort.MaxValue);
				qPositions[v * 3 + 1] = (ushort)math.clamp(math.round(p.y * quantScale), 0, ushort.MaxValue);
				qPositions[v * 3 + 2] = (ushort)math.clamp(math.round(p.z * quantScale), 0, ushort.MaxValue);
			}

			NativeArray<ushort> qIndices = new(indexCount, Allocator.Temp);

			for (int i = 0; i < indexCount; i++)
				qIndices[i] = (ushort)indices[i];

			using FastBufferWriter writer = CreateChunkMessage(chunk.chunkIndex, qPositions, qIndices);

			if (IsServer)
				Broadcast(writer, NetworkManager.LocalClientId);
			else
				NetworkManager.CustomMessagingManager.SendNamedMessage(
					MessageName, NetworkManager.ServerClientId, writer, Delivery);
		}

		private static FastBufferWriter CreateChunkMessage(int chunkIndex,
			NativeArray<ushort> qPositions, NativeArray<ushort> qIndices)
		{
			int size = sizeof(int) * 3 + (qPositions.Length + qIndices.Length) * sizeof(ushort);
			FastBufferWriter writer = new(size, Allocator.Temp);

			writer.WriteValueSafe(chunkIndex);
			writer.WriteValueSafe(qPositions);
			writer.WriteValueSafe(qIndices);

			return writer;
		}

		private void Broadcast(FastBufferWriter writer, ulong excludeClientId)
		{
			foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
			{
				if (clientId == NetworkManager.LocalClientId) continue;
				if (clientId == excludeClientId) continue;

				NetworkManager.CustomMessagingManager.SendNamedMessage(MessageName, clientId, writer, Delivery);
			}
		}

		private void OnChunkMessage(ulong senderClientId, FastBufferReader reader)
		{
			reader.ReadValueSafe(out int chunkIndex);

			if (chunkIndex < 0 || chunkIndex >= EnvScanner.Instance.ChunkTableLength)
				return;

			reader.ReadValueSafe(out NativeArray<ushort> qPositions, Allocator.Temp);
			reader.ReadValueSafe(out NativeArray<ushort> qIndices, Allocator.Temp);

			int vertCount = qPositions.Length / 3;

			// validate malformed or corrupt mesh data
			if (qPositions.Length % 3 != 0 || qIndices.Length % 3 != 0)
				return;

			for (int i = 0; i < qIndices.Length; i++)
				if (qIndices[i] >= vertCount)
					return;

			// clients can't message each other directly; the server relays
			if (IsServer)
			{
				using FastBufferWriter writer = CreateChunkMessage(chunkIndex, qPositions, qIndices);
				Broadcast(writer, senderClientId);
			}

			ApplyReceivedMesh(chunkIndex, qPositions, qIndices);
		}

		private void ApplyReceivedMesh(int chunkIndex, NativeArray<ushort> qPositions, NativeArray<ushort> qIndices)
		{
			Chunk chunk = EnvMesher.Instance.GetOrCreateChunk(chunkIndex);

			int vertCount = qPositions.Length / 3;
			NativeArray<Vector3> positions = new(vertCount, Allocator.Temp);
			NativeArray<int> indices = new(qIndices.Length, Allocator.Temp);

			float dequantScale = chunkSpan / ushort.MaxValue;

			for (int v = 0; v < vertCount; v++)
				positions[v] = new Vector3(
					qPositions[v * 3 + 0] * dequantScale,
					qPositions[v * 3 + 1] * dequantScale,
					qPositions[v * 3 + 2] * dequantScale);

			for (int i = 0; i < qIndices.Length; i++)
				indices[i] = qIndices[i];

			chunk.ApplyMeshData(positions, indices);

			if (indices.Length > 0)
				RemoteMeshApplied.Invoke(chunk);
		}

		[Rpc(SendTo.Everyone)]
		public void SetEnvMeshVisibleEveryoneRpc(bool visible)
		{
			EnvMesher.Instance.SetChunksVisible(visible);
		}
	}
}