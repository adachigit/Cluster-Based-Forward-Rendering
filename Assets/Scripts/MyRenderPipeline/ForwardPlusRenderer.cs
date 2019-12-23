using UnityEngine;
using UnityEngine.Rendering;

namespace MyRenderPipeline
{
    public partial class ForwardPlusRenderer : IPipelineRenderer
    {
        ScriptableRenderContext _context;
        Camera _camera;

        const string bufferName = "Forward+ Render";

        static ShaderTagId unlitShaderTagId = new ShaderTagId("SRPDefaultUnlit");

        CommandBuffer _buffer = new CommandBuffer() {
            name = bufferName
        };

        CullingResults cullingResults;

        #region For Editor Partial Methods

        partial void PrepareForSceneWindow();
        partial void DrawGizmos();

        #endregion
        
        private float clusterFarPlane;
        private float fov;
        private float cullFarPlane;
        private int maxLightsCount;
        private int maxLightsCountPerCluster;
        private bool debug;

        public void Setup(ScriptableRenderContext context, Camera camera)
        {
            if(camera.TryGetComponent<ForwardPlusCameraData>(out ForwardPlusCameraData data))
            {
                InitRendererByCameraData(context, camera);
            }
            else
            {
                if(camera.cameraType == CameraType.SceneView || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                {
                    ForwardPlusCameraData defaultData = new ForwardPlusCameraData();
                }
                else
                {
                    Debug.LogWarning($"Unsupported camera({camera.name}) with camera type '{camera.cameraType}'.");
                }
            }
        }

        private void InitRendererByCameraData(ScriptableRenderContext context, Camera camera)
        {
            MyRenderPipelineAsset pipelineAsset = GraphicsSettings.renderPipelineAsset as MyRenderPipelineAsset;
            ForwardPlusRendererData rendererData = pipelineAsset.GetRendererData<ForwardPlusRendererData>(MyRenderPipeline.RendererType.ForwardPlus);

            ForwardPlusCameraData cameraData = camera.GetComponent<ForwardPlusCameraData>();
            if(cameraData != null)
            {
                cullFarPlane = (cameraData.cullFarPlane > rendererData.clusterFarPlane) ? rendererData.clusterFarPlane : cameraData.cullFarPlane;
                maxLightsCount = cameraData.maxLightsCount;
                maxLightsCountPerCluster = cameraData.maxLightsCountPerCluster;
                debug = (camera.cameraType == CameraType.Game) ? cameraData.debug : false;
            }
            else
            {
                cullFarPlane = rendererData.defaultCullFarPlane;
                maxLightsCount = rendererData.defaultMaxLightsCount;
                maxLightsCountPerCluster = rendererData.defaultMaxLightsCountPerCluster;
            }

            fov = (camera.cameraType == CameraType.Game) ? camera.fieldOfView : rendererData.defaultFov;

            InitClusters();
        }

        private void InitClusters()
        {

        }

        public void Render(ScriptableRenderContext context, Camera camera)
        {
            _context = context;
            _camera = camera;

            PrepareForSceneWindow();
            if(!Cull())
                return;
            
            Setup();
            DrawVisibleGeometry();
            DrawGizmos();
            Submit();
        }

        // 设置
        void Setup()
        {
            _context.SetupCameraProperties(_camera);        // 先设置摄像机属性等Shader变量
            _buffer.ClearRenderTarget(true, true, Color.clear);     // 清除渲染目标，在SetupCameraProperties之后执行此步，可减少一次GL Draw的清屏操作
            _buffer.BeginSample(bufferName);
            ExecuteBuffer();
        }

        // 剪裁
        bool Cull()
        {
            if(_camera.TryGetCullingParameters(out ScriptableCullingParameters p))
            {
                cullingResults = _context.Cull(ref p);

                return true;
            }

            return false;
        }

        // 渲染物体
        void DrawVisibleGeometry()
        {
            //draw opaque geometry
            var sortingSettings = new SortingSettings(_camera);
            var drawSettings = new DrawingSettings(unlitShaderTagId, sortingSettings);
            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            _context.DrawRenderers(cullingResults, ref drawSettings, ref filteringSettings);
            
            //draw skybox
            _context.DrawSkybox(_camera);
            
            //draw transparency geometry
            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            drawSettings.sortingSettings = sortingSettings;
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;
            _context.DrawRenderers(cullingResults, ref drawSettings, ref filteringSettings);
        }

        // 向显卡提交所有绘制命令
        void Submit()
        {
            _buffer.EndSample(bufferName);
            ExecuteBuffer();
            _context.Submit();
        }

        // 将渲染命令刷入Context
        void ExecuteBuffer()
        {
            _context.ExecuteCommandBuffer(_buffer);
            _buffer.Clear();
        }
    }
}
