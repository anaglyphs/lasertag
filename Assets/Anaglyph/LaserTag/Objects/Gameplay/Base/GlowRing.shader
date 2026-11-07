// A glowing ring on a cube proxy, around the transform's Y axis.
// X/Z scale = 2 * (ring radius + bound radius), Y scale = 2 * bound radius.
Shader "Anaglyph/Lasertag/Glow Ring"
{
	Properties
	{
		_Color ("Color", Color) = (1, 0, 0, 1)
		_CoreRadius ("Core Radius (fraction of bound radius)", Range(0, 1)) = 0.1
		_GlowRadius ("Glow Radius (fraction of bound radius)", Range(0, 0.95)) = 0.6
		_Brightness ("Brightness", Float) = 1
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent"
		}

		Pass
		{
			Tags
			{
				"LightMode" = "UniversalForward"
			}

			// each pixel has exactly one back face whether the eye is inside or outside the proxy.
			// frag does the depth test at the glow, not on this far side
			Cull Front
			ZTest LEqual
			ZWrite Off
			Blend One OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include_with_pragmas "Assets/Anaglyph/XR/DepthKit/Occlusion/DepthKitEnvironmentOcclusion.hlsl"
			#include "Assets/Anaglyph/LaserTag/Weapons/Bullets/LaserExposure.hlsl"

			#define SAMPLES_PER_RAY_HALF 4
			#define CLOSEST_PASS_REFINEMENTS 2
			#define MIN_GLOW_FOR_DEPTH 0.02

			struct Attributes
			{
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 viewVec : TEXCOORD0; // P - O, not normalized
				nointerpolation float3 eyeFromCentre : TEXCOORD1;
				nointerpolation float3 axis : TEXCOORD2;
				nointerpolation float2 radii : TEXCOORD3; // x = ring radius, y = bound radius

				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
				half4 _Color;
				float _CoreRadius;
				float _GlowRadius;
				float _Brightness;
			CBUFFER_END

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				// also sets unity_StereoEyeIndex, which _WorldSpaceCameraPos indexes
				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

				const float3 P = TransformObjectToWorld(IN.positionOS.xyz);
				OUT.positionHCS = TransformWorldToHClip(P);

				const float4x4 M = UNITY_MATRIX_M;
				const float3 O = _WorldSpaceCameraPos;
				const float boundRadius = length(M._m01_m11_m21) * 0.5f;
				const float outerRadius = length(M._m00_m10_m20) * 0.5f;

				OUT.viewVec = P - O;
				OUT.eyeFromCentre = O - M._m03_m13_m23;
				OUT.axis = normalize(M._m01_m11_m21);
				OUT.radii = float2(max(outerRadius - boundRadius, 0.001f), boundRadius);

				return OUT;
			}

			// O + v * t in ring coordinates: radial is the part in the ring's plane, y is along its axis
			struct RingRay
			{
				float3 originRadial;
				float3 dirRadial;
				float dirRadialLength;
				float originY;
				float dirY;
				float ringRadius;
				GlowShape shape;
			};

			// A line of 1/(r² + kernel²)² emitters, integrated along itself
			float GlowDensity(float distanceToRing, GlowShape shape)
			{
				const float q = distanceToRing * distanceToRing + Sq(shape.kernelRadius);
				return Sq(shape.kernelRadius) * rsqrt(q) / q * GlowBoundFade(distanceToRing, shape);
			}

			float DistanceToRing(RingRay ray, float t)
			{
				const float radial = length(ray.originRadial + t * ray.dirRadial) - ray.ringRadius;
				const float axial = ray.originY + t * ray.dirY;
				return sqrt(radial * radial + axial * axial);
			}

			// Where the ray passes closest to the ring: the better of its crossings of the
			// ring's radius and plane, refined by Gauss-Newton steps
			float FindClosestPass(RingRay ray, float start, float end, float radiusCrossing)
			{
				const float a = clamp(radiusCrossing, start, end);
				const float b = clamp(-ray.originY / ray.dirY, start, end);
				float closestT = DistanceToRing(ray, a) <= DistanceToRing(ray, b) ? a : b;

				for (int i = 0; i < CLOSEST_PASS_REFINEMENTS; i++)
				{
					const float3 radial = ray.originRadial + closestT * ray.dirRadial;
					const float r = max(length(radial), 1e-6);
					const float radialRate = dot(radial, ray.dirRadial) / r;
					const float y = ray.originY + closestT * ray.dirY;
					const float correction = ((r - ray.ringRadius) * radialRate + y * ray.dirY)
						/ max(ray.dirY * ray.dirY + radialRate * radialRate, 1e-4);
					closestT = clamp(closestT - correction, start, end);
				}
				return closestT;
			}

			// Integrates one side of the ray with samples packed around its closest pass, using
			// a tan warp to match the glow's bell shape. Viewed side-on, the ring's ends keep
			// the ray near the tube for a long way, and the width grows to cover it.
			float IntegrateRayHalf(RingRay ray, float start, float end, float radiusCrossing, out float peak)
			{
				peak = clamp(radiusCrossing, start, end);
				if (end <= start)
					return 0;

				peak = FindClosestPass(ray, start, end, radiusCrossing);

				const float3 radialAtPeak = ray.originRadial + peak * ray.dirRadial;
				const float r = length(radialAtPeak);
				const float y = ray.originY + peak * ray.dirY;
				const float radialRate = dot(radialAtPeak, ray.dirRadial) / max(r, 1e-6);
				const float rateSquared = max(ray.dirY * ray.dirY + radialRate * radialRate, 1e-8);
				const float missDistance = sqrt((r - ray.ringRadius) * (r - ray.ringRadius) + y * y
					+ Sq(ray.shape.kernelRadius));
				const float width = min(missDistance * rsqrt(rateSquared),
					sqrt(2 * ray.ringRadius * missDistance) / ray.dirRadialLength);

				const float warpedStart = atan((start - peak) / width);
				const float warpedStep = (atan((end - peak) / width) - warpedStart) / SAMPLES_PER_RAY_HALF;

				float glow = 0;
				for (int i = 0; i < SAMPLES_PER_RAY_HALF; i++)
				{
					const float tanWarped = tan(warpedStart + (i + 0.5f) * warpedStep);
					const float t = peak + width * tanWarped;
					glow += GlowDensity(DistanceToRing(ray, t), ray.shape)
						* (1 + tanWarped * tanWarped);
				}
				return glow * width * warpedStep;
			}

			half4 frag(Varyings IN, out float depth : SV_Depth) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				const float3 v = normalize(IN.viewVec);
				const float3 axis = IN.axis;
				const float ringRadius = IN.radii.x;
				const float boundRadius = IN.radii.y;

				RingRay ray;
				ray.originY = dot(IN.eyeFromCentre, axis);
				const float dirAlongAxis = dot(v, axis);
				ray.dirY = abs(dirAlongAxis) > 1e-6 ? dirAlongAxis : 1e-6;
				ray.originRadial = IN.eyeFromCentre - ray.originY * axis;
				ray.dirRadial = v - dirAlongAxis * axis;
				ray.ringRadius = ringRadius;
				const GlowShape shape = MakeGlowShape(_CoreRadius, _GlowRadius, boundRadius, LineGlowProfilePower);
				ray.shape = shape;

				const float dirRadialSquared = max(dot(ray.dirRadial, ray.dirRadial), 1e-8);
				ray.dirRadialLength = sqrt(dirRadialSquared);

				// where the ray comes closest to the axis splits it into a near and far side of the ring
				const float closestToAxis = -dot(ray.originRadial, ray.dirRadial) / dirRadialSquared;
				const float3 closestRadial = ray.originRadial + closestToAxis * ray.dirRadial;
				const float closestSquared = dot(closestRadial, closestRadial);

				const float outerRadius = ringRadius + boundRadius;
				const float outerChord = sqrt(max(outerRadius * outerRadius - closestSquared, 0)) / ray.dirRadialLength;

				const float slabA = (-boundRadius - ray.originY) / ray.dirY;
				const float slabB = (boundRadius - ray.originY) / ray.dirY;

				const float3 O = _WorldSpaceCameraPos;
				float surfaceDistance;
				agEnvSurfaceDistance_float(O, v, surfaceDistance);

				const float start = max(max(0, closestToAxis - outerChord), min(slabA, slabB));
				const float end = min(min(surfaceDistance, closestToAxis + outerChord), max(slabA, slabB));

				// outside the glow's bounds, or entirely within the ring's hole
				const float innerRadius = ringRadius - boundRadius;
				if (end <= start
					|| closestSquared >= outerRadius * outerRadius
					|| max(length(ray.originRadial + start * ray.dirRadial),
						length(ray.originRadial + end * ray.dirRadial)) < innerRadius)
					discard;

				const float crossingOffset = sqrt(max(ringRadius * ringRadius - closestSquared, 0)) / ray.dirRadialLength;
				const float split = clamp(closestToAxis, start, end);

				float nearPeak, farPeak;
				const float nearGlow = IntegrateRayHalf(ray, start, split, closestToAxis - crossingOffset, nearPeak);
				const float farGlow = IntegrateRayHalf(ray, split, end, closestToAxis + crossingOffset, farPeak);

				// kept in front of the eye, so a ring passing through the head has a valid clip w
				const float glowDistance = max(nearGlow > MIN_GLOW_FOR_DEPTH ? nearPeak : farPeak, 0.001f);
				const float4 glowClip = TransformWorldToHClip(O + v * glowDistance);
				depth = saturate(glowClip.z / glowClip.w);

				// a tube of this density integrates to 2 at its centre
				const float glow = (nearGlow + farGlow) * 0.5f * _Brightness;
				return half4(LaserExposure(glow, shape, _Color.rgb), 0);
			}

			ENDHLSL
		}
	}
}
