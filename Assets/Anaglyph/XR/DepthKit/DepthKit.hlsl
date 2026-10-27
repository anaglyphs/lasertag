// Anaglyph depth kit

Texture2DArray<float> agDepthTex;
Texture2DArray<float4> agDepthNormalTex;
uniform uint2 agDepthTexSize;

SamplerState agBilinearClampSampler;
SamplerState agPointClampSampler;
SamplerComparisonState agLinearClampCompareSampler;


uniform float4x4 agDepthProj[2];
uniform float4x4 agDepthProjInv[2];

uniform float4x4 agDepthView[2];
uniform float4x4 agDepthViewInv[2];

uniform float4 agDepthZParams;

// Meta reports "no depth" (sky, dropouts) as 0, which decodes to a surface at the
// near plane. DepthKitDriver rewrites those texels to this: the largest D16 value
// below 1, far but finite, so reconstruction stays NaN-free and comparisons pass
#define AG_DEPTH_UNKNOWN (65534.0 / 65535.0)

// false for unknown texels and for out-of-bounds loads, which return 0
bool agDepthIsKnown(float depthNDC)
{
	return depthNDC > 0.0 && depthNDC < AG_DEPTH_UNKNOWN - 0.5 / 65535.0;
}


float3 agDepthEyePos(int eye = 0)
{
	return agDepthViewInv[eye]._m03_m13_m23;
}

float agDepthCompare(float compare, float2 uv, int eye = 0)
{
	return agDepthTex.SampleCmpLevelZero(agLinearClampCompareSampler, float3(uv, eye), compare);
}

float agDepthSample(float2 uv, int eye = 0)
{
	return agDepthTex.SampleLevel(agPointClampSampler, float3(uv.xy, eye), 0);
}

float agDepthSample(float2 uv, int eye, SamplerState samplerState)
{
	return agDepthTex.SampleLevel(samplerState, float3(uv.xy, eye), 0);
}

float agDepthNDCToLinear(float depthNDC, int eye = 0)
{
	float z = depthNDC * 2.0 - 1.0;
	float A = agDepthProj[eye][2][2];
	float B = agDepthProj[eye][2][3];
	return abs(B / (z + A));
}

float4 agDepthNormalSample(float2 uv, int eye = 0)
{
	return agDepthNormalTex.SampleLevel(agPointClampSampler, float3(uv.xy, eye), 0);
}

float4 agDepthNormalSample(float2 uv, int eye, SamplerState samplerState)
{
	return agDepthNormalTex.SampleLevel(samplerState, float3(uv.xy, eye), 0);
}

float4 agDepthWorldToHCS(float3 worldPos, int eye = 0)
{
	return mul(agDepthProj[eye], mul(agDepthView[eye], float4(worldPos, 1)));
}

float4 agDepthHCStoWorldH(float4 hcs, int eye = 0)
{
	return mul(agDepthViewInv[eye], mul(agDepthProjInv[eye], hcs));
}

float3 agDepthHCStoNDC(float4 hcs)
{
	return (hcs.xyz / hcs.w) * 0.5 + 0.5;
}

float4 agDepthNDCtoHCS(float3 ndc)
{
	return float4(ndc * 2.0 - 1.0, 1);
}

float3 agDepthWorldToNDC(float3 worldPos, int eye = 0)
{
	float4 hcs = agDepthWorldToHCS(worldPos, eye);
	return agDepthHCStoNDC(hcs);
}

float3 agDepthNDCtoWorld(float3 ndc, int eye = 0)
{
	float4 hcs = agDepthNDCtoHCS(ndc);
	float4 worldH = agDepthHCStoWorldH(hcs, eye);
	return worldH.xyz / worldH.w;
}

float3 agDepthSampleWorldToWorld(float3 worldPos, int eye = 0)
{
	float3 ndc = agDepthWorldToNDC(worldPos, eye);
	float depth = agDepthSample(ndc.xy, eye);

	return agDepthNDCtoWorld(float3(ndc.xy, depth), eye);
}

float agDepthCompareWorld(float3 worldPos, int eye = 0)
{
	float3 ndc = agDepthWorldToNDC(worldPos, eye);
	float result = agDepthCompare(ndc.z, ndc.xy, eye);
	return result;
}

float agDepthSampleWorldToLinear(float3 worldPos, int eye = 0)
{
	float3 ndc = agDepthWorldToNDC(worldPos, eye);
	float depthNDC = agDepthSample(ndc.xy, eye);

	return agDepthNDCToLinear(depthNDC, eye);
}

float3 agDepthSampleNDCtoWorld(float2 uv, int eye = 0)
{
	float3 ndc = float3(uv, agDepthSample(uv, eye));
	return agDepthNDCtoWorld(ndc, eye);
}

float2 agIdxToUV(uint2 idx)
{
	return (float2)idx / agDepthTexSize;
}

float3 agDepthSampleIdxToWorld(uint2 idx, int eye = 0)
{
	return agDepthSampleNDCtoWorld(agIdxToUV(idx), eye);
}
