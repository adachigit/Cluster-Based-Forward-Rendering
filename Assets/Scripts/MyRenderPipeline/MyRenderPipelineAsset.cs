using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/My Rendering Pipeline")]
    public class MyRenderPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline()
        {
            return new MyRenderPipeline();
        }
    }
}