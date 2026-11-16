#ifndef ANAGLYPH_PASSTHROUGH_STYLE_INCLUDED
#define ANAGLYPH_PASSTHROUGH_STYLE_INCLUDED

// Set by PassthroughStylingFeature. Colors are sRGB, like the headset's color LUT.
float4 _PassthroughTint;      // rgb tint, a = blend toward tinted luminance
float4 _PassthroughEdgeColor; // rgb edge color, a = edge opacity

static const float3 PassthroughLuminanceWeights = float3(0.2126, 0.7152, 0.0722);
static const float PassthroughEdgeSampleSpacingPixels = 1.5;
static const float PassthroughEdgeGain = 4.0;

bool IsPassthroughStyled()
{
    return _PassthroughTint.a > 0.0 || _PassthroughEdgeColor.a > 0.0;
}

float PassthroughLuminanceSrgb(float3 srgb)
{
    float3 approximateLinear = srgb * srgb;
    return sqrt(dot(approximateLinear, PassthroughLuminanceWeights));
}

float PassthroughEdgeStrength(float luminanceLeft, float luminanceRight, float luminanceDown, float luminanceUp)
{
    return saturate((abs(luminanceRight - luminanceLeft) + abs(luminanceUp - luminanceDown)) * PassthroughEdgeGain);
}

float3 ApplyPassthroughStyleSrgb(float3 srgb, float edgeStrength)
{
    float3 tinted = PassthroughLuminanceSrgb(srgb) * _PassthroughTint.rgb;
    srgb = lerp(srgb, tinted, _PassthroughTint.a);
    return lerp(srgb, _PassthroughEdgeColor.rgb, _PassthroughEdgeColor.a * edgeStrength);
}

// For colors already in the project's color space
float PassthroughLuminanceSrgbOfColorSpaceColor(float3 color)
{
#if UNITY_COLORSPACE_GAMMA
    return PassthroughLuminanceSrgb(color);
#else
    return sqrt(dot(max(color, 0.0), PassthroughLuminanceWeights));
#endif
}

float3 ApplyPassthroughStyleColorSpaceColor(float3 color, float edgeStrength)
{
#if UNITY_COLORSPACE_GAMMA
    return ApplyPassthroughStyleSrgb(color, edgeStrength);
#else
    float3 styledSrgb = ApplyPassthroughStyleSrgb(sqrt(max(color, 0.0)), edgeStrength);
    return styledSrgb * styledSrgb;
#endif
}

#endif
