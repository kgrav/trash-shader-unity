using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class TrashcoreRendererFeature : ScriptableRendererFeature
{
    public Shader m_compositeShader;
    public ComputeShader m_computeShader;
    public ComputeShader m_nihilismShader;
    //[Range(0.0f, 1.0f)] public float m_Intensity; // Clamped slider between 0 and 1
    [Range(-1f, 2f)] public float m_BlendWithOriginal = 1f;
    [Range(0, 5)] public int m_Cronch;
    [Range(0.0f, 1.0f)] public float m_Crunch = 0.2f;  // posterization levels. 0 = binary, 1.0 = 256 levels
    [Range(0, 7)] public int m_FuzzSize = 2;
    [Range(1,16)] public int m_FuzzyRuff = 8;
    public Juice m_Juice = Juice.HeavyPulpOhYeahBabay;
    public Theory m_Theory = Theory.WovenPixelSymphony;
    private Material m_material;
    public OutputMode m_OutputMode;
    private TrashcoreRenderPass m_renderPass;
    public enum OutputMode
    {
        FinalResult,
        YCbCr,
        Cronch,
        DCT,
        Crunch,
        IDCT,
        Fuzz,
        Unfuzz   
    }
    public enum Juice
    {
        SomePulp,
        SuspiciouslyPulpy,
        HeavyPulpOhYeahBabay,
    }
    public enum Theory
    {
        WovenPixelSymphony,
        AntsDanceTrancePerhaps,
        VelvetDumpsterOverture,
        CrunchUltimê
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            // Calling ConfigureInput with the ScriptableRenderPassInput.Color argument
            // ensures that the opaque texture is available to the Render Pass.
            m_renderPass.ConfigureInput(ScriptableRenderPassInput.Color);
            m_renderPass.SetBlendWithOriginal(m_BlendWithOriginal);
            m_renderPass.SetCronch(m_Cronch);
            m_renderPass.SetCrunch(m_Crunch);
            m_renderPass.SetFuzzSize(m_FuzzSize);
            m_renderPass.SetFuzzyRuff(m_FuzzyRuff);
            m_renderPass.SetJuice((int)m_Juice);
            m_renderPass.SetOutputMode(m_OutputMode);
            m_renderPass.SetTheory((int)m_Theory);
            renderer.EnqueuePass(m_renderPass);
        }
    }

    public override void Create()
    {
        Debug.Assert(m_computeShader != null);
        m_material = CoreUtils.CreateEngineMaterial(m_compositeShader);
        m_renderPass = new TrashcoreRenderPass(m_computeShader, m_nihilismShader, m_material);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_material);
    }
}