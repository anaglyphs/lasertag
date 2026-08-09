Shader "Unlit/XRGizmos"
{
    Properties
    {
		_Color("Color", Color) = (1,1,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue" = "Geometry+250"}
        LOD 100
		CGINCLUDE
		#pragma target 3.0
		ENDCG
		Blend Off
		AlphaToMask Off
		Cull Back
		ColorMask RGBA
		ZWrite On
		ZTest LEqual
		Offset 0 , 0

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
			#pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
            };

			struct v2f
			{
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			uniform half4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                return _Color;
            }
            ENDCG
        }
    }
}
