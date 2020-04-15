using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyRenderPipeline
{
    public abstract class BaseRendererJob : IDisposable
    {
        public abstract void BeforeRender(Camera camera, ScriptableRenderContext context, CullingResults cullingResults);
        public abstract void AfterRender();
        public abstract void Dispose();
    }
}