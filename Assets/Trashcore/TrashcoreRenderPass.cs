using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class TrashcoreRenderPass : ScriptableRenderPass
{
    ProfilingSampler m_ProfilingSampler = new ProfilingSampler("Trashcore");
    Material m_Material;
    float m_Intensity;
    private readonly RenderTexture t_ycbcrOutput;
    private readonly RenderTexture t_cronched;
    private readonly RenderTexture t_dctCoefficients;
    private readonly RenderTexture t_crunchedCoefficients;
    private readonly RenderTexture t_trashedOutput;
    private readonly ComputeShader m_computeShader;
    private int m_kernelIndex_ycbcr;
    private int m_kernelIndex_dct;
    private int m_kernelIndex_crunch;
    private int m_kernelIndex_cronch;
    private Vector2Int ycbcr_size = new(1024, 512);
    private Vector2Int dct_size = new(1024 / 2, 512 / 2);
    private int m_Fuzz = 1;
    private int m_Cronch = 2;
    private float m_Crunch = 0.2f; // posterization levels. 0 = binary, 1.0 = 256 levels
    private const int maximumMips = 6;

    public TrashcoreRenderPass(ComputeShader computeShader, Material material)
    {
        m_computeShader = computeShader;
        m_Material = material;
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        m_kernelIndex_ycbcr = computeShader.FindKernel("TrashcoreYCbCr");
		t_ycbcrOutput = new RenderTexture(1024, 512, 24)
		{
			enableRandomWrite = true,
            filterMode = FilterMode.Point,
		};
		t_ycbcrOutput.Create();

        m_kernelIndex_cronch = computeShader.FindKernel("TrashcoreCronch");
        t_cronched = new RenderTexture(dct_size.x, dct_size.y, 24)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
        };
        t_cronched.Create();

        m_kernelIndex_dct = computeShader.FindKernel("TrashcoreDct");
		t_dctCoefficients = new RenderTexture(dct_size.x, dct_size.y, 24)
		{
			enableRandomWrite = true,
            filterMode = FilterMode.Point,
		};
		t_dctCoefficients.Create();
    }

    public void SetIntensity(float intensity)
    {
        m_Intensity = intensity;
    }

    public override void Execute(ScriptableRenderContext context,
                                    ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (camera.cameraType != CameraType.Game) return;

        RenderTargetIdentifier cameraColorTarget = renderingData.cameraData.renderer.cameraColorTarget;
        CommandBuffer cmd_ycbcr = CommandBufferPool.Get("Trashhcore YCbCr");
        using (new UnityEngine.Rendering.ProfilingScope(cmd_ycbcr, m_ProfilingSampler))
        {
            cmd_ycbcr.SetComputeTextureParam(m_computeShader, m_kernelIndex_ycbcr, "Input", cameraColorTarget);
            cmd_ycbcr.SetComputeTextureParam(m_computeShader, m_kernelIndex_ycbcr, "Result", t_ycbcrOutput);
            cmd_ycbcr.SetComputeFloatParams(m_computeShader, "Resolution_x", ycbcr_size.x);
            cmd_ycbcr.SetComputeFloatParams(m_computeShader, "Resolution_y", ycbcr_size.y);
            int ranks = Mathf.CeilToInt(ycbcr_size.x / 8.0f);
            int files = Mathf.CeilToInt(ycbcr_size.y / 8.0f);
            cmd_ycbcr.DispatchCompute(m_computeShader, m_kernelIndex_ycbcr, ranks, files, 1);
        }
        context.ExecuteCommandBuffer(cmd_ycbcr);
        cmd_ycbcr.Clear();
        CommandBufferPool.Release(cmd_ycbcr);

        var clamped_cronch = (int)(Mathf.Clamp(m_Cronch, 1f, 6f - 1e-5f));
        Vector2Int crunched_size = new Vector2Int(dct_size.x, dct_size.y);
        CommandBuffer cmd_cronch = CommandBufferPool.Get("Trashhcore Cronch");
        cmd_cronch.SetComputeTextureParam(m_computeShader, m_kernelIndex_cronch, "Input", t_ycbcrOutput);
        cmd_cronch.SetComputeTextureParam(m_computeShader, m_kernelIndex_cronch, "Result", t_cronched);
        cmd_cronch.SetComputeFloatParams(m_computeShader, "Resolution_x", dct_size.x);
        cmd_cronch.SetComputeFloatParams(m_computeShader, "Resolution_y", dct_size.y);
        int cronch_ranks = Mathf.CeilToInt(ycbcr_size.x / 8.0f);
        int cronch_files = Mathf.CeilToInt(ycbcr_size.y / 8.0f);
        cmd_cronch.DispatchCompute(m_computeShader, m_kernelIndex_cronch, cronch_ranks, cronch_files, 1);
        context.ExecuteCommandBuffer(cmd_cronch);
        cmd_cronch.Clear();
        CommandBufferPool.Release(cmd_cronch);

        CommandBuffer cmd_dct = CommandBufferPool.Get("Trashhcore Compute");
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Input", t_cronched);
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Result", t_dctCoefficients);
        cmd_dct.SetComputeFloatParams(m_computeShader, "Resolution_x", dct_size.x);
        cmd_dct.SetComputeFloatParams(m_computeShader, "Resolution_y", dct_size.y);
        int threadGroupsX = Mathf.CeilToInt(dct_size.x / 8.0f) / 8;
        int threadGroupsY = Mathf.CeilToInt(dct_size.y / 8.0f) / 8;
        cmd_dct.DispatchCompute(m_computeShader, m_kernelIndex_dct, threadGroupsX, threadGroupsY, 1);
        context.ExecuteCommandBuffer(cmd_dct);
        cmd_dct.Clear();
        CommandBufferPool.Release(cmd_dct);
        
        CommandBuffer cmd = CommandBufferPool.Get("Trashhcore Postprocess");
        using (new UnityEngine.Rendering.ProfilingScope(cmd, m_ProfilingSampler))
        {
            m_Material.SetFloat("_Intensity", m_Intensity);
            m_Material.SetTexture("_ComputeOutput", t_cronched);
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, m_Material);
        }
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }
}