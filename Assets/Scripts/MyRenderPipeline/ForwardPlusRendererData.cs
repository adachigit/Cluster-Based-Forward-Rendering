using UnityEngine;

namespace MyRenderPipeline
{
    [CreateAssetMenu(menuName = "Rendering/ForwardPlus Renderer Data")]
    class ForwardPlusRendererData : ScriptablePipelineRendererData
    {
        public enum LightsCullingType
        {
            Frustum,
            Cluster,
        }
        
        [Header("Common")]
        public LightsCullingType lightsCullingType;
        public int maxLightsCount;
        
        [Header("Frustum Culling")]
        public int frustumGridSize;
        public int maxLightsCountPerFrustum;

        [Header("Cluster Culling")]
        public int clusterGridSize;
        public float clusterZStartStep;
        public float clusterZStepRatio;
        public float clusterZFarMax;
        public int maxLightsCountPerCluster;
        
        public float defaultFov;
        public ComputeShader clusterAABBComputerShader;
        public ComputeShader assignLightsComputerShader;
    }
}
