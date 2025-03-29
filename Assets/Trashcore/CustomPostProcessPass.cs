using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RendererUtils;
using UnityEditor;


[System.Serializable]
public class CustomPostProcessPass : ScriptableRenderPass
{
    private Material bloomMaterial;
    private Material compositeMaterial;
    private RTHandle m_colorTargetHandle;
    private RTHandle m_depthTargetHandle;
    private RenderTextureDescriptor m_Descriptor;

    const int k_MaxPyramidSize = 16;
    private int[] _BloomMipUp;
    private int[] _BloomMipDown;
    private RTHandle[] m_BloomMipUp;
    private RTHandle[] m_BloomMipDown;
    private GraphicsFormat hdrFormat;
    private BenDayBloomEffectComponent m_BloomEffect;

	public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
	{
		m_Descriptor = renderingData.cameraData.cameraTargetDescriptor;
	}
    internal void SetTarget(RTHandle colorTargetHandle, RTHandle depthTargetHandle)
    {
        m_colorTargetHandle = colorTargetHandle;
        m_depthTargetHandle = depthTargetHandle;
    }
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        VolumeStack stack = VolumeManager.instance.stack;
        m_BloomEffect = stack.GetComponent<BenDayBloomEffectComponent>();
        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, new ProfilingSampler("Custom Post Process Effects")))
        {
            SetupBloom(cmd, m_colorTargetHandle);
        }
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }

    public CustomPostProcessPass(Material bloomMaterial, Material compositeMaterial)
    {
        this.bloomMaterial = bloomMaterial;
        this.compositeMaterial = compositeMaterial;

        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        _BloomMipUp = new int[k_MaxPyramidSize];
        _BloomMipDown = new int[k_MaxPyramidSize];
        m_BloomMipUp = new RTHandle[k_MaxPyramidSize];
        m_BloomMipDown = new RTHandle[k_MaxPyramidSize];
        for (int i = 0; i < k_MaxPyramidSize; i++)
        {
            _BloomMipUp[i] = Shader.PropertyToID("_BloomMipUp" + i);
            _BloomMipDown[i] = Shader.PropertyToID("_BloomMipDown" + i);
            m_BloomMipUp[i] = RTHandles.Alloc(_BloomMipUp[i], name: "_BloomMipUp" + i);
            m_BloomMipDown[i] = RTHandles.Alloc(_BloomMipDown[i], name: "_BloomMipDown" + i);
        }
        const FormatUsage usage = FormatUsage.Linear | FormatUsage.Render;
        if (SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, usage))
        {
            hdrFormat = GraphicsFormat.B10G11R11_UFloatPack32;
        }
        else
        {
            hdrFormat = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;
        }
    }


    private void SetupBloom(CommandBuffer cmd, RTHandle source)
    {
        // Start at half-res
        int downres = 1;
        int tw = m_Descriptor.width >> downres;
        int th = m_Descriptor.height >> downres;

        // Determine iteration count
        int maxSize = Mathf.Max(tw, th);
        int iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) -1);
        int mipCount = Mathf.Clamp(iterations, 1, m_BloomEffect.maxIterations.value);

        // Pre-filtering parameters
        float clamp = m_BloomEffect.clamp.value;
        float threshold = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
        float thresholdKnee = threshold * 0.5f;

        // Material setup
        float scatter = Mathf.Lerp(0.05f, 0.95f, m_BloomEffect.scatter.value);
        var bloomMaterial = this.bloomMaterial;

        bloomMaterial.SetVector("_Params", new Vector4(scatter, clamp, threshold, thresholdKnee));

        // Prefilter
        var desc = GetCompatibleDescriptor(tw, th, hdrFormat);
        for (int i = 0; i < mipCount; i++)
        {
            //RenderingUtils.ReAllocateIfNeeded(ref m_BloomMipUp[i], desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_BloomMipUp[i].name);
            //RTHandles.ReAllocateIfNeeded(ref m_BloomMipDown[i], desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_BloomMipDown[i].name);
            desc.width = Mathf.Max(1, desc.width >> 1);
            desc.height = Mathf.Max(1, desc.height >> 1);
        }
        const int renderPassIndex = 0; // in the order of appearane in the .shader Subshader{Pass{}} dictionary
        Blitter.BlitCameraTexture(cmd, source, m_BloomMipDown[0], bloomMaterial, renderPassIndex);
        Blitter.BlitCameraTexture(cmd, m_BloomMipDown[0], m_BloomMipUp[0], bloomMaterial, renderPassIndex);

        // Downsample - gaussian pyramid
        var lastDown = m_BloomMipDown[0];
        for (int i = 1; i < mipCount; i++)
        {
            // Classic two pass gaussian blur - use mipUp as a temporary target
            //   First pass does 2x downsamplign + 9-tap gaussian
            //   Second pass does 9-tap gaussian using a 5-tap filter + bilinear

            // for readability, set indices to passes in the .shader Subshader{Pass{}} dictionary 
            const int bloomBlurHPassIndex = 1; 
            const int bloomBlurVPassIndex = 2;

            Blitter.BlitCameraTexture(cmd, lastDown, m_BloomMipUp[i], bloomMaterial, bloomBlurHPassIndex);
            Blitter.BlitCameraTexture(cmd,  m_BloomMipUp[i], m_BloomMipDown[i], bloomMaterial, bloomBlurVPassIndex);

            lastDown = m_BloomMipDown[i];
        }

        // Upsample (bilinear by default, HQ filtering does bicubic instead)
        for (int i = mipCount - 2; i >= 0; i--)
        {
            var lowMip = (i == mipCount - 2) ? m_BloomMipDown[i + 1] : m_BloomMipUp[i + 1];
            var highMip = m_BloomMipUp[i];
            var dst = m_BloomMipUp[i];

            const int bloomUpsampleIndex = 3; // index in .shader Subshader{Pass{}} dictionary
            cmd.SetGlobalTexture("_SourceTexLowMip", lowMip);
            Blitter.BlitCameraTexture(cmd, highMip, dst, bloomMaterial, bloomUpsampleIndex);
        }

        cmd.SetGlobalTexture("_Bloom_Texture", m_BloomMipUp[0]);
        cmd.SetGlobalFloat("_BloomIntensity", m_BloomEffect.intensity.value);

    }

    internal static RenderTextureDescriptor GetCompatibleDescriptor(int width, int height, GraphicsFormat format, DepthBits depthBufferBits = DepthBits.None)
    {
		RenderTextureDescriptor desc = new(width, height, format, 0)
		{
			depthBufferBits = (int)depthBufferBits,
			msaaSamples = 1,
			width = width,
			height = height,
			graphicsFormat = format
		};
		return desc;
    }

    internal static bool ReAllocateIfNeeded(
        ref RTHandle handle,
        Vector2 scaleFactor,
        in RenderTextureDescriptor descriptor,
        FilterMode filterMode = FilterMode.Point,
        TextureWrapMode wrapMode = TextureWrapMode.Repeat,
        bool isShadowMap = false,
        int anisoLevel = 1,
        float mipMapBias = 0,
        string name = "")
    {
        if (handle == null || handle.rt.width != descriptor.width || handle.rt.height != descriptor.height)
        {
            if (handle != null)
            {
                handle.Release();
            }

            handle = RTHandles.Alloc(
                width: descriptor.width,
                height: descriptor.height,
                slices: 1,
                depthBufferBits: DepthBits.None,
                colorFormat: descriptor.graphicsFormat,
                filterMode: FilterMode.Point,
                wrapMode: wrapMode,
                dimension: TextureDimension.Tex2D,
                enableRandomWrite: true,
                useMipMap: true,
                autoGenerateMips: false,
                isShadowMap: isShadowMap,
                anisoLevel: anisoLevel,
                mipMapBias: mipMapBias,
                msaaSamples: MSAASamples.None,
                bindTextureMS: false,
                useDynamicScale: false,
                memoryless: RenderTextureMemoryless.None,
                name: name);

            return true;
        }
        // handle matches descriptor, no need to reallocate
        return false;
    }
}
