using UnityEngine;

namespace MyRenderPipeline
{
    public class ForwardPlusCameraData : MonoBehaviour
    {
        [Header("Common")]
        [SerializeField]
        public int maxLightsCount;
        
        [Header("Frustum Culling")]
        [SerializeField]
        public int frustumGridSize;
        [SerializeField]
        public int maxLightsCountPerFrustum;
        
        [Header("Cluster Culling")]
        [SerializeField]
        public int clusterGridSize;
        [SerializeField]
        public float clusterZStartStep;
        [SerializeField]
        public float clusterZStepRatio;
        [SerializeField]
        public float clusterZFarMax;
        [SerializeField]
        public int maxLightsCountPerCluster;
        
        [SerializeField]
        public bool debug;
    }
}
