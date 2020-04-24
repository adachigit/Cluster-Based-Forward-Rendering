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

float3 GetLightingByScreenCoord(Surface surface, float2 screenCoord)
{
    int xFrustumIndex = floor(screenCoord.x / _FrustumParams.z);
    int yFrustumIndex = floor(screenCoord.y / _FrustumParams.z);
    if(_ProjectionParams.x < 0) // If current projection matrix is flipped, then flips the yIndex.
    {
        yFrustumIndex = _FrustumParams.y - (yFrustumIndex + 1);
    }
    int frustumIndex = yFrustumIndex * _FrustumParams.x + xFrustumIndex;
    
    float4 lightGrid = _FrustumLightGrids[frustumIndex];
    int indexListStartIndex = (int)lightGrid.x;
    int lightCount = (int)lightGrid.y;

    float3 lighting = 0;
    for(int i = 0; i < lightCount; ++i)
    {
        int listIndex = indexListStartIndex + i;
        lighting += GetLighting(surface, _LightIndexList[listIndex / 4][listIndex % 4]);
    }
    
//    if(frustumIndex >= 1)
//        return float3(1, 0, 0);
    
    return lighting;//GetLighting(surface, _LightIndexList[indexListStartIndex / 4][indexListStartIndex % 4]);
//    return _LightColors[_LightIndexList[indexListStartIndex / 4][indexListStartIndex % 4]].rgb;
//    return _LightColors[_LightIndexList[50][0]].rgb;
//    return float3((float)frustumIndex / _FrustumParams.w, 0, 0);
//    return float3((float)lightCount / MAX_LIGHTS_PER_FRUSTUM_COUNT, 0, 0);
}

#endif
