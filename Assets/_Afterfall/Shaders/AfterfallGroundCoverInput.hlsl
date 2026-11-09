#ifndef AFTERFALL_GROUND_COVER_INPUT_INCLUDED
#define AFTERFALL_GROUND_COVER_INPUT_INCLUDED

// Shared by every pass of Afterfall/GroundCover so the deformation (distance fade, wind, the
// player pushing through) is identical in the colour, depth and depth-normals passes.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    half _TintStrength;
    half _GroundGain;
    half _Smoothness;
    half _Translucency;
    half _NormalUp;
    half _Wind;
    float _Bend;
    float _FadeNear;
    float _FadeFar;
    half _Variation;
    half _Cull;
CBUFFER_END

// Set by WorldBuilder (average ground colour across the map) and GroundCover (the player).
TEXTURE2D(_AfterfallGroundTint);
SAMPLER(sampler_AfterfallGroundTint);
float4 _AfterfallGroundTintRect;
float4 _AfterfallGrassPusher;

struct GroundCoverInstance
{
    float3 positionWS;
    float3 pivotWS;
    float rank;
    float rank2;
    float height;
};

float AfterfallHash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Places a vertex: shrinks the instance into its root past its own cut-off distance (each instance
// gets a random one between _FadeNear and _FadeFar, so density thins smoothly with distance and
// nothing pops), then bends it with the wind and away from the player.
GroundCoverInstance AfterfallGroundCoverVertex(float3 positionOS)
{
    GroundCoverInstance o;
    float3 pivot = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    o.pivotWS = pivot;
    o.rank = AfterfallHash12(pivot.xz);
    o.rank2 = AfterfallHash12(pivot.zx + 17.31);
    float dist = distance(pivot, _WorldSpaceCameraPos.xyz);
    float cutoff = lerp(_FadeNear, _FadeFar, sqrt(o.rank));
    float fade = saturate((cutoff - dist) / 4.0);
    float3 positionWS = TransformObjectToWorld(positionOS * fade);
    float height = max(0.0, positionWS.y - pivot.y);
    o.height = height;

    float2 windDir = float2(0.82, 0.57);
    float t = _Time.y;
    float gust = sin(dot(pivot.xz, windDir) * 0.09 - t * 1.25) * 0.5 + 0.5;
    float sway = sin(t * 1.9 + o.rank * 6.2832) * 0.3 + gust * gust * 0.9 + 0.2;
    float flutter = sin(t * 6.7 + o.rank2 * 31.0 + height * 5.0) * 0.07;
    float bend = height * height * _Bend * _Wind;
    float2 offset = windDir * (sway + flutter) * bend;

    float2 away = positionWS.xz - _AfterfallGrassPusher.xz;
    float awayLen = length(away);
    float push = saturate(1.0 - awayLen / max(_AfterfallGrassPusher.w, 1e-3)) * step(abs(pivot.y - _AfterfallGrassPusher.y), 1.6);
    offset += away / max(awayLen, 1e-3) * push * push * height * 0.9;

    positionWS.xz += offset;
    // Bent blades get shorter instead of stretching.
    positionWS.y -= min(height * 0.6, dot(offset, offset) * 0.5 / max(height, 0.05)) + push * height * 0.25;
    o.positionWS = positionWS;
    return o;
}

#endif
