using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class TrashcoreRendererFeature : ScriptableRendererFeature
{
    public Shader m_compositeShader;
    public ComputeShader m_computeShader;
    public float m_Intensity;
    public int m_Fuzz = 1;
    public int m_Cronch = 2; 
    public float m_Crunch = 0.2f;  // posterization levels. 0 = binary, 1.0 = 256 levels
    private Material m_Material;
    private TrashcoreRenderPass m_renderPass;

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            // Calling ConfigureInput with the ScriptableRenderPassInput.Color argument
            // ensures that the opaque texture is available to the Render Pass.
            m_renderPass.ConfigureInput(ScriptableRenderPassInput.Color);
            m_renderPass.SetIntensity(m_Intensity);
            renderer.EnqueuePass(m_renderPass);
        }
    }

    public override void Create()
    {
        Debug.Assert(m_computeShader != null);
        m_Material = CoreUtils.CreateEngineMaterial(m_compositeShader);
        m_renderPass = new TrashcoreRenderPass(m_computeShader, m_Material);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
    }
}