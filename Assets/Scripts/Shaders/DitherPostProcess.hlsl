// Ghost Hunt — Surface-Stable Dithered 1-Bit Post-Process
//
// Based on Lucas Pope's Obra Dinn sphere-mapping technique and
// Rune Skovbo Johansen's Surface-Stable Fractal Dithering (Dither3D).
//
// Key VR requirements:
// - World-space dithering (NOT screen-space) to prevent stereo eye divergence
// - Alpha clip (NOT alpha blend) to avoid Quest Adreno transparency bug
// - Must hold locked 72Hz+ with zero ASW reliance
//
// This is the full-screen URP Blit pass version.
// For per-material dithering, see DitherSurface.hlsl.

#ifndef GHOST_HUNT_DITHER_POST_PROCESS
#define GHOST_HUNT_DITHER_POST_PROCESS

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// 8x8 Bayer matrix (normalized 0-1)
static const float BayerMatrix8x8[64] = {
     0.0/64.0,  32.0/64.0,   8.0/64.0,  40.0/64.0,   2.0/64.0,  34.0/64.0,  10.0/64.0,  42.0/64.0,
    48.0/64.0,  16.0/64.0,  56.0/64.0,  24.0/64.0,  50.0/64.0,  18.0/64.0,  58.0/64.0,  26.0/64.0,
    12.0/64.0,  44.0/64.0,   4.0/64.0,  36.0/64.0,  14.0/64.0,  46.0/64.0,   6.0/64.0,  38.0/64.0,
    60.0/64.0,  28.0/64.0,  52.0/64.0,  20.0/64.0,  62.0/64.0,  30.0/64.0,  54.0/64.0,  22.0/64.0,
     3.0/64.0,  35.0/64.0,  11.0/64.0,  43.0/64.0,   1.0/64.0,  33.0/64.0,   9.0/64.0,  41.0/64.0,
    51.0/64.0,  19.0/64.0,  59.0/64.0,  27.0/64.0,  49.0/64.0,  17.0/64.0,  57.0/64.0,  25.0/64.0,
    15.0/64.0,  47.0/64.0,   7.0/64.0,  39.0/64.0,  13.0/64.0,  45.0/64.0,   5.0/64.0,  37.0/64.0,
    63.0/64.0,  31.0/64.0,  55.0/64.0,  23.0/64.0,  61.0/64.0,  29.0/64.0,  53.0/64.0,  21.0/64.0
};

float GetBayerValue(float2 pixelCoord)
{
    int x = (int)fmod(pixelCoord.x, 8.0);
    int y = (int)fmod(pixelCoord.y, 8.0);
    return BayerMatrix8x8[y * 8 + x];
}

// Sphere-mapped dither coordinates (Pope/Tea for God technique)
// Maps dither pattern to a sphere centered on player's head position.
// Eliminates stereo eye divergence in VR by making both eyes sample
// the same world-space dither pattern.
float2 GetSphereStableDitherCoord(float3 worldPos, float3 headPos, float ditherScale)
{
    float3 dir = normalize(worldPos - headPos);

    // Spherical projection — atan2/asin maps direction to 2D
    float u = atan2(dir.x, dir.z) / (2.0 * 3.14159265);
    float v = asin(dir.y) / 3.14159265 + 0.5;

    return float2(u, v) * ditherScale;
}

// Main dither function: luminance -> 1-bit via Bayer threshold
float Dither1Bit(float luminance, float2 ditherCoord)
{
    float threshold = GetBayerValue(ditherCoord);
    return luminance > threshold ? 1.0 : 0.0;
}

// Full-screen post-process: convert scene color to 1-bit dithered output
float4 DitherPostProcessFragment(float2 uv, float2 screenPos, float3 headPos, float ditherScale)
{
    // Sample scene color
    float4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

    // Convert to luminance (Rec. 709)
    float luminance = dot(sceneColor.rgb, float3(0.2126, 0.7152, 0.0722));

    // Use screen position scaled by dither density
    // For VR: replace screenPos with sphere-stable coords in the vert shader
    float2 ditherCoord = screenPos * ditherScale;

    // 1-bit threshold
    float dithered = Dither1Bit(luminance, ditherCoord);

    // Output: black or white, fully opaque (alpha clip, not blend)
    return float4(dithered, dithered, dithered, 1.0);
}

#endif // GHOST_HUNT_DITHER_POST_PROCESS
