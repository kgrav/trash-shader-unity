Shader "Game Dev Guide/Composite"
{
    HLSLINCLUDE
        #pragma exclude_renderers gles
        #pragma multi_compile_local _ _USE_RGBM
        #pragma multi_compile _ _USE_DRAW_PROCEDURAL

        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

        TEXTURE2D_X(_SourceTex);
        //float4 _SourceTex_TexelSize;
        
        float4 _Params; // x: scatter, y: clamp, z: threshold (linear), w: threshold knee

        #define Scatter             _Params.x
        #define ClampMax            _Params.y
        #define Threshold           _Params.z
        #define ThresholdKnee       _Params.w

        half4 EncodeHDR(half3 color)
        {
        #if _USE_RGBM
            half4 outColor = EncodeRGBM(color);
        #else
            half4 outColor = half4(color, 1.0);
        #endif

        #if UNITY_COLORSPACE_GAMMA
            return half4(sqrt(outColor.xyz), outColor.w); // linear to γ
        #else
            return outColor;
        #endif
        }

        half3 DecodeHDR(half4 color)
        {
        #if UNITY_COLORSPACE_GAMMA
            color.xyz *= color.xyz; // γ to linear
        #endif

        #if _USE_RGBM
            return DecodeRGBM(color);
        #else
            return color.xyz;
        #endif
        }

        half4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);
            half3 color = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv));
            color.b = 1.0 - color.b; // Invert blue channel
            return EncodeHDR(color);
        }

        // half3 Upsample(float2 uv)
        // {
        //     half3 highMip = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTex, sampler_LinearClamp, uv));

        // #if _BLOOM_HQ && !defined(SHADER_API_GLES)
        //     half3 lowMip = DecodeHDR(SampleTexture2DBicubic(TEXTURE2D_X_ARGS(_SourceTexLowMip, sampler_LinearClamp), uv, _SourceTexLowMip_TexelSize.zwxy, (1.0).xx, unity_StereoEyeIndex));
        // #else
        //     half3 lowMip = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTexLowMip, sampler_LinearClamp, uv));
        // #endif

        //     return lerp(highMip, lowMip, Scatter);
        // }

        // half4 FragUpsample(Varyings input) : SV_Target
        // {
        //     UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        //     half3 color = Upsample(UnityStereoTransformScreenSpaceTex(input.uv));
        //     return EncodeHDR(color);
        // }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "Composite"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragComposite
            ENDHLSL
        }

        // Pass
        // {
        //     Name "Bloom Upsample"

        //     HLSLPROGRAM
        //         #pragma vertex FullscreenVert
        //         #pragma fragment FragUpsample
        //         #pragma multi_compile_local _ _BLOOM_HQ
        //     ENDHLSL
        // }
    }
}
