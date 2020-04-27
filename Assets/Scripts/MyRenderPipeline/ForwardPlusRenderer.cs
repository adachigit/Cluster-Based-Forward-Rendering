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
        static ShaderTagId litShaderTagId = new ShaderTagId("SRPDefaultLit");
        
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

        private CameraType cameraType;
        private bool debug;

        private bool useDynamicBatching;
        private bool useGPUInstancing;
        
        public ForwardPlusRenderer(bool useDynamicBatching, bool useGPUInstancing)
        {
            this.useDynamicBatching = useDynamicBatching;
            this.useGPUInstancing = useGPUInstancing;
        }
        
        public void Setup(ScriptableRenderContext context, Camera camera)
        {
            this.camera = camera;

            lightsCullingJob = new FrustumLightsCullingJob();
            lightsCullingJob.Init(camera, context);
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
            
            JobsAfterRender(camera, context, cullingResults);
        }

        // 设置
        void Setup()
        {
            context.SetupCameraProperties(camera);        // 先设置摄像机属性等Shader变量
            var clearFlags = camera.clearFlags;
            bool depthClear = clearFlags == CameraClearFlags.Depth || clearFlags == CameraClearFlags.Color || clearFlags == CameraClearFlags.SolidColor || clearFlags == CameraClearFlags.Skybox;
            bool colorClear = clearFlags == CameraClearFlags.Color || clearFlags == CameraClearFlags.SolidColor;
            cmdBuffer.ClearRenderTarget(depthClear, colorClear, (colorClear ? camera.backgroundColor : Color.clear));     // 清除渲染目标，在SetupCameraProperties之后执行此步，可减少一次GL Draw的清屏操作
            cmdBuffer.BeginSample(CMB_BUFFER_NAME);
            ExecuteBuffer();
        }

        // 剪裁
        bool Cull()
        {
            if(camera.TryGetCullingParameters(out ScriptableCullingParameters p))
            {
                lightsCullingJob.BeforeCulling(ref p);
                cullingResults = context.Cull(ref p);

                return true;
            }

            return false;
        }

        // 渲染物体
        void DrawVisibleGeometry()
        {
            //draw opaque geometry
            var sortingSettings = new SortingSettings(camera)
            {
                criteria = SortingCriteria.CommonOpaque,
            };
            var drawSettings = new DrawingSettings(unlitShaderTagId, sortingSettings)
            {
                enableDynamicBatching = useDynamicBatching,
                enableInstancing = useGPUInstancing,
            };
            drawSettings.SetShaderPassName(1, litShaderTagId);
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
            DisposeAllJobs();
        }

        private void JobsBeforeRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
            lightsCullingJob.BeforeRender(camera, context, cullingResults);
        }

        private void JobsAfterRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults)
        {
            lightsCullingJob.AfterRender(camera, context, cullingResults);
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
