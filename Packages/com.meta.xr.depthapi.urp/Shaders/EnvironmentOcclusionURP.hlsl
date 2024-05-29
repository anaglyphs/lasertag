#ifndef ENVIRONMENT_DEPTH_INCLUDED
#define ENVIRONMENT_DEPTH_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#define PREFER_HALF 0
#define SHADER_HINT_NICE_QUALITY 1

TEXTURE2D_X_FLOAT(_EnvironmentDepthTexture);

SAMPLER(sampler_EnvironmentDepthTexture);
float4 _EnvironmentDepthTexture_TexelSize;

float SampleEnvironmentDepth(const float2 reprojectedUV) {
  return SAMPLE_TEXTURE2D_X(_EnvironmentDepthTexture, sampler_EnvironmentDepthTexture, reprojectedUV).r;
}

#define META_DEPTH_CONVERT_OBJECT_TO_WORLD(objectPos) TransformObjectToWorld(objectPos).xyz

float DepthConvertDepthToLinear(float zspace) {
  return LinearEyeDepth(zspace, _ZBufferParams);
}

#include "Packages/com.meta.xr.depthapi/Runtime/Core/Shaders/EnvironmentOcclusion.cginc"

#endif
