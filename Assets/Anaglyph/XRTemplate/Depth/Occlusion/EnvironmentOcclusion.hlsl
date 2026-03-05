#include "../DepthKit.hlsl"

Texture2DArray<float> agOcclusionTex;
SamplerState linearClampSampler;

#define NEAR_CUTOFF 5

void agEnvOcclusion_float(
	float3 WorldPos,
	out float OutValue)
{
	// Unity sets this automatically per-eye in XR.
	uint eye = unity_StereoEyeIndex;

	const float4 clip = TransformWorldToHClip(WorldPos);

	float3 ndc = clip.xyz / clip.w;
	float2 screenUV = ndc.xy * 0.5 + 0.5;
	#if UNITY_UV_STARTS_AT_TOP
	screenUV.y = 1.0 - screenUV.y;
	#endif
	float occlusionValue = agOcclusionTex.Sample(linearClampSampler, float3(screenUV, eye));
	float distantOcclusionAlpha = ndc.z >= occlusionValue ? 1.0 : 0.0;

	float depthTexLinear = agDepthSampleWorldToLinear(WorldPos, eye);
	float meshDepthLinear = abs(LinearEyeDepth(ndc.z, _ZBufferParams));
	float nearOcclusionAlpha = meshDepthLinear < depthTexLinear ? 1.0 : 0.0;;

	bool near = meshDepthLinear < NEAR_CUTOFF || depthTexLinear < NEAR_CUTOFF;

	OutValue = near ? nearOcclusionAlpha : distantOcclusionAlpha;
}

void agEnvOcclusion_half(
	float3 WorldPos,
	out float OutValue)
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
