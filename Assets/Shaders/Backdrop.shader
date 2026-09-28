// Distant scenery (Greenwood's mountain ring) with its own aerial perspective.
//
// The scene fog is tuned for the road: exp2 at 0.002 leaves a ridge 700 m away with
// 14% of its contrast, which is why the old horizon peaks read as grey smudges. This
// shader skips Unity's fog and hazes towards the same fog colour at its own, lighter
// density, so the range stays legible and still melts into the sky at the horizon.
// Lit by the main light and ambient only - at this distance shadows add nothing.
Shader "RoadRage/Backdrop"
{
    Properties
    {
        _BaseMap ("Colour", 2D) = "grey" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Range(0, 3)) = 1.0
        _HazeDensity ("Haze Density (exp2, per metre)", Range(0, 0.005)) = 0.001
        _HazeMax ("Maximum Haze", Range(0, 1)) = 0.92
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BumpMap_ST;
                float _NormalScale;
                float _HazeDensity;
                float _HazeMax;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _NormalScale);
                float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                float3 normalWS = normalize(mul(normalTS, float3x3(input.tangentWS.xyz, bitangent, input.normalWS)));

                Light mainLight = GetMainLight();
                half3 diffuse = mainLight.color * saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 colour = albedo * (diffuse + ambient);

                float distance = length(input.positionWS - GetCameraPositionWS());
                float d = distance * _HazeDensity;
                float haze = min(_HazeMax, 1.0 - exp(-d * d));
                colour = lerp(colour, unity_FogColor.rgb, haze);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // Depth for the camera's depth texture (SSAO, soft particles).
        UsePass "Universal Render Pipeline/Unlit/DepthOnly"
    }
    FallBack Off
}
