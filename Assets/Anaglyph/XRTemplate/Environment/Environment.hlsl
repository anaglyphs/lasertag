uniform Texture2D<float2> agEnvHeightMap; // signed float

uniform float agEnvSizeMeters;
uniform SamplerState agEnvPointClampSampler;

float2 agEnvWorldToUV(float3 world)
{
    return float2(world.xz / agEnvSizeMeters + 0.5);
}

float2 agEnvUVToWorld(float2 uv)
{
    return float2((uv - 0.5) * agEnvSizeMeters);
}

float agEnvSample(float2 uv)
{
    return agEnvHeightMap.SampleLevel(agEnvPointClampSampler, uv, 0).r;
}