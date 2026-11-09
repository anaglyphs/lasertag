#include "../DepthKit.hlsl"

#pragma target 4.5

Texture2DArray<float> agOcclusionTex;


// the per-eye view-projection agOcclusionTex is rasterized with, set by
// DepthKitEnvironmentOcclusionFeature: GL.GetGPUProjectionMatrix(proj, true) * view.
// Explicit rather than whatever the current pass has bound, because outside XR Unity
// y-flips the bound projection per render target
float4x4 agOcclusionVP[2];

// xy: 1 / size, zw: size
float4 agOcclusionTexSize;
float agOcclusionDepthSoftness;

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

// 0 = hidden, 1 = visible. Feathers across occluder edges with a bilinear-weighted
// 4x4 texel box, and along depth over agOcclusionDepthSoftness meters
float agSoftEnvOcclusion(float3 positionWS, uint eye)
{
	float3 uvDepth = agOcclusionUVDepth(positionWS, eye);

	// signed device-depth span of the softness at this distance; the sign absorbs the Z direction
	float3 towardEye = normalize(_WorldSpaceCameraPos - positionWS);
	float rampWidth = agOcclusionUVDepth(positionWS + towardEye * agOcclusionDepthSoftness, eye).z - uvDepth.z;

	float2 texel = uvDepth.xy * agOcclusionTexSize.zw - 0.5;
	float2 baseTexel = floor(texel);
	float2 f = texel - baseTexel;
	float4 wx = float4(1 - f.x, 1, 1, f.x);
	float4 wy = float4(1 - f.y, 1, 1, f.y);

	float visibility = 0;
	[unroll] for (int gy = 0; gy < 2; gy++)
	[unroll] for (int gx = 0; gx < 2; gx++)
	{
		float2 gatherUV = (baseTexel + float2(gx, gy) * 2) * agOcclusionTexSize.xy;
		float4 occluderDepth = agOcclusionTex.Gather(agPointClampSampler, float3(gatherUV, eye));
		float4 texelVisibility = saturate((uvDepth.z - occluderDepth) / rampWidth + 0.5);
		// Gather order: x=(-u,+v) y=(+u,+v) z=(+u,-v) w=(-u,-v)
		float4 weights = float4(wx[gx * 2] * wy[gy * 2 + 1], wx[gx * 2 + 1] * wy[gy * 2 + 1],
		                        wx[gx * 2 + 1] * wy[gy * 2], wx[gx * 2] * wy[gy * 2]);
		visibility += dot(texelVisibility, weights);
	}
	return visibility / 9;
}

void agEnvOcclusion_float(float3 WorldPos, out float OutValue)
{
	#if SHADERGRAPH_PREVIEW
	OutValue = 1.0;
	#else
	OutValue = agSoftEnvOcclusion(WorldPos, unity_StereoEyeIndex);
	#endif
}

void agEnvOcclusion_half(float3 WorldPos, out float OutValue)
{
	agEnvOcclusion_float(WorldPos, OutValue);
}

// distance along a ray from the eye to the occluding surface.
// Reads the far plane's distance where nothing was rasterized
void agEnvSurfaceDistance_float(float3 EyePos, float3 RayDir, out float OutDistance)
{
	uint eye = unity_StereoEyeIndex;
	float2 uv = agOcclusionUVDepth(EyePos + RayDir, eye).xy;
	float depth = agOcclusionTex.SampleLevel(agPointClampSampler, float3(uv, eye), 0);

	// solves depth = clip.z / clip.w for clip = eyeClip + distance * dirClip
	float4 eyeClip = mul(agOcclusionVP[eye], float4(EyePos, 1));
	float4 dirClip = mul(agOcclusionVP[eye], float4(RayDir, 0));
	OutDistance = (eyeClip.z - depth * eyeClip.w) / (depth * dirClip.w - dirClip.z);
}
