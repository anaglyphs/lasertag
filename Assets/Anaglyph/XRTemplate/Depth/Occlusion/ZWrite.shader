Shader "Lasertag/DepthOnly"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
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
            Cull Front
        }
    }
}