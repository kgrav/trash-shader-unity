using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class TrashcoreRenderPass : ScriptableRenderPass
{
    ProfilingSampler m_ProfilingSampler = new ProfilingSampler("ColorBlit");
    Material m_Material;
    float m_Intensity;
    private RenderTexture _computeOutputTexture = null;
    private TrashcoreCompute m_Compute;

    public TrashcoreRenderPass(TrashcoreCompute compressor, Material material)
    {
        m_Compute = compressor;
        m_Material = material;
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public void SetIntensity(float intensity)
    {
        m_Intensity = intensity;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (camera.cameraType != CameraType.Game) return;

        if (_computeOutputTexture == null) {
            if (m_Compute.OutputTextureInitialized)
            {
                _computeOutputTexture = m_Compute.outputTexture;
            }
            else return;
        }

        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            m_Material.SetFloat("_Intensity", m_Intensity);
            m_Material.SetTexture("_ComputeOutput", _computeOutputTexture);
            //RenderingUtils.fullscreenMesh is a passthrough quad in clip space
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, m_Material);
        }
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        CommandBufferPool.Release(cmd);
    }
}