/************************************************************************************
Copyright : Copyright (c) Facebook Technologies, LLC and its affiliates. All rights reserved.

Your use of this SDK or tool is subject to the Oculus SDK License Agreement, available at
https://developer.oculus.com/licenses/oculussdk/

Unless required by applicable law or agreed to in writing, the Utilities SDK distributed
under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
ANY KIND, either express or implied. See the License for the specific language governing
permissions and limitations under the License.
************************************************************************************/

Shader "Unlit/OVROverlaySrcRectEditor"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _PaddingAndSize("Padding And Size", Vector) = (4, 4, 128, 128)
        _SrcRect ("SrcRect", Vector) = (0,0,1,1)
        _DragColor ("DragColor", Color) = (1, 0, 0, 1)
        _BackgroundColor("Background Color", Color) = (0.278, 0.278, 0.278, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 dragLeftRight : TEXCOORD1;
                float4 dragTopBottom : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float4 _PaddingAndSize;

            float4 _SrcRect;

            float4 _DragColor;
            float4 _BackgroundColor;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // Add padding
                o.uv = (o.uv - 0.5) * (_PaddingAndSize.xy + _PaddingAndSize.zw) / _PaddingAndSize.zw + 0.5;

                // left
                o.dragLeftRight.x = _SrcRect.x;
                o.dragLeftRight.y = _SrcRect.y + _SrcRect.w * 0.5;
                // right
                o.dragLeftRight.z = _SrcRect.x + _SrcRect.z;
                o.dragLeftRight.w = _SrcRect.y + _SrcRect.w * 0.5;
                // top
                o.dragTopBottom.x = _SrcRect.x + _SrcRect.z * 0.5;
                o.dragTopBottom.y = _SrcRect.y;
                // bottom
                o.dragTopBottom.z = _SrcRect.x + _SrcRect.z * 0.5;
                o.dragTopBottom.w = _SrcRect.y + _SrcRect.w;

                return o;
            }

            float onDrag(const float2 uv, const float2 xy)
            {
                const float2 handleSize = (_PaddingAndSize.xy / 2.0 + 1.0) / _PaddingAndSize.zw;
                const float2 offset = abs(uv - xy);
                return offset.x <= handleSize.x && offset.y <= handleSize.y;
            }

            float onLine(const float2 uv, const float4 rect)
            {
                return
                    (abs(uv.x - rect.x) < (1 / _PaddingAndSize.z) && uv.y >= rect.y && uv.y <= rect.y + rect.w) ||
                    (abs(uv.x - rect.x - rect.z) < (1 / _PaddingAndSize.z) && uv.y >= rect.y && uv.y <= rect.y + rect.w) ||
                    (abs(uv.y - rect.y) < (1 / _PaddingAndSize.w) && uv.x >= rect.x && uv.x <= rect.x + rect.z) ||
                    (abs(uv.y - rect.y - rect.w) < (1 / _PaddingAndSize.w) && uv.x >= rect.x && uv.x <= rect.x + rect.z);
            }

            float checkerboard(const float2 uv)
            {
                const float2 xy = floor(uv * (_PaddingAndSize.xy + _PaddingAndSize.zw) / 8 - _PaddingAndSize.xy / 8);
                return xy.x + xy.y - 2.0 * floor((xy.x + xy.y) / 2.0);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);

                col.rgb = lerp(0.41 - 0.13 * checkerboard(i.uv), col.rgb, col.a);

                if (i.uv.x < 0 || i.uv.x > 1 || i.uv.y < 0 || i.uv.y > 1)
                {
                    col = _BackgroundColor;
                }

                const float2 uv = i.uv;

                // now draw clipping objects
                const float drag = onLine(uv, _SrcRect) ||
                    onDrag(uv, i.dragLeftRight.xy) ||
                    onDrag(uv, i.dragLeftRight.zw) ||
                    onDrag(uv, i.dragTopBottom.xy) ||
                    onDrag(uv, i.dragTopBottom.zw);

                return lerp(col, _DragColor, drag);
            }
            ENDCG
        }
    }
}
