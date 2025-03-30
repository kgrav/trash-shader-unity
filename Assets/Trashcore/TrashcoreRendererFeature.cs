using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class TrashcoreRendererFeature : ScriptableRendererFeature
{
    public Shader m_Shader;
    public float m_Intensity;
    private Material m_Material;
    private TrashcoreRenderPass m_RenderPass;
    private TrashcoreCompute m_Compute = null;

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            // Calling ConfigureInput with the ScriptableRenderPassInput.Color argument
            // ensures that the opaque texture is available to the Render Pass.
            m_RenderPass.ConfigureInput(ScriptableRenderPassInput.Color);
            m_RenderPass.SetIntensity(m_Intensity);
            renderer.EnqueuePass(m_RenderPass);
        }
    }

    public override void Create()
    {
        m_Compute = GameObject.FindObjectOfType<TrashcoreCompute>();
        Debug.Assert(m_Compute != null, "Create a gameobject somewhere in the scene and attach the TrashcoreCompute script.");
        m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
        m_RenderPass = new TrashcoreRenderPass(m_Compute, m_Material);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
    }
}