#ifndef CUSTOM_LIGHT_INCLUDED
#define CUSTOM_LIGHT_INCLUDED

#define MAX_LIGHTS_COUNT                    512
#define MAX_LIGHTS_COUNT_PER_FRUSTUM        32
#define MAX_FRUSTUMS_COUNT                  2048

#define MAX_CLUSTERS_COUNT                  4096

#define LIGHTINDEXLIST_ENTRIES_COUNT        4096

CBUFFER_START(_LightBuffer)
    float4 _LightDirectionsOrPositions[MAX_LIGHTS_COUNT];
    float4 _LightColors[MAX_LIGHTS_COUNT];
    float4 _LightAttenuations[MAX_LIGHTS_COUNT];
    float4 _FrustumLightGrids[MAX_FRUSTUMS_COUNT];
CBUFFER_END

CBUFFER_START(_ClusterLightBuffer)
    float4 _ClusterLightGrids[MAX_CLUSTERS_COUNT];
CBUFFER_END

CBUFFER_START(_LightIndexListBuffer1)
    int4 _LightIndexList1[LIGHTINDEXLIST_ENTRIES_COUNT];
CBUFFER_END
CBUFFER_START(_LightIndexListBuffer2)
    int4 _LightIndexList2[LIGHTINDEXLIST_ENTRIES_COUNT];
CBUFFER_END
CBUFFER_START(_LightIndexListBuffer3)
    int4 _LightIndexList3[LIGHTINDEXLIST_ENTRIES_COUNT];
CBUFFER_END
CBUFFER_START(_LightIndexListBuffer4)
    int4 _LightIndexList4[LIGHTINDEXLIST_ENTRIES_COUNT];
CBUFFER_END
CBUFFER_START(_LightIndexListBuffer5)
    int4 _LightIndexList5[LIGHTINDEXLIST_ENTRIES_COUNT];
CBUFFER_END
CBUFFER_START(_LightIndexListBuffer6)
    int4 _LightIndexList6[LIGHTINDEXLIST_ENTRIES_COUNT];
CBUFFER_END
CBUFFER_START(_LightIndexListBuffer7)
    int4 _LightIndexList7[LIGHTINDEXLIST_ENTRIES_COUNT];
CBUFFER_END
CBUFFER_START(_LightIndexListBuffer8)
    int4 _LightIndexList8[LIGHTINDEXLIST_ENTRIES_COUNT];
CBUFFER_END

float4 _FrustumParams;     //x is frustums count in horizontal, y is frustums count in vertical, z is frustum's block size, w is total frustum count.
float4 _ClusterParams;
int _LightsCount;     //Total lights count.
int _UseClusterCulling;
float _ClusterZStartStep;
float _ClusterZStepRatio;

#endif
