using Anaglyph.XRTemplate.DepthKit;
using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(-10)]
	public class EnvironmentMapper : SingletonBehavior<EnvironmentMapper>
	{
		public const int PER_FRAME_UNWRITTEN = 0;

		public static Action<NativeArray<int>> OnPerFrameEnvMap = delegate { };
		public static Action OnApply = delegate { };

		[SerializeField] private ComputeShader compute;

		[SerializeField] private int textureSize = 512;
		public int TextureSize => textureSize;

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
		private static readonly int _HeightRange = ID(nameof(_HeightRange));

		private static readonly int _EdgeFilterSize = ID(nameof(_EdgeFilterSize));
		private static readonly int _GradientCutoff = ID(nameof(_GradientCutoff));

		private static readonly int _LerpHeight = ID(nameof(_LerpHeight));

		private RenderTexture envMap;
		public RenderTexture Map => envMap;
		//private RenderTexture perFrameMap;
		private ComputeBuffer perFrameMap;
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
		private Kernel ClearEnvMap;
		private Kernel ClearPerFrame;

		protected override void SingletonAwake()
		{
			
		}

		private void Start()
		{
			envMap = new RenderTexture(textureSize, textureSize, 0, GraphicsFormat.R16G16_SFloat);
			envMap.enableRandomWrite = true;

			var size = envMap.width * envMap.height;

			//perFrameMap = new RenderTexture(textureSize, textureSize, 0, GraphicsFormat.R32_SInt);
			//perFrameMap.enableRandomWrite = true;
			perFrameMap = new ComputeBuffer(size, sizeof(Int32), ComputeBufferType.Structured);//, ComputeBufferMode.SubUpdates);

			Shader.SetGlobalFloat(agEnvSizeMeters, envSize);
			Shader.SetGlobalTexture(agEnvHeightMap, envMap);

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

			ClearEnvMap = new Kernel(compute, 2);
			ClearEnvMap.Set(_EnvHeightMapWritable, envMap);

			ClearPerFrame = new Kernel(compute, 3);
			ClearPerFrame.Set(_PerFrameHeight, perFrameMap);

			StartCoroutine(ScanRoomLoop());
		}

		private IEnumerator ScanRoomLoop()
		{
			yield return new WaitForFixedUpdate();

			ClearEnvMap.Dispatch(textureSize, textureSize, 1);
			ClearPerFrame.Dispatch(textureSize, textureSize, 1);

			running = true;
			while (running)
			{
				Texture depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);
				if(depthTex == null)
				{
					yield return null;
					continue;
				}

				compute.SetVector(_HeightRange, heightRange);
				Accumulate.Set(DepthKitDriver.agDepthTex_ID, depthTex);
				Accumulate.Dispatch(depthSamples, depthSamples, 1);

				yield return new WaitForSeconds(1f / 10f);

				AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(perFrameMap);
				while (!request.done) yield return null;

				if (request.hasError)
				{
					Debug.Log("GPU readback error detected.");
				}
				else
				{
					var data = request.GetData<int>();
					OnPerFrameEnvMap.Invoke(data);
				}

				Apply.Dispatch(textureSize, textureSize, 1);
				ClearPerFrame.Dispatch(textureSize, textureSize, 1);

				yield return new WaitForSeconds(1f / 10f);

				OnApply.Invoke();

				yield return new WaitForSeconds(1f / 10f);
			}
		}

		public void ApplyData(int[] data)
		{
			perFrameMap.SetData(data);
			Apply.Dispatch(textureSize, textureSize, 1);
			ClearPerFrame.Dispatch(textureSize, textureSize, 1);
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

			OnPerFrameEnvMap = delegate { };
			OnApply = delegate { };
		}
	}
}
