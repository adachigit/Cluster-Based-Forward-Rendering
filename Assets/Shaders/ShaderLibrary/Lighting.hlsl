#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

#include "Light.hlsl"

float3 GetLighting(Surface surface, int index)
{
    half4 lightDirectionOrPosition = _LightDirectionsOrPositions[index];
    half4 lightAtten = _LightAttenuations[index];
    float3 lightColor = _LightColors[index].rgb;
    
    float3 lightVector = lightDirectionOrPosition.xyz - surface.position * lightDirectionOrPosition.w;
    float3 lightDirection = normalize(lightVector);
    half diffuse = saturate(dot(surface.normal, lightDirection));
    
    half rangeFade = dot(lightVector, lightVector) * lightAtten.x;
    rangeFade = saturate(1.0 - rangeFade);
//    rangeFade = saturate(1.0 - rangeFade * rangeFade);
//    rangeFade *= rangeFade;
    
   half distanceSqr = max(dot(lightVector, lightVector), 0.00001);
    diffuse *= rangeFade / distanceSqr;
    
    return diffuse * lightColor;
}

int GetLightIndex(int indexArrayIndex, int indexInArray)
{
    int lightIndex = 0;
    
    if(indexArrayIndex < 0)
        return 0;
        
    switch(indexArrayIndex)
    {
    case 0:
        lightIndex = _LightIndexList1[indexInArray / 4][indexInArray % 4];
        break;
    case 1:
        lightIndex = _LightIndexList2[indexInArray / 4][indexInArray % 4];
        break;
    case 2:
        lightIndex = _LightIndexList3[indexInArray / 4][indexInArray % 4];
        break;
    case 3:
        lightIndex = _LightIndexList4[indexInArray / 4][indexInArray % 4];
        break;
    }
    
    return lightIndex;
}

int2 GetFrustumIndex2D(float2 screenCoord)
{
    int xFrustumIndex = floor(screenCoord.x / _FrustumParams.z);
    int yFrustumIndex = floor(screenCoord.y / _FrustumParams.z);
    
    return int2(xFrustumIndex, yFrustumIndex);
}

int3 GetClusterIndex3D(Surface surface, float2 screenCoord)
{
    int xClusterIndex = floor(screenCoord.x / _ClusterParams.w);
    int yClusterIndex = floor(screenCoord.y / _ClusterParams.w);

    float4 posVS = mul(unity_MatrixV, float4(surface.position, 1.0));
//    float tmp = 1.0 - (surface.positionCS.w - _ProjectionParams.y) * (1.0 - _ClusterZStepRatio) / _ClusterZStartStep;
//    uint zClusterIndex = float(log2(tmp)) / log2(_ClusterZStepRatio);
    int zClusterIndex = log2(-posVS.z - _ProjectionParams.y);
    
    return int3(xClusterIndex, yClusterIndex, zClusterIndex);
}

int4 GetLightGridInfo(Surface surface, float2 screenCoord)
{
    [branch]
    if(_UseClusterCulling > 0)
    {
        float3 clusterIndex3D = GetClusterIndex3D(surface, screenCoord);
        float clusterIndex1D = clusterIndex3D.y * _ClusterParams.x + clusterIndex3D.x + (clusterIndex3D.z * _ClusterParams.x * _ClusterParams.y);
        
        return _ClusterLightGrids[clusterIndex1D];
    }
    else
    {
        float2 frustumIndex2D = GetFrustumIndex2D(screenCoord);
        float frustumIndex1D = frustumIndex2D.y * _FrustumParams.x + frustumIndex2D.x;
        
        return _FrustumLightGrids[frustumIndex1D];
    }
}

float3 GetLightingByScreenCoord(Surface surface, float2 screenCoord)
{
    int screenX = screenCoord.x;
    int screenY = screenCoord.y;
    
#if UNITY_UV_STARTS_AT_TOP
    if(_ProjectionParams.x < 0)
    {
        screenY = _ScreenParams.y - screenY;
    }
#else
    screenY = _ScreenParams.y - screenY;
#endif

    int4 lightGrid = GetLightGridInfo(surface, float2(screenX, screenY));
 
    float indexListStartIndex = lightGrid.x;
    float lightCount = lightGrid.y;
    float indexListArrayIndex = lightGrid.z;

    float3 lighting = 0;
    
    for(float i = 0; i < lightCount; ++i)
    {
        lighting += GetLighting(surface, GetLightIndex(indexListArrayIndex, indexListStartIndex + i));
    }
  
    return lighting;
}

#endif
