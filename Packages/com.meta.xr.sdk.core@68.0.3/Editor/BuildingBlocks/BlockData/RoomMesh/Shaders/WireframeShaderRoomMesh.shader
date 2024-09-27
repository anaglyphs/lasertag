Shader "Unlit/WireframeShaderRoomMesh" {
  Properties {
    _WireframeColor("WireframeColor", Color) = (1, 0, 0, 1)
    _Color("Color", Color) = (1, 1, 1, 1)
    _DistanceMultipler("DistanceMultiplier", Range(1, 5)) = 1
  }

  SubShader {
    Pass {
      CGPROGRAM
      #include "UnityCG.cginc"
      #pragma vertex vert
      #pragma fragment frag

      half4 _WireframeColor, _Color;
      float _LineThickness, _DistanceMultipler;

      struct appdata
      {
        float4 vertex : POSITION;
        float4 color : COLOR; // barycentric coords
        UNITY_VERTEX_INPUT_INSTANCE_ID
      };

      struct v2f
      {
        float4 vertex : SV_POSITION;
        float3 vertexView : TEXCOORD0;
        float3 color: COLOR;
        UNITY_VERTEX_OUTPUT_STEREO
      };

      v2f vert(appdata v)
      {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_OUTPUT(v2f, o);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.vertexView = UnityObjectToViewPos(v.vertex);
        o.color = v.color;
        return o;
      }

      fixed4 frag(v2f i) : SV_Target
      {
        // on edge, one or the coordinates is 0
        float closest = min(i.color.x, min(i.color.y, i.color.z));

        // use distance to make far away edges visible
        float distance = length(i.vertexView) * _DistanceMultipler * 0.002;
        float val = closest/distance;

        return lerp(_WireframeColor, _Color, clamp(val,0,1));
      }
      ENDCG
    }
  }
}
