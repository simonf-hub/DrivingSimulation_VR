// Stylized sky (DrivingPractice.Sky.cs): a smooth gradient from the horizon colour up to the zenith colour,
// with a soft glow and small disc around the sun. Below the horizon it stays the horizon colour (the mountains
// and ground cover it). Cheap enough for phones: one lerp and one pow per pixel.
Shader "BCRoot/Sky Gradient"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.33, 0.55, 0.86, 1)
        _Horizon ("Horizon", Color) = (0.79, 0.87, 0.93, 1)
        _SunColor ("Sun glow", Color) = (1, 0.95, 0.85, 1)
        _SunDirection ("Direction to the sun", Vector) = (0, 0.6, 0.8, 0)
        _Exponent ("Gradient curve", Float) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Horizon, _SunColor;
                float4 _SunDirection;
                float _Exponent;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                float up = saturate(dir.y);
                half3 sky = lerp(_Horizon.rgb, _Zenith.rgb, pow(up, _Exponent));
                float toSun = saturate(dot(dir, normalize(_SunDirection.xyz)));
                sky += _SunColor.rgb * (pow(toSun, 24.0) * 0.35 + smoothstep(0.9993, 0.9996, toSun)); // glow + disc
                return half4(sky, 1);
            }
            ENDHLSL
        }
    }
}
