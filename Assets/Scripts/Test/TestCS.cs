using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class TestCS : MonoBehaviour
{
    public ComputeShader shader;

    struct OutStruct
    {
        Vector4 f;
    };

    private ComputeBuffer uintInputBuf;
    private ComputeBuffer uintOutputBuf;
    private uint inoutTestValue;

    // Start is called before the first frame update
    void Start()
    {
        var projectionMatrix = GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false);
        var ProjectionMatrixInvers = projectionMatrix.inverse;

        shader.SetMatrix("inMatrix", ProjectionMatrixInvers);

        shader.SetInts("ClusterCB_GridDim", new int[] { 1, 2, 3 });
        shader.SetFloat("ClusterCB_ViewNear", 0.5f);
        shader.SetInts("ClusterCB_Size", new int[] { 32, 32 });
        shader.SetFloat("ClusterCB_NearK", 1.0f + 34.5f);
        shader.SetFloat("ClusterCB_LogGridDimY", 0.34f);
        shader.SetVector("ClusterCB_ScreenDimensions", new Vector4((float)Screen.width, (float)Screen.height, 1.0f / Screen.width, 1.0f / Screen.height));

        int stride = Marshal.SizeOf(typeof(OutStruct));
        ComputeBuffer cb = new ComputeBuffer(1, stride);

        uintInputBuf = new ComputeBuffer(1, sizeof(uint));
        uintOutputBuf = new ComputeBuffer(1, sizeof(uint));

        int kernel = shader.FindKernel("InOutTestMain");
        shader.SetBuffer(kernel, "uintInputBuf", uintInputBuf);
        shader.SetBuffer(kernel, "uintOutputBuf", uintOutputBuf);
        inoutTestValue = 0;

        kernel = shader.FindKernel("CSMain");
        shader.SetBuffer(kernel, "output", cb);
        shader.Dispatch(kernel, 1, 1, 1);

        OutStruct[] out_data = new OutStruct[1];
        cb.GetData(out_data);

        cb.Dispose();        
    }

    void OnDestroy()
    {
        if(uintInputBuf != null)
        {
            uintInputBuf.Release();
            uintInputBuf = null;
        }

        if(uintOutputBuf != null)
        {
            uintOutputBuf.Release();
            uintOutputBuf = null;
        }
    }

    // Update is called once per frame
    void Update()
    {
        uintInputBuf.SetData(new uint[] { ++inoutTestValue });

        int kernel = shader.FindKernel("InOutTestMain");
        shader.Dispatch(kernel, 1, 1, 1);

        uint[] outValue = new uint[1];
        uintOutputBuf.GetData(outValue);
        Debug.Log("Out value is " + outValue[0]);
    }
}
