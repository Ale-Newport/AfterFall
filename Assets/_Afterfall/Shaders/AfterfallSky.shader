// AFTERFALL sky: an analytic day/night skybox driven every frame by DayNightCycle (SkyRig).
// Gradient from the horizon (exactly the fog colour, so distant terrain melts into the sky) to the
// zenith, a sun halo and warm twilight band, a sun disc, a moon with maria, twinkling stars and a
// drifting procedural cloud layer. All colours arrive in linear space through SetVector.
Shader "Afterfall/Sky"
{
    Properties
    {
        _ZenithColor("Zenith (linear)", Vector) = (0.06, 0.2, 0.5, 1)
        _HorizonColor("Horizon = fog colour (linear)", Vector) = (0.45, 0.55, 0.62, 1)
        _HorizonGlowColor("Twilight band towards the sun (linear)", Vector) = (0, 0, 0, 1)
        _SunHaloColor("Sun halo (linear)", Vector) = (0.6, 0.5, 0.4, 1)
        _SunDiscColor("Sun disc (linear HDR)", Vector) = (20, 18, 15, 1)
        _SunDirection("Direction towards the sun", Vector) = (0.5, 0.7, 0.3, 0)
        _SunSize("Sun disc (cos outer, cos inner)", Vector) = (0.99955, 0.9998, 0, 0)
        _MoonColor("Moon disc (linear HDR)", Vector) = (1.2, 1.25, 1.4, 1)
        _MoonDirection("Direction towards the moon", Vector) = (-0.5, -0.7, -0.3, 0)
        _MoonSize("Moon disc (cos outer, cos inner, angular radius)", Vector) = (0.99965, 0.99975, 0.0265, 0)
        _StarIntensity("Stars", Range(0, 4)) = 0
        _CloudCover("Cloud cover", Range(0, 1)) = 0.42
        _CloudLitColor("Cloud lit side (linear)", Vector) = (1, 1, 1, 1)
        _CloudShadeColor("Cloud shaded side (linear)", Vector) = (0.4, 0.45, 0.5, 1)
        _CloudDrift("Cloud drift (uv per second)", Vector) = (0.0035, 0.0012, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Sky"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SkyVertex
            #pragma fragment SkyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _HorizonGlowColor;
                float4 _SunHaloColor;
                float4 _SunDiscColor;
                float4 _SunDirection;
                float4 _SunSize;
                float4 _MoonColor;
                float4 _MoonDirection;
                float4 _MoonSize;
                float _StarIntensity;
                float _CloudCover;
                float4 _CloudLitColor;
                float4 _CloudShadeColor;
                float4 _CloudDrift;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings SkyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz, false);
                return output;
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = p - i;
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash12(i);
                float b = Hash12(i + float2(1.0, 0.0));
                float c = Hash12(i + float2(0.0, 1.0));
                float d = Hash12(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Five octaves, rotated between octaves so the value-noise grid never lines up.
            float Fbm(float2 p)
            {
                const float2x2 rot = float2x2(1.6, 1.2, -1.2, 1.6);
                float sum = 0.0;
                float amp = 0.5;
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    sum += amp * ValueNoise(p);
                    p = mul(rot, p);
                    amp *= 0.5;
                }
                return sum / 0.97;
            }

            float3 Stars(float3 d, float up)
            {
                // One candidate star per cell of a 3D grid around the sphere, pulled onto the sphere so
                // every chosen star is visible; fixed in world space.
                const float scale = 120.0;
                float3 p = d * scale;
                float3 cell = floor(p);
                float pick = Hash13(cell);
                if (pick < 0.972) return float3(0.0, 0.0, 0.0);
                float3 centre = cell + float3(Hash13(cell + 17.1), Hash13(cell + 31.7), Hash13(cell + 47.3)) * 0.5 + 0.25;
                float mag = Hash13(cell + 5.3);
                float radius = lerp(0.1, 0.2, mag);
                float core = saturate(1.0 - length(p - normalize(centre) * scale) / radius);
                float twinkle = 0.75 + 0.25 * sin(_Time.y * (1.3 + 3.5 * pick) + mag * 40.0);
                float3 tint = lerp(float3(0.75, 0.85, 1.0), float3(1.0, 0.88, 0.72), Hash13(cell + 9.9));
                return tint * (core * core * (0.5 + 3.5 * mag * mag * mag) * twinkle * smoothstep(0.0, 0.2, up));
            }

            float3 Moon(float3 d, float3 moonDir)
            {
                float cm = dot(d, moonDir);
                float3 glow = _MoonColor.rgb * (pow(saturate(cm), 900.0) * 0.06 + pow(saturate(cm), 60.0) * 0.012);
                if (cm < _MoonSize.x) return glow;
                float3 refAxis = abs(moonDir.y) > 0.95 ? float3(1.0, 0.0, 0.0) : float3(0.0, 1.0, 0.0);
                float3 right = normalize(cross(refAxis, moonDir));
                float3 upAxis = cross(moonDir, right);
                float2 uv = float2(dot(d, right), dot(d, upAxis)) / _MoonSize.z;
                float maria = Fbm(uv * 2.2 + 3.7);
                float limb = sqrt(saturate(1.0 - dot(uv, uv)));
                float shade = (0.72 + 0.28 * limb) * (1.08 - 0.5 * smoothstep(0.45, 0.7, maria));
                return glow + _MoonColor.rgb * shade * smoothstep(_MoonSize.x, _MoonSize.y, cm);
            }

            half4 SkyFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 d = normalize(input.directionWS);
                float3 sunDir = normalize(_SunDirection.xyz);
                float3 moonDir = normalize(_MoonDirection.xyz);
                float up = d.y;
                float above = smoothstep(-0.03, 0.02, up);

                // Gradient: a hazy band at the horizon that deepens quickly towards the zenith.
                float h = saturate(up);
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, (1.0 - exp(-h * 4.0)) / 0.9817);

                // Twilight band hugging the horizon, strongest towards the sun's azimuth (both sides
                // of the horizon line, so the sky below distant terrain edges stays seamless).
                float2 dh = d.xz / max(length(d.xz), 1e-4);
                float2 sh = sunDir.xz / max(length(sunDir.xz), 1e-4);
                float az = dot(dh, sh) * 0.5 + 0.5;
                col += _HorizonGlowColor.rgb * exp(-abs(up) * 6.0) * (az * az * az * 0.85 + 0.15);

                // Sun halo (forward scattering).
                float cs = dot(d, sunDir);
                float halo = pow(saturate(cs), 6.0) * 0.22 + pow(saturate(cs), 48.0) * 0.5 + pow(saturate(cs), 900.0) * 1.6;
                col += _SunHaloColor.rgb * halo * above;

                // Night: stars and the moon (hidden by clouds below).
                col += Stars(d, up) * _StarIntensity;
                col += Moon(d, moonDir) * above;

                // Cloud layer projected on a flattened dome, gathered into banks and clear patches by
                // a slow coverage noise. Thick cores are darker underneath; edges facing the sun catch light.
                float2 cuv = d.xz / (max(up, 0.0) + 0.09) * 1.2 + _Time.y * _CloudDrift.xy;
                cuv += (float2(ValueNoise(cuv * 0.7 + 11.3), ValueNoise(cuv * 0.7 + 47.1)) - 0.5) * 0.6;
                float coverage = 1.0 - _CloudCover - (ValueNoise(cuv * 0.22 + 5.0) - 0.5) * 0.3;
                float n = Fbm(cuv);
                float nSun = Fbm(cuv + sunDir.xz * 0.22);
                float density = smoothstep(coverage, coverage + 0.17, n);
                float thick = smoothstep(coverage + 0.08, coverage + 0.32, n);
                float lit = saturate(0.6 + (n - nSun) * 3.5) * (1.0 - thick * 0.4);
                float3 cloud = lerp(_CloudShadeColor.rgb, _CloudLitColor.rgb, lit);
                cloud += _SunHaloColor.rgb * pow(saturate(cs), 5.0) * (1.0 - thick) * 0.9;
                float cloudAlpha = density * smoothstep(0.0, 0.22, up) * 0.93;
                col = lerp(col, cloud, cloudAlpha);

                // Sun disc, dimmed behind clouds and cut at the horizon.
                float disc = smoothstep(_SunSize.x, _SunSize.y, cs) * smoothstep(-0.005, 0.01, up);
                col += _SunDiscColor.rgb * disc * (1.0 - cloudAlpha * 0.85);

                return half4(max(col, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
