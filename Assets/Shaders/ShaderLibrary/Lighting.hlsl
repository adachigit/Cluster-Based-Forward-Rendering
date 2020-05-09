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
    switch(indexArrayIndex)
    {
    case 0:
        return _LightIndexList1[indexInArray / 4][indexInArray % 4];
    case 1:
        return _LightIndexList2[indexInArray / 4][indexInArray % 4];
    case 2:
        return _LightIndexList3[indexInArray / 4][indexInArray % 4];
    case 3:
        return _LightIndexList4[indexInArray / 4][indexInArray % 4];
    case 4:
        return _LightIndexList5[indexInArray / 4][indexInArray % 4];
    case 5:
        return _LightIndexList6[indexInArray / 4][indexInArray % 4];
    case 6:
        return _LightIndexList7[indexInArray / 4][indexInArray % 4];
    case 7:
        return _LightIndexList8[indexInArray / 4][indexInArray % 4];
    }
    
    return 0;
}

float GetClusterZIndex(Surface surface, float2 screenCoord)
{
    int xClusterIndex = floor(screenCoord.x / _ClusterParams.z);
    int yClusterIndex = floor(screenCoord.y / _ClusterParams.z);

    float4 posVS = mul(unity_MatrixV, float4(surface.position, 1.0));
    float z = (posVS.z + _ProjectionParams.y) / _ClusterZStartStep;// + _ClusterZStartStep;
    int zClusterIndex = floor((float)log(-z) / log(_ClusterZStepRatio));
    
    return zClusterIndex;
}

float4 GetLightGridInfo(Surface surface, float2 screenCoord)
{
    if(_UseClusterCulling > 0)
    {
        int xClusterIndex = floor(screenCoord.x / _ClusterParams.z);
        int yClusterIndex = floor(screenCoord.y / _ClusterParams.z);

        float4 posVS = mul(unity_MatrixV, float4(surface.position, 1.0));
        float z = (posVS.z + _ProjectionParams.y) / _ClusterZStartStep;// + _ClusterZStartStep;
        int zClusterIndex = floor((float)log(-z) / log(_ClusterZStepRatio));

        int clusterIndex = yClusterIndex * _ClusterParams.x + xClusterIndex + (zClusterIndex * _ClusterParams.x * _ClusterParams.y);
        
        return _ClusterLightGrids[clusterIndex];
    }
    else
    {
        int xFrustumIndex = floor(screenCoord.x / _FrustumParams.z);
        int yFrustumIndex = floor(screenCoord.y / _FrustumParams.z);
        int frustumIndex = yFrustumIndex * _FrustumParams.x + xFrustumIndex;
        
        return _FrustumLightGrids[frustumIndex];
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
/*
    int xFrustumIndex = floor(screenX / _FrustumParams.z);
    int yFrustumIndex = floor(screenY / _FrustumParams.z);
    int frustumIndex = yFrustumIndex * _FrustumParams.x + xFrustumIndex;
    
    float4 lightGrid = _FrustumLightGrids[frustumIndex];
*/
    float4 lightGrid = GetLightGridInfo(surface, float2(screenX, screenY));
    
    int indexListStartIndex = (int)lightGrid.x;
    int lightCount = (int)lightGrid.y;
    int indexListArrayIndex = (int)lightGrid.z;

    float3 lighting = 0;
    
    for(int i = 0; i < lightCount; ++i)
    {
        uint listIndex = indexListStartIndex + i;
        lighting += GetLighting(surface, GetLightIndex(indexListArrayIndex, listIndex));
    }
    
    return lighting;
}

#endif
