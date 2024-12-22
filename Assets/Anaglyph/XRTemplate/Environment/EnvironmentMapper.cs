using Anaglyph.XRTemplate.DepthKit;
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(-10)]
	public class EnvironmentMapper : SingletonBehavior<EnvironmentMapper>
	{
		public static Action<ComputeBuffer> OnPerFrameEnvMap = delegate { };
		public static Action<RenderTexture> OnEnvMap = delegate { };

		public static int UNWRITTEN_INT = -32000;

		[SerializeField] private ComputeShader compute;

		[SerializeField] private int textureSize = 512;

		[SerializeField] private Vector2 depthRange = new Vector2(0.5f, 6f);
		[SerializeField] private Vector2 heightRange = new Vector2(-3f, 0.5f);

		[SerializeField] private float edgeFilterSize = 0.02f;
		[SerializeField] private float gradientCutoff = 0.2f;

		[SerializeField] private float lerpHeight = 0.2f;

		[SerializeField] private float envSize = 50;
		public float EnvironmentSize => envSize;

		[SerializeField] private int depthSamples = 128;

		private static int ID(string str) => Shader.PropertyToID(str);

		private static readonly int agEnvSizeMeters = ID(nameof(agEnvSizeMeters));
		private static readonly int agEnvHeightMap = ID(nameof(agEnvHeightMap));

		private static readonly int _PerFrameHeight = ID(nameof(_PerFrameHeight));
		private static readonly int _EnvHeightMapWritable = ID(nameof(_EnvHeightMapWritable));
		private static readonly int _TexSize = ID(nameof(_TexSize));
		
		private static readonly int _DepthSamples = ID(nameof(_DepthSamples));

		private static readonly int _DepthRange = ID(nameof(_DepthRange));
		private static readonly int _DepthFrameCrop = ID(nameof(_DepthFrameCrop));
		private static readonly int _HeightRange = ID(nameof(_HeightRange));

		private static readonly int _DepthFramePos = ID(nameof(_DepthFramePos));

		private static readonly int _EdgeFilterSize = ID(nameof(_EdgeFilterSize));
		private static readonly int _GradientCutoff = ID(nameof(_GradientCutoff));

		private static readonly int _LerpHeight = ID(nameof(_LerpHeight));

		private RenderTexture envMap;
		public RenderTexture Map => envMap;
		private ComputeBuffer perFrameEnvMap;

		private struct Kernel
		{
			public ComputeShader shader;
			public int index;
			public (int x, int y, int z) groupSize;

			public Kernel(ComputeShader shader, int index)
			{
				this.shader = shader;
				this.index = index;

				uint x, y, z;
				shader.GetKernelThreadGroupSizes(index, out x, out y, out z);
				groupSize = ((int)x, (int)y, (int)z);
			}

			public void SetTexture(int id, Texture texture)
			{
				shader.SetTexture(index, id, texture);
			}

			public void SetBuffer(int id, ComputeBuffer buffer)
			{
				shader.SetBuffer(index, id, buffer);
			}

			public void Dispatch(int fillX, int fillY, int fillZ)
			{
				int numGroupsX = fillX / groupSize.x;
				int numGroupsY = fillY / groupSize.y;
				int numGroupsZ = fillZ / groupSize.z;

				shader.Dispatch(index, numGroupsX, numGroupsY, numGroupsZ);
			}
		}

		private Kernel Accumulate;
		private Kernel Apply;
		private Kernel Init;

		protected override void SingletonAwake()
		{
			envMap = new RenderTexture(textureSize, textureSize, 0, GraphicsFormat.R16G16_SFloat);
			envMap.enableRandomWrite = true;

			var size = envMap.width * envMap.height;
			var sizeOfInt = Marshal.SizeOf<int>();
			perFrameEnvMap = new ComputeBuffer(size, sizeOfInt, ComputeBufferType.Structured);

			Shader.SetGlobalFloat(agEnvSizeMeters, envSize);
			Shader.SetGlobalTexture(agEnvHeightMap, envMap);
		}

		private void Start()
		{
			compute.SetInt(_TexSize, envMap.width);

			compute.SetFloat(_TexSize, envMap.width);
			compute.SetInt(_DepthSamples, depthSamples);

			compute.SetVector(_DepthRange, depthRange);
			compute.SetVector(_HeightRange, heightRange);

			compute.SetFloat(_EdgeFilterSize, edgeFilterSize);
			compute.SetFloat(_GradientCutoff, gradientCutoff);

			compute.SetFloat(_LerpHeight, lerpHeight);

			Accumulate = new Kernel(compute, 0);
			Accumulate.SetBuffer(_PerFrameHeight, perFrameEnvMap);
			Accumulate.SetTexture(_EnvHeightMapWritable, envMap);

			Apply = new Kernel(compute, 1);
			Apply.SetBuffer(_PerFrameHeight, perFrameEnvMap);
			Apply.SetTexture(_EnvHeightMapWritable, envMap);

			Init = new Kernel(compute, 2);
			Init.SetBuffer(_PerFrameHeight, perFrameEnvMap);
			Init.SetTexture(_EnvHeightMapWritable, envMap);

			Init.Dispatch(textureSize, textureSize, 1);
		}

		private void FixedUpdate()
		{
			if (!DepthKitDriver.DepthAvailable) return;

			Texture depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);
			if (depthTex == null) return;

			compute.SetVector(_DepthFramePos, DepthKitDriver.LastDepthFramePose.position);

			Accumulate.SetTexture(DepthKitDriver.agDepthTex_ID, depthTex);

			Accumulate.Dispatch(depthSamples, depthSamples, 1);
			OnPerFrameEnvMap.Invoke(perFrameEnvMap);
			//Apply.Dispatch(textureSize, textureSize, 1);
			//OnEnvMap.Invoke(envMap);
		}

		public void ApplyData(int[] data)
		{
			perFrameEnvMap.SetData(data);
			Apply.Dispatch(textureSize, textureSize, 1);
			OnEnvMap.Invoke(envMap);
		}

		public void ClearMap()
		{
			RenderTexture.active = envMap;
			GL.Clear(true, true, Color.black);
			RenderTexture.active = null;
		}

		protected override void OnSingletonDestroy()
		{
			ClearMap();
			perFrameEnvMap.Release();
		}
	}
}
