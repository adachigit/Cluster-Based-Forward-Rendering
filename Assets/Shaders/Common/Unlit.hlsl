#ifndef FWDPLUS_UNLIT_INCLUDED
#define FWDPLUS_UNLIT_INCLUDED

#define UNITY_MATRIX_M unity_ObjectToWorld

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"

void UnlitPassVertex()
{
}

float4 UnlitPassFragment() : SV_TARGET
{
}

#endif