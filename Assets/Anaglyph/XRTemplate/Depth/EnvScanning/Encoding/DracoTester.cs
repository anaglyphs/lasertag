using System.Text;
using Draco.Encode;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using Utilities.XR;

namespace Anaglyph.DepthKit.EnvScanning.Encoding
{
    public class DracoTester : MonoBehaviour
    {
		private NativeList<sbyte> compressedData;

		private int uncompressedSize;
		private int compressedSize;

		private readonly StringBuilder reportText = new();

		private void Start()
		{
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
			compressedData.Dispose();
		}

		private async void TestLoop()
		{
			EnvScanner scanner = EnvScanner.Instance;
			EnvMesher mesher = EnvMesher.Instance;

			while (enabled)
			{
				await Awaitable.NextFrameAsync();
				
				int3 chunkCoord = scanner.WorldPosToChunkCoord(transform.position);
				int chunkIndex = scanner.ChunkCoordToChunkIndex(chunkCoord);

				if (!mesher.TryGetChunk(chunkIndex, out Chunk chunk))
					continue;
				
				if(chunk.mesh == null || !chunk.meshIsPopulated)
					continue;
				
				EncodeResult[] encodeResults = await DracoEncoder.EncodeMesh(chunk.mesh);
				
				if (encodeResults == null)
				{
					Debug.LogError("Encoding Draco failed!");
					continue;
				}
				
				compressedSize = 0;
				uncompressedSize = 0;
				
				foreach (EncodeResult result in encodeResults)
					compressedSize += result.data.Length;
				
				uncompressedSize = (int)Profiler.GetRuntimeMemorySizeLong(chunk.mesh);
				
				float ratio = compressedSize / (float)uncompressedSize;

				reportText.Clear();
				reportText.AppendLine($"Ratio -------- {ratio}");
				reportText.AppendLine($"Compressed --- {compressedSize}");
				reportText.AppendLine($"Uncompressed - {uncompressedSize}");

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