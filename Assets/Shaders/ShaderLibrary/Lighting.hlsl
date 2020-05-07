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

    int xFrustumIndex = floor(screenX / _FrustumParams.z);
    int yFrustumIndex = floor(screenY / _FrustumParams.z);
    int frustumIndex = yFrustumIndex * _FrustumParams.x + xFrustumIndex;
    
    float4 lightGrid = _FrustumLightGrids[frustumIndex];
    int indexListStartIndex = (int)lightGrid.x;
    int lightCount = (int)lightGrid.y;

    float3 lighting = 0;
    
    for(int i = 0; i < lightCount; ++i)
    {
        uint listIndex = indexListStartIndex + i;
        lighting += GetLighting(surface, _LightIndexList[listIndex / 4][listIndex % 4]);
    }
    
    return lighting;
}

#endif
