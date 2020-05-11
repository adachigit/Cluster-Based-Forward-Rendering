#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

#include "Light.hlsl"

float3 GetLighting(Surface surface, int index)
{
    float4 lightDirectionOrPosition = _LightDirectionsOrPositions[index];
    float4 lightAtten = _LightAttenuations[index];
    float3 lightColor = _LightColors[index].rgb;
    
    float3 lightVector = lightDirectionOrPosition.xyz - surface.position * lightDirectionOrPosition.w;
    float3 lightDirection = normalize(lightVector);
    float diffuse = saturate(dot(surface.normal, lightDirection));
    
    float rangeFade = dot(lightVector, lightVector) * lightAtten.x;
    rangeFade = saturate(1.0 - rangeFade * rangeFade);
    rangeFade *= rangeFade;
    
    float distanceSqr = max(dot(lightVector, lightVector), 0.00001);
    diffuse *= rangeFade / distanceSqr;
    
    return diffuse * lightColor;
}

int GetLightIndex(int indexArrayIndex, int indexInArray)
{
    float lightIndex = 0;
    
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
    case 4:
        lightIndex = _LightIndexList5[indexInArray / 4][indexInArray % 4];
        break;
    case 5:
        lightIndex = _LightIndexList6[indexInArray / 4][indexInArray % 4];
        break;
    case 6:
        lightIndex = _LightIndexList7[indexInArray / 4][indexInArray % 4];
        break;
    case 7:
        lightIndex = _LightIndexList8[indexInArray / 4][indexInArray % 4];
        break;
    }
    
    return floor(lightIndex + 0.5);
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
    float tmp = 1.0 - (-posVS.z - _ProjectionParams.y) * (1.0 - _ClusterZStepRatio) / _ClusterZStartStep;
    int zClusterIndex = floor(log(tmp) / log(_ClusterZStepRatio));
    
    return int3(xClusterIndex, yClusterIndex, zClusterIndex);
}

float4 GetLightGridInfo(Surface surface, float2 screenCoord)
{
    [branch]
    if(_UseClusterCulling > 0)
    {
        int3 clusterIndex3D = GetClusterIndex3D(surface, screenCoord);
        int clusterIndex1D = clusterIndex3D.y * _ClusterParams.x + clusterIndex3D.x + (clusterIndex3D.z * _ClusterParams.x * _ClusterParams.y);
        
        return _ClusterLightGrids[clusterIndex1D];
    }
    else
    {
        int2 frustumIndex2D = GetFrustumIndex2D(screenCoord);
        int frustumIndex1D = frustumIndex2D.y * _FrustumParams.x + frustumIndex2D.x;
        
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

    float4 lightGrid = GetLightGridInfo(surface, float2(screenX, screenY));
    
    int indexListStartIndex = floor(lightGrid.x + 0.5);
    int lightCount = floor(lightGrid.y + 0.5);
    int indexListArrayIndex = floor(lightGrid.z + 0.5);

    float3 lighting = 0;
    
    for(int i = 0; i < lightCount; ++i)
    {
        uint listIndex = indexListStartIndex + i;
        lighting += GetLighting(surface, GetLightIndex(indexListArrayIndex, listIndex));
    }
  
//    int3 clusterIndex = GetClusterIndex3D(surface, screenCoord);
//    return float3(((float)clusterIndex.z / _ClusterParams.z), 0, 0);
//    float4 posVS = mul(unity_MatrixV, float4(surface.position, 1.0));
//    return float3((-posVS.z - _ProjectionParams.y) / _ProjectionParams.z, 0, 0);
    return lighting;
}

#endif
