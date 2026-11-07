Shader "Anaglyph/Lasertag/DepthLight"
{
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" }

		// Pass 0: lights the depth map at depth resolution, one slice per eye through
		// single-pass stereo, walking the Forward+ clusters of the eye that sees each texel
		Pass
		{
			Name "DepthLightIrradiance"
			ZWrite Off
			ZTest Always
			Cull Off

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#pragma multi_compile _ _LIGHT_LAYERS

			#define _RECEIVE_SHADOWS_OFF 1
			// no probes are read here, which also leaves ClusterNext its single-word path
			#define _ENVIRONMENTREFLECTIONS_OFF 1
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Assets/Anaglyph/XR/DepthKit/DepthKit.hlsl"

			// the depth map's pose lags the render pose and the composite's guess at the
			// surface adds parallax, so texels just outside the eye can still be sampled
			#define CAMERA_UV_MARGIN 0.1

			float4x4 _DepthClipToWorld[2];
			float4x4 _DepthLightWorldToCameraClip[2];
			float2 _DepthLightTexelSize;
			float _DepthLightMaxLights;
			uint _EnvironmentRenderingLayers;

			struct Attributes
			{
				uint vertexID : SV_VertexID;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
				return output;
			}

			#if USE_CLUSTER_LIGHT_LOOP
			// ClusterInit for a pass outside the camera: it takes the view explicitly, where
			// ClusterInit reads the bound camera, stereo eye and foveation. Must track
			// Clustering.hlsl. cameraUV is y-up, matching TilingJob's rows
			ClusterIterator DepthLightClusterInit(float2 cameraUV, float viewZ, uint view)
			{
				ClusterIterator state = (ClusterIterator)0;

				uint2 tileCount = uint2(URP_FP_TILE_COUNT_X, URP_FP_TILE_COUNT / URP_FP_TILE_COUNT_X);
				uint2 tileCoord = min(uint2(saturate(cameraUV) * URP_FP_TILE_SCALE), tileCount - 1);
				uint tileIndex = tileCoord.y * URP_FP_TILE_COUNT_X + tileCoord.x + URP_FP_TILE_COUNT * view;
				state.tileWordsOffset = tileIndex * URP_FP_WORDS_PER_TILE;

				int zBin = (int)(log2(viewZ) * URP_FP_ZBIN_SCALE + URP_FP_ZBIN_OFFSET);
				uint zBinIndex = min((uint)max(zBin, 0), URP_FP_ZBIN_COUNT - 1) + URP_FP_ZBIN_COUNT * view;
				uint zBinOffset = zBinIndex * (2 + URP_FP_WORDS_PER_TILE);
				state.zBinWordsOffset = zBinOffset + 2;

				// header 0 spans the lights; header 1 would be the reflection probes
				uint header = Select4(asuint(urp_ZBins[zBinOffset / 4]), zBinOffset % 4);
				#if MAX_LIGHTS_PER_TILE > 32 || CLUSTER_HAS_REFLECTION_PROBES
				state.entityIndexNextMax = header;
				#else
				if (URP_FP_WORDS_PER_TILE > 0)
				{
					state.tileMask =
						Select4(asuint(urp_Tiles[state.tileWordsOffset / 4]), state.tileWordsOffset % 4) &
						Select4(asuint(urp_ZBins[state.zBinWordsOffset / 4]), state.zBinWordsOffset % 4) &
						(0xFFFFFFFFu << (header & 0x1F)) & (0xFFFFFFFFu >> (31 - (header >> 16)));
				}
				#endif
				return state;
			}
			#endif

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				#if !USE_CLUSTER_LIGHT_LOOP
				return 0;
				#else
				int eye = unity_StereoEyeIndex;
				int2 texel = int2(input.positionCS.xy);

				float depth = agDepthTex.Load(int4(texel, eye, 0));
				if (!agDepthIsKnown(depth)) return 0;

				float2 depthUV = (texel + 0.5) * _DepthLightTexelSize;
				float4 worldH = mul(_DepthClipToWorld[eye], agDepthNDCtoHCS(float3(depthUV, depth)));
				float3 positionWS = worldH.xyz / worldH.w;

				// w is the distance along the view axis, which is what the Z bins index
				float4 cameraClip = mul(_DepthLightWorldToCameraClip[eye], float4(positionWS, 1));
				if (!(cameraClip.w > 0)) return 0;
				float2 cameraUV = cameraClip.xy / cameraClip.w * 0.5 + 0.5;
				if (any(abs(cameraUV - 0.5) > 0.5 + CAMERA_UV_MARGIN)) return 0;

				float3 normalWS = agDepthNormalTex.Load(int4(texel, eye, 0)).xyz;
				if (!(dot(normalWS, normalWS) > 0)) return 0;
				normalWS = normalize(normalWS);

				half3 lighting = 0;
				uint evaluated = 0;
				uint maxLights = (uint)_DepthLightMaxLights;
				uint lightIndex;
				ClusterIterator cluster = DepthLightClusterInit(cameraUV, cameraClip.w, (uint)eye);
				[loop] while (evaluated < maxLights && ClusterNext(cluster, lightIndex))
				{
					lightIndex += URP_FP_DIRECTIONAL_LIGHTS_COUNT;
					#if defined(_LIGHT_LAYERS)
					if (!IsMatchingLightLayer(asuint(_AdditionalLightsLayerMasks[lightIndex]), _EnvironmentRenderingLayers)) continue;
					#endif

					Light light = GetAdditionalPerObjectLight(lightIndex, positionWS);
					lighting += LightingLambert(light.color * light.distanceAttenuation, light.direction, normalWS);
					evaluated++;
				}
				return half4(lighting, 1);
				#endif
			}
			ENDHLSL
		}

		// Pass 1: adds the irradiance under everything already drawn, weighted by how much
		// passthrough each pixel still shows (1 - eye alpha). Coverage alpha already encodes
		// virtual objects in front, occluded ones (alpha = occlusion) and transparents, so
		// no depth test is needed. Drawn as one quad per Forward+ tile, collapsing quads no
		// light can reach
		Pass
		{
			Name "DepthLightComposite"
			ZWrite Off
			ZTest Always
			Cull Off
			Blend OneMinusDstAlpha One
			ColorMask RGB

			HLSLPROGRAM
			#pragma target 4.5
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Assets/Anaglyph/XR/DepthKit/DepthKit.hlsl"

			Texture2DArray<half4> _DepthLightIrradiance;
			float4x4 _WorldToDepthClip[2];

			struct Attributes
			{
				uint vertexID : SV_VertexID;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float4 depthClip : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			#if USE_CLUSTER_LIGHT_LOOP
			// Tile masks hold lights in the low bits and reflection probes above them
			bool TileHasLight(int2 tile, uint tileCountX, uint tileCountY, uint view)
			{
				if (any(tile < 0) || tile.x >= (int)tileCountX || tile.y >= (int)tileCountY) return false;

				uint firstWord = (tile.y * tileCountX + tile.x + URP_FP_TILE_COUNT * view) * URP_FP_WORDS_PER_TILE;
				uint lightCount = URP_FP_PROBES_BEGIN;
				uint lightWords = min(URP_FP_WORDS_PER_TILE, (lightCount + 31) / 32);
				for (uint word = 0; word < lightWords; word++)
				{
					uint lightBits = lightCount - word * 32;
					uint lightMask = lightBits >= 32 ? 0xFFFFFFFFu : (1u << lightBits) - 1;
					uint wordIndex = firstWord + word;
					if ((Select4(asuint(urp_Tiles[wordIndex / 4]), wordIndex % 4) & lightMask) != 0) return true;
				}
				return false;
			}

			// a pixel can read a texel whose surface projects into the next tile, from depth
			// map pose lag and parallax
			bool TileOrNeighborHasLight(int2 tile, uint tileCountX, uint tileCountY, uint view)
			{
				for (int y = -1; y <= 1; y++)
					for (int x = -1; x <= 1; x++)
						if (TileHasLight(tile + int2(x, y), tileCountX, tileCountY, view)) return true;
				return false;
			}
			#endif

			Varyings Vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				#if USE_CLUSTER_LIGHT_LOOP
				static const float2 corners[6] =
				{
					float2(0, 0), float2(1, 0), float2(1, 1),
					float2(0, 0), float2(1, 1), float2(0, 1)
				};

				uint tileIndex = input.vertexID / 6;
				if (tileIndex >= URP_FP_TILE_COUNT) return output;

				uint tileCountX = URP_FP_TILE_COUNT_X;
				uint tileCountY = URP_FP_TILE_COUNT / tileCountX;
				int2 tile = int2(tileIndex % tileCountX, tileIndex / tileCountX);
				#if defined(USING_STEREO_MATRICES)
				uint view = unity_StereoEyeIndex;
				#else
				uint view = 0;
				#endif
				if (!TileOrNeighborHasLight(tile, tileCountX, tileCountY, view)) return output;

				// Tiles are y-up in the unflipped projection. A flipped target negates the
				// projection's whole y row, so the sign of _m11 recovers the unflipped row
				float4x4 projection = UNITY_MATRIX_P;
				float ySign = projection._m11 < 0 ? -1 : 1;
				float2 cameraNDC = (tile + corners[input.vertexID % 6]) / URP_FP_TILE_SCALE * 2 - 1;
				float2 viewPlane = float2(
					(cameraNDC.x + projection._m02) / projection._m00,
					(cameraNDC.y + ySign * projection._m12) / abs(projection._m11));

				float quadDistance = 2.0 * _ProjectionParams.y;
				output.positionCS = mul(projection, float4(viewPlane * quadDistance, -quadDistance, 1));

				// the surface is guessed on a plane 1 m along the view axis
				float3 guessedViewPos = float3(viewPlane, -1);
				float3 guessedSurfaceWS = mul(UNITY_MATRIX_I_V, float4(guessedViewPos, 1)).xyz;
				output.depthClip = mul(_WorldToDepthClip[unity_StereoEyeIndex], float4(guessedSurfaceWS, 1));
				#endif
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				int eye = unity_StereoEyeIndex;
				float4 depthClip = input.depthClip;
				#if defined(SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
				UNITY_BRANCH if (_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
				{
					float2 renderUV = input.positionCS.xy / GetScaledScreenParams().xy;
					float2 linearUV = FoveatedRemapNonUniformToLinear(renderUV);
					float3 cameraWS = GetCameraPositionWS();
					float3 viewRayWS = ComputeWorldSpacePosition(linearUV, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP) - cameraWS;
					float3 guessedSurfaceWS = cameraWS + viewRayWS / dot(viewRayWS, GetViewForwardDir());
					depthClip = mul(_WorldToDepthClip[eye], float4(guessedSurfaceWS, 1));
				}
				#endif

				if (!(depthClip.w > 0)) discard;
				float2 depthUV = agDepthHCStoNDC(depthClip).xy;
				if (!all(depthUV >= 0) || !all(depthUV <= 1)) discard;

				half3 lighting = _DepthLightIrradiance.SampleLevel(agBilinearClampSampler, float3(depthUV, eye), 0).rgb;
				if (!any(lighting > 0)) discard;
				return half4(lighting, 0);
			}
			ENDHLSL
		}
	}
}
