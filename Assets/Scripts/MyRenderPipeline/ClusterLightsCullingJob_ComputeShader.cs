using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyRenderPipeline
{
    public class ClusterLightsCullingJob_ComputeShader : BaseRendererJob
    {
        private static readonly int ShaderPropId_GridDim = Shader.PropertyToID("ClusterCB_GridDim");
        private static readonly int ShaderPropId_ViewNear = Shader.PropertyToID("ClusterCB_ViewNear");
        private static readonly int ShaderPropId_GridSize = Shader.PropertyToID("ClusterCB_Size");
        private static readonly int ShaderPropId_NearKRatio = Shader.PropertyToID("ClusterCB_NearK");
        private static readonly int ShaderPropId_LogGridDimY = Shader.PropertyToID("ClusterCB_LogGridDimY");
        private static readonly int ShaderPropId_ScreenDimension = Shader.PropertyToID("ClusterCB_ScreenDimensions");
        private static readonly int ShaderPropId_InverseProjMatrix = Shader.PropertyToID("_InverseProjectionMatrix");

        private static readonly int ShaderPropId_ClusterAABBs = Shader.PropertyToID("RWClusterAABBs");
        
        struct Cluster_Dimension_Info
        {
            public float halfFOVRadian;
            public float zNear;
            public float zFar;

            public float tanHalfFOVDivDimY;
            public float logDimY;
            public float logDepth;

            public int clusterDimX;
            public int clusterDimY;
            public int clusterDimZ;
            public int clusterDimXYZ;
        };

//        private ForwardPlusRendererData rendererData;
        
        private int clusterGridBlockSize;
        private int maxLightsCount;
        private int maxLightsCountPerCluster;

        private Vector4 screenDimension;
        private Matrix4x4 inverseProjMatrix;
        
        private Cluster_Dimension_Info clusterDimensionInfo;

        private DataTypes.AABB[] clusterAABBsData;
        // for compute shader
        private ComputeBuffer cbClusterAABBs;
        private ComputeShader clusterAABBComputeShader;

        public override void Init(Camera camera, ScriptableRenderContext content)
        {
            MyRenderPipelineAsset pipelineAsset = GraphicsSettings.renderPipelineAsset as MyRenderPipelineAsset;
            var rendererData = pipelineAsset.GetRendererData<ForwardPlusRendererData>(MyRenderPipeline.RendererType.ForwardPlus);

            ForwardPlusCameraData cameraData = camera.GetComponent<ForwardPlusCameraData>();
            if(cameraData != null)
            {
                clusterDimensionInfo.zFar = (cameraData.clusterZFarMax > rendererData.clusterZFarMax) ? rendererData.clusterZFarMax : cameraData.clusterZFarMax;
                clusterGridBlockSize = cameraData.clusterGridSize > 0 ? cameraData.clusterGridSize : rendererData.clusterGridSize;
                maxLightsCount = cameraData.maxLightsCount > 0 ? cameraData.maxLightsCount : rendererData.maxLightsCount;
                maxLightsCountPerCluster = cameraData.maxLightsCountPerCluster > 0 ? cameraData.maxLightsCountPerCluster : rendererData.maxLightsCountPerCluster;
            }
            else
            {
                clusterDimensionInfo.zFar = rendererData.clusterZFarMax;
                clusterGridBlockSize = rendererData.clusterGridSize;
                maxLightsCount = rendererData.maxLightsCount;
                maxLightsCountPerCluster = rendererData.maxLightsCountPerCluster;
            }

            screenDimension.x = Screen.width;
            screenDimension.y = Screen.height;
            screenDimension.z = 1.0f / Screen.width;
            screenDimension.w = 1.0f / Screen.height;

            clusterAABBComputeShader = rendererData.clusterAABBComputerShader;
        }

        private void InitComputeBuffers()
        {
            int kernel = clusterAABBComputeShader.FindKernel("CSMain");
            // Create AABBs compute buffer
            cbClusterAABBs = new ComputeBuffer(clusterDimensionInfo.clusterDimXYZ, Marshal.SizeOf<DataTypes.AABB>());
            clusterAABBComputeShader.SetBuffer(kernel, ShaderPropId_ClusterAABBs, cbClusterAABBs);
            clusterAABBsData = new DataTypes.AABB[clusterDimensionInfo.clusterDimXYZ];
        }

        private void InitClusterParameter(Camera camera)
        {
            clusterDimensionInfo.zNear = camera.nearClipPlane;
            clusterDimensionInfo.halfFOVRadian = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;

            clusterDimensionInfo.clusterDimX = Mathf.CeilToInt(camera.scaledPixelWidth / (float)clusterGridBlockSize);
            clusterDimensionInfo.clusterDimY = Mathf.CeilToInt(camera.scaledPixelHeight / (float) clusterGridBlockSize);
            
            /* 具体算法：在X、Y、Z三个方向对视锥体进行切分，X、Y方向在屏幕分辨率下使用clusterGridBlockSize为单位切分，clusterGridBlockSize为像素长度值。
                        在Z方向使用指数方式分割，具体数值等于对应cluster纵切面的高度值。
               使用公式：根据上面的描述，定义NEAR𝑘为Z方向上摄像机到第k个cluster的距离，H𝑘为Z方向第k个cluster的高度，那么NEAR𝑘 = NEAR𝑘₋₁ + H𝘬₋₁，因此NEAR₀ = NEAR
                        设视锥体FOV为2Ɵ，那么H₀ = (2 * NEAR * tanƟ) / (clusterDimY)
                        根据通项公式可得，NEAR𝑘 = NEAR * (1 + (2 * tanƟ) / clusterDimY)ᵏ
                        最终求解 k = |log(-Z𝑣𝑠 / NEAR) / log(1 + (2 * tanƟ) / clusterDimY)|
               说明：cluster为视空间下的计算结果
            */
            // 预计算 (2 * tanƟ) / clusterDimY
            clusterDimensionInfo.tanHalfFOVDivDimY = (2.0f * Mathf.Tan(clusterDimensionInfo.halfFOVRadian) / clusterDimensionInfo.clusterDimY);
            // 预计算 log(1 + (2 * tanƟ) / clusterDimY)
            clusterDimensionInfo.logDimY = 1.0f / Mathf.Log(1.0f + clusterDimensionInfo.tanHalfFOVDivDimY);
            // 利用最终求解公式计算在Z方向的cluster切分数量，即将zFar代入公式即可
            clusterDimensionInfo.logDepth = Mathf.Log(clusterDimensionInfo.zFar / clusterDimensionInfo.zNear);
            clusterDimensionInfo.clusterDimZ = Mathf.FloorToInt(clusterDimensionInfo.logDepth * clusterDimensionInfo.logDimY);

            clusterDimensionInfo.clusterDimXYZ = clusterDimensionInfo.clusterDimX * clusterDimensionInfo.clusterDimY * clusterDimensionInfo.clusterDimZ;
            
            int[] gridDims = { clusterDimensionInfo.clusterDimX, clusterDimensionInfo.clusterDimY, clusterDimensionInfo.clusterDimZ };
            
            clusterAABBComputeShader.SetInts(ShaderPropId_GridDim, gridDims);
            clusterAABBComputeShader.SetFloat(ShaderPropId_ViewNear, clusterDimensionInfo.zNear);
            clusterAABBComputeShader.SetInts(ShaderPropId_GridSize, new int[] { clusterGridBlockSize, clusterGridBlockSize });
            clusterAABBComputeShader.SetFloat(ShaderPropId_NearKRatio, 1.0f + clusterDimensionInfo.tanHalfFOVDivDimY);
            clusterAABBComputeShader.SetFloat(ShaderPropId_LogGridDimY, clusterDimensionInfo.logDimY);
            clusterAABBComputeShader.SetVector(ShaderPropId_ScreenDimension, screenDimension);

            inverseProjMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false).inverse;
            clusterAABBComputeShader.SetMatrix(ShaderPropId_InverseProjMatrix, inverseProjMatrix);
        }

        private void CalculateClustersData()
        {
            int threadsGroup = Mathf.CeilToInt(clusterDimensionInfo.clusterDimXYZ / 256.0f);

            int kernel = clusterAABBComputeShader.FindKernel("CSMain");
            clusterAABBComputeShader.Dispatch(kernel, threadsGroup, 1, 1);
            
            cbClusterAABBs.GetData(clusterAABBsData);
        }
        
        public override void BeforeCulling(ref ScriptableCullingParameters param)
        {
            param.maximumVisibleLights = maxLightsCount;
        }

        public override void BeforeRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
            if ((int) screenDimension.x != camera.scaledPixelWidth || (int) screenDimension.y != camera.scaledPixelHeight)
            {
                InitClusterParameter(camera);
                ReleaseComputeBuffers();
                InitComputeBuffers();
                
                CalculateClustersData();
            }
        }

        public override void AfterRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
        }

        private void ReleaseComputeBuffers()
        {
            if (cbClusterAABBs != null)
            {
                cbClusterAABBs.Release();
                cbClusterAABBs = null;
            }

            clusterAABBsData = null;
        }
        
        public override void Dispose()
        {
            ReleaseComputeBuffers();
        }
    }
}