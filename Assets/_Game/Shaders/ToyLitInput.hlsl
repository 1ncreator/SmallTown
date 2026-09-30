#ifndef SMALLTOWN_TOYLIT_INPUT_INCLUDED
#define SMALLTOWN_TOYLIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    half _Smoothness;
    half _SnowBlend;
    half _EmissionBoost;
    half _Pad0;
CBUFFER_END

#ifdef TOY_INSTANCED_COLORS
UNITY_INSTANCING_BUFFER_START(ToyProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _InstColorA)
    UNITY_DEFINE_INSTANCED_PROP(float4, _InstColorB)
UNITY_INSTANCING_BUFFER_END(ToyProps)
#endif

// Globals driven by the environment view
half _ST_Snow;
half _ST_Wet;
half _ST_Night;
half4 _ST_WindowColor;
half4 _ST_AmbientSky;
half4 _ST_AmbientGround;

#endif
