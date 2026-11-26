// PS1-style full-screen effect for URP's Full Screen Pass Renderer Feature.
// Reduces colour depth (PS1 = 15-bit colour, 32 levels per channel) with the PS1's 4x4 ordered
// dither. Runs at the camera's (low) render scale, so the dither pattern is chunky like the real thing.
Shader "Retro/PS1 Screen"
{
    Properties
    {
        _ColorLevels ("Colour levels per channel (PS1 = 32)", Range(2, 256)) = 32
        _DitherStrength ("Dither strength", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "PS1Screen"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _ColorLevels;
            float _DitherStrength;

            // The PS1 GPU's 4x4 dither matrix (values -4..3).
            static const float kDither[16] = { -4, 0, -3, 1,
                                                2, -2, 3, -1,
                                               -3, 1, -4, 0,
                                                3, -1, 2, -2 };

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
                float3 c = LinearToSRGB(saturate(col.rgb));            // quantize in display space
                uint2 p = uint2(input.positionCS.xy) & 3;
                float d = kDither[p.y * 4 + p.x] / 8.0;                  // -0.5 .. 0.375
                float steps = max(_ColorLevels - 1, 1);
                c = floor(c * steps + 0.5 + d * _DitherStrength) / steps;
                return half4(SRGBToLinear(saturate(c)), col.a);
            }
            ENDHLSL
        }
    }
}
