// Mirror glass (DrivingPractice.Mirror.cs, DrivingPractice.SideMirrors.cs): shows the mirror camera's image, unlit,
// with a faint diagonal sheen so it reads as glass, not a screen. No keywords, so builds never strip the variant
// (runtime URP Lit materials with _EMISSION / transparent did get stripped from the web build: grey, blank mirrors).
Shader "BCRoot/Mirror Glass"
{
    Properties
    {
        _MainTex ("Mirror image", 2D) = "black" {}
        _Sheen ("Sheen strength", Range(0, 1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Sheen;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 image = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;
                // A soft light band across the glass, brighter toward the top.
                half band = saturate(1 - abs(input.uv.x + input.uv.y - 1.1) * 3) * _Sheen + input.uv.y * _Sheen * .5;
                return half4(lerp(image, 1, band), 1);
            }
            ENDHLSL
        }
    }
}
