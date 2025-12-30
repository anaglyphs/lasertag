Shader "UI/Prerendered"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Color("Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _AlphaWrite("Alpha Write", Int) = 0
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}

        Blend One OneMinusSrcAlpha, [_AlphaWrite] OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ALPHA_SQUARED
            #pragma multi_compile _ EXPENSIVE
            #pragma multi_compile _ OVERLAP_MASK

            #include "UnityCG.cginc"

            struct appdata_t {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                half2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert(appdata_t v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                #if OVERLAP_MASK
                // perform 4x multitap sample, selecting min value
                float2 dx = 0.5 * ddx(i.texcoord);
                float2 dy = 0.5 * ddy(i.texcoord);
                // sample the corners of the pixel
                fixed4 col = min(
                    min(
                        tex2D(_MainTex, i.texcoord + dx + dy),
                        tex2D(_MainTex, i.texcoord - dx + dy)),
                    min(
                        tex2D(_MainTex, i.texcoord + dx - dy),
                        tex2D(_MainTex, i.texcoord - dx - dy)));
                #elif EXPENSIVE
                // perform 4x multitap sample
                float2 dx = 0.25 * ddx(i.texcoord);
                float2 dy = 0.25 * ddy(i.texcoord);
                // sample four points inside the pixel
                fixed4 col = 0.25 * (
                    tex2D(_MainTex, i.texcoord + dx + dy) +
                    tex2D(_MainTex, i.texcoord - dx + dy) +
                    tex2D(_MainTex, i.texcoord + dx - dy) +
                    tex2D(_MainTex, i.texcoord - dx - dy));
                #else
                fixed4 col = tex2D(_MainTex, i.texcoord);
                #endif

                #if ALPHA_SQUARED
                // prerended UI will have a = Alpha * SrcAlpha, so we need to sqrt
                // to get the original alpha value
                col.a = sqrt(col.a);
                #endif

                // It should be noted that with Gamma lighting on PC,
                // the blend will result in not correct colors of transparent
                // portions of the overlay

                col *= _Color;
                return col;
            }
            ENDCG
        }
    }
}
