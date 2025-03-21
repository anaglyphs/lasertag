using UnityEngine;
using UnityEngine.Rendering;

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

		public void Set(string id, Texture texture)
		{
			shader.SetTexture(index, id, texture);
		}

		public void Set(string id, ComputeBuffer buffer)
		{
			shader.SetBuffer(index, id, buffer);
		}

		public void Dispatch(int x, int y, int z)
			=> shader.Dispatch(index, x, y, z);

		public void DispatchGroups(int fillX, int fillY, int fillZ = 1)
		{
			int numGroupsX = Mathf.CeilToInt(fillX / (float)groupSize.x);
			int numGroupsY = Mathf.CeilToInt(fillY / (float)groupSize.y);
			int numGroupsZ = Mathf.CeilToInt(fillZ / (float)groupSize.z);

			Dispatch(numGroupsX, numGroupsY, numGroupsZ);
		}

		public void DispatchGroups(RenderTexture tex) =>
			DispatchGroups(tex.width, tex.height, tex.volumeDepth);
	}
}
