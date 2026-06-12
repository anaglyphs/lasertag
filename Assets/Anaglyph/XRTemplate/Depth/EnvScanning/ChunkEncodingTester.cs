using Anaglyph.DepthKit.EnvScanning;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.DepthKit.EnvScanningV2
{
	public class ChunkEncodingTester : MonoBehaviour
	{
		private EnvScanner.ChunkReadbackBuffer readbackBuffer;
		private NativeArray<EnvScanner.Voxel> decodedVoxels;
		private NativeList<sbyte> compressedData;

		private void Start()
		{
			EnvScanner scanner = EnvScanner.Instance;
			readbackBuffer = scanner.CreateChunkReadbackBuffer();
			decodedVoxels = new NativeArray<EnvScanner.Voxel>(readbackBuffer.data.Length, Allocator.Persistent);
			compressedData = new NativeList<sbyte>(Allocator.Persistent);

			TestLoop();
		}

		private void OnEnable()
		{
			if (didStart)
				TestLoop();
		}

		private void OnDestroy()
		{
			readbackBuffer.Dispose();
			decodedVoxels.Dispose();
			compressedData.Dispose();
		}

		private async void TestLoop()
		{
			EnvScanner scanner = EnvScanner.Instance;

			while (enabled)
			{
				int3 chunkCoord = scanner.WorldPosToChunkCoord(transform.position);
				int chunkIndex = scanner.ChunkCoordToChunkIndex(chunkCoord);

				bool success = await scanner.ReadbackChunkInto(chunkIndex, readbackBuffer);
				if (!success) Debug.LogError("Chunk readback failed!");

				await ChunkCompression.EncodeChunk(readbackBuffer.data, compressedData);

				success = await ChunkCompression.DecodeChunk(compressedData.AsArray(), decodedVoxels);

				if (!success) Debug.LogError("Chunk decode failed!");

				int mismatches = 0;

				for (int i = 0; i < decodedVoxels.Length; i++)
					if (readbackBuffer.data[i].value != decodedVoxels[i].value)
						mismatches++;

				Debug.Log(
					$"Uncompressed: {readbackBuffer.data.Length} | Compressed: {compressedData.Length} | Mismatches: {mismatches}");
			}
		}

		private void OnDrawGizmos()
		{
			EnvScanner scanner = EnvScanner.Instance;

			if (!scanner) return;

			int3 chunkCoord = scanner.WorldPosToChunkCoord(transform.position);
			Vector3 cornerPos = scanner.ChunkCoordToCornerWorldPos(chunkCoord);

			Vector3 center = cornerPos + ChunkManager.ChunkWorldSizeHalf;

			Gizmos.color = Color.red;
			Gizmos.DrawWireCube(center, ChunkManager.ChunkWorldSize);
		}
	}
}