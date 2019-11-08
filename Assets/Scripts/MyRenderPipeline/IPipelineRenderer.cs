using UnityEngine;
using UnityEngine.Rendering;

namespace MyRenderPipeline
{
    public interface IPipelineRenderer
    {
        void Setup(ScriptableRenderContext context, Camera camera);
        void Render(ScriptableRenderContext context, Camera camera);
    }
}
