// https://github.com/oculus-samples/Unity-DepthAPI/issues/16

#if defined(SHADER_API_D3D11) 
#define FLIP_UVS 1
#endif

uniform Texture2DArray<float> dk_DepthTexture;
uniform SamplerState pointClampSampler;

uniform float4x4 dk_Proj[2];
uniform float4x4 dk_InvProj[2];

uniform float4x4 dk_View[2];
uniform float4x4 dk_InvView[2];

uniform float4 dk_ZBufferParams;
uniform float4x4 dk_StereoMatrixV[2];
uniform float4x4 dk_StereoMatrixVP[2];
uniform float4x4 dk_StereoMatrixInvVP[2];
uniform float4x4 dk_StereoMatrixInvP[2];

#define NORMAL_CALC_UV_OFFSET float2(0.001f, 0.001f)

float SampleDepth(float2 uv, int slice)
{
#if FLIP_UVS
	uv.y = 1 - uv.y;
#endif
	
	return dk_DepthTexture.SampleLevel(pointClampSampler, float3(uv.xy, slice), 0);
}

// world to hcs to ndc

float4 WorldtoHCS(float3 worldPos, int slice)
{
	return mul(dk_Proj[slice], mul(dk_View[slice], float4(worldPos, 1)));
}

float4 HCStoWorldH(float4 hcs, int slice)
{
	return mul(dk_InvView[slice], mul(dk_InvProj[slice], hcs));
}

float3 HCStoNDC(float4 hcs)
{
	return (hcs.xyz / hcs.w) * 0.5 + 0.5;
}

float4 NDCtoHCS(float3 ndc)
{
	return float4(ndc * 2.0 - 1.0, 1);
}

float3 WorldtoNDC(float3 worldPos, int slice)
{
	float4 hcs = WorldtoHCS(worldPos, slice);
	return HCStoNDC(hcs);
}

float3 NDCtoWorld(float3 ndc, int slice)
{
	float4 hcs = NDCtoHCS(ndc);
	float4 worldH = HCStoWorldH(hcs, slice);
	return worldH / worldH.w;
}

//float4 ComputeClipSpacePosition(float2 positionNDC, float deviceDepth)
//{
//    float4 positionCS = float4(positionNDC * 2.0 - 1.0, deviceDepth, 1.0);

//#if UNITY_UV_STARTS_AT_TOP
//    // Our world space, view space, screen space and NDC space are Y-up.
//    // Our clip space is flipped upside-down due to poor legacy Unity design.
//    // The flip is baked into the projection matrix, so we only have to flip
//    // manually when going from CS to NDC and back.
//    positionCS.y = -positionCS.y;
//#endif

//    return positionCS;
//}

//float3 ComputeWorldSpacePosition(float2 positionNDC, float deviceDepth, float4x4 invViewProjMatrix)
//{
//    float4 positionCS = ComputeClipSpacePosition(positionNDC, deviceDepth);
//    float4 hpositionWS = mul(invViewProjMatrix, positionCS);
//    return hpositionWS.xyz / hpositionWS.w;
//}

//float3 NDCtoWorldTest(float3 ndc, int slice)
//{
//    float4 ndc4 = (ndc, 1);
//    float4 homoView = mul(dk_InvProj[slice], ndc4);
//    float4 view = homoView / homoView.w;
//    return mul(dk_InvView[slice], view).xyz;
//}

//float3 SampleDepthWorld(float3 worldPos, int slice, out float2 uv, out float w)
//{
//    float3 ndc = WorldtoNDC(worldPos, slice, w);
//    uv = ndc.xy;

//    float envDepth = SampleDepth(uv, slice);
	
//    return NDCtoWorld(float3(uv, envDepth), slice, w).xyz;
//}

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