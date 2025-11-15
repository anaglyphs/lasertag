using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.DepthKit
{
	public class EnvMapper2 : MonoBehaviour
	{
		public EnvMapper2 Instance { get; private set; }

		[SerializeField] private ComputeShader comp;

		private ComputeKernel Integrate;
		private ComputeBuffer blocks;

		[GenerateHLSL(PackingRules.Exact, false)]
		public struct Block
		{
			public int3 pos_ws; // 24
			public uint offset; //  8
			public uint ptr; //  8
			public uint3 padding; // 24
			public const int Size = 32;
		}

		private void Awake()
		{
			Instance = this;

			blocks = new ComputeBuffer(512, Block.Size);

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

			var data = result.GetData<Block>();
			var oldCount = blocks.count;
			blocks.Dispose();
			blocks = new ComputeBuffer(oldCount * 2, Block.Size);
			blocks.SetData(data);

			Integrate.Set("blocks", blocks);
		}
	}
}