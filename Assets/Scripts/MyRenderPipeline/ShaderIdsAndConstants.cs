using Unity.Mathematics;
using UnityEngine;

namespace MyRenderPipeline
{
    public class ShaderIdsAndConstants
    {
        public static readonly int PropId_FrustumParamsId = Shader.PropertyToID("_FrustumParams");
        public static readonly int PropId_ClusterParamsId = Shader.PropertyToID("_ClusterParams");
        public static readonly int PropId_LightsCountId = Shader.PropertyToID("_LightsCount");
        public static readonly int PropId_UseClusterCulling = Shader.PropertyToID("_UseClusterCulling");
        public static readonly int PropId_ClusterZStartStep = Shader.PropertyToID("_ClusterZStartStep");
        public static readonly int PropId_ClusterZStepRatio = Shader.PropertyToID("_ClusterZStepRatio");

        public static readonly int ConstBufId_LightBufferId = Shader.PropertyToID("_LightBuffer");
        public static readonly int ConstBufId_ClusterLightBufferId = Shader.PropertyToID("_ClusterLightBuffer");
        public static readonly int ConstBufId_LightIndexListBuffer1Id = Shader.PropertyToID("_LightIndexListBuffer1");
        public static readonly int ConstBufId_LightIndexListBuffer2Id = Shader.PropertyToID("_LightIndexListBuffer2");
        public static readonly int ConstBufId_LightIndexListBuffer3Id = Shader.PropertyToID("_LightIndexListBuffer3");
        public static readonly int ConstBufId_LightIndexListBuffer4Id = Shader.PropertyToID("_LightIndexListBuffer4");

        public static readonly int MaxConstantBufferEntriesCount = 4096;
        
        public static readonly int MaxLightsCount = 512;
        public static readonly int MaxFrustumsCount = 2048;
        public static readonly int MaxClustersCount = 4096;

        public static readonly int LightIndexList_Capacity = MaxConstantBufferEntriesCount * 4;

        public const int MaxLightIndexListCount = 4;
        
        // constant buffer _LightBuffer size
        public static readonly unsafe int PropSize_LightDirectionsOrPositions = sizeof(float4) * MaxLightsCount;
        public static readonly unsafe int PropSize_LightColors = sizeof(float4) * MaxLightsCount;
        public static readonly unsafe int PropSize_LightAttenuations = sizeof(float4) * MaxLightsCount;
        public static readonly unsafe int PropSize_FrustumLightGrids = sizeof(float4) * MaxFrustumsCount;
        public static readonly int ConstBuf_LightBuffer_Size = PropSize_LightDirectionsOrPositions + PropSize_LightColors + PropSize_LightAttenuations + PropSize_FrustumLightGrids;
        // constant buffer _LightBuffer total float4 count
        public static readonly int ConstBuf_LightBuffer_EntriesCount = MaxLightsCount * 3 + MaxFrustumsCount;
        
        //constant buffer _ClusterLightGrids total float4 count
        public static readonly int ConstBuf_ClusterLightBuffer_EntriesCount = MaxClustersCount;
        //constant buffer _ClusterLightGrids size
        public static readonly unsafe int ConstBuf_ClusterLightBuffer_Size = sizeof(float4) * ConstBuf_ClusterLightBuffer_EntriesCount;
        
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
