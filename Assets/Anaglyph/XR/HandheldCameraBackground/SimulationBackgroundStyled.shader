// AR Foundation 6.7.1's Simulation Background Simple with PassthroughStyle applied
Shader "Anaglyph/SimulationBackgroundStyled"
{
    Properties
    {
        _TextureSingle ("TextureSingle", 2D) = "white" {}
        _TextureSingleDepth ("TextureSingleDepth", 2D) = "black" {}
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "ForceNoShadowCasting" = "True"
        }

        Pass
        {
            Cull Off
            ZTest LEqual
            ZWrite On
            Lighting Off
            LOD 100
            Tags
            {
                "LightMode" = "Always"
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_local __ SIMULATION_OCCLUSION_ENABLED
            #include "UnityCG.cginc"
            #include "PassthroughStyle.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            struct fragment_output
            {
                float4 color : SV_Target;
                float depth : SV_Depth;
            };

            sampler2D _TextureSingle;
            sampler2D _TextureSingleDepth;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            uniform float _UnityCameraForwardScale;

            float ConvertDistanceToDepth(float d)
            {
                d = _UnityCameraForwardScale > 0.0 ? _UnityCameraForwardScale * d : d;

                float zBufferParamsW = 1.0 / _ProjectionParams.y;
                float zBufferParamsY = _ProjectionParams.z * zBufferParamsW;
                float zBufferParamsX = 1.0 - zBufferParamsY;
                float zBufferParamsZ = zBufferParamsX * _ProjectionParams.w;

                // Clip any distances smaller than the near clip plane, and compute the depth value from the distance.
                return (d < _ProjectionParams.y) ? 1.0f : ((1.0 / zBufferParamsZ) * ((1.0 / d) - zBufferParamsW));
            }

            fragment_output frag (v2f i)
            {
                // sample the texture
                fixed4 col = tex2D(_TextureSingle, i.uv);

                float2 edgeStepX = ddx(i.uv) * PassthroughEdgeSampleSpacingPixels;
                float2 edgeStepY = ddy(i.uv) * PassthroughEdgeSampleSpacingPixels;
                if (IsPassthroughStyled())
                {
                    float edgeStrength = 0.0;
                    if (_PassthroughEdgeColor.a > 0.0)
                    {
                        edgeStrength = PassthroughEdgeStrength(
                            PassthroughLuminanceSrgbOfColorSpaceColor(tex2D(_TextureSingle, i.uv - edgeStepX).rgb),
                            PassthroughLuminanceSrgbOfColorSpaceColor(tex2D(_TextureSingle, i.uv + edgeStepX).rgb),
                            PassthroughLuminanceSrgbOfColorSpaceColor(tex2D(_TextureSingle, i.uv - edgeStepY).rgb),
                            PassthroughLuminanceSrgbOfColorSpaceColor(tex2D(_TextureSingle, i.uv + edgeStepY).rgb));
                    }
                    col.rgb = ApplyPassthroughStyleColorSpaceColor(col.rgb, edgeStrength);
                }
                fragment_output o;
                o.color = col;
#if SIMULATION_OCCLUSION_ENABLED
                float depth = tex2D(_TextureSingleDepth, i.uv).x;
                o.depth = 1 - ConvertDistanceToDepth(depth);
#else
#if defined(UNITY_REVERSED_Z)
                o.depth = 0.0f;
#else
                o.depth = 1.0f;
#endif
#endif
                return o;
            }
            ENDCG
        }
    }
}
