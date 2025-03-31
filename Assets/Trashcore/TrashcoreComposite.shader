Shader "TrashcoreComposite"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            Name "TrashcoreCompositePass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionHCS   : POSITION;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4  positionCS  : SV_POSITION;
                float2  uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // mesh is in clip space so position passes through
                output.positionCS = float4(input.positionHCS.xyz, 1.0);

                #if UNITY_UV_STARTS_AT_TOP
                output.positionCS.y *= -1;
                #endif

                output.uv = input.uv;
                return output;
            }

            TEXTURE2D_X(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);
			TEXTURE2D_X(_ComputeOutput);
			SAMPLER(sampler_ComputeOutput);

            float _BlendWithOriginal;
            float _ComputeUScale;
            float _ComputeVScale;

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float u_scale = (_ComputeUScale != 0.0) ? _ComputeUScale : 1.0;
                float v_scale = (_ComputeVScale != 0.0) ? _ComputeVScale : 1.0;
                float2 computeUV = float2(input.uv.x / u_scale, input.uv.y / v_scale);
                float4 computeColor = SAMPLE_TEXTURE2D_X(_ComputeOutput, sampler_ComputeOutput, computeUV);
                float4 cameraColor = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, input.uv);
                return lerp(cameraColor, computeColor, _BlendWithOriginal);
            }
            ENDHLSL
        }
    }
}