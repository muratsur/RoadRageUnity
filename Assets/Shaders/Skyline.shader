// The real far mountains (RoadRage.Skyline): silhouette strips coloured per vertex.
//
// Vertex rgb is the ground's own colour, alpha how much of it the air between has
// hidden (baked from the real distance, 1 to 25 km). Like Backdrop.shader this skips
// Unity's fog, which is tuned for the road and would erase anything past 1 km, and
// hazes towards the same fog colour instead; _HazeBoost thickens it in bad weather.
Shader "RoadRage/Skyline"
{
    Properties
    {
        _HazeBoost ("Haze Boost", Range(1, 6)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _HazeBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Lit from above and by a share of the sun, so night and dusk darken it too.
                half3 light = SampleSH(half3(0, 1, 0)) + GetMainLight().color * 0.5;
                half3 colour = input.color.rgb * light;
                half haze = 1.0 - pow(saturate(1.0 - input.color.a), _HazeBoost);
                return half4(lerp(colour, unity_FogColor.rgb, haze), 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Unlit/DepthOnly"
    }
    FallBack Off
}
