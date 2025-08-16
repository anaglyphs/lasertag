// Anaglyph depth kit

Texture2DArray<float> agDepthTex;
uniform Texture2DArray<float4> agDepthEdgeTex;
Texture2DArray<float4> agDepthNormalTex;
SamplerState bilinearClampSampler;
SamplerState pointClampSampler;
uint2 agDepthTexSize;

float4x4 agDepthProj[2];
float4x4 agDepthProjInv[2];

float4x4 agDepthView[2];
float4x4 agDepthViewInv[2];

uniform float4 agDepthZParams;

float3 agDepthEyePos(int eye = 0)
{
	return agDepthViewInv[eye]._m03_m13_m23;
}

float agDepthSample(float2 uv, int eye = 0)
{	
	return agDepthTex.SampleLevel(pointClampSampler, float3(uv.xy, eye), 0);
}

float agDepthSample(float2 uv, int eye, SamplerState samplerState)
{
	return agDepthTex.SampleLevel(samplerState, float3(uv.xy, eye), 0);
}

float4 agDepthSampleEdge(float2 uv, int eye = 0)
{
	return agDepthEdgeTex.SampleLevel(pointClampSampler, float3(uv.xy, eye), 0);
}

float agDepthNDCToLinear(float depthNDC)
{
	depthNDC = depthNDC * 2.0 - 1.0;
	return (1.0f / (depthNDC + agDepthZParams.y)) * agDepthZParams.x;
}

float4 agDepthNormalSample(float2 uv, int eye = 0)
{
	return agDepthNormalTex.SampleLevel(pointClampSampler, float3(uv.xy, eye), 0);
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

// todo linearz function