// Three-layer ground blend for the streamed terrain ribbons.
//
// Blend weights ride in the mesh's vertex colour (R/G/B), written by BuildRibbon from
// slope, noise and distance-from-road. That keeps the control data in geometry we already
// generate - no control texture, no extra sampler - and means each biome only needs its
// three ground albedos.
//
// Built on URP's UniversalFragmentPBR so it lights, shadows and fogs exactly like Lit.
Shader "RoadRage/TerrainSplat"
{
    Properties
    {
        _Splat0 ("Layer 0 (base)", 2D) = "grey" {}
        _Splat1 ("Layer 1 (mid)", 2D) = "grey" {}
        _Splat2 ("Layer 2 (detail)", 2D) = "grey" {}
        _Normal0 ("Normal 0", 2D) = "bump" {}
        _Normal1 ("Normal 1", 2D) = "bump" {}
        _Normal2 ("Normal 2", 2D) = "bump" {}
        _Tile0 ("Tiling 0", Float) = 0.08
        _Tile1 ("Tiling 1", Float) = 0.11
        _Tile2 ("Tiling 2", Float) = 0.16
        _Tint0 ("Tint 0", Color) = (1,1,1,1)
        _Tint1 ("Tint 1", Color) = (1,1,1,1)
        _Tint2 ("Tint 2", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.18
        _NormalScale ("Normal Scale", Range(0,2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Splat0); SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1); SAMPLER(sampler_Splat1);
            TEXTURE2D(_Splat2); SAMPLER(sampler_Splat2);
            TEXTURE2D(_Normal0); SAMPLER(sampler_Normal0);
            TEXTURE2D(_Normal1); SAMPLER(sampler_Normal1);
            TEXTURE2D(_Normal2); SAMPLER(sampler_Normal2);

            CBUFFER_START(UnityPerMaterial)
                float _Tile0, _Tile1, _Tile2;
                float4 _Tint0, _Tint1, _Tint2;
                float _Smoothness, _NormalScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
                float4 color      : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
                float4 shadowCoord: TEXCOORD5;
            };

            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.color = input.color;
                o.fogCoord = ComputeFogFactor(p.positionCS.z);
                o.shadowCoord = GetShadowCoord(p);
                return o;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // Weights come from vertex colour; normalise so lighting energy is stable
                // even if the generator's weights don't sum exactly to one.
                float3 w = max(input.color.rgb, 0.0001);
                w /= (w.r + w.g + w.b);

                // World-space UVs: terrain ribbons have stretched UVs, and planar mapping
                // keeps texel density even across a strip that is 300 m wide.
                float2 uv = input.positionWS.xz;
                float4 a0 = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, uv * _Tile0) * _Tint0;
                float4 a1 = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat1, uv * _Tile1) * _Tint1;
                float4 a2 = SAMPLE_TEXTURE2D(_Splat2, sampler_Splat2, uv * _Tile2) * _Tint2;
                half3 albedo = a0.rgb * w.r + a1.rgb * w.g + a2.rgb * w.b;

                half3 n0 = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal0, sampler_Normal0, uv * _Tile0), _NormalScale);
                half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal1, sampler_Normal1, uv * _Tile1), _NormalScale);
                half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal2, sampler_Normal2, uv * _Tile2), _NormalScale);
                half3 normalTS = normalize(n0 * w.r + n1 * w.g + n2 * w.b);

                float sgn = input.tangentWS.w;
                float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
                half3x3 tbn = half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(mul(normalTS, tbn));
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = input.shadowCoord;
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = 0.0;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = normalTS;
                surfaceData.occlusion = 1.0;
                surfaceData.alpha = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // Shadow, depth and depth-normal passes reuse URP's own implementations.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
