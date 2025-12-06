Texture2DArray<float> agOcclusionTex;
SamplerState linearClampSampler;

void agEnvOcclusion_half(
	float3 WorldPos,
	out float OutValue)
{
	// Unity sets this automatically per-eye in XR.
	uint eye = unity_StereoEyeIndex;

	float4 clip = mul(UNITY_MATRIX_P, mul(UNITY_MATRIX_V, float4(WorldPos, 1.0)));

	float3 ndc = clip.xyz / clip.w;

	float2 screenUV = ndc.xy * 0.5 + 0.5;

	float depthSample = agOcclusionTex.Sample(linearClampSampler, float3(screenUV, eye)).r;

	float linearDepth = saturate(ndc.z * 0.5 + 0.5);

	OutValue = linearDepth <= depthSample ? 0.0 : 1.0;
}

void agEnvOcclusion_float(
	float3 WorldPos,
	out float OutValue)
{
	// Unity sets this automatically per-eye in XR.
	uint eye = unity_StereoEyeIndex;

	float4 clip = mul(UNITY_MATRIX_P, mul(UNITY_MATRIX_V, float4(WorldPos, 1.0)));

	float3 ndc = clip.xyz / clip.w;

	float2 screenUV = ndc.xy * 0.5 + 0.5;

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
