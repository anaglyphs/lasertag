// https://github.com/oculus-samples/Unity-DepthAPI/issues/16

#define UNITY_SINGLE_PASS_STEREO
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#if defined(SHADER_API_D3D11) 
#define FLIP_UVS 1
#endif

uniform Texture2DArray<float> DepthTextureDK;
uniform SamplerState trilinearClampSampler;
uniform float4x4 DepthTex3DOFMatricesDK[2];
uniform float4x4 DepthTexReprojMatricesDK[2];
uniform float4 DepthTexZBufferParamsDK;

uniform float4 ZBufferParams;
uniform float4x4 StereoMatrixV[2];
uniform float4x4 StereoMatrixVP[2];
uniform float4x4 StereoMatrixInvVP[2];
uniform float4x4 StereoMatrixInvP[2];

#define NORMAL_CALC_UV_OFFSET float2(0.001f, 0.001f)

float2 HeadRotCorrectDK(float2 uv, const int slice)
{
	float4x4 reprojMat = DepthTex3DOFMatricesDK[slice];
	
	return mul(reprojMat, float4(uv.x, uv.y, 0.0, 1.0)).xy;
}

float2 UVFromWorldDK(float3 worldPos, const int slice)
{
    const float4 depthSpace =
		mul(DepthTexReprojMatricesDK[slice], float4(worldPos, 1.0));
	
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
	
    uv = HeadRotCorrectDK(uv, slice);
	
	// depth z buffer value
    const float inputDepthEye = DepthTextureDK.SampleLevel(trilinearClampSampler, float3(uv.xy, slice), 0);
	const float4 envZBufParams = DepthTexZBufferParamsDK;
	
	const float inputDepthNdc = inputDepthEye * 2.0 - 1.0;
	const float envLinearDepth = (1.0f / (inputDepthNdc + envZBufParams.y)) * envZBufParams.x;

	// depth camera z buffer
	float envDepth = (1 - envLinearDepth * ZBufferParams.w) / (envLinearDepth * ZBufferParams.z);

	return envDepth;
}

float4 ComputeClipSpacePositionDK(float2 positionNDC, float deviceDepth)
{
	float4 positionCS = float4(positionNDC * 2.0 - 1.0, deviceDepth, 1.0);
	return positionCS;
}

float3 ApplyMatrixDK(float2 positionNDC, float deviceDepth, float4x4 invViewProjMatrix)
{
	float4 positionCS = ComputeClipSpacePositionDK(positionNDC, deviceDepth);
	float4 hpositionWS = mul(invViewProjMatrix, positionCS);
	return hpositionWS.xyz / hpositionWS.w;
}

float3 ComputeWorldSpacePositionDK(float2 positionNDC, float deviceDepth, int slice)
{
	return ApplyMatrixDK(positionNDC, deviceDepth, unity_StereoMatrixInvVP[slice]);
}

// https://gist.github.com/bgolus/a07ed65602c009d5e2f753826e8078a0
float3 ComputeWorldSpaceNormalDK(float2 uv, float3 worldPos, int slice)
{
	// get current pixel's view space position
    float3 viewSpacePos_c = worldPos;

	// get view space position at 1 pixel offsets in each major direction
	float2 offsetUV = uv + float2(1.0, 0.0) * NORMAL_CALC_UV_OFFSET;
	float deviceDepth = SampleDepthDK(offsetUV, slice);
	float3 viewSpacePos_r = ComputeWorldSpacePositionDK(offsetUV, deviceDepth, slice);

	offsetUV = uv + float2(0.0, 1.0) * NORMAL_CALC_UV_OFFSET;
    deviceDepth = SampleDepthDK(offsetUV, slice);
	float3 viewSpacePos_u = ComputeWorldSpacePositionDK(offsetUV, deviceDepth, slice);

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