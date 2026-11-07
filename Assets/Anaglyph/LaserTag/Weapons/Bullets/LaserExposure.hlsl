#ifndef LASER_EXPOSURE_INCLUDED
#define LASER_EXPOSURE_INCLUDED

// Every glow is a profile (1 + (r / kernelRadius)²)^-profilePower scaled by an exposure,
// then tonemapped per channel. The white burn-out fades out by the core radius and the
// colour by the glow radius. Both radii are fractions of the proxy's bound radius, so a
// glow scales with its object. The profile's value at the bound is subtracted, so every
// glow reaches zero exactly at its proxy's edge without a visible cut-off.

static const float GlowEdgeExposure = 0.02;
static const float GlowCoreWhiteExposure = 4;

// a line of point emitters seen side-on: lasers, rings, streaks
static const float LineGlowProfilePower = 1;
// a single point emitter: sparks
static const float PointGlowProfilePower = 1.5;
// the glow must reach the edge exposure before the profile reaches zero at the bound
static const float MaxGlowFraction = 0.95;

struct GlowShape
{
	float kernelRadius;
	float glowRadius;
	float boundRadius;
	float profilePower;
	float profileAtBound;
	float exposure;
	half whiteness;
};

float GlowProfile(float distance, float kernelRadius, float profilePower)
{
	return pow(1 + Sq(distance / kernelRadius), -profilePower);
}

// 1 at the centre
float GlowProfile(float distance, GlowShape shape)
{
	return GlowProfile(distance, shape.kernelRadius, shape.profilePower);
}

// times the profile: the profile minus its value at the bound, with the centre kept at 1.
// Times any other glow, such as an integral: a smooth fade to zero at the bound
float GlowBoundFade(float distance, GlowShape shape)
{
	const float profile = GlowProfile(distance, shape);
	return max(profile - shape.profileAtBound, 0) / max(profile * (1 - shape.profileAtBound), 1e-6);
}

GlowShape MakeGlowShape(float coreFraction, float glowFraction, float boundRadius, float profilePower)
{
	GlowShape shape;
	shape.profilePower = profilePower;
	shape.boundRadius = boundRadius;
	shape.glowRadius = min(glowFraction, MaxGlowFraction) * boundRadius;
	shape.kernelRadius = max(coreFraction * boundRadius, 1e-6)
		* rsqrt(pow(GlowCoreWhiteExposure / GlowEdgeExposure, 1 / profilePower) - 1);
	shape.profileAtBound = GlowProfile(boundRadius, shape.kernelRadius, profilePower);
	const float fadedProfileAtGlowRadius = GlowProfile(shape.glowRadius, shape) * GlowBoundFade(shape.glowRadius, shape);
	shape.exposure = GlowEdgeExposure / max(fadedProfileAtGlowRadius, 1e-6);
	shape.whiteness = saturate(GlowCoreWhiteExposure / shape.exposure);
	return shape;
}

// glow is the profile, or an integral normalised to match it: 1 at the centre
half3 LaserExposure(float glow, GlowShape shape, half3 color)
{
	// dim colours are boosted part of the way to equal luminance: radii stay close
	// across colours without saturated blues and reds clipping to a hard edge
	const half3 luminanceBalancedColor = color * rsqrt(max(dot(color, half3(0.2126, 0.7152, 0.0722)), 0.001));
	const half3 spectrum = lerp(luminanceBalancedColor, 1, shape.whiteness);
	return 1 - exp(-glow * shape.exposure * spectrum);
}

#endif
