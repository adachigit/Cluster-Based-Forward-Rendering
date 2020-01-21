using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace MyRenderPipeline
{
    partial class ForwardPlusRenderer
    {
#if UNITY_EDITOR
        struct ClusterGizmosInfo
        {
            public Vector3 minLeftTop;
            public Vector3 minLeftBottom;
            public Vector3 minRightTop;
            public Vector3 minRightBottom;
            public Vector3 maxLeftTop;
            public Vector3 maxLeftBottom;
            public Vector3 maxRightTop;
            public Vector3 maxRightBottom;
        };
        private ClusterGizmosInfo[] clusterGizmoInfos;
        private float clusterGizmoFactor = 0.7f;
        
        partial void DrawGizmos()
        {
            if(Handles.ShouldRenderGizmos())
            {
                context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
                context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
            }
        }

        partial void PrepareForSceneWindow()
        {
            if(camera.cameraType == CameraType.SceneView)
            {
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
            }
        }

        partial void TransformClusterGizmoInfos()
        {
            clusterGizmoInfos = new ClusterGizmosInfo[clusterAABBsData.Length];

            for(int i = 0; i < clusterAABBsData.Length; ++i)
            {
                AABB aabb = clusterAABBsData[i];
                Vector4 delta = (aabb.Max - aabb.Min) * (1.0f - clusterGizmoFactor);
                Vector4 Min = aabb.Min + delta;
                Vector4 Max = aabb.Max - delta;

                ClusterGizmosInfo info = new ClusterGizmosInfo();
                info.minLeftTop = new Vector3(Min.x, Max.y, Min.z);
                info.minLeftBottom = new Vector3(Min.x, Min.y, Min.z);
                info.minRightTop = new Vector3(Max.x, Max.y, Min.z);
                info.minRightBottom = new Vector3(Max.x, Min.y, Min.z);
                info.maxLeftTop = new Vector3(Min.x, Max.y, Max.z);
                info.maxLeftBottom = new Vector3(Min.x, Min.y, Max.z);
                info.maxRightTop = new Vector3(Max.x, Max.y, Max.z);
                info.maxRightBottom = new Vector3(Max.x, Min.y, Max.z);
                clusterGizmoInfos[i] = info;
            }
        }
#endif

    }
}