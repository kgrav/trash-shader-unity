using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class CustomPostProcessRenderFeature : ScriptableRendererFeature
{
    [SerializeField]
    private Shader m_bloomShader;
    [SerializeField]
    private Shader m_compositeShader;
    private Material m_bloomMaterial;
    private Material m_compositeMaterial;
    private CustomPostProcessPass m_pass;

   public void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            m_pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            m_pass.ConfigureInput(ScriptableRenderPassInput.Color);
            var colorTargetHandle = RTHandles.Alloc(renderer.cameraColorTarget);
            var depthTargetHandle = RTHandles.Alloc(renderer.cameraDepthTarget);
            m_pass.SetTarget(colorTargetHandle, depthTargetHandle);
        }
        // Setup the render pass here if needed
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_pass);
    }

    public override void Create()
    {
        m_bloomMaterial = CoreUtils.CreateEngineMaterial(m_bloomShader);
        m_compositeMaterial = CoreUtils.CreateEngineMaterial(m_compositeShader);
        m_pass = new CustomPostProcessPass(m_bloomMaterial, m_compositeMaterial);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_bloomMaterial);
        CoreUtils.Destroy(m_compositeMaterial);
    }
}

