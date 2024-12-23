using Anaglyph.XRTemplate.DepthKit;
using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(-10)]
	public class EnvironmentMapper : SingletonBehavior<EnvironmentMapper>
	{
		public static Action<int[]> OnPerFrameEnvMap = delegate { };

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
		//private RenderTexture perFrameMap;
		private ComputeBuffer perFrameMap;
		private int[] lastPerFrameEnvMapData;
		private bool running = true; 

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

			public void Set(int id, Texture texture)
			{
				shader.SetTexture(index, id, texture);
			}

			public void Set(int id, ComputeBuffer buffer)
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
			lastPerFrameEnvMapData = new int[size];
			//perFrameMap = new RenderTexture(textureSize, textureSize, 0, GraphicsFormat.R32_SInt);
			//perFrameMap.enableRandomWrite = true;
			perFrameMap = new ComputeBuffer(size, Marshal.SizeOf<int>(), ComputeBufferType.Structured);

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
			Accumulate.Set(_PerFrameHeight, perFrameMap);
			Accumulate.Set(_EnvHeightMapWritable, envMap);

			Apply = new Kernel(compute, 1);
			Apply.Set(_PerFrameHeight, perFrameMap);
			Apply.Set(_EnvHeightMapWritable, envMap);

			Init = new Kernel(compute, 2);
			Init.Set(_PerFrameHeight, perFrameMap);
			Init.Set(_EnvHeightMapWritable, envMap);

			Init.Dispatch(textureSize, textureSize, 1);

			StartCoroutine(ScanRoomLoop());
		}

		private IEnumerator ScanRoomLoop()
		{
			running = true;
			while (running)
			{
				Texture depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);
				if(depthTex == null)
				{
					yield return null;
					continue;
				}

				compute.SetVector(_DepthFramePos, DepthKitDriver.LastDepthFramePose.position);
				compute.SetVector(_HeightRange, heightRange);
				Accumulate.Set(DepthKitDriver.agDepthTex_ID, depthTex);
				Accumulate.Dispatch(depthSamples, depthSamples, 1);

				AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(perFrameMap);
				while (!request.done) yield return null;

				if (request.hasError)
				{
					Debug.Log("GPU readback error detected.");
				}
				else
				{
					request.GetData<int>().CopyTo(lastPerFrameEnvMapData);
					OnPerFrameEnvMap.Invoke(lastPerFrameEnvMapData);
					yield return null;
				}

				Apply.Dispatch(textureSize, textureSize, 1);

				yield return new WaitForSeconds(1f / 30f);
			}
		}

		public void ApplyData(int[] data)
		{
			

			//Apply.Dispatch(textureSize, textureSize, 1);
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
			running = false;
		}
	}
}
