Shader "Lasertag/DepthOnly"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" }

		Pass
		{
			Tags { "LightMode" = "DepthOnly" }

			ZWrite On
			ZTest LEqual
			ColorMask 0
		}
	}
}

//Shader "Custom/ZDepth"
//{
//    Properties
//    {
//        
//    }
//    SubShader
//    {
//        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
//        LOD 100
//
//        Pass
//        {
//            ZWrite On
//            Cull Back
//            
//            HLSLPROGRAM
//            #pragma vertex vert
//            #pragma fragment frag
//            #pragma multi_compile_instancing
//            #pragma target 3.0
//            
//            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
//
//            struct Attributes
//            {
//                float4 positionOS : POSITION;
//                
//                UNITY_VERTEX_INPUT_INSTANCE_ID
//            };
//
//            struct Varyings
//            {
//                float4 positionCS : SV_POSITION;
//                float3 positionWS : TEXCOORD0;
//                
//                UNITY_VERTEX_OUTPUT_STEREO
//            };
//
//            Varyings vert(Attributes v)
//            {
//                Varyings o;
//                
//                UNITY_SETUP_INSTANCE_ID(v); //Insert
//                // UNITY_INITIALIZE_OUTPUT(Varyings, o); //Insert
//                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); //Insert
//
//                o.positionCS = TransformObjectToHClip(v.positionOS);
//                o.positionWS = TransformObjectToWorld(v.positionOS);
//                
//                return o;
//            }
//            
//            void ComputeDepthsFromWorldPos(float3 worldPos, out float linearEyeDepth, out float depth01)
//            {
//                float4 viewPos4 = mul(UNITY_MATRIX_V, float4(worldPos, 1.0));
//                linearEyeDepth = -viewPos4.z;
//                
//                float4 clipPos = TransformWorldToHClip(worldPos);
//                depth01 = clipPos.z / clipPos.w;
//            }
//
//            float4 frag(Varyings i) : SV_Target
//            {
//                // UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
//
//                float linearEyeDepth;
//                float depth01;
//                ComputeDepthsFromWorldPos(i.positionWS, linearEyeDepth, depth01);
//
//                return float4(depth01, depth01, depth01, 1);
//            }
//
//            ENDHLSL
//        }
//    }
//
//    Fallback Off
//}