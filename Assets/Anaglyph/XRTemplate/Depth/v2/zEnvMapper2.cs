using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.DepthKit
{
	public class zEnvMapper2 : MonoBehaviour
	{
		public zEnvMapper2 Instance { get; private set; }

		[SerializeField] private ComputeShader comp;

		private ComputeKernel Integrate;
		private ComputeBuffer blocks;

		[GenerateHLSL(PackingRules.Exact, false)]
		public struct BlockEntry
		{
			public int3 pos_bs; // 24
			public uint block_index; //  8
			public const int ByteSize = 32;
		}

		private void Awake()
		{
			Instance = this;

			blocks = new ComputeBuffer(1024 * 1024, sizeof(float));

			Integrate.Set("blocks", blocks);
		}

		private async Task ExpandBuffer()
		{
			var tcs = new TaskCompletionSource<AsyncGPUReadbackRequest>();

			AsyncGPUReadback.Request(blocks, (req) =>
			{
				if (req.hasError)
					tcs.SetException(new System.Exception("GPU readback failed"));
				else
					tcs.SetResult(req);
			});

			var result = await tcs.Task;

			if (result.hasError)
				return;

			var data = result.GetData<BlockEntry>();
			var oldCount = blocks.count;
			blocks.Dispose();
			blocks = new ComputeBuffer(oldCount * 2, BlockEntry.ByteSize);
			blocks.SetData(data);

			Integrate.Set("blocks", blocks);
		}
	}
}