using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LensFlareRendererFeature : ScriptableRendererFeature
{
    class LensFlarePass : ScriptableRenderPass
    {
        private Material _material;
        private Mesh _mesh;

        public LensFlarePass(Material material, Mesh mesh)
        {
            _material = material;
            _mesh = mesh;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(name: "LensFlarePass");
            Camera camera = renderingData.cameraData.camera;
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            Vector3 scale = 0.5f * new Vector3(1, camera.aspect, 1);

            foreach (VisibleLight visibleLight in renderingData.lightData.visibleLights)
            {
                Light light = visibleLight.light;

                Vector3 viewportPosition =
                    camera.WorldToViewportPoint(light.transform.position) * 2 - Vector3.one;
                // Set the z coordinate of the quads to 0 so that Uniy draws them on the same plane.
                viewportPosition.z = 0;

                var thisRotation = Quaternion.Euler(0, 0, -30f * viewportPosition.x);

                cmd.DrawMesh(_mesh, Matrix4x4.TRS(viewportPosition, thisRotation, scale),
                    _material, 0, 0);
            }

            //cmd.DrawMesh(_mesh, Matrix4x4.identity, _material, 0, 0);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }

    private LensFlarePass _lensFlarePass;
    public Material material;
    public Mesh mesh;


    public override void Create()
    {
        _lensFlarePass = new LensFlarePass(material, mesh);
        _lensFlarePass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material != null || mesh != null)
        {
            renderer.EnqueuePass(_lensFlarePass);
        }
    }
}