using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ComputeShaderTest : MonoBehaviour
{
    public ComputeShader computeShader;
    public RenderTexture renderTexture;

    void Start()
    {
        renderTexture = new  RenderTexture(256, 256, 24);
        renderTexture.enableRandomWrite = true;
        renderTexture.Create();


        computeShader.SetTexture(0, "Result", renderTexture);
        computeShader.SetFloat("Resolution", renderTexture.width);
        computeShader.Dispatch(0, renderTexture.width/ 8, renderTexture.height / 8, 1);

        Material material = GetComponent<Renderer>().material;
        material.SetTexture("_BaseMap", renderTexture);
    }


    void Update()
    {
        
    }
}
