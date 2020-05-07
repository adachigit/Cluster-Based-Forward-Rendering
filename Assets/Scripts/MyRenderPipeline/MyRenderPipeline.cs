using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MyRenderPipeline
{
    public class MyRenderPipeline : RenderPipeline
    {
        public enum RendererType
        {
            ForwardPlus,
        }

        private Camera lastRenderCamera;
        IDictionary<Camera, IPipelineRenderer> cameraRendererDic = new Dictionary<Camera, IPipelineRenderer>();

        private bool useDynamicBatching;
        private bool useGPUInstancing;

        public MyRenderPipeline(bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher)
        {
            this.useDynamicBatching = useDynamicBatching;
            this.useGPUInstancing = useGPUInstancing;
            GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
            GraphicsSettings.lightsUseLinearIntensity = true;
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach(var cam in cameras)
            {
                if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection || cam.cameraType == CameraType.VR)// == LayerMask.NameToLayer("UI"))
                {
                    continue;
                }
                IPipelineRenderer renderer;
                if(!cameraRendererDic.TryGetValue(cam, out renderer))
                {
                    renderer = new ForwardPlusRenderer(useDynamicBatching, useGPUInstancing);
                    cameraRendererDic.Add(cam, renderer);
                    renderer.Setup(context, cam);
                }

                renderer.Render(context, cam, lastRenderCamera);
                lastRenderCamera = cam;
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            foreach (var renderer in cameraRendererDic.Values)
            {
                renderer.Dispose();
            }
            
            cameraRendererDic.Clear();
            lastRenderCamera = null;
        }
    }
}
