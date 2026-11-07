Shader "Anaglyph/Lasertag/Laser"
{
	Properties
	{
		_Color ("Color", Color) = (1, 0, 0, 1)
		_CoreRadius ("Core Radius (fraction of mesh radius)", Range(0, 1)) = 0.05
		_GlowRadius ("Glow Radius (fraction of mesh radius)", Range(0, 0.95)) = 0.6
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

			// each pixel has exactly one back face whether the eye is inside or
			// outside the proxy, and each eye may be on a different side of it.
			// frag does the depth test at the glow, not on this far side
			Cull Front
			ZTest LEqual
			ZWrite Off
			Blend One OneMinusSrcAlpha

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "LaserExposure.hlsl"
			#include_with_pragmas "Assets/Anaglyph/XR/DepthKit/Occlusion/DepthKitEnvironmentOcclusion.hlsl"

			/// P = world position
			/// O = camera view pos
			/// v = view direction
			/// t = distance along view
			///
			/// A = tail end of laser
			/// D = laser direction
			/// L = laser length
			/// s = distance from tail
			///		s = 0 = tail, s = L = head
			///	R = mesh radius

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 viewVec : TEXCOORD0; // not normalized. P - O
				nointerpolation float4 dirLen : TEXCOORD1; // xyz = D, w = L
				nointerpolation float4 wRad : TEXCOORD2; // xyz = w (A - O), w = R
				
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;

				// also sets unity_StereoEyeIndex under single-pass instanced, which
				// _WorldSpaceCameraPos indexes, so it must run before reading O
				UNITY_SETUP_INSTANCE_ID(IN);
				// writes the SV_RenderTargetArrayIndex that UNITY_VERTEX_OUTPUT_STEREO
				// declares; d3d11 rejects the shader if it is left unwritten
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
				
				const float3 P = TransformObjectToWorld(IN.positionOS.xyz); // surface pos
				OUT.positionHCS = TransformWorldToHClip(P);

				const float4x4 M = UNITY_MATRIX_M; // laser transform
				const float3 centre = M._m03_m13_m23; // laser pos
				const float3 O = _WorldSpaceCameraPos; // camera pos
				const float3 D = normalize(M._m02_m12_m22); // laser direction
				const float R = length(M._m00_m10_m20) * 0.5f; // glow radius
				const float L = max(length(M._m02_m12_m22) - 2.0f * R, 0.0f); // length
				const float3 w = centre - O; // camera to laser center
				
				OUT.viewVec = float3(P - O);
				OUT.dirLen = float4(D.xyz, L);
				OUT.wRad = float4(w.xyz, R);
				
				return OUT;
			}

			CBUFFER_START(UnityPerMaterial)
				half4 _Color;
				float _CoreRadius;
				float _GlowRadius;
			CBUFFER_END

			// w = laser centre - O, so s runs from -L/2 (tail) to +L/2 (head).
			// Point kernel 1/(r² + eps²)²: integrated along the view ray it is
			// π/(2 q^3/2) with q = h² + eps², so a line falls off as 1/h² sideways.
			// The softer 1/r² kernel only falls off as ln(L/h), which fills the
			// whole proxy instead of reading as a line.
			// Returns glow scaled so the centre of a long laser seen side-on is 1,
			// hMin = closest distance between the view ray and the segment, and
			// sMin = where along the segment (from the centre) that happens.
			float LaserGlow(float3 v, float3 w, float3 D, float L, float eps, out float hMin, out float sMin)
			{
			    float Dv = dot(D, v);
			    float3 wAcrossView = w - dot(w, v) * v;
			    float a = 1 - Dv * Dv;                        // 0 when viewed end-on
			    float b = 2 * dot(wAcrossView, D);
			    float c0 = dot(wAcrossView, wAcrossView);     // h² at the centre
			    float c = c0 + eps * eps;
			    // distance between the view ray and the laser's line, times sin(angle)
			    float lineToRay = dot(w, cross(D, v));
			    // h²(s) = a s² + b s + c0, minimised over the segment;
			    // end-on, the nearest end is the first thing the ray meets
			    sMin = a < 1e-4 ? -0.5 * L * sign(Dv) : clamp(-b / (2 * a), -0.5 * L, 0.5 * L);
			    hMin = sqrt(max(a * sMin * sMin + b * sMin + c0, 0));
			    // ∫-L/2..L/2 ds / q^3/2, q = a s² + b s + c
			    if (a < 1e-4)                                 // end-on: h is constant
			        return 0.5 * eps * eps * L * rsqrt(c) / c;
			    // antiderivative 2 (2as + b) / (Δ sqrt(q)), Δ = 4ac - b²
			    float delta = max(4 * (a * eps * eps + lineToRay * lineToRay), 1e-20);
			    float q0 = 0.25 * a * L * L - 0.5 * b * L + c;
			    float q1 = 0.25 * a * L * L + 0.5 * b * L + c;
			    float F0 = (-a * L + b) * rsqrt(q0);
			    float F1 = ( a * L + b) * rsqrt(q1);
			    return eps * eps * (F1 - F0) / delta;
			}

			float ShareInFrontOfSurface(float surfaceToGlow, float hMin, float eps)
			{
				const float u = surfaceToGlow * rsqrt(hMin * hMin + eps * eps);
				return 0.5 + (atan(u) + u / (1 + u * u)) / PI;
			}

			half4 frag(Varyings IN, out float depth : SV_Depth) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				const float3 viewVec = IN.viewVec;
				const float3 v = normalize(viewVec);
				const float3 D = IN.dirLen.xyz;
				const float L = IN.dirLen.w;
				const float3 w = IN.wRad.xyz;
				const float R = IN.wRad.w;
				const GlowShape shape = MakeGlowShape(_CoreRadius, _GlowRadius, R, LineGlowProfilePower);
				const float eps = shape.kernelRadius;

				float hMin, sMin;
				float glow = LaserGlow(v, w, D, L, eps, hMin, sMin);

				// occlude at the point on this pixel's view ray nearest the laser,
				// which is where most of the pixel's glow comes from
				const float3 O = _WorldSpaceCameraPos;
				// kept in front of the eye: a laser passing through or behind the
				// head still needs a valid clip w, and clamps to the near plane
				const float glowDist = max(dot(w + sMin * D, v), 0.001f);
				const float3 glowPos = O + v * glowDist;

				const float4 glowClip = TransformWorldToHClip(glowPos);
				depth = saturate(glowClip.z / glowClip.w);

				// share of the kernel's ray integral that lies in front of the surface:
				// the core cuts crisply there while the wider glow fades through it
				float envDist;
				agEnvSurfaceDistance_float(O, v, envDist);
				glow *= ShareInFrontOfSurface(envDist - glowDist, hMin, eps);

				glow *= GlowBoundFade(hMin, shape);

				return half4(LaserExposure(glow, shape, _Color.rgb), 0);
			}
			
			ENDHLSL
		}
	}
}