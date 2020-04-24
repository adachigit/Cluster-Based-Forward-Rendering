#ifndef CUSTOM_LIGHT_INCLUDED
#define CUSTOM_LIGHT_INCLUDED

#define MAX_LIGHTS_COUNT                    512
#define MAX_LIGHTS_PER_FRUSTUM_COUNT        32
#define MAX_FRUSTUMS_COUNT                  2048

#define LIGHTINDEXLIST_ENTRIES_COUNT        4096

CBUFFER_START(_LightBuffer)
    float4 _LightDirectionsOrPositions[MAX_LIGHTS_COUNT];
    float4 _LightColors[MAX_LIGHTS_COUNT];
    float4 _LightAttenuations[MAX_LIGHTS_COUNT];
    float4 _FrustumLightGrids[MAX_FRUSTUMS_COUNT];
CBUFFER_END

CBUFFER_START(_LightIndexListBuffer)
    int4 _LightIndexList[LIGHTINDEXLIST_ENTRIES_COUNT];
CBUFFER_END

float4 _FrustumParams;     //x is frustums count in horizontal, y is frustums count in vertical, z is frustum's block size, w is total frustum count.
int _LightsCount;     //Total lights count.

#endif
