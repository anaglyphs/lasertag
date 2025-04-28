Shader "VFX_Klaus/fx_explosion_apb"
{
	Properties
	{
		_Main_Tex("Main_Tex", 2D) = "white" {}
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
	}

	Category 
	{
		SubShader
		{
		LOD 0

			Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
			Blend SrcAlpha OneMinusSrcAlpha
			ColorMask RGB
			Cull Off
			Lighting Off 
			ZWrite Off
			ZTest LEqual
			
			Pass {
			
				CGPROGRAM
				
				#pragma vertex vert
				#pragma fragment frag
				#pragma target 2.0
				#pragma multi_compile_particles
				#pragma multi_compile_fog

				#include "UnityCG.cginc"

				struct appdata_t 
				{
					float4 vertex : POSITION;
					fixed4 color : COLOR;
					float4 texcoord : TEXCOORD0;
					UNITY_VERTEX_INPUT_INSTANCE_ID
					float4 k_texcoord1 : TEXCOORD1;
				};

				struct v2f 
				{
					float4 vertex : SV_POSITION;
					fixed4 color : COLOR;
					float4 texcoord : TEXCOORD0;
					UNITY_FOG_COORDS(1)
					#ifdef SOFTPARTICLES_ON
					float4 projPos : TEXCOORD2;
					#endif
					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO
					float4 k_texcoord3 : TEXCOORD3;
				};
				
				#if UNITY_VERSION >= 560
				UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
				#else
				uniform sampler2D_float _CameraDepthTexture;
				#endif

				uniform sampler2D _Main_Tex;
				uniform float4 _Main_Tex_ST;

				v2f vert ( appdata_t v )
				{
					v2f o;
					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					UNITY_TRANSFER_INSTANCE_ID(v, o);
					o.k_texcoord3 = v.k_texcoord1;

					v.vertex.xyz += float3( 0, 0, 0 ) ;
					o.vertex = UnityObjectToClipPos(v.vertex);
					#ifdef SOFTPARTICLES_ON
						o.projPos = ComputeScreenPos (o.vertex);
						COMPUTE_EYEDEPTH(o.projPos.z);
					#endif
					o.color = v.color;
					o.texcoord = v.texcoord;
					UNITY_TRANSFER_FOG(o,o.vertex);
					return o;
				}

				fixed4 frag ( v2f i  ) : SV_Target
				{
					#ifdef SOFTPARTICLES_ON
						float sceneZ = LinearEyeDepth (SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
						float partZ = i.projPos.z;
						float fade = saturate (i.k_texcoord3.w * (sceneZ-partZ));
						i.color.a *= fade;
					#endif

					float2 uv0_Main_Tex = i.texcoord.xy * _Main_Tex_ST.xy + _Main_Tex_ST.zw;
					float2 uv_Main_Tex = i.texcoord.xy * _Main_Tex_ST.xy + _Main_Tex_ST.zw;
					float4 tex2DResult = tex2D( _Main_Tex, ( uv0_Main_Tex + ( tex2D( _Main_Tex, uv_Main_Tex ).b * i.k_texcoord3.z ) ) );
					float clampResult = clamp( (( i.k_texcoord3.x * -1.0 * i.k_texcoord3.y ) + (( tex2DResult.g + ( 1.0 - i.k_texcoord3.x ) ) - 0.0) * (1.0 - ( i.k_texcoord3.x * -1.0 * i.k_texcoord3.y )) / (( ( i.k_texcoord3.x * 0.1 ) + 1.0 ) - 0.0)) , 0.0 , 1.0 );

					fixed4 col = ( tex2DResult.r * i.color * clampResult );
					UNITY_APPLY_FOG(i.fogCoord, col);
					return col;
				}
				ENDCG 
			}
		}	
	}
}