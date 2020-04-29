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
    [branch]
    if(_ProjectionParams.x < 0) // If current projection matrix is flipped, then flips the yIndex.
    {
        yFrustumIndex = _FrustumParams.y - (yFrustumIndex + 1);
    }
    int frustumIndex = yFrustumIndex * _FrustumParams.x + xFrustumIndex;
    
    float4 lightGrid = _FrustumLightGrids[frustumIndex];
    int indexListStartIndex = (int)lightGrid.x;
    int lightCount = (int)lightGrid.y;

    float3 lighting = 0;
//    [unroll(MAX_LIGHTS_PER_FRUSTUM_COUNT)]
    for(int i = 0; i < lightCount; ++i)
    {
        int listIndex = indexListStartIndex + i;
        lighting += GetLighting(surface, _LightIndexList[listIndex / 4][listIndex % 4]);
    }
/*    
    if(lightCount <= 16)
        return float3(0, 0, 0.4) + lighting;
    if(lightCount <= 24)
        return float3(0, 0.4, 0) + lighting;
    else
        return float3(0.4, 0, 0) + lighting;
*/
    return lighting;
}

#endif
