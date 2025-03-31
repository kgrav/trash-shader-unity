using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class TrashcoreRenderPass : ScriptableRenderPass
{
    private readonly ComputeShader m_computeShader;
    private readonly ComputeShader m_nihilismShader;
	readonly Material m_Material;
    private readonly int p_blend = Shader.PropertyToID("_BlendWithOriginal");
    private readonly int p_crunchLevels = Shader.PropertyToID("crunch_levels");
    private readonly int p_fuzz_size = Shader.PropertyToID("nihilism_block_size");
    private readonly int p_fuzz_levels = Shader.PropertyToID("posterize_levels");
    private readonly int p_t_fuzz_width = Shader.PropertyToID("t_fuzz_width");
    private readonly int p_t_fuzz_height = Shader.PropertyToID("t_fuzz_height");
    private readonly int p_juice = Shader.PropertyToID("juice");
    private readonly int p_resolution_x = Shader.PropertyToID("Resolution_x");
    private readonly int p_resolution_y = Shader.PropertyToID("Resolution_y");
    private readonly int p_resolution_ix = Shader.PropertyToID("zresolution_ix");
    private readonly int p_resolution_iy = Shader.PropertyToID("resolution_iy");

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
    // oh how demurre, how vulgar but humble, but it worked with basically zero debugging.
    private static Vector2Int size_cronch_level_0 = new(512, 256); // same as max dct
    private static Vector2Int size_cronch_level_1 = new(256, 128);
    private static Vector2Int size_cronch_level_2 = new(128, 64);
    private static Vector2Int size_cronch_level_3 = new(64, 32);
    private static Vector2Int size_cronch_level_4 = new(32, 16);
    private static Vector2Int size_cronch_level_5 = new(16, 8);
    private float m_blendWithOriginal = 0.0f;
    private int m_cronch;
    private float m_crunch = 0.2f; // posterization levels. 0 = binary, 1.0 = 256 levels
    private int m_fuzz = 1;
    private int m_juice = 8;
    private TrashcoreRendererFeature.OutputMode m_outputMode;

    public TrashcoreRenderPass(ComputeShader computeShader, ComputeShader nihilismShader, Material material)
    {
        m_computeShader = computeShader;
        m_nihilismShader = nihilismShader;
        m_Material = material;
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        var shader = computeShader;

        // in sequence: first convert to ycbcr at "max" 1024 x 512 resolution. 
        (m_kernelIndex_ycbcr,  t_ycbcrOutput)    = KernelTexture(shader, "TrashcoreYCbCr", ycbcr_size);

        // then create mimaps of the ycbcr texture, which is the input to the cronch shader.
        (m_kernelIndex_cronch, t_cronched_0)     = KernelTexture(shader, "TrashcoreCronch", max_dct_size);
        t_cronched_1 = CronchTexture(size_cronch_level_1);
        t_cronched_2 = CronchTexture(size_cronch_level_2);
        t_cronched_3 = CronchTexture(size_cronch_level_3);
        t_cronched_4 = CronchTexture(size_cronch_level_4);
        t_cronched_5 = CronchTexture(size_cronch_level_5);
        
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
        Vector2Int size)
    {
        int kernelIndex = computeShader.FindKernel(kernelName);
        RenderTexture renderTexture = new(
            size.x, 
            size.y, 
            24,
            RenderTextureFormat.DefaultHDR,
            RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
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
    public void SetFuzz(int fuzz)
    {
        m_fuzz = fuzz;
    }
    public void SetJuice(int juice_factor)
    {
        m_juice = juice_factor;
        Debug.Assert(0 <= juice_factor  && juice_factor <= 8, 
            $"TrashcoreRenderPass: {juice_factor} must be between 0 and 2 inclusive, clamp the editor slider in feature.");
    }

    // Cronch = chroma subsampling
    protected void Cronch(ScriptableRenderContext context,
                            RenderTexture fromTexture,
                            RenderTexture toTexture,
                            Vector2Int outputSize,
                            string level_description)
    {
        CommandBuffer cmd = CommandBufferPool.Get(level_description);
        cmd.SetComputeTextureParam(m_computeShader, m_kernelIndex_cronch, "Input", fromTexture);
        cmd.SetComputeTextureParam(m_computeShader, m_kernelIndex_cronch, "Result", toTexture);
        cmd.SetComputeFloatParam(m_computeShader, p_resolution_x, outputSize.x);
        cmd.SetComputeFloatParam(m_computeShader, p_resolution_y, outputSize.y);
        int ranks = Mathf.CeilToInt(outputSize.x / 8.0f);
        int files = Mathf.CeilToInt(outputSize.y / 8.0f);
        cmd.DispatchCompute(m_computeShader, m_kernelIndex_cronch, ranks, files, 1);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
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
        cmd.SetComputeFloatParam(m_computeShader, p_resolution_x, outputSize.x);
        cmd.SetComputeFloatParam(m_computeShader, p_resolution_y, outputSize.y);
        cmd.SetComputeFloatParams(m_computeShader, "Crunch", number_of_levels);
        int crunch_ranks = Mathf.CeilToInt(outputSize.x / 8.0f);
        int crunch_files = Mathf.CeilToInt(outputSize.y / 8.0f);
        cmd.DispatchCompute(m_computeShader, m_kernelIndex_crunch, crunch_ranks, crunch_files, 1);
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    const int CRONCH_MAX_LEVEL = 5;
    private readonly static Vector2Int[] cronchSizes = new[]
    {
        size_cronch_level_0, // Level 0
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

        #region FUZZ 🧸 ▲■■■■▲△■□□▲■■▲□▲▲▲□■■■■▲△■■■□■■■■▲△▲□■■■▲▲▲▲□▲■■■■■▲△▲■□□■■■□▲
        bool fuzzy = m_fuzz > 1;
        if (fuzzy)
        {
            float t = (float)(7 - m_fuzz);
            float fuzz_exponent = Mathf.Lerp(1.0f, 4.0f, Mathf.Clamp01(t / 7.0f));
            float fuzz_levels = Mathf.Pow(2.0f, fuzz_exponent);
            // the size of the nihilism block is stored as the editor-exposed integer value 0 < m_fuzz < 8
            int fuzz_width = Mathf.CeilToInt((float)ycbcr_size.x / (float)m_fuzz);
            int fuzz_height = Mathf.CeilToInt((float)ycbcr_size.y / (float)m_fuzz);

            Debug.Assert(ycbcr_size.y == ycbcr_size.x / 2, 
                $"TrashcoreRenderPass: YCbCr size mismatch. Expected y ({ycbcr_size.y}) to be half of x ({ycbcr_size.x}).");
            CommandBuffer cmd_fuzz = CommandBufferPool.Get("Trashhcore Fuzz");

            cmd_fuzz.SetComputeTextureParam(m_nihilismShader, 0, "Input", t_ycbcrOutput);
            cmd_fuzz.SetComputeTextureParam(m_nihilismShader, 0, "Result", t_fuzz);
            cmd_fuzz.SetComputeIntParam(m_nihilismShader, p_resolution_ix, ycbcr_size.x);
            cmd_fuzz.SetComputeIntParam(m_nihilismShader, p_resolution_iy, ycbcr_size.y);
            cmd_fuzz.SetComputeIntParam(m_nihilismShader, p_fuzz_size, m_fuzz);
            cmd_fuzz.SetComputeIntParam(m_nihilismShader, p_t_fuzz_width, fuzz_width);
            cmd_fuzz.SetComputeIntParam(m_nihilismShader, p_t_fuzz_height, fuzz_height);
            cmd_fuzz.SetComputeFloatParam(m_nihilismShader, p_fuzz_levels, fuzz_levels);
            cmd_fuzz.DispatchCompute(m_nihilismShader, m_kernelIndex_fuzz, fuzz_width, fuzz_height, 1);
            context.ExecuteCommandBuffer(cmd_fuzz);
            cmd_fuzz.Clear();
            CommandBufferPool.Release(cmd_fuzz);
        }
        #endregion

        // cronch levels are mipmaps but count up from zero = pixel height of 8,
        // each level increases the height by a factor of 2. 
        #region CRONCH 🥣😋🗜️▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛▟▛
        
        // Originally, this region followed the traditional mip structure where level 0
        // is the source image and every successive mipmap decreases in dimension by a factor of 2.
        // After getting "difficult-to-find" logical errors that resulted in invalid threadgroup 
        // size aka threadgroup size of zero, this region was restructured to do all logical
        // calculations relative to the minimum output image size, which is 8 pixels tall 
        // and a multiple of 8 width. Thats why cronch level 0 is 8 pixels tall, so its
        // impossible to mess up.  Vs. the maximum mip level which varies based on input size.
        
        // map the input scalar to an integer from 5 to zero, cronched output height in pixels is equal 
        // to 8 * (1 << clampedCronch). minCronchLevel is the coarsest resolution to which we will process.
        
        Debug.Assert(m_cronch >= 0 && m_cronch <= CRONCH_MAX_LEVEL, 
            $"TrashcoreRenderPass: Invalid cronch level {m_cronch}. Must be between 0 and {CRONCH_MAX_LEVEL}.");
        
        // cronch level 0 has a height = 8 pixels, i.e. is one dct block.
        //      cronch level 1: h = 16 pixels
        //      cronch level 2: h = 32 pixels
        //      cronch level 3: h = 64 pixels
        //      cronch level 4: h = 128 pixels
        //      cronch level 5: h = 256 pixels
        // Atm DCT textures are 2w:1h aspect ratio. Future @Todo: implement "fit
        // width to screen logic" to make error pixels square. Not necessary for now.

        Cronch(context, t_ycbcrOutput, t_cronched_0, size_cronch_level_0, "Trashhcore Cronch Level 0");
        Cronch(context, t_cronched_0,  t_cronched_1, size_cronch_level_1, "Trashhcore Cronch Level 1");
        Cronch(context, t_cronched_1,  t_cronched_2, size_cronch_level_2, "Trashhcore Cronch Level 2");
        Cronch(context, t_cronched_2,  t_cronched_3, size_cronch_level_3, "Trashhcore Cronch Level 3");
        Cronch(context, t_cronched_3,  t_cronched_4, size_cronch_level_4, "Trashhcore Cronch Level 4");
        Cronch(context, t_cronched_4,  t_cronched_5, size_cronch_level_5, "Trashhcore Cronch Level 5");

        var textures = new[]
        {
            t_cronched_0,
            t_cronched_1,
            t_cronched_2,
            t_cronched_3,
            t_cronched_4,
            t_cronched_5
        };

        var t_cronched = textures[m_cronch];
        var CronchSize = cronchSizes[m_cronch];
        var cronchRankCount = CronchSize.y / 8;
        var cronchFileCount = CronchSize.x / 8;
        #endregion

        #region DCT 🧠 ∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿ 
        CommandBuffer cmd_dct = CommandBufferPool.Get("Trashhcore Dct 🧠");
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Input", t_cronched);
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Result", t_dctCoeffs);
        cmd_dct.SetComputeIntParam(m_computeShader, p_juice, m_juice);
        cmd_dct.SetComputeFloatParam(m_computeShader, p_resolution_x, CronchSize.x);
        cmd_dct.DispatchCompute(m_computeShader, m_kernelIndex_dct, cronchFileCount, cronchRankCount, 1);
        context.ExecuteCommandBuffer(cmd_dct);
        cmd_dct.Clear();
        CommandBufferPool.Release(cmd_dct);
        #endregion

        #region CRUNCH 🥣🗜️😋 aka posterize ░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓
        // convert unorm to a power relationship of range 0-1 to 2-127 levels
        float exponent = Mathf.Lerp(1.0f, 4.85f, Mathf.Clamp01(1f - m_crunch));
        float crunchLevels = Mathf.Round(Mathf.Pow(2.718f, exponent));
        CommandBuffer cmd_crunch = CommandBufferPool.Get("Trashhcore Crunch 🥣");        
        cmd_crunch.SetComputeTextureParam(m_computeShader, m_kernelIndex_crunch, "Input", t_dctCoeffs);
        cmd_crunch.SetComputeTextureParam(m_computeShader, m_kernelIndex_crunch, "Result", t_crunchedCoeffs);
        cmd_crunch.SetComputeFloatParam(m_computeShader, p_crunchLevels, crunchLevels);
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
            cmd_output.SetComputeTextureParam(m_nihilismShader, m_kernelIndex_unfuzz, "Result", t_trashedOutput);
            cmd_output.SetComputeTextureParam(m_nihilismShader, m_kernelIndex_unfuzz, "Chroma", t_idctOutput);
            cmd_output.SetComputeTextureParam(m_nihilismShader, m_kernelIndex_unfuzz, "Fuzz", t_fuzz);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_fuzz_size, m_fuzz);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_resolution_ix, CronchSize.x);
            cmd_output.SetComputeIntParam(m_nihilismShader, p_resolution_iy, CronchSize.y);
            cmd_output.DispatchCompute(m_nihilismShader, m_kernelIndex_unfuzz, ycbcr_size.x, ycbcr_size.y, 1);
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
        switch (m_outputMode)
        {
            case TrashcoreRendererFeature.OutputMode.YCbCr:
                m_Material.SetTexture("_ComputeOutput", t_ycbcrOutput);
                m_Material.SetFloat("_ComputeUScale", 1.0f);
                m_Material.SetFloat("_ComputeVScale", 1.0f);
                break;
            case TrashcoreRendererFeature.OutputMode.Cronch:
                m_Material.SetTexture("_ComputeOutput", t_cronched);
                m_Material.SetFloat("_ComputeUScale", 1f);
                m_Material.SetFloat("_ComputeVScale", 1f);
                break;
            case TrashcoreRendererFeature.OutputMode.DCT:
                m_Material.SetTexture("_ComputeOutput", t_dctCoeffs);
                m_Material.SetFloat("_ComputeUScale", max_dct_size.x / CronchSize.x);
                m_Material.SetFloat("_ComputeVScale", max_dct_size.y / CronchSize.y);
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