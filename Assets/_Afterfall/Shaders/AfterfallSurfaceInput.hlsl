#ifndef AFTERFALL_SURFACE_INPUT_INCLUDED
#define AFTERFALL_SURFACE_INPUT_INCLUDED

// Shared by every pass of Afterfall/Surface so the material constant buffer is identical
// (SRP Batcher) and wind moves the shadow and depth passes exactly like the colour pass.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);
SAMPLER(sampler_BumpMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half4 _EmissionColor;
    half _Smoothness;
    half _Metallic;
    half _BumpScale;
    half _OcclusionStrength;
    half _TriplanarSharpness;
    half _ObjectSpace;
    half _WindStrength;
    half _Cutoff;
CBUFFER_END

// World position of the object's pivot (the base of a tree crown, a grass tuft...).
float3 AfterfallObjectPivotWS()
{
    return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
}

// Gentle sway that grows with height above the pivot. Strength 0 leaves the vertex untouched.
float3 AfterfallApplyWind(float3 positionWS)
{
    float3 result = positionWS;
    UNITY_BRANCH
    if (_WindStrength > 0.0001)
    {
        float3 pivot = AfterfallObjectPivotWS();
        float height = max(0.0, positionWS.y - pivot.y);
        float phase = dot(pivot.xz, float2(0.173, 0.131));
        float t = _Time.y;
        float sway = sin(t * 1.15 + phase) * 0.65 + sin(t * 2.35 + phase * 1.7) * 0.35;
        float flutter = sin(t * 6.3 + dot(positionWS, float3(1.7, 0.9, 1.3))) * 0.18;
        float bend = height * height * 0.012 + height * 0.02;
        result.xz += float2(0.82, 0.57) * (sway + flutter) * bend * _WindStrength;
    }
    return result;
}

// Mapping coordinates: world space for static scenery, scaled object space for anything that moves.
void AfterfallMappingSpace(float3 positionOS, float3 normalOS, float3 positionWS, float3 normalWS, out float3 mapPos, out half3 mapNormal)
{
    UNITY_BRANCH
    if (_ObjectSpace > 0.5)
    {
        float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
        mapPos = positionOS * scale;
        mapNormal = normalize(normalOS / max(scale, 1e-4));
    }
    else
    {
        mapPos = positionWS;
        mapNormal = normalWS;
    }
}

half3 AfterfallDecodeNormal(half4 packed, half strength)
{
    half2 xy = (packed.rg * 2.0h - 1.0h) * strength;
    return half3(xy, sqrt(saturate(1.0h - dot(xy, xy))));
}

// Triplanar sampling with whiteout-blended normals (sign-corrected for back faces).
// Returns albedo (rgb, a = smoothness multiplier), the normal in mapping space and the cavity term.
void AfterfallSampleTriplanar(float3 pos, half3 nrm, out half4 albedo, out half3 normalOut, out half cavity)
{
    half3 blend = pow(abs(nrm), _TriplanarSharpness);
    blend /= max(dot(blend, half3(1.0h, 1.0h, 1.0h)), 1e-4h);

    float2 uvX = pos.zy * _BaseMap_ST.xy + _BaseMap_ST.zw;
    float2 uvY = pos.xz * _BaseMap_ST.xy + _BaseMap_ST.zw;
    float2 uvZ = pos.xy * _BaseMap_ST.xy + _BaseMap_ST.zw;
    half3 axisSign = half3(nrm.x < 0 ? -1.0h : 1.0h, nrm.y < 0 ? -1.0h : 1.0h, nrm.z < 0 ? -1.0h : 1.0h);
    uvX.x *= axisSign.x;
    uvY.x *= axisSign.y;
    uvZ.x *= -axisSign.z;

    half4 aX = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX);
    half4 aY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY);
    half4 aZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ);
    albedo = aX * blend.x + aY * blend.y + aZ * blend.z;

    half4 bX = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX);
    half4 bY = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY);
    half4 bZ = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ);
    half3 tX = AfterfallDecodeNormal(bX, _BumpScale);
    half3 tY = AfterfallDecodeNormal(bY, _BumpScale);
    half3 tZ = AfterfallDecodeNormal(bZ, _BumpScale);
    tX.x *= axisSign.x;
    tY.x *= axisSign.y;
    tZ.x *= -axisSign.z;
    tX = half3(tX.xy + nrm.zy, abs(tX.z) * nrm.x);
    tY = half3(tY.xy + nrm.xz, abs(tY.z) * nrm.y);
    tZ = half3(tZ.xy + nrm.xy, abs(tZ.z) * nrm.z);
    normalOut = normalize(tX.zyx * blend.x + tY.xzy * blend.y + tZ.xyz * blend.z);
    cavity = bX.b * blend.x + bY.b * blend.y + bZ.b * blend.z;
}

#endif
