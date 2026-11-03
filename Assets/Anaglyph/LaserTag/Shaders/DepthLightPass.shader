Shader "Anaglyph/Lasertag/DepthLight"
{
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" }

		// Pass 0: lights one depth-map slice at depth resolution, walking the Forward+
		// clusters of the camera eye that sees each texel
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
			float4x4 _DepthLightWorldToCameraClip;
			float2 _DepthLightTexelSize;
			float _DepthLightEye;
			float _DepthLightClusterView;
			float _DepthLightMaxLights;
			uint _EnvironmentRenderingLayers;

			float4 Vert(uint vertexID : SV_VertexID) : SV_POSITION
			{
				return GetFullScreenTriangleVertexPosition(vertexID);
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

			half4 Frag(float4 positionCS : SV_POSITION) : SV_Target
			{
				#if !USE_CLUSTER_LIGHT_LOOP
				return 0;
				#else
				int eye = (int)_DepthLightEye;
				int2 texel = int2(positionCS.xy);

				float depth = agDepthTex.Load(int4(texel, eye, 0));
				if (!agDepthIsKnown(depth)) return 0;

				float2 depthUV = (texel + 0.5) * _DepthLightTexelSize;
				float4 worldH = mul(_DepthClipToWorld[eye], agDepthNDCtoHCS(float3(depthUV, depth)));
				float3 positionWS = worldH.xyz / worldH.w;

				// w is the distance along the view axis, which is what the Z bins index
				float4 cameraClip = mul(_DepthLightWorldToCameraClip, float4(positionWS, 1));
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
				ClusterIterator cluster = DepthLightClusterInit(cameraUV, cameraClip.w, (uint)_DepthLightClusterView);
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
		// no depth test is needed
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
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Assets/Anaglyph/XR/DepthKit/DepthKit.hlsl"
			#include "Assets/Anaglyph/XR/DepthKit/ViewQuad.hlsl"

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
				float3 viewRayWS : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				float3 positionWS = agViewQuadWorld(input.vertexID);
				output.positionCS = TransformWorldToHClip(positionWS);
				output.viewRayWS = positionWS - GetCameraPositionWS();
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				int eye = unity_StereoEyeIndex;
				float3 cameraWS = GetCameraPositionWS();
				float3 viewRayWS = input.viewRayWS;
				#if defined(SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
				UNITY_BRANCH if (_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
				{
					float2 renderUV = input.positionCS.xy / GetScaledScreenParams().xy;
					float2 linearUV = FoveatedRemapNonUniformToLinear(renderUV);
					viewRayWS = ComputeWorldSpacePosition(linearUV, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP) - cameraWS;
				}
				#endif

				float4 depthClip = mul(_WorldToDepthClip[eye], float4(cameraWS + normalize(viewRayWS), 1));
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
