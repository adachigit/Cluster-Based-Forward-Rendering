using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/My Rendering Pipeline")]
    public class MyRenderPipelineAsset : RenderPipelineAsset
    {
        [System.Serializable]
        public class RendererDataInfo
        {
            public MyRenderPipeline.RendererType rendererType;
            public ScriptablePipelineRendererData rendererData;
        }

        [SerializeField] private bool useDynamicBatching = true;
        [SerializeField] private bool useGPUInstancing = true;
        [SerializeField] private bool useSRPBatcher = true;
        

        public List<RendererDataInfo> rendererDataInfos;

        public T GetRendererData<T>(MyRenderPipeline.RendererType type) where T : ScriptablePipelineRendererData
        {
            foreach(var info in rendererDataInfos)
            {
                if(info.rendererType.Equals(type))
                {
                    return (T)info.rendererData;
                }
            }

            return null;
        }

        protected override RenderPipeline CreatePipeline()
        {
            return new MyRenderPipeline(useDynamicBatching, useGPUInstancing, useSRPBatcher);
        }
    }
}