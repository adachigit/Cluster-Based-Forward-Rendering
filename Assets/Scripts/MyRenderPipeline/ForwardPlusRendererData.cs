using UnityEngine;

namespace MyRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/ForwardPlus Renderer Data")]
    class ForwardPlusRendererData : ScriptablePipelineRendererData
    {
        public int clusterBlockGridSize;
        public float clusterFarPlane;
        public float defaultCullFarPlane;
        public int defaultMaxLightsCount;
        public int defaultMaxLightsCountPerCluster;
        public float defaultFov;
        public ComputeShader clusterAABBComputerShader;
        public ComputeShader assignLightsComputerShader;
    }
}
