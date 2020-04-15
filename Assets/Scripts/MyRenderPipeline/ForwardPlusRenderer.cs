using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyRenderPipeline
{
    public partial class ForwardPlusRenderer : IPipelineRenderer
    {
        ScriptableRenderContext context;
        Camera camera;
        private ForwardPlusRendererData rendererData;

        const string CMB_BUFFER_NAME = "Forward+ Render";

        static ShaderTagId unlitShaderTagId = new ShaderTagId("SRPDefaultUnlit");
        
        private static readonly int ShaderPropId_GridDim = Shader.PropertyToID("ClusterCB_GridDim");
        private static readonly int ShaderPropId_ViewNear = Shader.PropertyToID("ClusterCB_ViewNear");
        private static readonly int ShaderPropId_GridSize = Shader.PropertyToID("ClusterCB_Size");
        private static readonly int ShaderPropId_NearKRatio = Shader.PropertyToID("ClusterCB_NearK");
        private static readonly int ShaderPropId_LogGridDimY = Shader.PropertyToID("ClusterCB_LogGridDimY");
        private static readonly int ShaderPropId_ScreenDimension = Shader.PropertyToID("ClusterCB_ScreenDimensions");
        private static readonly int ShaderPropId_InverseProjMatrix = Shader.PropertyToID("_InverseProjectionMatrix");

        private static readonly int ShaderPropId_ClusterAABBs = Shader.PropertyToID("RWClusterAABBs");
        
        CommandBuffer cmdBuffer = new CommandBuffer() {
            name = CMB_BUFFER_NAME
        };

        CullingResults cullingResults;

        private BaseRendererJob lightsCullingJob;
        
        #region For Editor Partial Methods
        partial void PrepareForSceneWindow();
        partial void DrawGizmos();
        partial void TransformClusterGizmoInfos();
        #endregion

        private struct AABB
        {
            public Vector4 Min;
            public Vector4 Max;
        }
        
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

        private int clusterGridBlockSize;
        private Vector4 screenDimension;
        private Matrix4x4 inverseProjMatrix;
        
        private int maxLightsCount;
        private int maxLightsCountPerCluster;
        
        private CameraType cameraType;
        private bool debug;

        private Cluster_Dimension_Info clusterDimensionInfo;

        private AABB[] clusterAABBsData;
        // for compute shader
        private ComputeBuffer cbClusterAABBs;
        
        public void Setup(ScriptableRenderContext context, Camera camera)
        {
            InitRendererByCamera(context, camera);

            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;

            lightsCullingJob = new FrustumLightsCullingJob(clusterGridBlockSize, maxLightsCount, maxLightsCountPerCluster);
        }

        private void InitRendererByCamera(ScriptableRenderContext context, Camera camera)
        {
            MyRenderPipelineAsset pipelineAsset = GraphicsSettings.renderPipelineAsset as MyRenderPipelineAsset;
            rendererData = pipelineAsset.GetRendererData<ForwardPlusRendererData>(MyRenderPipeline.RendererType.ForwardPlus);

            ForwardPlusCameraData cameraData = camera.GetComponent<ForwardPlusCameraData>();
            if(cameraData != null)
            {
                clusterDimensionInfo.zFar = (cameraData.cullFarPlane > rendererData.clusterFarPlane) ? rendererData.clusterFarPlane : cameraData.cullFarPlane;
                clusterGridBlockSize = cameraData.clusterGridBlockSize > 0 ? cameraData.clusterGridBlockSize : rendererData.clusterBlockGridSize;
                maxLightsCount = cameraData.maxLightsCount > 0 ? cameraData.maxLightsCount : rendererData.defaultMaxLightsCount;
                maxLightsCountPerCluster = cameraData.maxLightsCountPerCluster > 0 ? cameraData.maxLightsCountPerCluster : rendererData.defaultMaxLightsCountPerCluster;
                debug = cameraData.debug;
            }
            else
            {
                clusterDimensionInfo.zFar = rendererData.defaultCullFarPlane;
                clusterGridBlockSize = rendererData.clusterBlockGridSize;
                maxLightsCount = rendererData.defaultMaxLightsCount;
                maxLightsCountPerCluster = rendererData.defaultMaxLightsCountPerCluster;
                debug = false;
            }

            screenDimension.x = Screen.width;
            screenDimension.y = Screen.height;
            screenDimension.z = 1.0f / Screen.width;
            screenDimension.w = 1.0f / Screen.height;

            this.camera = camera;
/*            
            InitClusterParameter();
            InitComputeBuffers();
            
            CalculateClustersData();
*/            
        }

        private void InitComputeBuffers()
        {
            int kernel = rendererData.clusterAABBComputerShader.FindKernel("CSMain");
            // Create AABBs compute buffer
            cbClusterAABBs = new ComputeBuffer(clusterDimensionInfo.clusterDimXYZ, Marshal.SizeOf<AABB>());
            rendererData.clusterAABBComputerShader.SetBuffer(kernel, ShaderPropId_ClusterAABBs, cbClusterAABBs);
            clusterAABBsData = new AABB[clusterDimensionInfo.clusterDimXYZ];
        }

        private void ReleaseComputeBuffers()
        {
            if (cbClusterAABBs != null)
            {
                cbClusterAABBs.Release();
                cbClusterAABBs = null;
            }
        }
        
        private void InitClusterParameter()
        {
            clusterDimensionInfo.zNear = camera.nearClipPlane;
            clusterDimensionInfo.halfFOVRadian = camera.fieldOfView * Mathf.Deg2Rad * 0.5f;

            clusterDimensionInfo.clusterDimX = Mathf.CeilToInt(Screen.width / (float)clusterGridBlockSize);
            clusterDimensionInfo.clusterDimY = Mathf.CeilToInt(Screen.height / (float) clusterGridBlockSize);
            
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
            
            rendererData.clusterAABBComputerShader.SetInts(ShaderPropId_GridDim, gridDims);
            rendererData.clusterAABBComputerShader.SetFloat(ShaderPropId_ViewNear, clusterDimensionInfo.zNear);
            rendererData.clusterAABBComputerShader.SetInts(ShaderPropId_GridSize, new int[] { clusterGridBlockSize, clusterGridBlockSize });
            rendererData.clusterAABBComputerShader.SetFloat(ShaderPropId_NearKRatio, 1.0f + clusterDimensionInfo.tanHalfFOVDivDimY);
            rendererData.clusterAABBComputerShader.SetFloat(ShaderPropId_LogGridDimY, clusterDimensionInfo.logDimY);
            rendererData.clusterAABBComputerShader.SetVector(ShaderPropId_ScreenDimension, screenDimension);

            inverseProjMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false).inverse;
            rendererData.clusterAABBComputerShader.SetMatrix(ShaderPropId_InverseProjMatrix, inverseProjMatrix);
        }

        private void CalculateClustersData()
        {
            int threadsGroup = Mathf.CeilToInt(clusterDimensionInfo.clusterDimXYZ / 256.0f);

            int kernel = rendererData.clusterAABBComputerShader.FindKernel("CSMain");
            rendererData.clusterAABBComputerShader.Dispatch(kernel, threadsGroup, 1, 1);
            
            cbClusterAABBs.GetData(clusterAABBsData);
            TransformClusterGizmoInfos();
        }
        
        public void Render(ScriptableRenderContext context, Camera camera, Camera lastRenderCamera)
        {
            this.context = context;
            this.camera = camera;

            PrepareForSceneWindow();
            if(!Cull())
                return;
            
            JobsBeforeRender(camera, context, cullingResults);

            Setup();
            DrawVisibleGeometry();
            DrawGizmos();
            Submit();
            
            JobsAfterRender();
        }

        // 设置
        void Setup()
        {
            context.SetupCameraProperties(camera);        // 先设置摄像机属性等Shader变量
            cmdBuffer.ClearRenderTarget(true, true, Color.clear);     // 清除渲染目标，在SetupCameraProperties之后执行此步，可减少一次GL Draw的清屏操作
            cmdBuffer.BeginSample(CMB_BUFFER_NAME);
            ExecuteBuffer();
        }

        // 剪裁
        bool Cull()
        {
            if(camera.TryGetCullingParameters(out ScriptableCullingParameters p))
            {
                cullingResults = context.Cull(ref p);

                return true;
            }

            return false;
        }

        // 渲染物体
        void DrawVisibleGeometry()
        {
            //draw opaque geometry
            var sortingSettings = new SortingSettings(camera);
            var drawSettings = new DrawingSettings(unlitShaderTagId, sortingSettings);
            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            context.DrawRenderers(cullingResults, ref drawSettings, ref filteringSettings);
            
            //draw skybox
            context.DrawSkybox(camera);
            
            //draw transparency geometry
            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            drawSettings.sortingSettings = sortingSettings;
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(cullingResults, ref drawSettings, ref filteringSettings);
        }

        // 向显卡提交所有绘制命令
        void Submit()
        {
            cmdBuffer.EndSample(CMB_BUFFER_NAME);
            ExecuteBuffer();
            context.Submit();
        }

        // 将渲染命令刷入Context
        void ExecuteBuffer()
        {
            context.ExecuteCommandBuffer(cmdBuffer);
            cmdBuffer.Clear();
        }

        public void Dispose()
        {
            ReleaseComputeBuffers();
            DisposeAllJobs();
        }

        private void JobsBeforeRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
            lightsCullingJob.BeforeRender(camera, context, cullingResults);
        }

        private void JobsAfterRender()
        {
            lightsCullingJob.AfterRender();
        }

        private void DisposeAllJobs()
        {
            if (lightsCullingJob != null)
            {
                lightsCullingJob.Dispose();
                lightsCullingJob = null;
            }
        }
    }
}
