using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static System.Linq.Enumerable;

internal class TrashcoreRenderPass : ScriptableRenderPass
{
    private readonly ComputeShader m_computeShader;
	readonly Material m_Material;
    float m_Intensity;
    private readonly RenderTexture t_ycbcrOutput;
    private readonly RenderTexture t_cronched;
    private readonly RenderTexture t_dctCoeffs;
    private readonly RenderTexture t_crunchedCoeffs;
    private readonly RenderTexture t_trashedOutput;
    private readonly RenderTexture t_temporary;
    private readonly int m_kernelIndex_ycbcr;
    private readonly int m_kernelIndex_cronch;
    private readonly int m_kernelIndex_dct;
    private readonly int m_kernelIndex_crunch;
    private readonly int m_kernelIndex_idct;
    private Vector2Int ycbcr_size = new(1024, 512);
    private Vector2Int dct_size = new(1024 / 2, 512 / 2);
    private float m_fuzz = 1;
    private float m_cronch = 2;
    private float m_crunch = 0.2f; // posterization levels. 0 = binary, 1.0 = 256 levels

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
        RenderTexture renderTexture = new(size.x, size.y, 24)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
        };
        renderTexture.Create();
        return (kernelIndex, renderTexture);
    }

    public void SetCronch(float cronch)
    {
        m_cronch = cronch;
    }
    public void SetCrunch(float crunch)
    {
        m_crunch = crunch;
    }
    public void SetFuzz(float fuzz)
    {
        m_fuzz = fuzz;
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
                            string level_description)
    {
        CommandBuffer cmd = CommandBufferPool.Get(level_description);
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

    const int CRONCH_MAX_LEVEL = 5;
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
        int minCronchLevel = Mathf.RoundToInt(Mathf.Lerp(5f, 0f, Mathf.Clamp01(m_cronch)));
        var debugFinalTexture = t_cronched;
        
        var dct_unit = new Vector2Int(8, 8);
        
        // cronch level 0 has a height = 8 pixels, i.e. is one dct block.
        //      cronch level 1: h = 16 pixels
        //      cronch level 2: h = 32 pixels
        //      cronch level 3: h = 64 pixels
        //      cronch level 4: h = 128 pixels
        //      cronch level 5: h = 256 pixels
        // Atm DCT textures are 2w:1h aspect ratio. Future @Todo: implement "fit
        // width to screen logic" to make error pixels square. Not necessary for now.

        // the dimensions of cronch level.   
        int[] heightPerLevel = Range(0, 1 + TrashcoreRenderPass.CRONCH_MAX_LEVEL)
                                .Select(i => 8 * (1 << i))
                                .ToArray();

        // mipIndex = mip level - 1, where mip level 0 is the source image at full scale
        var numberOfMips = 6 - minCronchLevel;
        for (int mipIndex = 0; mipIndex < numberOfMips; mipIndex++)
        {
            var levelHeight = heightPerLevel[5 - mipIndex];
            var levelSize = new Vector2Int(2 * levelHeight, levelHeight);
            var levelDescription = $"Trashhcore Cronch Level {mipIndex}";

            // the first iteration reads from t_ycbcrOutput, the last writes to t_cronched
            // intermediate iterations alternate between one temporary texture and the final output
            var reverseFlow = (numberOfMips - mipIndex) % 2 == 0;
            
            var input_texture = mipIndex == 0 ? t_ycbcrOutput : (reverseFlow ? t_cronched : t_temporary);
            var output_texture = reverseFlow ? t_temporary : t_cronched;
            Cronch(context, input_texture, output_texture, levelSize, levelDescription);
            debugFinalTexture = output_texture;
        }
        Debug.Assert(debugFinalTexture == t_cronched, "TrashcoreRenderPass should ultimately write to t_cronched.");
        var cronched_height = heightPerLevel[minCronchLevel];
        var CronchSize = new Vector2Int(2 * cronched_height, cronched_height);
        var cronchRankCount = CronchSize.y / 8;
        var cronchFileCount = CronchSize.x / 8;
        #endregion

        #region DCT 🧠 ∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿∿ 
        CommandBuffer cmd_dct = CommandBufferPool.Get("Trashhcore Dct 🧠");
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Input", t_cronched);
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Result", t_dctCoeffs);
        cmd_dct.SetComputeFloatParams(m_computeShader, "Resolution_x", CronchSize.x);
        cmd_dct.SetComputeFloatParams(m_computeShader, "Resolution_y", CronchSize.y);
        int threadGroupsX = Mathf.CeilToInt(CronchSize.x / 8.0f) / 8;
        int threadGroupsY = Mathf.CeilToInt(CronchSize.y / 8.0f) / 8;
        cmd_dct.DispatchCompute(m_computeShader, m_kernelIndex_dct, cronchFileCount, cronchRankCount, 1);
        context.ExecuteCommandBuffer(cmd_dct);
        cmd_dct.Clear();
        CommandBufferPool.Release(cmd_dct);
        #endregion

        #region CRUNCH 🥣🗜️😋 aka posterize ░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓░░▒▒▓▓
        CommandBuffer cmd_crunch = CommandBufferPool.Get("Trashhcore Crunch 🥣");        
        cmd_crunch.SetComputeTextureParam(m_computeShader, m_kernelIndex_crunch, "Input", t_dctCoeffs);
        cmd_crunch.SetComputeTextureParam(m_computeShader, m_kernelIndex_crunch, "Result", t_crunchedCoeffs);
        cmd_crunch.SetComputeFloatParams(m_computeShader, "Resolution_x", CronchSize.x);
        cmd_crunch.SetComputeFloatParams(m_computeShader, "Resolution_y", CronchSize.y);
        cmd_crunch.SetComputeFloatParams(m_computeShader, "Crunch", m_crunch);
        cmd_crunch.DispatchCompute(m_computeShader, m_kernelIndex_crunch, 8 * cronchFileCount, 8 * cronchRankCount, 1);
        context.ExecuteCommandBuffer(cmd_crunch);
        cmd_crunch.Clear();
        CommandBufferPool.Release(cmd_crunch);
        #endregion

        #region IDCT ✨🎛️📈🎇 🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬🧬
        CommandBuffer cmd_idct = CommandBufferPool.Get("Trashhcore Idct ✨");
        cmd_idct.SetComputeTextureParam(m_computeShader, m_kernelIndex_idct, "Input", t_temporary);
        cmd_idct.SetComputeTextureParam(m_computeShader, m_kernelIndex_idct, "Result", t_trashedOutput);
        cmd_idct.SetComputeFloatParams(m_computeShader, "Resolution_x", CronchSize.x);
        cmd_idct.SetComputeFloatParams(m_computeShader, "Resolution_y", CronchSize.y);
        cmd_idct.DispatchCompute(m_computeShader, m_kernelIndex_idct, cronchFileCount, cronchRankCount, 1);
        context.ExecuteCommandBuffer(cmd_idct);
        cmd_idct.Clear();
        CommandBufferPool.Release(cmd_idct);
        #endregion

        #region COMPOSITE 🔧🛠️🖼️📸 ⊎∪⊎∪⊎∪⊎⋃⊎∪⊎∪⊎∪⊎⋃⊎∪⊎∪⊎∪⊎⋃⊎∪⊎∪⊎∪⊎⋃⊎∪⊎∪⊎∪⊎⋃⊎∪⊎∪⊎∪⊎
        CommandBuffer cmd = CommandBufferPool.Get("Trashhcore Composite");
        m_Material.SetFloat("_Intensity", m_Intensity);
        if (false)
        {
            m_Material.SetTexture("_ComputeOutput", t_cronched);
            m_Material.SetFloat("_ComputeUScale", (float)dct_size.x / (float)CronchSize.x );
            m_Material.SetFloat("_ComputeVScale", (float)dct_size.y / (float)CronchSize.y );
        }
        else
        {
            m_Material.SetTexture("_ComputeOutput", t_ycbcrOutput);
            m_Material.SetFloat("_ComputeUScale", 1.2f);
            m_Material.SetFloat("_ComputeVScale", 1.2f);
        }
        cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, m_Material);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
        #endregion
    }
}