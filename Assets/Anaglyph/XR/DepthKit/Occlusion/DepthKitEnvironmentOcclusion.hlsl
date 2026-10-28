#include "../DepthKit.hlsl"

#pragma target 4.5

Texture2DArray<float> agOcclusionTex;
SamplerState linearClampSampler;

float agOcclusionActive = 1.0;

// the per-eye view-projection agOcclusionTex is rasterized with, set by
// DepthKitEnvironmentOcclusionFeature: GL.GetGPUProjectionMatrix(proj, true) * view.
// Explicit rather than whatever the current pass has bound, because outside XR Unity
// y-flips the bound projection per render target
float4x4 agOcclusionVP[2];

// xy: agOcclusionTex uv, z: device depth comparable with its texels
float3 agOcclusionUVDepth(float3 positionWS, uint eye)
{
	float4 clip = mul(agOcclusionVP[eye], float4(positionWS, 1));
	float3 ndc = clip.xyz / clip.w;
	float2 uv = ndc.xy * 0.5 + 0.5;
	#if UNITY_UV_STARTS_AT_TOP
	// render-texture projections are flipped here, so texture rows run bottom-up
	uv.y = 1.0 - uv.y;
	#endif
	#if !UNITY_REVERSED_Z
	ndc.z = ndc.z * 0.5 + 0.5; // OpenGL clip z [-1,1] -> depth [0,1]
	#endif
	return float3(uv, ndc.z);
}

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
	float3 uvDepth = agOcclusionUVDepth(WorldPos, eye);
	OutValue = agOcclusionTex.SampleCmpLevelZero(agLinearClampCompareSampler, float3(uvDepth.xy, eye), uvDepth.z);

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
