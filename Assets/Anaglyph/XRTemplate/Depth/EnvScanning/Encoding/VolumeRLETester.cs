using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Utilities.XR;

namespace Anaglyph.DepthKit.EnvScanning.Encoding
{
	public class VolumeRLETester : MonoBehaviour
	{
		private EnvScanner.ChunkReadbackBuffer readbackBuffer;
		private NativeArray<EnvScanner.Voxel> decodedVoxels;
		private NativeList<sbyte> compressedData;

		private int uncompressedSize;
		private int compressedSize;
		private int mismatches;

		private readonly StringBuilder reportText = new();

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

				await VolumeRLE.EncodeChunk(readbackBuffer.data, compressedData);

				success = await VolumeRLE.DecodeChunk(compressedData.AsArray(), decodedVoxels);

				if (!success) Debug.LogError("Chunk decode failed!");

				int mismatchCount = 0;

				for (int i = 0; i < decodedVoxels.Length; i++)
					if (readbackBuffer.data[i].value != decodedVoxels[i].value)
						mismatchCount++;

				uncompressedSize = readbackBuffer.data.Length;
				compressedSize = compressedData.Length;
				mismatches = mismatchCount;
				float ratio = compressedSize / (float)uncompressedSize;

				reportText.Clear();
				reportText.AppendLine($"Ratio -------- {ratio}");
				reportText.AppendLine($"Compressed --- {compressedSize}");
				reportText.AppendLine($"Uncompressed - {uncompressedSize}");
				reportText.AppendLine($"Mismatches --- {mismatchCount}");

				Debug.Log(reportText.ToString());
			}
		}

		private void Update()
		{
			EnvScanner scanner = EnvScanner.Instance;

			if (!scanner) return;

			int3 chunkCoord = scanner.WorldPosToChunkCoord(transform.position);

			scanner.DrawChunkBounds(chunkCoord, Color.red);
			XRGizmos.DrawSphere(transform.position, 0.1f, Color.red);

			XRGizmos.DrawString(reportText.ToString(), transform.position, transform.rotation, Color.red);
		}
	}
}