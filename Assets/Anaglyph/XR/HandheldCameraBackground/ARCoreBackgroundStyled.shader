// ARCore 6.7.1's ARCoreBackground with PassthroughStyle applied; the GLES3 pass repeats PassthroughStyle.hlsl in GLSL
Shader "Anaglyph/ARCoreBackgroundStyled"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _EnvironmentDepth("Texture", 2D) = "black" {}
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////
    // GLSL shader for OpenGLES 3. GLES3 cannot use HLSL shader like Vulkan because of dependency on
    // GL_OES_EGL_image_external_essl3 extension. Thus they are kept in different subshaders.
    /////////////////////////////////////////////////////////////////////////////////////////////////
    SubShader
    {
        Name "ARCore Background (Before Opaques) for GLES3"
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "ForceNoShadowCasting" = "True"
        }

        Pass
        {
            Name "AR Camera Background (ARCore)"
            Cull Off
            ZTest Always
            ZWrite On
            Lighting Off
            LOD 100
            Tags
            {
                "LightMode" = "Always"
            }

            GLSLPROGRAM

            #pragma only_renderers gles3

            #pragma multi_compile_local __ ARCORE_ENVIRONMENT_DEPTH_ENABLED
            #pragma multi_compile_local __ ARCORE_IMAGE_STABILIZATION_ENABLED

            #include "UnityCG.glslinc"

#ifdef SHADER_API_GLES3
#extension GL_OES_EGL_image_external_essl3 : require
#endif // SHADER_API_GLES3

#ifndef ARCORE_IMAGE_STABILIZATION_ENABLED
#define ARCORE_TEXCOORD_TYPE vec2
#else // ARCORE_IMAGE_STABILIZATION_ENABLED
#define ARCORE_TEXCOORD_TYPE vec3
#endif // !ARCORE_IMAGE_STABILIZATION_ENABLED

            // Device display transform is provided by the AR Foundation camera background renderer.
            uniform mat4 _UnityDisplayTransform;

#ifdef VERTEX
            varying ARCORE_TEXCOORD_TYPE textureCoord;

            void main()
            {
#ifdef SHADER_API_GLES3
                // Transform the position from object space to clip space.
                gl_Position = gl_ModelViewProjectionMatrix * gl_Vertex;

#ifdef ARCORE_IMAGE_STABILIZATION_ENABLED
                textureCoord = gl_MultiTexCoord0.xyz;
#else
                // Remap the texture coordinates based on the device rotation.
                // _UnityDisplayTransform is provided as "Row Major" for all mobile platforms, so use the
                // 'Row Vector * Matrix' operator. The GLSL '*' operator is overloaded to use a row vector
                // when a matrix is left-multiplied by a vector. Refer to this doc for more information:
                // https://en.wikibooks.org/wiki/GLSL_Programming/Vector_and_Matrix_Operations#Operators
                textureCoord = (vec4(gl_MultiTexCoord0.x, gl_MultiTexCoord0.y, 1.0f, 0.0f) * _UnityDisplayTransform).xy;
#endif
#endif // SHADER_API_GLES3
            }
#endif // VERTEX

#ifdef FRAGMENT
            varying ARCORE_TEXCOORD_TYPE textureCoord;
            uniform samplerExternalOES _MainTex;
            uniform float _UnityCameraForwardScale;

#ifdef ARCORE_ENVIRONMENT_DEPTH_ENABLED
            uniform sampler2D _EnvironmentDepth;
#endif // ARCORE_ENVIRONMENT_DEPTH_ENABLED

            uniform vec4 _PassthroughTint;
            uniform vec4 _PassthroughEdgeColor;
            const vec3 PassthroughLuminanceWeights = vec3(0.2126, 0.7152, 0.0722);
            const float PassthroughEdgeSampleSpacingPixels = 1.5;
            const float PassthroughEdgeGain = 4.0;

            float PassthroughLuminanceSrgb(vec3 srgb)
            {
                return sqrt(dot(srgb * srgb, PassthroughLuminanceWeights));
            }

            vec3 ApplyPassthroughStyleSrgb(vec3 srgb, float edgeStrength)
            {
                vec3 tinted = PassthroughLuminanceSrgb(srgb) * _PassthroughTint.rgb;
                srgb = mix(srgb, tinted, _PassthroughTint.a);
                return mix(srgb, _PassthroughEdgeColor.rgb, _PassthroughEdgeColor.a * edgeStrength);
            }

#if defined(SHADER_API_GLES3) && !defined(UNITY_COLORSPACE_GAMMA)
            float GammaToLinearSpaceExact(float value)
            {
                if (value <= 0.04045F)
                    return value / 12.92F;
                else if (value < 1.0F)
                    return pow((value + 0.055F) / 1.055F, 2.4F);
                else
                    return pow(value, 2.2F);
            }

            vec3 GammaToLinearSpace(vec3 sRGB)
            {
                // Approximate version from http://chilliant.blogspot.com.au/2012/08/srgb-approximations-for-hlsl.html?m=1
                return sRGB * (sRGB * (sRGB * 0.305306011F + 0.682171111F) + 0.012522878F);

                // Precise version, useful for debugging, but the pow() function is too slow.
                // return vec3(GammaToLinearSpaceExact(sRGB.r), GammaToLinearSpaceExact(sRGB.g), GammaToLinearSpaceExact(sRGB.b));
            }
#endif // SHADER_API_GLES3 && !UNITY_COLORSPACE_GAMMA

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

            void main()
            {
#ifdef SHADER_API_GLES3
#ifdef ARCORE_IMAGE_STABILIZATION_ENABLED
                vec2 tc = textureCoord.xy / textureCoord.z;
#else
                vec2 tc = textureCoord;
#endif
                vec3 result = texture(_MainTex, tc).xyz;
                float depth = 1.0;

                vec2 edgeStepX = dFdx(tc) * PassthroughEdgeSampleSpacingPixels;
                vec2 edgeStepY = dFdy(tc) * PassthroughEdgeSampleSpacingPixels;
                if (_PassthroughTint.a > 0.0 || _PassthroughEdgeColor.a > 0.0)
                {
                    float edgeStrength = 0.0;
                    if (_PassthroughEdgeColor.a > 0.0)
                    {
                        float luminanceLeft = PassthroughLuminanceSrgb(texture(_MainTex, tc - edgeStepX).xyz);
                        float luminanceRight = PassthroughLuminanceSrgb(texture(_MainTex, tc + edgeStepX).xyz);
                        float luminanceDown = PassthroughLuminanceSrgb(texture(_MainTex, tc - edgeStepY).xyz);
                        float luminanceUp = PassthroughLuminanceSrgb(texture(_MainTex, tc + edgeStepY).xyz);
                        edgeStrength = clamp((abs(luminanceRight - luminanceLeft) + abs(luminanceUp - luminanceDown)) * PassthroughEdgeGain, 0.0, 1.0);
                    }
                    result = ApplyPassthroughStyleSrgb(result, edgeStrength);
                }

#ifdef ARCORE_ENVIRONMENT_DEPTH_ENABLED
                float distance = texture(_EnvironmentDepth, tc).x;
                depth = ConvertDistanceToDepth(distance);
#endif // ARCORE_ENVIRONMENT_DEPTH_ENABLED

#ifndef UNITY_COLORSPACE_GAMMA
                result = GammaToLinearSpace(result);
#endif // !UNITY_COLORSPACE_GAMMA

                gl_FragColor = vec4(result, 1.0);
                gl_FragDepth = depth;
#endif // SHADER_API_GLES3
            }

#endif // FRAGMENT
            ENDGLSL
        }
    }

    /////////////////////////////////////////////////////////////////////////////////////////////
    // HLSL shader for Vulkan. It should be kept the same with GLES3 except for the syntax diff.
    /////////////////////////////////////////////////////////////////////////////////////////////
    SubShader
    {
        Name "ARCore Background (Before Opaques) for Vulkan"
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "ForceNoShadowCasting" = "True"
        }

        Pass
        {
            Name "AR Camera Background (ARCore)"
            Cull Off
            ZTest Always
            ZWrite On
            Lighting Off
            LOD 100
            Tags
            {
                "LightMode" = "Always"
            }

            HLSLPROGRAM

            #pragma only_renderers vulkan

            #pragma multi_compile_local __ ARCORE_ENVIRONMENT_DEPTH_ENABLED
            #pragma multi_compile_local __ ARCORE_IMAGE_STABILIZATION_ENABLED

            #include "UnityCG.cginc"
            #include "PassthroughStyle.hlsl"

            #pragma vertex vert
            #pragma fragment frag

#ifndef ARCORE_IMAGE_STABILIZATION_ENABLED
#define ARCORE_TEXCOORD_TYPE float2
#else // ARCORE_IMAGE_STABILIZATION_ENABLED
#define ARCORE_TEXCOORD_TYPE float3
#endif // !ARCORE_IMAGE_STABILIZATION_ENABLED

            // Device display transform is provided by the AR Foundation camera background renderer.
            float4x4 _UnityDisplayTransform;

            struct vertexInput
            {
                float4 vertex : POSITION;
                float3 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                ARCORE_TEXCOORD_TYPE textureCoord : TEXCOORD0;
            };

            v2f vert(vertexInput i)
            {
                v2f o;

                // Transform the position from object space to clip space.
                o.position = UnityObjectToClipPos(i.vertex.xyz);

#ifdef ARCORE_IMAGE_STABILIZATION_ENABLED
                o.textureCoord = i.uv.xyz;
#else
                // Remap the texture coordinates based on the device rotation.
                // _UnityDisplayTransform is provided as "Row Major" for all mobile platforms, so use the
                // 'Row Vector * Matrix' operator. In HLSL, with mul(x, y) if x is a vector, it treated
                // as a row vector. For more information:
                // https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-mul
                o.textureCoord = mul(float4(i.uv.x, i.uv.y, 1.0f, 0.0f), _UnityDisplayTransform).xy;
#endif
                return o;
            }

            sampler2D _MainTex;
            float _UnityCameraForwardScale;

#ifdef ARCORE_ENVIRONMENT_DEPTH_ENABLED
            sampler2D _EnvironmentDepth;
#endif // ARCORE_ENVIRONMENT_DEPTH_ENABLED

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

            struct fragOutput
            {
                float4 color : SV_Target;
                float depth : SV_Depth;
            };

            fragOutput frag(v2f i)
            {
#ifdef ARCORE_IMAGE_STABILIZATION_ENABLED
                float2 tc = i.textureCoord.xy / i.textureCoord.z;
#else
                float2 tc = i.textureCoord;
#endif
                float3 result = tex2D(_MainTex, tc).xyz;
                float depth = 1.0;

                float2 edgeStepX = ddx(tc) * PassthroughEdgeSampleSpacingPixels;
                float2 edgeStepY = ddy(tc) * PassthroughEdgeSampleSpacingPixels;
                if (IsPassthroughStyled())
                {
                    float edgeStrength = 0.0;
                    if (_PassthroughEdgeColor.a > 0.0)
                    {
                        edgeStrength = PassthroughEdgeStrength(
                            PassthroughLuminanceSrgbOfColorSpaceColor(tex2D(_MainTex, tc - edgeStepX).xyz),
                            PassthroughLuminanceSrgbOfColorSpaceColor(tex2D(_MainTex, tc + edgeStepX).xyz),
                            PassthroughLuminanceSrgbOfColorSpaceColor(tex2D(_MainTex, tc - edgeStepY).xyz),
                            PassthroughLuminanceSrgbOfColorSpaceColor(tex2D(_MainTex, tc + edgeStepY).xyz));
                    }
                    result = ApplyPassthroughStyleColorSpaceColor(result, edgeStrength);
                }

#ifdef ARCORE_ENVIRONMENT_DEPTH_ENABLED
                float distance = tex2D(_EnvironmentDepth, tc).x;
                depth = ConvertDistanceToDepth(distance);
#endif // ARCORE_ENVIRONMENT_DEPTH_ENABLED

                fragOutput o;

                o.color = float4(result, 1.0);
                o.depth = 1.0 - depth; // Unity Vulkan uses reverse Z.

                return o;
            }

            ENDHLSL
        }
    }

    FallBack Off
}
