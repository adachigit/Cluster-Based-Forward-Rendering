using UnityEngine;

namespace MyRenderPipeline
{
    public class ForwardPlusCameraData : MonoBehaviour
    {
        [SerializeField]
        public int m_ClusterBlockGridSize;
        [SerializeField]
        public int m_MaxLightsCount;
        [SerializeField]
        public int m_MaxLightsCountPerCluster;
        [SerializeField]
        public ComputeShader cs_ComputeClusterAABB;
        [SerializeField]
        public ComputeShader cs_AssignLightsToCluster;
        [SerializeField]
        public ComputeShader cs_ClusterSample;

    }
}
