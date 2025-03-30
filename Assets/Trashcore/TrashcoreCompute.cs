using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrashcoreCompute : MonoBehaviour
{
    public ComputeShader computeShader;
    public RenderTexture outputTexture;
    private bool _outputTextureInitialized = false;
    public bool OutputTextureInitialized
    {
        get { return _outputTextureInitialized; }
    }

    void Start()
    {
        outputTexture = new  RenderTexture(256, 256, 24);
        outputTexture.enableRandomWrite = true;
        outputTexture.Create();
        _outputTextureInitialized = true;

        computeShader.SetTexture(0, "Result", outputTexture);
        computeShader.SetFloat("Resolution", outputTexture.width);
        computeShader.Dispatch(0, outputTexture.width/ 8, outputTexture.height / 8, 1);

        Material material = GetComponent<Renderer>().material;
        material.SetTexture("_BaseMap", outputTexture);
    }
}
