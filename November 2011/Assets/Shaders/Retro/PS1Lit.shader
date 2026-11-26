// PS1-style lit shader for URP.
//  - Vertex snapping: vertices jump to a coarse screen grid -> the classic PS1 "wobble".
//  - Affine texture mapping: textures warp across big polygons like the PS1 (no perspective fix).
//  - Per-vertex (Gouraud) lighting from the main light + ambient, no specular.
//  - Fog, GPU instancing, SRP Batcher compatible.
// Use point-filtered, small textures (Filter Mode: Point, no mipmaps).
Shader "Retro/PS1 Lit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _SnapResolution ("Vertex Snap Grid (width, height)", Vector) = (320, 240, 0, 0)
        _Affine ("Texture Warp (0 = correct, 1 = PS1 affine)", Range(0, 1)) = 1
        _Lighting ("Lighting (0 = unlit, 1 = lit)", Range(0, 1)) = 1
        [Toggle(_VERTEX_COLORS)] _VertexColors ("Use mesh colours (per-triangle colour models)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float4 _SnapResolution;
            half _Affine;
            half _Lighting;
        CBUFFER_END
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // Snap a clip-space position to a coarse screen grid (PS1 had no sub-pixel precision).
        float4 SnapToGrid(float4 positionCS)
        {
            if (positionCS.w <= 0) return positionCS;
            float2 grid = max(_SnapResolution.xy, 1) * 0.5;
            float2 ndc = positionCS.xy / positionCS.w;
            ndc = round(ndc * grid) / grid;
            positionCS.xy = ndc * positionCS.w;
            return positionCS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local _VERTEX_COLORS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;     // per-triangle colours from Blender (sRGB)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;                 // perspective-correct
                noperspective float2 uvAffine : TEXCOORD1;   // PS1-style affine
                half3 light : TEXCOORD2;               // Gouraud lighting
                half fog : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float4 positionCS = SnapToGrid(TransformWorldToHClip(positionWS));
                half3 normalWS = TransformObjectToWorldNormal(v.normalOS);

                Light mainLight = GetMainLight();
                half3 lit = mainLight.color * saturate(dot(normalWS, mainLight.direction)) + SampleSH(normalWS);
                o.light = lerp(half3(1, 1, 1), lit, _Lighting);
                #if defined(_VERTEX_COLORS)
                    o.light *= SRGBToLinear(v.color.rgb);   // Blender exports sRGB colours
                #endif

                float2 uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.uv = uv;
                o.uvAffine = uv;
                o.positionCS = positionCS;
                o.fog = ComputeFogFactor(positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = lerp(i.uv, i.uvAffine, _Affine);
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                col.rgb *= i.light;
                col.rgb = MixFog(col.rgb, i.fog);
                return half4(col.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                // Same snapping as the colour pass so depth matches exactly.
                o.positionCS = SnapToGrid(TransformObjectToHClip(v.positionOS.xyz));
                return o;
            }

            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
    FallBack Off
}
