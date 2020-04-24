using Unity.Mathematics;
using UnityEngine;

namespace MyRenderPipeline
{
    public class ShaderIdsAndConstants
    {
        public static readonly int PropId_FrustumParamsId = Shader.PropertyToID("_FrustumParams");
        public static readonly int PropId_LightsCountId = Shader.PropertyToID("_LightsCount");

        public static readonly int ConstBufId_LightBufferId = Shader.PropertyToID("_LightBuffer");
        public static readonly int ConstBufId_LightIndexListBufferId = Shader.PropertyToID("_LightIndexListBuffer");

        public static readonly int MaxConstantBufferEntriesCount = 4096;
        
        public static readonly int MaxLightsCount = 512;
        public static readonly int MaxLightsCountPerFrustum = 32;
        public static readonly int MaxFrustumsCount = 2048;
        
        // constant buffer _LightBuffer size
        public static readonly unsafe int PropSize_LightDirectionsOrPositions = sizeof(float4) * MaxLightsCount;
        public static readonly unsafe int PropSize_LightColors = sizeof(float4) * MaxLightsCount;
        public static readonly unsafe int PropSize_LightAttenuations = sizeof(float4) * MaxLightsCount;
        public static readonly unsafe int PropSize_FrustumLightGrids = sizeof(float4) * MaxFrustumsCount;
        public static readonly int ConstBuf_LightBuffer_Size = PropSize_LightDirectionsOrPositions + PropSize_LightColors + PropSize_LightAttenuations + PropSize_FrustumLightGrids;
        // constant buffer _LightBuffer total float4 count
        public static readonly int ConstBuf_LightBuffer_EntriesCount = MaxLightsCount * 3 + MaxFrustumsCount;
        
        // constant buffer _LightIndexListBuffer total float4 count
        public static readonly int ConstBuf_LightIndexListBuffer_Entries_Count = MaxConstantBufferEntriesCount;
        // constant buffer _LightIndexListBuffer
        public static readonly unsafe int ConstBuf_LightIndexListBuffer_Size = sizeof(int4) * ConstBuf_LightIndexListBuffer_Entries_Count;

        //  prop's start offset of constant buffer _LightBuffer in float4
        public static readonly int PropOffset_LightDirectionsOrPositions = 0;
        public static readonly int PropOffset_LightColors = PropOffset_LightDirectionsOrPositions + MaxLightsCount;
        public static readonly int PropOffset_LightAttenuations = PropOffset_LightColors + MaxLightsCount;
        public static readonly int PropOffset_FrustumLightGrids = PropOffset_LightAttenuations + MaxLightsCount;
    }
}
