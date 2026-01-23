Texture2DArray<float> agOcclusionTex;
SamplerState linearClampSampler;

void agEnvOcclusion_half(
	float3 WorldPos,
	out float OutValue)
{
	// Unity sets this automatically per-eye in XR.
	uint eye = unity_StereoEyeIndex;
	
	float4 clip = TransformWorldToHClip(WorldPos);

	float3 ndc = clip.xyz / clip.w;

	float2 screenUV = ndc.xy * 0.5 + 0.5;

	#if UNITY_UV_STARTS_AT_TOP
	screenUV.y = 1.0 - screenUV.y; 
	#endif

	float depthSample = agOcclusionTex.Sample(linearClampSampler, float3(screenUV, eye)).r;

	float linearDepth = saturate(ndc.z);

	OutValue = linearDepth <= depthSample ? 0.0 : 1.0;
}

void agEnvOcclusion_float(
	float3 WorldPos,
	out float OutValue)
{
	// Unity sets this automatically per-eye in XR.
	uint eye = unity_StereoEyeIndex;

	float4 clip = TransformWorldToHClip(WorldPos);

	float3 ndc = clip.xyz / clip.w;

	float2 screenUV = ndc.xy * 0.5 + 0.5;

	#if UNITY_UV_STARTS_AT_TOP
	screenUV.y = 1.0 - screenUV.y;
	#endif

	float depthSample = agOcclusionTex.Sample(linearClampSampler, float3(screenUV, eye)).r;

	float linearDepth = saturate(ndc.z);

	OutValue = linearDepth <= depthSample ? 0.0 : 1.0;
}

void agEnvOcclusionSample_half(float2 uv, out float val)
{
	val = agOcclusionTex.Sample(linearClampSampler, float3(uv, 0)).r;
}

void agEnvOcclusionSample_float(float2 uv, out float val)
{
	val = agOcclusionTex.Sample(linearClampSampler, float3(uv, 0)).r;
}
