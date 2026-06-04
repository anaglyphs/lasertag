#include "../DepthKit.hlsl"

#pragma target 4.5

Texture2DArray<float> agOcclusionTex;
SamplerState linearClampSampler;

float agOcclusionActive = 1.0;

#define NEAR_CUTOFF 5

void agEnvOcclusion_float(float3 WorldPos, out float OutValue)
{
	#if SHADERGRAPH_PREVIEW
	OutValue = 1.0;
	return;
	#else

	// if (agOcclusionActive < 0.5)
	// {
	// 	OutValue = 1;
	// 	return;
	// }

	uint eye = unity_StereoEyeIndex;

	const float4 clip = TransformWorldToHClip(WorldPos);

	float3 ndc = clip.xyz / clip.w;
	float2 screenUV = ndc.xy * 0.5 + 0.5;

	#if UNITY_UV_STARTS_AT_TOP
	screenUV.y = 1.0 - screenUV.y;
	#endif

	const float3 uvEye = float3(screenUV, eye);
	OutValue = agOcclusionTex.SampleCmpLevelZero(agLinearClampCompareSampler, uvEye, ndc.z);

	#endif
}

void agEnvOcclusion_half(float3 WorldPos, out float OutValue)
{
	agEnvOcclusion_float(WorldPos, OutValue);
}

void agEnvOcclusionSample_half(float2 uv, out float val)
{
	val = agOcclusionTex.Sample(linearClampSampler, float3(uv, 0)).r;
}

void agEnvOcclusionSample_float(float2 uv, out float val)
{
	val = agOcclusionTex.Sample(linearClampSampler, float3(uv, 0)).r;
}
