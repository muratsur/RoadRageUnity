// Skybox from a baked upper-hemisphere panorama (Tools/Blender/build_sky.py).
//
// The image covers elevations 0 to 90 degrees only, 4:1, because below the horizon the
// game shows terrain and fog. Near and below the horizon the sky blends into
// _HorizonColor, which the game sets to the scene's fog colour, so fogged mountains and
// ground meet the sky without a visible edge.
Shader "RoadRage/SkyPanorama"
{
    Properties
    {
        _MainTex ("Panorama (upper hemisphere, 4:1)", 2D) = "grey" {}
        _Exposure ("Exposure", Range(0, 4)) = 1.0
        _Rotation ("Rotation (degrees)", Range(0, 360)) = 0
        _HorizonColor ("Horizon Colour", Color) = (0.6, 0.62, 0.65, 1)
        _HorizonBlend ("Horizon Blend Height", Range(0.001, 0.5)) = 0.08
        _HorizonAmount ("Horizon Blend Amount", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Exposure;
                float _Rotation;
                float4 _HorizonColor;
                float _HorizonBlend;
                float _HorizonAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.direction = input.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float longitude = atan2(dir.x, dir.z) + radians(_Rotation);
                float elevation = asin(saturate(dir.y));
                float2 uv = float2(frac(longitude / (2.0 * PI) + 0.5), elevation / (0.5 * PI));
                // Explicit gradients from the unwrapped longitude: the frac() seam at the
                // back of the sky would otherwise pick the smallest mip in a one-pixel
                // column and draw a visible line.
                float2 duvdx = float2(ddx(longitude) / (2.0 * PI), ddx(elevation) / (0.5 * PI));
                float2 duvdy = float2(ddy(longitude) / (2.0 * PI), ddy(elevation) / (0.5 * PI));
                duvdx.x = abs(duvdx.x) > 0.5 ? 0.0 : duvdx.x;
                duvdy.x = abs(duvdy.x) > 0.5 ? 0.0 : duvdy.x;
                half3 sky = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv, duvdx, duvdy).rgb * _Exposure;

                // Fade into the fog colour towards and below the horizon.
                float toHorizon = 1.0 - saturate(dir.y / _HorizonBlend);
                toHorizon = toHorizon * toHorizon * (3.0 - 2.0 * toHorizon);
                sky = lerp(sky, _HorizonColor.rgb, toHorizon * _HorizonAmount);
                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
