// AFTERFALL ground cover (grass tufts, wildflowers, ferns, reeds, pebbles, leaf litter), drawn
// with GPU instancing by GroundCover. Colour comes from the vertex colours (0.5 = neutral, alpha =
// ambient occlusion) times the material colour, blended by uv0.x toward the average colour of the
// ground the instance grows from, so tufts match the terrain beneath them. Instances thin out with
// distance one by one, sway in rolling gusts and bend away from the player. Normals are bent
// toward the sky so the cover is lit like the ground, plus a little light through the blades.
Shader "Afterfall/GroundCover"
{
    Properties
    {
        _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _TintStrength("Ground tint strength", Range(0, 1)) = 1
        _GroundGain("Ground tint gain", Range(0, 3)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.15
        _Translucency("Translucency", Range(0, 2)) = 0.5
        _NormalUp("Bend normals up", Range(0, 1)) = 0.6
        _Wind("Wind", Range(0, 3)) = 1
        _Bend("Bend per square metre of height", Float) = 1
        _FadeNear("Thinning starts (m)", Float) = 16
        _FadeFar("Gone by (m)", Float) = 50
        _Variation("Per-instance colour variation", Range(0, 1)) = 0.25
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CoverVertex
            #pragma fragment CoverFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "AfterfallGroundCoverInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 albedo : TEXCOORD2;
                half3 extra : TEXCOORD3; // x: translucency weight, y: fog factor
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings CoverVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                GroundCoverInstance inst = AfterfallGroundCoverVertex(input.positionOS.xyz);
                output.positionWS = inst.positionWS;
                output.positionCS = TransformWorldToHClip(inst.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                float2 tintUV = inst.pivotWS.xz * _AfterfallGroundTintRect.xy + _AfterfallGroundTintRect.zw;
                half3 ground = SAMPLE_TEXTURE2D_LOD(_AfterfallGroundTint, sampler_AfterfallGroundTint, tintUV, 0).rgb;
                half3 base = lerp(_BaseColor.rgb, ground * _GroundGain, _TintStrength * saturate(input.uv.x));
                // Per-instance variation: brightness and a shift toward yellow for some.
                half v = (half)(inst.rank2 - 0.5) * _Variation;
                base *= 1.0h + v;
                base = lerp(base, base * half3(1.12h, 1.04h, 0.78h), saturate((half)inst.rank * 2.0h - 1.2h) * _Variation * 2.0h);
                output.albedo = half4(base * input.color.rgb * 2.0h, input.color.a);
                output.extra = half3(saturate(inst.height * 3.0h), ComputeFogFactor(output.positionCS.z), 0.0h);
                return output;
            }

            void CoverFragment(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 n = normalize(input.normalWS);
                n = IS_FRONT_VFACE(frontFace, true, false) ? n : -n;
                n = normalize(lerp(n, half3(0.0h, 1.0h, 0.0h), _NormalUp));

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = input.albedo.rgb;
                surfaceData.alpha = 1.0h;
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = _Smoothness;
                surfaceData.specular = half3(0.0h, 0.0h, 0.0h);
                surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
                surfaceData.occlusion = input.albedo.a;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.extra.y);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.bakedGI = SampleSH(n);
                inputData.shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // Sunlight shining through thin blades when looking toward the sun.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half back = pow(saturate(dot(inputData.viewDirectionWS, -mainLight.direction)), 3.0h);
                color.rgb += mainLight.color * mainLight.shadowAttenuation * back * _Translucency * input.extra.x * input.albedo.rgb;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                outColor = color;
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
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
            #pragma target 3.5
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            #include "AfterfallGroundCoverInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformWorldToHClip(AfterfallGroundCoverVertex(input.positionOS.xyz).positionWS);
                return output;
            }

            half DepthFragment(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "AfterfallGroundCoverInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformWorldToHClip(AfterfallGroundCoverVertex(input.positionOS.xyz).positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            void DepthNormalsFragment(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                n = IS_FRONT_VFACE(frontFace, true, false) ? n : -n;
                n = normalize(lerp(n, half3(0.0h, 1.0h, 0.0h), _NormalUp));
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(n);
                float2 remapped = saturate(octNormalWS * 0.5 + 0.5);
                outNormalWS = half4(PackFloat2To888(remapped), 0.0);
            #else
                outNormalWS = half4(n, 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
