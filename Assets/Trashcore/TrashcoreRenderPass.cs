using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal class TrashcoreRenderPass : ScriptableRenderPass
{
    ProfilingSampler m_ProfilingSampler = new ProfilingSampler("Trashcore");
    Material m_Material;
    float m_Intensity;
    private RenderTexture m_computeOutput;
    private readonly ComputeShader m_computeShader;
    private int m_kernelIndex_dct;
    private Vector2Int dct_size = new(1024, 512);

    public TrashcoreRenderPass(ComputeShader computeShader, Material material)
    {
        m_computeShader = computeShader;
        m_Material = material;
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        m_kernelIndex_dct = computeShader.FindKernel("CSMain");
		m_computeOutput = new RenderTexture(dct_size.x, dct_size.y, 24)
		{
			enableRandomWrite = true
		};
		m_computeOutput.Create();
    }

    public void SetIntensity(float intensity)
    {
        m_Intensity = intensity;
    }

    public override void Execute(ScriptableRenderContext context,
                                    ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (camera.cameraType != CameraType.Game) return;

        RenderTargetIdentifier cameraColorTarget = renderingData.cameraData.renderer.cameraColorTarget;
        CommandBuffer cmd_dct = CommandBufferPool.Get("Trashhcore Compute");
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Input", cameraColorTarget);
        cmd_dct.SetComputeTextureParam(m_computeShader, m_kernelIndex_dct, "Result", m_computeOutput);
        cmd_dct.SetComputeFloatParams(m_computeShader, "Resolution_x", dct_size.x);
        cmd_dct.SetComputeFloatParams(m_computeShader, "Resolution_y", dct_size.y);
        int threadGroupsX = Mathf.CeilToInt(dct_size.x / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(dct_size.y / 8.0f);
        cmd_dct.DispatchCompute(m_computeShader, m_kernelIndex_dct, threadGroupsX, threadGroupsY, 1);
        context.ExecuteCommandBuffer(cmd_dct);
        CommandBufferPool.Release(cmd_dct);
        
        CommandBuffer cmd = CommandBufferPool.Get("Trashhcore Postprocess");
        using (new UnityEngine.Rendering.ProfilingScope(cmd, m_ProfilingSampler))
        {
            m_Material.SetFloat("_Intensity", m_Intensity);
            m_Material.SetTexture("_ComputeOutput", m_computeOutput);
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, m_Material);
        }
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }
}