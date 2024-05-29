#ifndef META_DEPTH_ENVIRONMENT_OCCLUSION_INCLUDED
#define META_DEPTH_ENVIRONMENT_OCCLUSION_INCLUDED

uniform float4x4 _EnvironmentDepthReprojectionMatrices[2];
uniform float4x4 _EnvironmentDepth3DOFReprojectionMatrices[2];
uniform float4 _EnvironmentDepthZBufferParams;

#define SAMPLE_OFFSET_PIXELS 6.0f
#define RELATIVE_ERROR_SCALE 0.015f
#define SOFT_OCCLUSIONS_SCREENSPACE_OFFSET SAMPLE_OFFSET_PIXELS / _EnvironmentDepthTexture_TexelSize.zw


float SampleEnvironmentDepthLinear_Internal(float2 uv)
{
  const float inputDepthEye = SampleEnvironmentDepth(uv);

  const float inputDepthNdc = inputDepthEye * 2.0 - 1.0;
  const float linearDepth = (1.0f / (inputDepthNdc + _EnvironmentDepthZBufferParams.y)) * _EnvironmentDepthZBufferParams.x;

  return linearDepth;
}

float CalculateEnvironmentDepthHardOcclusion_Internal(float2 depthUv, float sceneDepth)
{
  return SampleEnvironmentDepthLinear_Internal(depthUv) > sceneDepth;
}

float CalculateSoftOcclusionPixelValue_Internal(float2 depthUv, float sceneDepth)
{
  const float environmentDepth = SampleEnvironmentDepthLinear_Internal(depthUv);
  const float relativeError = sceneDepth / environmentDepth - 1;
  const float scaledOcclusionValue = relativeError / RELATIVE_ERROR_SCALE;

  // occlusion is 0.5 if environment == sceneDepth
  return clamp(0.5 - 0.5 * scaledOcclusionValue, 0.0, 1.0);
}

// Not guarded by occlusion keywords, use CalculateEnvironmentDepthOcclusion
float CalculateEnvironmentDepthSoftOcclusion_Internal(float2 uv, float sceneDepth)
{
  #define TEST_SAMPLES 4

  static const uint POISSON_SAMPLES = 11;
  static const float2 poissonDisk[POISSON_SAMPLES] = {
    float2( 0.9789267005017435f, 0.14401885022921376f ),
    float2( -0.9043537867387434f, -0.3606923565241508f ),
    float2( 0.311521973180601f, -0.9132562206107396f ),
    float2( -0.2305685898894408f, 0.9070738255481436f ),
    float2( -0.8865688823018791f, 0.4204923507525414f ),
    float2( 0.4020375708925012f, 0.8941011867274369f ),
    float2( -0.33142350418249433f, -0.8611052735427547f ),
    float2( 0.6716543228214223f, -0.426789950875022f ),
    float2( -0.3604733289097599f, 0.11357279143380132f ),
    float2( 0.23595175158496592f, 0.26950844847277605f ),
    float2( 0.033190036742041545f, -0.3504560360183565f )
  };

  const float2 scale = SOFT_OCCLUSIONS_SCREENSPACE_OFFSET;
  float result = 0.0;
  uint i = 0;

  UNITY_UNROLL
  for (i = 0; i < TEST_SAMPLES; ++i) {
    const float2 offset = poissonDisk[i] * scale;
    result += CalculateSoftOcclusionPixelValue_Internal(uv + offset, sceneDepth);
  }

  UNITY_BRANCH
  if ((TEST_SAMPLES - result ) * result < 0.001f) {
    return result / TEST_SAMPLES;
  }

  UNITY_UNROLL
  for (i = TEST_SAMPLES; i < POISSON_SAMPLES; ++i) {
    const float2 offset = poissonDisk[i] * scale;
    result += CalculateSoftOcclusionPixelValue_Internal(uv + offset, sceneDepth);
  }

  return result / POISSON_SAMPLES;
}

float2 ReprojectScreenspace3DOF(float2 uv)
{
  return mul(_EnvironmentDepth3DOFReprojectionMatrices[unity_StereoEyeIndex], float4(uv.x, uv.y, 0.0, 1.0)).xy;
}

float Sample3DOFReprojectedEnvironmentDepthLinear(float2 uv)
{
  return SampleEnvironmentDepthLinear_Internal(ReprojectScreenspace3DOF(uv));
}

float CalculateEnvironmentDepthOcclusionLinearWithBias(float2 uv, float sceneLinearDepth, float bias)
{
  const float2 uvDepthSpace = ReprojectScreenspace3DOF(uv);

  float sceneDepthWithBias = sceneLinearDepth - bias * sceneLinearDepth * UNITY_NEAR_CLIP_VALUE;

  #if defined(HARD_OCCLUSION)
    return CalculateEnvironmentDepthHardOcclusion_Internal(uvDepthSpace, sceneDepthWithBias);
  #elif defined(SOFT_OCCLUSION)
    return CalculateEnvironmentDepthSoftOcclusion_Internal(uvDepthSpace, sceneDepthWithBias);
  #endif

  return 1.0f;
}

float CalculateEnvironmentDepthOcclusionInEnvDepthSpaceWithBias(float3 worldCoords, float bias)
{
  const float4 depthSpace =
    mul(_EnvironmentDepthReprojectionMatrices[unity_StereoEyeIndex], float4(worldCoords, 1.0));

  const float2 uvCoords = (depthSpace.xy / depthSpace.w + 1.0f) * 0.5f;

  float linearSceneDepth = (1.0f / ((depthSpace.z / depthSpace.w) + _EnvironmentDepthZBufferParams.y)) * _EnvironmentDepthZBufferParams.x;
  linearSceneDepth -= bias * linearSceneDepth * UNITY_NEAR_CLIP_VALUE;

  #if defined(HARD_OCCLUSION)
   return CalculateEnvironmentDepthHardOcclusion_Internal(uvCoords, linearSceneDepth);
  #elif defined(SOFT_OCCLUSION)
   return CalculateEnvironmentDepthSoftOcclusion_Internal(uvCoords, linearSceneDepth);
  #endif

  return 1.0f;
}

float CalculateEnvironmentDepthOcclusionWithBias(float2 uv, float sceneDepth, float bias)
{
  return CalculateEnvironmentDepthOcclusionLinearWithBias(uv, DepthConvertDepthToLinear(sceneDepth), bias);
}

float CalculateEnvironmentDepthOcclusion(float2 uv, float sceneDepth)
{
  return CalculateEnvironmentDepthOcclusionWithBias(uv, sceneDepth, 0.0f);
}


#if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)

#define META_DEPTH_VERTEX_OUTPUT(number) \
  float3 posWorld : TEXCOORD##number;

#define META_DEPTH_INITIALIZE_VERTEX_OUTPUT(output, vertex) \
  output.posWorld = META_DEPTH_CONVERT_OBJECT_TO_WORLD(vertex)

#define META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(posWorld, zBias) \
  CalculateEnvironmentDepthOcclusionInEnvDepthSpaceWithBias(posWorld.xyz, zBias);

#define META_DEPTH_GET_OCCLUSION_VALUE(input, zBias) META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(input.posWorld, zBias);

#define META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(posWorld, output, zBias) \
    float occlusionValue = META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(posWorld, zBias); \
    if (occlusionValue < 0.01) { \
      discard; \
    } \
    output *= occlusionValue; \

#define META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS_NAME(input, fieldName, output, zBias) \
  META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(input . ##fieldName, output, zBias)

#define META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY(input, output, zBias) \
  META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(input.posWorld, output, zBias)

#else

#define META_DEPTH_VERTEX_OUTPUT(number)
#define META_DEPTH_INITIALIZE_VERTEX_OUTPUT(output, vertex)
#define META_DEPTH_GET_OCCLUSION_VALUE_WORLDPOS(posWorld, zBias) 1.0
#define META_DEPTH_GET_OCCLUSION_VALUE(input, zBias) 1.0
#define META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(posWorld, output, zBias)
#define META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS_NAME(input, fieldName, output, zBias)
#define META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY(input, output, zBias) output = output

#endif
#endif
