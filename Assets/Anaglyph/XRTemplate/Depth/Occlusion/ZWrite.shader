Shader "Lasertag/DepthOnly"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off
        }
    }
}