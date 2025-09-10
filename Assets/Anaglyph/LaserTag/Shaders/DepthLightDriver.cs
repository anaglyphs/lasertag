using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class DepthLightDriver : MonoBehaviour
	{
		[StructLayout(LayoutKind.Sequential)]
		struct Light
		{
			public Vector3 position;
			public Vector3 color;
			public float intensity;
			float padding;
		}

		private Light[] lights = new Light[16];

		[SerializeField] private Material depthLightEffectMat;
		private ComputeBuffer depthLightBuffer;

		public static List<DepthLight> AllLights = new();

		private readonly int LightsID = Shader.PropertyToID("_Lights");
		private readonly int LightCountID = Shader.PropertyToID("_LightCount");

		private void Start()
		{
			depthLightBuffer = new ComputeBuffer(16, Marshal.SizeOf<Light>());
			depthLightEffectMat.SetInt(LightCountID, 5);
		}

		private void LateUpdate()
		{
			int count = Math.Min(AllLights.Count, lights.Length);

			for (int i = 0; i < count; i++)
			{
				DepthLight depthLight = AllLights[i];

				Color c = depthLight.color;

				Light light = new Light()
				{
					position = depthLight.transform.position,
					color = new(c.r, c.g, c.b),
					intensity = depthLight.intensity,
				};

				lights[i] = light;
			}

			depthLightBuffer.SetData(lights);

			depthLightEffectMat.SetInt(LightCountID, count);
			depthLightEffectMat.SetBuffer(LightsID, depthLightBuffer);
		}

		private void OnDestroy()
		{
			depthLightBuffer.Release();
		}
	}
}
