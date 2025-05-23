using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class TrashcoreRenderPass : ScriptableRenderPass
{
    private readonly ComputeShader m_computeShader;
    private readonly ComputeShader m_nihilismShader;
	readonly Material m_Material;
    private readonly int p_blend = Shader.PropertyToID("_BlendWithOriginal");
    private readonly int p_chroma_size_x = Shader.PropertyToID("chroma_size_x");
    private readonly int p_chroma_size_y = Shader.PropertyToID("chroma_size_y");
    private readonly int p_cronch = Shader.PropertyToID("cronch");
    private readonly int p_crunch = Shader.PropertyToID("crunch");
    private readonly int p_fuzz = Shader.PropertyToID("fuzz");
    private readonly int p_input_mip_level = Shader.PropertyToID("input_mip_level");
    private readonly int p_input_size_x = Shader.PropertyToID("input_size_x");
    private readonly int p_input_size_y = Shader.PropertyToID("input_size_y");
    private readonly int p_juice = Shader.PropertyToID("juice");
    private readonly int p_nihilism_block_size = Shader.PropertyToID("nihilism_block_size");
    private readonly int p_output_size_x = Shader.PropertyToID("output_size_x");
    private readonly int p_output_size_y = Shader.PropertyToID("output_size_y");
    private readonly int p_resolution_x = Shader.PropertyToID("Resolution_x");
    private readonly int p_resolution_y = Shader.PropertyToID("Resolution_y");
    private readonly int p_theory = Shader.PropertyToID("theory");

    // originally this was implemented with just one temporary and one output texture,
    // but after wasting more than two hours trying to debug, rewrote in vulgar form
    // like you see here and it worked perfectly the first time. :shrug: 
    private readonly RenderTexture t_cronched_0;
    private readonly RenderTexture t_cronched_1;
    private readonly RenderTexture t_cronched_2;
    private readonly RenderTexture t_cronched_3;
    private readonly RenderTexture t_cronched_4;
    private readonly RenderTexture t_cronched_5;

    private readonly RenderTexture t_crunchedCoeffs;
    private readonly RenderTexture t_dctCoeffs;
    private readonly RenderTexture t_fuzz;
    private readonly RenderTexture t_idctOutput;
    private readonly RenderTexture t_trashedOutput;
    private readonly RenderTexture t_ycbcrOutput;
    private readonly int m_kernelIndex_cronch;
    private readonly int m_kernelIndex_crunch;
    private readonly int m_kernelIndex_dct;
    private readonly int m_kernelIndex_fuzz;
    private readonly int m_kernelIndex_idct;
    private readonly int m_kernelIndex_output;
    private readonly int m_kernelIndex_unfuzz = 0;
    private readonly int m_kernelIndex_ycbcr;
    private Vector2Int ycbcr_size = new(1024, 512);
    // dct will run at the final cronched size. final cronch will not be larger than 512 x 256
    private Vector2Int max_dct_size = new(512, 256);
    private float m_blendWithOriginal = 0.0f;
    private int m_cronch;
    private float m_crunch = 0.2f; // posterization levels. 0 = binary, 1.0 = 256 levels
    private int m_fuzz_size = 1;
    private int m_juice = 8;
    private int m_theory = 0;
    private TrashcoreRendererFeature.OutputMode m_outputMode;

    public TrashcoreRenderPass(ComputeShader computeShader, ComputeShader nihilismShader, Material material)
    {
        m_computeShader = computeShader;
        m_nihilismShader = nihilismShader;
        m_Material = material;
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        var shader = computeShader;

        // in sequence: first convert to ycbcr at "max" 1024 x 512 resolution. 
        (m_kernelIndex_ycbcr,  t_ycbcrOutput)    = KernelTexture(shader, "TrashcoreYCbCr", ycbcr_size, true);
        
        // then perform 2d dct and posterize the values
        (m_kernelIndex_dct,    t_dctCoeffs)      = KernelTexture(shader, "TrashcoreDct",    max_dct_size);
        (m_kernelIndex_crunch, t_crunchedCoeffs) = KernelTexture(shader, "TrashcoreCrunch", max_dct_size);
        (m_kernelIndex_idct,   t_idctOutput)     = KernelTexture(shader, "TrashcoreIdct",   max_dct_size);
        (m_kernelIndex_output, t_trashedOutput)  = KernelTexture(shader, "TrashcoreOutput", ycbcr_size);
        (m_kernelIndex_fuzz,   t_fuzz)           = KernelTexture(nihilismShader, "NihilismSingleChannel", max_dct_size);
        
        // the fuzz kernel is the same as 'm_kernelIndex_output'
        //    - except that it computes brightness from fuzz texture instead of y from ycbcr full res

        m_kernelIndex_unfuzz = nihilismShader.FindKernel("TrashcoreUnfuzz"); 
    }

    private RenderTexture CronchTexture(Vector2Int size)
    {
        RenderTexture renderTexture = new(
            size.x,
            size.y,
            24,
            RenderTextureFormat.DefaultHDR,
            RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            // Removed sRGB assignment as it is read-only
        };
        renderTexture.Create();
        return renderTexture;
    }

    private (int kernelIndex, RenderTexture renderTexture) KernelTexture(
        ComputeShader computeShader,
        string kernelName,
        Vector2Int size,
        bool enableMipmaps = false)
    {
        int kernelIndex = computeShader.FindKernel(kernelName);
        RenderTexture renderTexture = new(
            size.x, 
            size.y, 
            24,
            RenderTextureFormat.DefaultHDR,
           enableMipmaps ? 8 : 0 // mipmap count
            )
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            useMipMap = enableMipmaps,
            autoGenerateMips = false,
        };
        renderTexture.Create();
        return (kernelIndex, renderTexture);
    }
    public void SetBlendWithOriginal(float blend_with_original)
    {
        m_blendWithOriginal = blend_with_original;
    }
    public void SetCronch(int cronch)
    {
        m_cronch = cronch;
    }
    public void SetCrunch(float crunch)
    {
        m_crunch = crunch;
    }
    public void SetOutputMode(TrashcoreRendererFeature.OutputMode outputMode)
    {
        m_outputMode = outputMode;
    }
    public void SetFuzzSize(int fuzz_size)
    {
        m_fuzz_size = fuzz_size;
    }
    public void SetJuice(int juice_factor)
    {
        m_juice = juice_factor;
        Debug.Assert(0 <= juice_factor  && juice_factor <= 8, 
            $"TrashcoreRenderPass: {juice_factor} must be between 0 and 2 inclusive, clamp the editor slider in feature.");
    }
    public void SetTheory(int theory)
    {
        m_theory = theory;
        Debug.Assert(0 <= theory && theory <= 3, 
            $"TrashcoreRenderPass: {theory} must be between 0 and 3 inclusive, clamp the editor slider in feature.");
    }

    const int CRONCH_MAX_LEVEL = 5;
    private readonly static Vector2Int[] cronchSizes = new[]
    {
        new Vector2Int(512, 256), // Level 0
        new Vector2Int(256, 128), // Level 1
        new Vector2Int(128, 64),  // Level 2
        new Vector2Int(64, 32),   // Level 3
        new Vector2Int(32, 16),   // Level 4
        new Vector2Int(16, 8)     // Level 5
    };
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
        cmd_ycbcr.SetComputeFloatParam(m_computeShader, p_resolution_x, ycbcr_size.x);
        cmd_ycbcr.SetComputeFloatParams(m_computeShader, "Resolution_y", ycbcr_size.y);
        int ycbcr_ranks = Mathf.CeilToInt(ycbcr_size.y / 8.0f); // rows
        int ycbcr_files = Mathf.CeilToInt(ycbcr_size.x / 8.0f); // columns
        cmd_ycbcr.DispatchCompute(m_computeShader, m_kernelIndex_ycbcr, ycbcr_files, ycbcr_ranks, 1);
        context.ExecuteCommandBuffer(cmd_ycbcr);
        cmd_ycbcr.Clear();
        CommandBufferPool.Release(cmd_ycbcr);
        #endregion

        #region CRONCH 🥣😋🗜️▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛
        t_ycbcrOutput.GenerateMips();
        #endregion

        #region FUZZ 🧸 ▲■■■■▲△■□□▲■■▲□▲▲▲□■■■■▲△■■■□■■■■▲△▲□■■■▲▲▲▲□▲■■■■■▲△▲■□□■■■□▲
        bool fuzzy = m_fuzz_size >= 1;
        // FIND THE MIPMAP that corresponds as closely to 4x4 pixels per block
        // need this in outer scope so we can use it in composite pass
        // RANKS AND FILES are the number of nihilism blocks in the y and x dimensions
        int block_size = 4 * m_fuzz_size;
        int fuzz_output_width = Mathf.CeilToInt((float)ycbcr_size.x / (float)block_size);
        int fuzz_output_height = Mathf.CeilToInt((float)ycbcr_size.y / (float)block_size);
        int fuzzMipLevel = m_fuzz_size;
        int input_width = 2 * ycbcr_size.x >> fuzzMipLevel;
        int input_height = 2 * ycbcr_size.y >> fuzzMipLevel;
        if (fuzzy)
        {
            // the size of the nihilism block is stored as the editor-exposed integer value 0 < m_fuzz < 8
            CommandBuffer cmd_fuzz = CommandBufferPool.Get("Trashhcore Fuzz");
            cmd_fuzz.SetComputeTextureParam(m_nihilismShader, 0, "Input", t_ycbcrOutput);
            cmd_fuzz.SetComputeTextureParam(m_nihilismShader, 0, "Result", t_fuzz);
            cmd_fuzz.SetComputeIntParam    (m_nihilismShader, p_nihilism_block_size, 4);
            cmd_fuzz.SetComputeIntParam    (m_nihilismShader, p_input_size_x, input_width);
            cmd_fuzz.SetComputeIntParam    (m_nihilismShader, p_input_size_y, input_height);
            cmd_fuzz.SetComputeIntParam    (m_nihilismShader, p_input_mip_level, fuzzMipLevel);
            cmd_fuzz.SetComputeIntParam    (m_nihilismShader, p_output_size_x, fuzz_output_width);
            cmd_fuzz.SetComputeIntParam    (m_nihilismShader, p_output_size_y, fuzz_output_height);
            cmd_fuzz.DispatchCompute       (m_nihilismShader, m_kernelIndex_fuzz, fuzz_output_width, fuzz_output_height, 1);
            context.ExecuteCommandBuffer   (cmd_fuzz);
            cmd_fuzz.Clear                 ();
            CommandBufferPool.Release      (cmd_fuzz);
        }
        #endregion        

        #region DCT 🧠 ∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿ 
        var CronchSize = cronchSizes[m_cronch];
        var cronchRankCount = CronchSize.y / 8;
        var cronchFileCount = CronchSize.x / 8;
        CommandBuffer cmd_dct = CommandBufferPool.Get("Trashhcore Dct 🧠");
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Input", t_ycbcrOutput);
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Result", t_dctCoeffs);
        cmd_dct.SetComputeIntParam(m_computeShader, p_cronch, m_cronch);
        cmd_dct.SetComputeIntParam(m_computeShader, p_juice, m_juice);
        cmd_dct.SetComputeIntParam(m_computeShader, p_output_size_x, CronchSize.x);
        cmd_dct.SetComputeIntParam(m_computeShader, p_output_size_y, CronchSize.y);
        cmd_dct.SetComputeIntParam(m_computeShader, p_input_size_x, CronchSize.x);
        cmd_dct.SetComputeIntParam(m_computeShader, p_input_size_y, CronchSize.y);
        cmd_dct.DispatchCompute(m_computeShader, m_kernelIndex_dct, cronchFileCount, cronchRankCount, 1);
        context.ExecuteCommandBuffer(cmd_dct);
        cmd_dct.Clear();
        CommandBufferPool.Release(cmd_dct);
        #endregion

        #region CRUNCH 🥣🗜️😋 aka posterize ░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓
        // convert unorm to a power relationship of range 0-1 to 2-127 levels
        CommandBuffer cmd_crunch = CommandBufferPool.Get("Trashhcore Crunch 🥣");        
        cmd_crunch.SetComputeTextureParam(m_computeShader, m_kernelIndex_crunch, "Input", t_dctCoeffs);
        cmd_crunch.SetComputeTextureParam(m_computeShader, m_kernelIndex_crunch, "Result", t_crunchedCoeffs);
        cmd_crunch.SetComputeIntParam(m_computeShader, p_fuzz, m_fuzz_size);
        cmd_crunch.SetComputeFloatParam(m_computeShader, p_crunch, m_crunch);
        cmd_crunch.DispatchCompute(m_computeShader, m_kernelIndex_crunch, 8 * cronchFileCount, 8 * cronchRankCount, 1);
        context.ExecuteCommandBuffer(cmd_crunch);
        cmd_crunch.Clear();
        CommandBufferPool.Release(cmd_crunch);
        #endregion

        #region IDCT ✨🎛️📈🎇 🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬
        CommandBuffer cmd_idct = CommandBufferPool.Get("Trashhcore Idct ✨");
        cmd_idct.SetComputeTextureParam(m_computeShader, m_kernelIndex_idct, "Input", t_crunchedCoeffs);
        cmd_idct.SetComputeTextureParam(m_computeShader, m_kernelIndex_idct, "Result", t_idctOutput);
        cmd_idct.SetComputeTextureParam(m_computeShader, m_kernelIndex_idct, "Original", t_ycbcrOutput);
        cmd_idct.SetComputeIntParam(m_computeShader, p_juice, m_juice);
        cmd_idct.SetComputeFloatParam(m_computeShader, p_resolution_x, CronchSize.x);
        cmd_idct.DispatchCompute(m_computeShader, m_kernelIndex_idct, cronchFileCount, cronchRankCount, 1);
        context.ExecuteCommandBuffer(cmd_idct);
        cmd_idct.Clear();
        CommandBufferPool.Release(cmd_idct);
        #endregion

        #region YCbCr to RGB 🔧🌀  @@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
        CommandBuffer cmd_output = CommandBufferPool.Get("Trashhcore YCbCr to RGB");
        if (fuzzy)
        {
            int fuzz_input_width = fuzz_output_width;
            int fuzz_input_height = fuzz_output_height;
            cmd_output.SetComputeTextureParam(m_nihilismShader, m_kernelIndex_unfuzz, "Result", t_trashedOutput);
            cmd_output.SetComputeTextureParam(m_nihilismShader, m_kernelIndex_unfuzz, "Chroma", t_idctOutput);
            cmd_output.SetComputeTextureParam(m_nihilismShader, m_kernelIndex_unfuzz, "Fuzz", t_fuzz);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_chroma_size_x, CronchSize.x);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_chroma_size_y, CronchSize.y);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_cronch, m_cronch);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_input_size_x, fuzz_input_width);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_input_size_y, fuzz_input_height);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_nihilism_block_size, m_fuzz_size);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_output_size_x, ycbcr_size.x);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_output_size_y, ycbcr_size.y);
            cmd_output.DispatchCompute(m_nihilismShader, m_kernelIndex_unfuzz, ycbcr_size.x, ycbcr_size.y, 1);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_theory, m_theory);
        }
        else
        {
            cmd_output.SetComputeTextureParam(m_computeShader, m_kernelIndex_output, "Result", t_trashedOutput);
            cmd_output.SetComputeTextureParam(m_computeShader, m_kernelIndex_output, "Chroma", t_idctOutput);
            cmd_output.SetComputeTextureParam(m_computeShader, m_kernelIndex_output, "Luma", t_ycbcrOutput);
            cmd_output.SetComputeFloatParam(m_computeShader, p_resolution_x, CronchSize.x);
            cmd_output.SetComputeFloatParam(m_computeShader, p_resolution_y, CronchSize.y);
            cmd_output.DispatchCompute(m_computeShader, m_kernelIndex_output, ycbcr_size.x, ycbcr_size.y, 1);
        }
        context.ExecuteCommandBuffer(cmd_output);
        cmd_output.Clear();
        CommandBufferPool.Release(cmd_output);
        #endregion

        #region COMPOSITE 🎨🖼️📸 ⊎∪⊎∪⊎∪⊎⋃⊎∪⊎∪⊎∪⊎⋃⊎∪⊎∪⊎∪⊎⋃⊎∪⊎∪⊎∪⊎⋃⊎∪⊎∪⊎∪⊎⋃⊎∪⊎∪⊎∪⊎
        CommandBuffer cmd = CommandBufferPool.Get("Trashhcore Composite");

        m_Material.SetFloat(p_blend, m_blendWithOriginal);
        m_Material.SetInt(p_theory, m_theory);
        switch (m_outputMode)
        {
            case TrashcoreRendererFeature.OutputMode.YCbCr:
                m_Material.SetTexture("_ComputeOutput", t_ycbcrOutput);
                m_Material.SetFloat("_ComputeUScale", 1.0f);
                m_Material.SetFloat("_ComputeVScale", 1.0f);
                break;
            case TrashcoreRendererFeature.OutputMode.Cronch:
                m_Material.SetTexture("_ComputeOutput", t_ycbcrOutput);
                m_Material.SetFloat("_ComputeUScale", 1f);
                m_Material.SetFloat("_ComputeVScale", 1f);
                break;
            case TrashcoreRendererFeature.OutputMode.DCT:
                m_Material.SetTexture("_ComputeOutput", t_dctCoeffs);
                m_Material.SetFloat("_ComputeUScale", 1f );
                m_Material.SetFloat("_ComputeVScale",  1f );
                break;
            case TrashcoreRendererFeature.OutputMode.Crunch:
                m_Material.SetTexture("_ComputeOutput", t_crunchedCoeffs);
                m_Material.SetFloat("_ComputeUScale", max_dct_size.x / CronchSize.x);
                m_Material.SetFloat("_ComputeVScale", max_dct_size.y / CronchSize.y);
                break;
            case TrashcoreRendererFeature.OutputMode.IDCT:
                m_Material.SetTexture("_ComputeOutput", t_idctOutput);
                m_Material.SetFloat("_ComputeUScale", max_dct_size.x / CronchSize.x);
                m_Material.SetFloat("_ComputeVScale", max_dct_size.y / CronchSize.y);
                break;
            case TrashcoreRendererFeature.OutputMode.Fuzz:
                float fuzzScale = (float)ycbcr_size.x / (float)fuzz_output_width / 2f;
                m_Material.SetTexture("_ComputeOutput", t_fuzz);
                m_Material.SetFloat("_ComputeUScale", fuzzScale);
                m_Material.SetFloat("_ComputeVScale", fuzzScale);
                break;
            case TrashcoreRendererFeature.OutputMode.Unfuzz:
                m_Material.SetTexture("_ComputeOutput", t_fuzz);
                m_Material.SetFloat("_ComputeUScale", max_dct_size.x / CronchSize.x);
                m_Material.SetFloat("_ComputeVScale", max_dct_size.y / CronchSize.y);
                break;
            default:
                m_Material.SetTexture("_ComputeOutput", t_trashedOutput);
                m_Material.SetFloat("_ComputeUScale", 1f);
                m_Material.SetFloat("_ComputeVScale", 1f);
                break;
        }
        cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, m_Material);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
        #endregion
    }
}