using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class TrashcoreRendererFeature : ScriptableRendererFeature
{
    public Shader m_compositeShader;
    public ComputeShader m_computeShader;
    //[Range(0.0f, 1.0f)] public float m_Intensity; // Clamped slider between 0 and 1
    private float m_Intensity = 1f;
    [Range(0.0f, 1.0f)] public float m_Fuzz = 1f;
    [Range(0.0f, 1.0f)] public float m_Cronch; // Clamped slider between 0 and 1
    [Range(0.0f, 1.0f)] public float m_Crunch = 0.2f;  // posterization levels. 0 = binary, 1.0 = 256 levels
    public Juice m_Juice = Juice.HeavyPulpOhYeahBabay;
    private Material m_material;
    private TrashcoreRenderPass m_renderPass;
    public enum TrashcoreDebugMode
    {
        None,
        YCbCr,
        Cronch,
        DCT,
        Crunch,
        IDCT
    }
    public enum Juice
    {
        SomePulp,
        SuspiciouslyPulpy,
        HeavyPulpOhYeahBabay,
    }

    public TrashcoreDebugMode m_DebugMode;

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            // Calling ConfigureInput with the ScriptableRenderPassInput.Color argument
            // ensures that the opaque texture is available to the Render Pass.
            m_renderPass.ConfigureInput(ScriptableRenderPassInput.Color);
            m_renderPass.SetIntensity(m_Intensity);
            m_renderPass.SetCronch(m_Cronch);
            m_renderPass.SetCrunch(m_Crunch);
            m_renderPass.SetFuzz(m_Fuzz);
            m_renderPass.SetDebugMode(m_DebugMode);
            m_renderPass.SetJuice((int)m_Juice);
            renderer.EnqueuePass(m_renderPass);
        }
    }

    public override void Create()
    {
        Debug.Assert(m_computeShader != null);
        m_material = CoreUtils.CreateEngineMaterial(m_compositeShader);
        m_renderPass = new TrashcoreRenderPass(m_computeShader, m_material);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_material);
    }
}