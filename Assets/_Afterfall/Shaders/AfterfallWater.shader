// AFTERFALL lake water (URP). Reads the camera depth and opaque textures to refract and absorb
// light through the water column (murky green-brown with depth), reflects a sky gradient built
// from the fog and ambient colours (with a dark tree-line band at the horizon), adds a sun glint,
// animates three scrolling layers of a procedural ripple normal map and fades softly into the
// shore with a faint foam line. WorldBuilder assigns the ripple map at runtime.
Shader "Afterfall/Water"
{
    Properties
    {
        _ShallowTint("Shallow tint", Color) = (0.86, 0.9, 0.8, 1)
        _DeepColor("Deep (scattering) colour", Color) = (0.03, 0.055, 0.045, 1)
        _Absorption("Absorption per metre (RGB)", Vector) = (0.85, 0.42, 0.5, 0)
        _RippleMap("Ripple normals (RGB) and height (A)", 2D) = "bump" {}
        _RippleTile("Ripple tiling (1/m)", Float) = 0.1667
        _RippleStrength("Ripple strength", Range(0, 2)) = 0.55
        _RippleSpeed("Ripple speed", Float) = 1
        _Refraction("Refraction", Range(0, 0.2)) = 0.035
        _HorizonBand("Tree-line reflection", Color) = (0.16, 0.19, 0.15, 1)
        _SunGlint("Sun glint", Range(0, 20)) = 6
        _ShoreFade("Shore fade depth (m)", Float) = 0.22
        _FoamColor("Shore foam", Color) = (0.72, 0.72, 0.66, 1)
        _FoamWidth("Foam width (m)", Float) = 0.14
        _Smoothness("Unused smoothness", Range(0, 1)) = 0.92
        _BaseColor("Unused tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_RippleMap);
            SAMPLER(sampler_RippleMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowTint;
                half4 _DeepColor;
                float4 _Absorption;
                float4 _RippleMap_ST;
                float _RippleTile;
                half _RippleStrength;
                float _RippleSpeed;
                half _Refraction;
                half4 _HorizonBand;
                half _SunGlint;
                float _ShoreFade;
                half4 _FoamColor;
                float _FoamWidth;
                half _Smoothness;
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings WaterVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Ripple(float2 uv)
            {
                half4 s = SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, uv);
                return half4(s.rgb * 2.0h - 1.0h, s.a);
            }

            half4 WaterFragment(Varyings input) : SV_Target
            {
                float3 posWS = input.positionWS;
                float3 camWS = GetCameraPositionWS();
                float3 toSurface = posWS - camWS;
                float viewDist = length(toSurface);
                half3 V = (half3)(-toSurface / max(viewDist, 1e-4));

                // Three ripple layers drifting in different directions; flatter far away (less aliasing).
                float t = _Time.y * _RippleSpeed;
                float2 uv = posWS.xz * _RippleTile;
                half4 r1 = Ripple(uv + float2(0.021, 0.013) * t);
                half4 r2 = Ripple(uv * 0.41 + float2(-0.009, 0.016) * t);
                half4 r3 = Ripple(uv * 2.3 + float2(0.031, -0.024) * t);
                half2 slope = (r1.xy + r2.xy * 1.2h + r3.xy * 0.45h) * _RippleStrength;
                slope *= lerp(0.2h, 1.0h, saturate(1.0h - viewDist / 180.0h));
                half3 N = normalize(half3(slope.x, 1.0h, slope.y));

                // Depth of the scene behind the water, and how far the view ray travels through it.
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float surfaceEye = -TransformWorldToView(posWS).z;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float3 scenePos = camWS + toSurface * (sceneEye / max(surfaceEye, 1e-4));
                float depthBelow = max(0.0, posWS.y - scenePos.y);

                float2 refrUV = screenUV + slope * _Refraction * saturate(depthBelow * 1.5);
                float refrEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
                if (refrEye < surfaceEye)
                {
                    refrUV = screenUV;
                    refrEye = sceneEye;
                }
                float path = min(viewDist * max(0.0, refrEye / max(surfaceEye, 1e-4) - 1.0), 60.0);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(posWS));
                half shadow = mainLight.shadowAttenuation;
                half3 ambient = SampleSH(half3(0.0h, 1.0h, 0.0h));
                half3 sunLight = mainLight.color * saturate(mainLight.direction.y) * shadow;

                // Light reaching the eye through the water: the refracted bed, absorbed with distance,
                // replaced by murky in-scattered light.
                half3 transmit = (half3)exp(-_Absorption.rgb * path);
                half3 bed = SampleSceneColor(refrUV) * _ShallowTint.rgb;
                half3 scatter = _DeepColor.rgb * (ambient * 1.2h + sunLight * 0.6h);
                half3 underwater = bed * transmit + scatter * (1.0h - transmit);

                // Reflection: horizon (fog colour) to zenith (ambient sky), with the dark tree line.
                half3 R = reflect(-V, N);
                half3 horizon = unity_FogColor.rgb;
                half3 zenith = ambient * 0.9h;
                half3 sky = lerp(horizon, zenith, saturate(R.y * 1.6h));
                half3 treeLine = _HorizonBand.rgb * (ambient + sunLight * 0.35h);
                sky = lerp(treeLine, sky, smoothstep(0.02h, 0.16h, R.y));
                half fresnel = 0.02h + 0.98h * pow(1.0h - saturate(dot(N, V)), 5.0h);

                half3 H = normalize(mainLight.direction + V);
                half nh = saturate(dot(N, H));
                half3 glint = mainLight.color * shadow * (pow(nh, 900.0h) * _SunGlint + pow(nh, 90.0h) * 0.12h);

                half3 color = lerp(underwater, sky, fresnel) + glint;

                // Soft shoreline with a faint, broken foam line.
                half foam = (1.0h - smoothstep(0.0h, _FoamWidth, depthBelow)) * smoothstep(0.45h, 0.65h, r1.a * 0.6h + r3.a * 0.4h);
                color = lerp(color, _FoamColor.rgb * (ambient + sunLight), foam * 0.35h);
                half alpha = saturate(depthBelow / max(_ShoreFade, 1e-3));

                color = MixFog(color, InitializeInputDataFog(float4(posWS, 1.0), input.fogFactor));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
