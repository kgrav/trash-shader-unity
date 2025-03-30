using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


internal class TrashcoreRenderPass : ScriptableRenderPass
{
    private readonly ComputeShader m_computeShader;
    Material m_Material;
    float m_Intensity;
    private readonly RenderTexture t_ycbcrOutput;
    private readonly RenderTexture t_cronched;
    private readonly RenderTexture t_dctCoeffs;
    private readonly RenderTexture t_crunchedCoeffs;
    private readonly RenderTexture t_trashedOutput;
    private readonly RenderTexture t_temporary;
    private int m_kernelIndex_ycbcr;
    private int m_kernelIndex_cronch;
    private int m_kernelIndex_dct;
    private int m_kernelIndex_crunch;
    private int m_kernelIndex_idct;
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
        var shader = computeShader;
        (m_kernelIndex_ycbcr,  t_ycbcrOutput)    = KernelTexture(shader, "TrashcoreYCbCr", ycbcr_size);
        (m_kernelIndex_cronch, t_cronched)       = KernelTexture(shader, "TrashcoreCronch", dct_size);
        (m_kernelIndex_dct,    t_dctCoeffs)      = KernelTexture(shader, "TrashcoreDct", dct_size);
        (m_kernelIndex_crunch, t_crunchedCoeffs) = KernelTexture(shader, "TrashcoreCrunch", dct_size);

        t_temporary = new RenderTexture(dct_size.x, dct_size.y, 24)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
        };
    }

    private (int kernelIndex, RenderTexture renderTexture) KernelTexture(
        ComputeShader computeShader,
        string kernelName,
        Vector2Int size)
    {
        int kernelIndex = computeShader.FindKernel(kernelName);
        RenderTexture renderTexture = new RenderTexture(size.x, size.y, 24)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
        };
        renderTexture.Create();
        return (kernelIndex, renderTexture);
    }

    public void SetIntensity(float intensity)
    {
        m_Intensity = intensity;
    }

    // Cronch = chroma subsampling
    protected void Cronch(ScriptableRenderContext context,
                            RenderTexture fromTexture,
                            RenderTexture toTexture,
                            Vector2Int outputSize,
                            int level)
    {
        CommandBuffer cmd = CommandBufferPool.Get($"Trashhcore Cronch Level {level}");
        cmd.SetComputeTextureParam(m_computeShader, m_kernelIndex_cronch, "Input", fromTexture);
        cmd.SetComputeTextureParam(m_computeShader, m_kernelIndex_cronch, "Result", toTexture);
        cmd.SetComputeFloatParams(m_computeShader, "Resolution_x", outputSize.x);
        cmd.SetComputeFloatParams(m_computeShader, "Resolution_y", outputSize.y);
        int ranks = Mathf.CeilToInt(outputSize.x / 8.0f);
        int files = Mathf.CeilToInt(outputSize.y / 8.0f);
        cmd.DispatchCompute(m_computeShader, m_kernelIndex_cronch, ranks, files, 1);
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    // Crunch = posterize. Crunch scalar is a float between 0 and 1.
    // 0 = binary, 1 = 256 levels. The higher the value, the more levels are used.
    protected void Crunch(ScriptableRenderContext context,
                            RenderTexture fromTexture,
                            RenderTexture toTexture,
                            Vector2Int outputSize,
                            float crunch_scalar,
                            string description)
    {
        int number_of_levels = Mathf.CeilToInt(Mathf.Pow(2f, 8f * crunch_scalar));
        CommandBuffer cmd = CommandBufferPool.Get($"Trashhcore Crunch Level {description}");
        cmd.SetComputeTextureParam(m_computeShader, m_kernelIndex_crunch, "Input", fromTexture);
        cmd.SetComputeTextureParam(m_computeShader, m_kernelIndex_crunch, "Result", toTexture);
        cmd.SetComputeFloatParams(m_computeShader, "Resolution_x", outputSize.x);
        cmd.SetComputeFloatParams(m_computeShader, "Resolution_y", outputSize.y);
        cmd.SetComputeFloatParams(m_computeShader, "Crunch", number_of_levels);
        int crunch_ranks = Mathf.CeilToInt(outputSize.x / 8.0f);
        int crunch_files = Mathf.CeilToInt(outputSize.y / 8.0f);
        cmd.DispatchCompute(m_computeShader, m_kernelIndex_crunch, crunch_ranks, crunch_files, 1);
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void Execute(ScriptableRenderContext context,
                                    ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (camera.cameraType != CameraType.Game) return;
    
        #region YCBCR 🔁🔄⟳🪽 ••••••••••••••••••••••••••••••••••••••••••••••••••••
        RenderTargetIdentifier cameraColorTarget = renderingData.cameraData.renderer.cameraColorTarget;
        CommandBuffer cmd_ycbcr = CommandBufferPool.Get("Trashhcore YCbCr");
        cmd_ycbcr.SetComputeTextureParam(m_computeShader, m_kernelIndex_ycbcr, "Input", cameraColorTarget);
        cmd_ycbcr.SetComputeTextureParam(m_computeShader, m_kernelIndex_ycbcr, "Result", t_ycbcrOutput);
        cmd_ycbcr.SetComputeFloatParams(m_computeShader, "Resolution_x", ycbcr_size.x);
        cmd_ycbcr.SetComputeFloatParams(m_computeShader, "Resolution_y", ycbcr_size.y);
        int ycbcr_ranks = Mathf.CeilToInt(ycbcr_size.x / 8.0f); // rows
        int ycbcr_files = Mathf.CeilToInt(ycbcr_size.y / 8.0f); // columns
        cmd_ycbcr.DispatchCompute(m_computeShader, m_kernelIndex_ycbcr, ycbcr_ranks, ycbcr_files, 1);
        context.ExecuteCommandBuffer(cmd_ycbcr);
        cmd_ycbcr.Clear();
        CommandBufferPool.Release(cmd_ycbcr);
        #endregion

        #region CRONCH 🥣😋🗜️▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛
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
        #endregion

        #region DCT 🧠 ∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿ 
        CommandBuffer cmd_dct = CommandBufferPool.Get("Trashhcore Dct 🧠");
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Input", t_cronched);
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Result", t_dctCoeffs);
        cmd_dct.SetComputeFloatParams(m_computeShader, "Resolution_x", dct_size.x);
        cmd_dct.SetComputeFloatParams(m_computeShader, "Resolution_y", dct_size.y);
        int threadGroupsX = Mathf.CeilToInt(dct_size.x / 8.0f) / 8;
        int threadGroupsY = Mathf.CeilToInt(dct_size.y / 8.0f) / 8;
        cmd_dct.DispatchCompute(m_computeShader, m_kernelIndex_dct, threadGroupsX, threadGroupsY, 1);
        context.ExecuteCommandBuffer(cmd_dct);
        cmd_dct.Clear();
        CommandBufferPool.Release(cmd_dct);
        #endregion


        #region CRUNCH 🥣🗜️😋 aka posterize ░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓
        CommandBuffer cmd_crunch = CommandBufferPool.Get("Trashhcore Crunch 🥣");
        
        cmd_crunch.SetComputeTextureParam(m_computeShader, m_kernelIndex_crunch, "Input", t_dctCoeffs);
        cmd_crunch.SetComputeTextureParam(m_computeShader, m_kernelIndex_crunch, "Result", t_crunchedCoeffs);
        cmd_crunch.SetComputeFloatParams(m_computeShader, "Resolution_x", dct_size.x);
        cmd_crunch.SetComputeFloatParams(m_computeShader, "Resolution_y", dct_size.y);
        cmd_crunch.SetComputeFloatParams(m_computeShader, "Crunch", m_Crunch);
        int ranks = Mathf.CeilToInt(dct_size.x / 8.0f);
        int files = Mathf.CeilToInt(dct_size.y / 8.0f);
        cmd_crunch.DispatchCompute(m_computeShader, m_kernelIndex_crunch, ranks, files, 1);
        context.ExecuteCommandBuffer(cmd_crunch);
        cmd_crunch.Clear();
        CommandBufferPool.Release(cmd_crunch);
        #endregion

        #region IDCT ✨🎛️📈🎇 🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬
        CommandBuffer cmd_idct = CommandBufferPool.Get("Trashhcore Idct ✨");
        cmd_idct.SetComputeTextureParam(m_computeShader, m_kernelIndex_idct, "Input", t_temporary);
        cmd_idct.SetComputeTextureParam(m_computeShader, m_kernelIndex_idct, "Result", t_trashedOutput);
        cmd_idct.SetComputeFloatParams(m_computeShader, "Resolution_x", dct_size.x);
        cmd_idct.SetComputeFloatParams(m_computeShader, "Resolution_y", dct_size.y);
        int idct_ranks = Mathf.CeilToInt(dct_size.x / 8.0f) / 8;
        int idct_files = Mathf.CeilToInt(dct_size.y / 8.0f) / 8;
        cmd_idct.DispatchCompute(m_computeShader, m_kernelIndex_idct, idct_ranks, idct_files, 1);
        context.ExecuteCommandBuffer(cmd_idct);
        cmd_idct.Clear();
        CommandBufferPool.Release(cmd_idct);
        #endregion

        #region COMPOSITE 🔧🛠️🖼️📸 ──────────────────────────────────────────────
        CommandBuffer cmd = CommandBufferPool.Get("Trashhcore Composite");
        m_Material.SetFloat("_Intensity", m_Intensity);
        m_Material.SetTexture("_ComputeOutput", t_cronched);
        cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, m_Material);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
        #endregion
    }
}