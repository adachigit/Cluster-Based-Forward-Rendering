using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

[ExecuteInEditMode]
//[ImageEffectAllowedInSceneView]
public class Script_ClusterBasedLighting : MonoBehaviour
{
    public Camera m_Camera;
    public int m_ClusterGridBlockSize;
    public ComputeShader cs_ComputeClusterAABB;
    public ComputeShader cs_AssignLightsToCluster;
    public ComputeShader cs_ClusterSample;
    public int m_MaxLightsCount;
    public int m_MaxLightsCountPerCluster;
    public GameObject m_LightsGroupObject;
    public GameObject m_SceneObjectParent;

    private RenderTexture m_globalDepthTexture;
    private List<Material> m_ObjMaterialList = new List<Material>();
    private List<MeshFilter> m_ObjMeshList = new List<MeshFilter>();
    private List<Transform> m_ObjTransformList = new List<Transform>();

    struct CD_DIM
    {
        public float fieldOfViewY;
        public float zNear;
        public float zFar;

        public float sD;
        public float logDimY;
        public float logDepth;

        public int clusterDimX;
        public int clusterDimY;
        public int clusterDimZ;
        public int clusterDimXYZ;
    };

    struct AABB
    {
        public Vector4 Min;
        public Vector4 Max;
    };

    private RenderTexture _rtColor;
    private RenderTexture _rtDepth;
    private CD_DIM m_DimData;
    private ComputeBuffer cb_ClusterAABBs;
    private ComputeBuffer cb_ClusterFlags;
#if UNITY_EDITOR
    private AABB[] m_ClusterAABBInfos;
    private float[] m_ClusterFlagInfos;
#endif

    private const uint LIGHT_DIRECTION  = 1;
    private const uint LIGHT_POINT      = 2;
    private const uint LIGHT_SPOT       = 3;

    struct LightInfo
    {
        public Vector4 worldSpacePos;
        public Vector4 viewSpacePos;
        public Vector4 worldSpaceDir;
        public Vector4 viewSpaceDir;
        public Vector4 color;
        public float spotLightAngle;
        public float range;
        public float intensity;
        public uint type;
    };
    private LightInfo[] m_ActiveLightInfos;

    private ComputeBuffer cb_ActiveLights;
    private ComputeBuffer cb_LightIndexCounter;
    private ComputeBuffer cb_LightGridIndexOffset;
    private ComputeBuffer cb_LightIndexList;
#if UNITY_EDITOR
    private Vector2Int[] m_ClusterLightIndexInfos;
#endif

#if UNITY_EDITOR
    #region For Debug Info and Gizmos
    struct ClusterGizmosInfo
    {
        public Vector3 minLeftTop;
        public Vector3 minLeftBottom;
        public Vector3 minRightTop;
        public Vector3 minRightBottom;
        public Vector3 maxLeftTop;
        public Vector3 maxLeftBottom;
        public Vector3 maxRightTop;
        public Vector3 maxRightBottom;
    };
    private ClusterGizmosInfo[] m_ClusterGizmosInfos;
    public bool m_ShowDebugInfo;
    [Range(0.5f, 1.0f)]
    public float m_ClusterDebugFactor = 0.8f;
    #endregion
#endif

    // Start is called before the first frame update
    void Start()
    {
        _rtColor = new RenderTexture(Screen.width, Screen.height, 24);
        _rtDepth = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);    

        InitSceneObjects();

        CalculateMDim(m_Camera ? m_Camera : Camera.main);

        CreateAndInitBuffers();
        Pass_ComputeClusterAABB();

#if UNITY_EDITOR
        if(m_ShowDebugInfo)
        {
            cb_ClusterAABBs.GetData(m_ClusterAABBInfos);
            TransformAABBDatasToClusterGizmosInfos();
        }
#endif

//        m_Camera.depthTextureMode = DepthTextureMode.Depth;
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    void InitSceneObjects()
    {
        if(null == m_SceneObjectParent) return;

        m_ObjMaterialList.Clear();
        m_ObjMeshList.Clear();
        m_ObjTransformList.Clear();

        MeshFilter[] meshFilters = m_SceneObjectParent.GetComponentsInChildren<MeshFilter>();
        foreach(MeshFilter mf in meshFilters)
        {
            m_ObjMeshList.Add(mf);
        }

        Transform[] transforms = m_SceneObjectParent.GetComponentsInChildren<Transform>();
        foreach(Transform tf in transforms)
        {
            m_ObjTransformList.Add(tf);
        }

        
        MeshRenderer[] renderers = m_SceneObjectParent.GetComponentsInChildren<MeshRenderer>();
        foreach(MeshRenderer renderer in renderers)
        {
            for(int i = 0; i < renderer.sharedMaterials.Length; ++i)
            {
                Material mat = new Material(Shader.Find("ClusterFWD/Lit/Texture"));
                mat.SetTexture("_MainTex", renderer.sharedMaterials[i].GetTexture("_MainTex"));
                renderer.sharedMaterials[i] = mat;
                m_ObjMaterialList.Add(mat);
            }
        }
    }

    void CreateAndInitBuffers()
    {
        Debug.Log("Create buffers");

        int stride = Marshal.SizeOf(typeof(AABB));
        cb_ClusterAABBs = new ComputeBuffer(m_DimData.clusterDimXYZ, stride);

        cb_ActiveLights = new ComputeBuffer(m_MaxLightsCount, Marshal.SizeOf(typeof(LightInfo)));
        cb_LightIndexCounter = new ComputeBuffer(1, sizeof(uint));
        cb_LightGridIndexOffset = new ComputeBuffer(m_DimData.clusterDimXYZ, Marshal.SizeOf(typeof(Vector2Int)));
        cb_LightIndexList = new ComputeBuffer(m_DimData.clusterDimXYZ * m_MaxLightsCountPerCluster, sizeof(uint));

        cb_ClusterFlags = new ComputeBuffer(m_DimData.clusterDimXYZ, sizeof(float));

        int kernel = cs_ComputeClusterAABB.FindKernel("CSMain");
        cs_ComputeClusterAABB.SetBuffer(kernel, "RWClusterAABBs", cb_ClusterAABBs);

        kernel = cs_AssignLightsToCluster.FindKernel("CSMain");
        cs_AssignLightsToCluster.SetBuffer(kernel, "ClusterAABBs", cb_ClusterAABBs);
        cs_AssignLightsToCluster.SetBuffer(kernel, "ActiveLights", cb_ActiveLights);
        cs_AssignLightsToCluster.SetBuffer(kernel, "RWLightIndexCounter_Cluster", cb_LightIndexCounter);
        cs_AssignLightsToCluster.SetBuffer(kernel, "RWLightGrid_Cluster", cb_LightGridIndexOffset);
        cs_AssignLightsToCluster.SetBuffer(kernel, "RWLightIndexList_Cluster", cb_LightIndexList);

        kernel = cs_ClusterSample.FindKernel("CSMain");
        cs_ClusterSample.SetBuffer(kernel, "RWClusterFlags", cb_ClusterFlags);

        m_ActiveLightInfos = new LightInfo[m_MaxLightsCount];
        for(int i = 0; i < m_MaxLightsCount; ++i)
        {
            m_ActiveLightInfos[i] = new LightInfo();
        }

        m_globalDepthTexture = new RenderTexture(Screen.width, Screen.height, 16, RenderTextureFormat.ARGBFloat);
        m_globalDepthTexture.SetGlobalShaderProperty("_CameraDepthTexture");

#if UNITY_EDITOR
        m_ClusterAABBInfos = new AABB[m_DimData.clusterDimXYZ];
        m_ClusterLightIndexInfos = new Vector2Int[m_DimData.clusterDimXYZ];
        m_ClusterFlagInfos = new float[m_DimData.clusterDimXYZ];
#endif
    }

    void ReleaseBuffers()
    {
        Debug.Log("Release buffers");

        if(cb_ClusterAABBs != null)
        {
            cb_ClusterAABBs.Release();
            cb_ClusterAABBs = null;
#if UNITY_EDITOR
            m_ClusterAABBInfos = null;
#endif
        }

        if(cb_ActiveLights != null)
        {
            cb_ActiveLights.Release();
            cb_ActiveLights = null;
        }

        if(cb_ActiveLights != null)
        {
            cb_ActiveLights.Release();
            cb_ActiveLights = null;
        }

        if(cb_LightIndexCounter != null)
        {
            cb_LightIndexCounter.Release();
            cb_LightIndexCounter = null;
        }

        if(cb_LightGridIndexOffset != null)
        {
            cb_LightGridIndexOffset.Release();
            cb_LightGridIndexOffset = null;
        }

        if(cb_LightIndexList != null)
        {
            cb_LightIndexList.Release();
            cb_LightIndexList = null;
        }

        if(cb_ClusterFlags != null)
        {
            cb_ClusterFlags.Release();
            cb_ClusterFlags = null;
#if UNITY_EDITOR
            m_ClusterFlagInfos = null;
#endif
        }
    }

    void OnRenderImage(RenderTexture sourceTexture, RenderTexture destTexture)
    {
//        Graphics.SetRenderTarget(_rtColor.colorBuffer, _rtDepth.depthBuffer);

//        GL.Clear(true, true, Color.gray);

//        Pass_DepthPre();

//        Graphics.Blit(_rtColor, destTexture);
    }

    void CalculateMDim(Camera cam)
    {   //计算生成Cluster所需要的各个参数
        float halfFieldOfViewY = cam.fieldOfView * Mathf.Deg2Rad * 0.5f;    //FOV的一半
        float zNear = cam.nearClipPlane;    // 近剪裁面
        float zFar = cam.farClipPlane;      // 远剪裁面
        //屏幕横向切分cluster的数量
        int clusterDimX = Mathf.CeilToInt(Screen.width / (float)m_ClusterGridBlockSize);
        //屏幕纵向切分cluster的数量
        int clusterDimY = Mathf.CeilToInt(Screen.height / (float)m_ClusterGridBlockSize);

        // k = |log(-Zvs / NEAR) / log(1 + 2 * tan(halfFOV) / clusterDimY)|
        // NEARk = NEAR * (1 + 2 * tan(halfFOV) / clusterDimY) ^ k
        float sD = 2.0f * Mathf.Tan(halfFieldOfViewY) / (float)clusterDimY;
        float logDimY = 1.0f / Mathf.Log(1.0f + sD);

        float logDepth = Mathf.Log(zFar / zNear);
        //延摄像机Z轴切分的cluster数量
        int clusterDimZ = Mathf.FloorToInt(logDepth * logDimY);

        m_DimData.zNear = zNear;
        m_DimData.zFar = zFar;
        m_DimData.sD = sD;
        m_DimData.fieldOfViewY = halfFieldOfViewY;
        m_DimData.logDepth = logDepth;
        m_DimData.logDimY = logDimY;
        m_DimData.clusterDimX = clusterDimX;
        m_DimData.clusterDimY = clusterDimY;
        m_DimData.clusterDimZ = clusterDimZ;
        m_DimData.clusterDimXYZ = clusterDimX * clusterDimY * clusterDimZ;
    }

    void UpdateClusterCBuffer(ComputeShader cs)
    {
        //三维空间下cluster在各个分量上的个数
        int[] gridDims = { m_DimData.clusterDimX, m_DimData.clusterDimY, m_DimData.clusterDimZ };
        //屏幕空间下cluster的像素大小
        int[] sizes = { m_ClusterGridBlockSize, m_ClusterGridBlockSize };
        //屏幕分辨率参数 { width, height, 1 / width, 1 / height }
        Vector4 screenDim = new Vector4((float)Screen.width, (float)Screen.height, 1.0f / Screen.width, 1.0f / Screen.height);
        //近剪裁平面
        float viewNear = m_DimData.zNear;

        cs.SetInts("ClusterCB_GridDim", gridDims);
        cs.SetFloat("ClusterCB_ViewNear", viewNear);
        cs.SetInts("ClusterCB_Size", sizes);
        cs.SetFloat("ClusterCB_NearK", 1.0f + m_DimData.sD);
        cs.SetFloat("ClusterCB_LogGridDimY", m_DimData.logDimY);
        cs.SetVector("ClusterCB_ScreenDimensions", screenDim);
    }

    void Pass_ComputeClusterAABB()
    {
        UpdateClusterCBuffer(cs_ComputeClusterAABB);

        //求投影矩阵的逆矩阵
        Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix, false);
        Matrix4x4 projectionMatrixInvers = m_Camera.projectionMatrix.inverse;
        cs_ComputeClusterAABB.SetMatrix("_InverseProjectionMatrix", projectionMatrixInvers);
        //计算分配的cs线程组数量
        int threadGroups = Mathf.CeilToInt(m_DimData.clusterDimXYZ / 512.0f);

        int kernel = cs_ComputeClusterAABB.FindKernel("CSMain");
        cs_ComputeClusterAABB.Dispatch(kernel, threadGroups, 1, 1);
    }

    void DrawMeshListNow()
    {
        for(int i = 0; i < m_ObjMeshList.Count; ++i)
        {
            Graphics.DrawMeshNow(m_ObjMeshList[i].sharedMesh, m_ObjTransformList[i].localToWorldMatrix);
        }
    }

    void Pass_DepthPre()
    {
        for(int i = 0; i < m_ObjMaterialList.Count; ++i)
        {
            m_ObjMaterialList[i].SetPass(0);
        }

        DrawMeshListNow();
    }

    void UpdateLightBuffer()
    {
        if(cb_ActiveLights == null) return;

        List<Light> activeLights = new List<Light>();
        m_LightsGroupObject.GetComponentsInChildren<Light>(false, activeLights);

//        LightInfo[] lightInfos = new LightInfo[m_MaxLightsCount];
        int activeLightCount = 0;
        for(int i = 0; (i < activeLights.Count && i < m_MaxLightsCount); ++i)
        {
            Light l = activeLights[i];
            if(!l.isActiveAndEnabled) continue;

//            LightInfo info = m_ActiveLightInfos[i];//new LightInfo();
            m_ActiveLightInfos[i].color = l.color;
            m_ActiveLightInfos[i].intensity = l.intensity;

            if(l.type == LightType.Directional)
            {
                m_ActiveLightInfos[i].type = LIGHT_DIRECTION;
                m_ActiveLightInfos[i].worldSpaceDir = l.transform.localToWorldMatrix.GetColumn(2);   // Z轴即朝向
            }
            else if(l.type == LightType.Spot)
            {
                m_ActiveLightInfos[i].type = LIGHT_SPOT;
                m_ActiveLightInfos[i].worldSpacePos = l.transform.localToWorldMatrix.GetColumn(3);   // 位移即位置
                m_ActiveLightInfos[i].worldSpaceDir = l.transform.localToWorldMatrix.GetColumn(2);  // Z轴即朝向
                m_ActiveLightInfos[i].spotLightAngle = l.spotAngle;   // 聚光灯椎体顶角角度值
                m_ActiveLightInfos[i].range = l.range;
            }
            else if(l.type == LightType.Point)
            {
                m_ActiveLightInfos[i].type = LIGHT_POINT;
                m_ActiveLightInfos[i].worldSpacePos = l.transform.localToWorldMatrix.GetColumn(3);
                m_ActiveLightInfos[i].range = l.range;
            }

            m_ActiveLightInfos[i].viewSpacePos = m_Camera.transform.worldToLocalMatrix * m_ActiveLightInfos[i].worldSpacePos;
            m_ActiveLightInfos[i].viewSpaceDir = m_Camera.transform.worldToLocalMatrix * m_ActiveLightInfos[i].worldSpaceDir;

//            lightInfos[i] = info;
            ++activeLightCount;
        }

//        cb_ActiveLights.SetData(lightInfos);
        cb_ActiveLights.SetData(m_ActiveLightInfos);
        cs_AssignLightsToCluster.SetInt("ActiveLightCount", activeLightCount);
    }

    void ClearLightGridIndexCounter()
    {
        cb_LightIndexCounter.SetData(new uint[] { 0 });
    }

    void Pass_AssignLightsToClusters()
    {
        ClearLightGridIndexCounter();

        int kernel = cs_AssignLightsToCluster.FindKernel("CSMain");
        cs_AssignLightsToCluster.Dispatch(kernel, m_DimData.clusterDimXYZ, 1, 1);

#if UNITY_EDITOR
        if(m_ShowDebugInfo)
        {
            cb_LightGridIndexOffset.GetData(m_ClusterLightIndexInfos);
        }
#endif
    }

    void ClearClusterFlags()
    {
#if UNITY_EDITOR
        Array.Clear(m_ClusterFlagInfos, 0, m_ClusterFlagInfos.Length);
        cb_ClusterFlags.SetData(m_ClusterFlagInfos);
#else
        float[] flags = new float[m_DimData.clusterDimXYZ];
        Array.Clear(flags, 0, flags.Length);
        cb_ClusterFlags.SetData(flags);
#endif
    }

    void Pass_ClusterSample()
    {
        ClearClusterFlags();
        UpdateClusterCBuffer(cs_ClusterSample);

        //求投影矩阵的逆矩阵
        Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix, false);
        Matrix4x4 projectionMatrixInvers = m_Camera.projectionMatrix.inverse;
        cs_ClusterSample.SetMatrix("_InverseProjectionMatrix", projectionMatrixInvers);

        int kernel = cs_ClusterSample.FindKernel("CSMain");
        cs_ClusterSample.SetTextureFromGlobal(kernel, "_DepthTexture", "_CameraDepthTexture");
        cs_ClusterSample.Dispatch(kernel, Mathf.CeilToInt(Screen.width / 32.0f), Mathf.CeilToInt(Screen.height / 32.0f), 1);

#if UNITY_EDITOR
        if(m_ShowDebugInfo)
        {
            cb_ClusterFlags.GetData(m_ClusterFlagInfos);
        }
#endif
    }

    // Update is called once per frame
    void Update()
    {
        Pass_DepthPre();

        UpdateLightBuffer();
        Pass_AssignLightsToClusters();

        ClearClusterFlags();
        Pass_ClusterSample();
    }

#if UNITY_EDITOR
    void TransformAABBDatasToClusterGizmosInfos()
    {   //将cluster的AABB信息转换为绘制Gizmos需要的信息
        m_ClusterGizmosInfos = new ClusterGizmosInfo[m_ClusterAABBInfos.Length];

        for(int i = 0; i < m_ClusterAABBInfos.Length; ++i)
        {
            AABB aabb = m_ClusterAABBInfos[i];
            Vector4 delta = (aabb.Max - aabb.Min) * (1.0f - m_ClusterDebugFactor);
            Vector4 Min = aabb.Min + delta;
            Vector4 Max = aabb.Max - delta;

            ClusterGizmosInfo info = new ClusterGizmosInfo();
            info.minLeftTop = new Vector3(Min.x, Max.y, Min.z);
            info.minLeftBottom = new Vector3(Min.x, Min.y, Min.z);
            info.minRightTop = new Vector3(Max.x, Max.y, Min.z);
            info.minRightBottom = new Vector3(Max.x, Min.y, Min.z);
            info.maxLeftTop = new Vector3(Min.x, Max.y, Max.z);
            info.maxLeftBottom = new Vector3(Min.x, Min.y, Max.z);
            info.maxRightTop = new Vector3(Max.x, Max.y, Max.z);
            info.maxRightBottom = new Vector3(Max.x, Min.y, Max.z);
            m_ClusterGizmosInfos[i] = info;
        }
    }
#endif

    /// <summary>
    /// Callback to draw gizmos that are pickable and always drawn.
    /// </summary>
    void OnDrawGizmos()
    {
#if UNITY_EDITOR
        if(!m_ShowDebugInfo) return;

        if(null == m_ClusterGizmosInfos) return;

        Gizmos.color = Color.white;
        var viewToWorldMatrix = m_Camera.cameraToWorldMatrix;
        viewToWorldMatrix.m22 *= -1;

        var oldGizmosMatrix = Gizmos.matrix;
        Gizmos.matrix = viewToWorldMatrix;

        for(int i = m_ClusterGizmosInfos.Length - 1; i >= 0; --i)
        {
            if(m_ClusterFlagInfos[i] <= 0.0f) continue;
//            Debug.Log(i + " cluster");
            ClusterGizmosInfo info = m_ClusterGizmosInfos[i];

            if(i < m_ClusterLightIndexInfos.Length && m_ClusterLightIndexInfos[i].y > 0)
            {
                Gizmos.color = Color.red;
            }
            else
            {
//                continue;
                Gizmos.color = new Color(m_ClusterFlagInfos[i], m_ClusterFlagInfos[i], m_ClusterFlagInfos[i]);//Color.white;
            }
            
            //front square
            Gizmos.DrawLine(info.minLeftTop, info.minRightTop);
            Gizmos.DrawLine(info.minRightTop, info.minRightBottom);
            Gizmos.DrawLine(info.minRightBottom, info.minLeftBottom);
            Gizmos.DrawLine(info.minLeftBottom, info.minLeftTop);
            //back square
            Gizmos.DrawLine(info.maxLeftTop, info.maxRightTop);
            Gizmos.DrawLine(info.maxRightTop, info.maxRightBottom);
            Gizmos.DrawLine(info.maxRightBottom, info.maxLeftBottom);
            Gizmos.DrawLine(info.maxLeftBottom, info.maxLeftTop);
            //connect line
            Gizmos.DrawLine(info.minLeftTop, info.maxLeftTop);
            Gizmos.DrawLine(info.minRightTop, info.maxRightTop);
            Gizmos.DrawLine(info.minRightBottom, info.maxRightBottom);
            Gizmos.DrawLine(info.minLeftBottom, info.maxLeftBottom);
        }

        Gizmos.matrix = oldGizmosMatrix;
#endif
    }
}
