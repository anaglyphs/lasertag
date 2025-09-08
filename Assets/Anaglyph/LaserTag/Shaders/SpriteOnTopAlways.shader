Shader "Custom/Sprite On Top Always"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
		_Color ("Tint", Color) = (1,1,1,1)
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
		ZWrite Off
		ZTest Always 
		Blend SrcAlpha OneMinusSrcAlpha
		Cull Off
		Lighting Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			CBUFFER_START(UnityPerMaterial)
			sampler2D _MainTex;
			fixed4 _Color;
			CBUFFER_END

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
				fixed4 color : COLOR; // Vertex color
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
				fixed4 color : COLOR;
			};

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.color = v.color;
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				fixed4 texCol = tex2D(_MainTex, i.uv) * _Color;
				return texCol * i.color; // Apply vertex color
			}
			ENDCG
		}
	}
}