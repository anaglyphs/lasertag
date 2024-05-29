#ifndef META_DEPTH_ENVIRONMENT_OCCLUSION_BIRP_INCLUDED
#define META_DEPTH_ENVIRONMENT_OCCLUSION_BIRP_INCLUDED

uniform UNITY_DECLARE_TEX2DARRAY(_EnvironmentDepthTexture);
float4 _EnvironmentDepthTexture_TexelSize;

#include "UnityCG.cginc"

float SampleEnvironmentDepth(float2 reprojectedUV) {
  return UNITY_SAMPLE_TEX2DARRAY(_EnvironmentDepthTexture,
           float3(reprojectedUV, (float)unity_StereoEyeIndex)).r;
}

#define META_DEPTH_CONVERT_OBJECT_TO_WORLD(objectPos) mul(unity_ObjectToWorld, objectPos).xyz;

float3 DepthConvertDepthToLinear(float zspace) {
  return LinearEyeDepth(zspace);
}

#include "Packages/com.meta.xr.depthapi/Runtime/Core/Shaders/EnvironmentOcclusion.cginc"

#endif
