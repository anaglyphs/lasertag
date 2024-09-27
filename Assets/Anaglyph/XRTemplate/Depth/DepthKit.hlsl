// https://github.com/oculus-samples/Unity-DepthAPI/issues/16

#if defined(SHADER_API_D3D11) 
#define FLIP_UVS 1
#endif

uniform Texture2DArray<float> dk_DepthTexture;
uniform SamplerState trilinearClampSampler;
uniform float4x4 dk_DepthTexReprojMatrices[2];
uniform float4x4 dk_DepthView[2];
uniform float4x4 dk_InvDepthTexReprojMatrices[2];
uniform float4 dk_DepthTexZBufferParams;

uniform float4 dk_ZBufferParams;
uniform float4x4 dk_StereoMatrixV[2];
uniform float4x4 dk_StereoMatrixVP[2];
uniform float4x4 dk_StereoMatrixInvVP[2];
uniform float4x4 dk_StereoMatrixInvP[2];

#define NORMAL_CALC_UV_OFFSET float2(0.0005f, 0.0005f)

float SampleDepth(float2 uv, int slice)
{
#if FLIP_UVS
	uv.y = 1 - uv.y;
#endif
	
	const float envDepth = dk_DepthTexture.SampleLevel(trilinearClampSampler, float3(uv.xy, slice), 0);

    return envDepth;
}

// world to hcs to ndc

float4 WorldtoHCS(float3 worldPos, int slice)
{
    return mul(dk_DepthTexReprojMatrices[slice], float4(worldPos, 1));
}

float4 HCStoWorld(float4 hcs, int slice)
{
    return mul(dk_InvDepthTexReprojMatrices[slice], hcs);
}

float3 HCStoNDC(float4 hcs)
{
    return (hcs.xyz / hcs.w) * 0.5 + 0.5;
}

float4 NDCtoHCS(float3 ndc, float w)
{
    return float4(ndc * 2.0 - 1.0, 1) * w;
}

float3 WorldtoNDC(float3 worldPos, int slice, out float w)
{
    float4 hcs = WorldtoHCS(worldPos, slice);
    w = hcs.w;
    return HCStoNDC(hcs);
}

float4 NDCtoWorld(float3 ndc, int slice, float w)
{
    float4 hcs = NDCtoHCS(ndc, w);
    return HCStoWorld(hcs, slice);
}

float3 SampleDepthWorld(float3 worldPos, int slice, out float2 uv, out float w)
{
    float3 ndc = WorldtoNDC(worldPos, slice, w);
    uv = ndc.xy;

    float envDepth = SampleDepth(uv, slice);
	
    return NDCtoWorld(float3(uv, envDepth), slice, w).xyz;
}

//// https://gist.github.com/bgolus/a07ed65602c009d5e2f753826e8078a0
//float3 ComputeWorldSpaceNormalDK(float2 uv, float3 worldPos, int slice)
//{
//	// get current pixel's view space position
//	float3 viewSpacePos_c = worldPos;

//	// get view space position at 1 pixel offsets in each major direction
//	float2 offsetUV = uv + float2(1.0, 0.0) * NORMAL_CALC_UV_OFFSET;
//	float deviceDepth = SampleDepth(offsetUV, slice);
//    float3 viewSpacePos_r = SampleDepthWorld(offsetUV, 1, slice);

//	offsetUV = uv + float2(0.0, 1.0) * NORMAL_CALC_UV_OFFSET;
//	deviceDepth = SampleDepthDK(offsetUV, slice);
//    float3 viewSpacePos_u = SampleDepthWorld(offsetUV, 1, slice);

//	// get the difference between the current and each offset position
//	float3 hDeriv = viewSpacePos_r - viewSpacePos_c;
//	float3 vDeriv = viewSpacePos_u - viewSpacePos_c;

//	// get view space normal from the cross product of the diffs 
//	float3 viewNormal = -normalize(cross(hDeriv, vDeriv)); 

//#if FLIP_UVS
//	viewNormal = -viewNormal;
//#endif

//	return viewNormal;
//}