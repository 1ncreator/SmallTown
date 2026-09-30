#ifndef SMALLTOWN_TOYLIT_FORWARD_INCLUDED
#define SMALLTOWN_TOYLIT_FORWARD_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Vertex layout of all procedural meshes:
//   COLOR     albedo
//   TEXCOORD1 x = night emission (lit windows, lamps, headlights)
//             y = tint mask A (instance colour A), z = tint mask B (instance colour B)
//             w = snow mask (1 = can be covered by snow)
struct ToyAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    half4 color       : COLOR;
    float4 uv1        : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ToyVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    half4 color       : TEXCOORD2;
    float4 uv1        : TEXCOORD3;
    half fogFactor    : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

ToyVaryings ToyVert(ToyAttributes v)
{
    ToyVaryings o = (ToyVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 ws = TransformObjectToWorld(v.positionOS.xyz);
    o.positionWS = ws;
    o.positionCS = TransformWorldToHClip(ws);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    half4 col = v.color * _BaseColor;
#ifdef TOY_INSTANCED_COLORS
    float4 a = UNITY_ACCESS_INSTANCED_PROP(ToyProps, _InstColorA);
    float4 b = UNITY_ACCESS_INSTANCED_PROP(ToyProps, _InstColorB);
    col.rgb = lerp(col.rgb, a.rgb, saturate(v.uv1.y));
    col.rgb = lerp(col.rgb, b.rgb, saturate(v.uv1.z));
#endif
    o.color = col;
    o.uv1 = v.uv1;
    o.fogFactor = ComputeFogFactor(o.positionCS.z);
    return o;
}

half4 ToyFrag(ToyVaryings i) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float3 n = normalize(i.normalWS);
    half3 albedo = i.color.rgb;
    half up = saturate((n.y - 0.35h) * 2.2h);
    half snow = saturate(_ST_Snow * up * i.uv1.w * _SnowBlend * 1.15h);
    half wet = _ST_Wet * up * (1.0h - snow);
    albedo *= lerp(1.0h, 0.74h, wet);
    albedo = lerp(albedo, half3(0.94h, 0.96h, 1.0h), snow);

    SurfaceData s = (SurfaceData)0;
    s.albedo = albedo;
    s.alpha = 1.0h;
    s.metallic = 0.0h;
    s.specular = half3(0.0h, 0.0h, 0.0h);
    s.smoothness = saturate(_Smoothness + wet * 0.62h - snow * 0.1h);
    s.occlusion = 1.0h;
    s.normalTS = half3(0.0h, 0.0h, 1.0h);
    s.emission = _ST_WindowColor.rgb * i.uv1.x * _ST_Night * 2.4h * _EmissionBoost;

    InputData d = (InputData)0;
    d.positionWS = i.positionWS;
    d.positionCS = i.positionCS;
    d.normalWS = n;
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
#else
    d.shadowCoord = float4(0, 0, 0, 0);
#endif
    d.fogCoord = i.fogFactor;
    d.vertexLighting = half3(0, 0, 0);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
    d.bakedGI = lerp(_ST_AmbientGround.rgb, _ST_AmbientSky.rgb, n.y * 0.5h + 0.5h);
    d.shadowMask = half4(1, 1, 1, 1);

    half4 c = UniversalFragmentPBR(d, s);
    c.rgb = MixFog(c.rgb, i.fogFactor);
    c.a = 1.0h;
    return c;
}

#endif
