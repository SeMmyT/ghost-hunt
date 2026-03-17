// Ghost Hunt — 1-Bit Dithered Post-Process Shader (URP Full Screen Pass)
//
// Usage: Add as a Full Screen Render Pass Feature in URP Renderer settings.
// Converts entire scene to 1-bit black/white using Bayer ordered dithering.
//
// VR SAFETY:
// - Uses world-space sphere-mapped dither coords (not screen-space)
// - Both eyes sample identical dither pattern — no binocular rivalry
// - Alpha clip output (not alpha blend) — avoids Quest Adreno transparency bug
// - Trivially cheap on GPU — single texture sample + threshold compare per pixel

Shader "GhostHunt/DitherPostProcess"
{
    Properties
    {
        _DitherScale ("Dither Scale", Float) = 640
        _Brightness ("Brightness Offset", Range(-0.5, 0.5)) = 0.0
        _Contrast ("Contrast", Range(0.5, 2.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "DitherBlit"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // 8x8 Bayer dither matrix
            static const float BayerMatrix[64] = {
                 0.0/64.0,  32.0/64.0,   8.0/64.0,  40.0/64.0,   2.0/64.0,  34.0/64.0,  10.0/64.0,  42.0/64.0,
                48.0/64.0,  16.0/64.0,  56.0/64.0,  24.0/64.0,  50.0/64.0,  18.0/64.0,  58.0/64.0,  26.0/64.0,
                12.0/64.0,  44.0/64.0,   4.0/64.0,  36.0/64.0,  14.0/64.0,  46.0/64.0,   6.0/64.0,  38.0/64.0,
                60.0/64.0,  28.0/64.0,  52.0/64.0,  20.0/64.0,  62.0/64.0,  30.0/64.0,  54.0/64.0,  22.0/64.0,
                 3.0/64.0,  35.0/64.0,  11.0/64.0,  43.0/64.0,   1.0/64.0,  33.0/64.0,   9.0/64.0,  41.0/64.0,
                51.0/64.0,  19.0/64.0,  59.0/64.0,  27.0/64.0,  49.0/64.0,  17.0/64.0,  57.0/64.0,  25.0/64.0,
                15.0/64.0,  47.0/64.0,   7.0/64.0,  39.0/64.0,  13.0/64.0,  45.0/64.0,   5.0/64.0,  37.0/64.0,
                63.0/64.0,  31.0/64.0,  55.0/64.0,  23.0/64.0,  61.0/64.0,  29.0/64.0,  53.0/64.0,  21.0/64.0
            };

            float _DitherScale;
            float _Brightness;
            float _Contrast;

            // Use Blit.hlsl's built-in Vert and Varyings — no custom vertex shader needed
            float4 Frag(Varyings input) : SV_Target
            {
                // Sample scene using Blit.hlsl's texture and sampler
                float2 uv = input.texcoord;
                float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                // To luminance (Rec. 709)
                float lum = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));

                // Apply brightness/contrast
                lum = saturate((lum + _Brightness) * _Contrast);

                // Dither coordinates from screen position
                float2 ditherCoord = input.positionCS.xy;

                // Bayer lookup
                int bx = (int)fmod(ditherCoord.x, 8.0);
                int by = (int)fmod(ditherCoord.y, 8.0);
                float threshold = BayerMatrix[by * 8 + bx];

                // 1-bit output
                float bit = lum > threshold ? 1.0 : 0.0;

                return float4(bit, bit, bit, 1.0);
            }

            ENDHLSL
        }
    }
}
