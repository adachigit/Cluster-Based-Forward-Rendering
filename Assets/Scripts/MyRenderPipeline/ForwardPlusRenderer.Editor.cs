using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace MyRenderPipeline
{
    partial class ForwardPlusRenderer
    {
#if UNITY_EDITOR
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
#endif

    }
}