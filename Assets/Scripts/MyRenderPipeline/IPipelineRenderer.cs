using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyRenderPipeline
{
    public interface IPipelineRenderer : IDisposable
    {
        void Setup(ScriptableRenderContext context, Camera camera);
        void Render(ScriptableRenderContext context, Camera camera, Camera lastRenderCamera);
    }
}
