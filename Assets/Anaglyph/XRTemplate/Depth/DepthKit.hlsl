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

float2 UVFromWorldDK(float3 worldPos, const int slice)
{
	const float4 depthSpace =
		mul(dk_DepthTexReprojMatrices[slice], float4(worldPos, 1.0));
	
	float2 uv = (depthSpace.xy / depthSpace.w + 1.0f) * 0.5f;
	
#if FLIP_UVS
	uv.y = 1 - uv.y;
#endif
	
	return uv;
}

float SampleDepthDK(float2 uv, int slice)
{
#if FLIP_UVS
	uv.y = 1 - uv.y;
#endif
	
	// depth z buffer value
	const float inputDepthEye = dk_DepthTexture.SampleLevel(trilinearClampSampler, float3(uv.xy, slice), 0);
	const float4 envZBufParams = dk_DepthTexZBufferParams;
	
	const float inputDepthNdc = inputDepthEye * 2.0 - 1.0;
	const float envLinearDepth = (1.0f / (inputDepthNdc + envZBufParams.y)) * envZBufParams.x;

	// depth camera z buffer
	float envDepth = (1 - envLinearDepth * dk_ZBufferParams.w) / (envLinearDepth * dk_ZBufferParams.z);

	return envDepth;
}

float2 HCStoUV(float4 hcs)
{ // x / w * .5 + .5
	return hcs.xy / hcs.w * 0.5 + float2(0.5, 0.5);
}

float4 UVtoHCS(float2 uv, float zNDC, float w)
{ // x * w * 2 - 1
    return float4(uv, zNDC, 1) * w * 2.0 - (1.0, 1.0, 1.0, 1.0);
}


float4 ComputeClipSpacePositionDK(float2 positionNDC, float deviceDepth)
{
	return float4(positionNDC * 2.0 - 1.0, deviceDepth, 1.0);
}

float3 ApplyMatrixDK(float2 positionNDC, float deviceDepth, float4x4 invViewProjMatrix)
{
	float4 positionCS = ComputeClipSpacePositionDK(positionNDC, deviceDepth);
	float4 hpositionWS = mul(invViewProjMatrix, positionCS);
	return hpositionWS.xyz / hpositionWS.w;
}

float3 ComputeWorldSpacePositionDK(float2 uv, float deviceDepth, float w, int slice)
{
	float4 hcs = UVtoHCS(uv, deviceDepth, w);
	return mul(dk_InvDepthTexReprojMatrices[slice], hcs);
}

// https://gist.github.com/bgolus/a07ed65602c009d5e2f753826e8078a0
float3 ComputeWorldSpaceNormalDK(float2 uv, float3 worldPos, int slice)
{
	// get current pixel's view space position
	float3 viewSpacePos_c = worldPos;

	// get view space position at 1 pixel offsets in each major direction
	float2 offsetUV = uv + float2(1.0, 0.0) * NORMAL_CALC_UV_OFFSET;
	float deviceDepth = SampleDepthDK(offsetUV, slice);
	float3 viewSpacePos_r = ComputeWorldSpacePositionDK(offsetUV, deviceDepth, 1, slice);

	offsetUV = uv + float2(0.0, 1.0) * NORMAL_CALC_UV_OFFSET;
	deviceDepth = SampleDepthDK(offsetUV, slice);
	float3 viewSpacePos_u = ComputeWorldSpacePositionDK(offsetUV, deviceDepth, 1, slice);

	// get the difference between the current and each offset position
	float3 hDeriv = viewSpacePos_r - viewSpacePos_c;
	float3 vDeriv = viewSpacePos_u - viewSpacePos_c;

	// get view space normal from the cross product of the diffs 
	float3 viewNormal = -normalize(cross(hDeriv, vDeriv)); 

#if FLIP_UVS
	viewNormal = -viewNormal;
#endif

	return viewNormal;
}