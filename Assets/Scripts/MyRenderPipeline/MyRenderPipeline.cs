using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyRenderPipeline
{
    public class MyRenderPipeline : RenderPipeline
    {
        IPipelineRenderer _renderer;
        IDictionary<Camera, IPipelineRenderer> cameraRendererDic = new Dictionary<Camera, IPipelineRenderer>();

        public MyRenderPipeline()
        {
            Debug.Log("Create Render Pipeline.");
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach(var cam in cameras)
            {
                if(cam.gameObject.layer == LayerMask.NameToLayer("UI"))
                    continue;
                IPipelineRenderer renderer;
                if(!cameraRendererDic.TryGetValue(cam, out renderer))
                {
                    renderer = new ForwardPlusRenderer();
                    cameraRendererDic.Add(cam, renderer);
                    renderer.Setup(context, cam);
                }

                renderer.Render(context, cam);
            }
        }
    }
}
