using UnityEngine;

namespace MyRenderPipeline
{
    public class ForwardPlusCameraData : MonoBehaviour
    {
        [SerializeField]
        public float cullFarPlane;
        [SerializeField]
        public int clusterGridBlockSize;
        [SerializeField]
        public int maxLightsCount;
        [SerializeField]
        public int maxLightsCountPerCluster;
        [SerializeField]
        public bool debug;
    }
}
