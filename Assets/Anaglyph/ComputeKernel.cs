using UnityEngine;

namespace Anaglyph
{
	public struct ComputeKernel
	{
		public ComputeShader shader;
		public int index;
		public (int x, int y, int z) groupSize;

		public ComputeKernel(ComputeShader shader, int index)
		{
			this.shader = shader;
			this.index = index;

			uint x, y, z;
			shader.GetKernelThreadGroupSizes(index, out x, out y, out z);
			groupSize = ((int)x, (int)y, (int)z);
		}

		public ComputeKernel(ComputeShader shader, string kernel) 
			: this(shader, shader.FindKernel(kernel)) { }

		public void Set(int id, Texture texture)
		{
			shader.SetTexture(index, id, texture);
		}

		public void Set(int id, ComputeBuffer buffer)
		{
			shader.SetBuffer(index, id, buffer);
		}

		public void Dispatch(int fillX, int fillY, int fillZ = 1)
		{
			int numGroupsX = fillX / groupSize.x;
			int numGroupsY = fillY / groupSize.y;
			int numGroupsZ = fillZ / groupSize.z;

			shader.Dispatch(index, numGroupsX, numGroupsY, numGroupsZ);
		}
	}
}
