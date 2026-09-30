Shader "SmallTown/Water"
{
    Properties
    {
        _ShallowColor("Shallow", Color) = (0.46, 0.78, 0.84, 1)
        _DeepColor("Deep", Color) = (0.18, 0.46, 0.60, 1)
        _Alpha("Alpha", Range(0, 1)) = 0.86
        _WaveScale("Wave Scale", Float) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half _Alpha;
                half _WaveScale;
            CBUFFER_END

            half _ST_Wet;
            half _ST_Night;
            half4 _ST_AmbientSky;
            float _ST_Time;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half fog : TEXCOORD2; };

            V vert(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(ws);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float t = _ST_Time;
                float2 p = i.positionWS.xz;
                float3 n = normalize(i.normalWS);
                if (n.y > 0.5)
                {
                    float2 g = float2(
                        sin(p.x * 0.35 + t * 1.3) * 0.5 + sin(p.y * 0.21 + p.x * 0.13 + t * 0.9) * 0.5,
                        cos(p.y * 0.31 - t * 1.1) * 0.5 + sin(p.x * 0.17 - p.y * 0.23 + t * 1.7) * 0.5);
                    float rain = _ST_Wet * (sin(p.x * 3.1 + t * 9.0) * sin(p.y * 2.7 - t * 8.0));
                    n = normalize(float3(g.x * _WaveScale * 0.35 + rain * 0.08, 1.0, g.y * _WaveScale * 0.35 + rain * 0.08));
                }
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light main = GetMainLight();
                half fres = pow(1.0 - saturate(dot(n, viewDir)), 3.0);
                half3 baseCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, 0.45 + 0.35 * n.y);
                half ndl = saturate(dot(n, main.direction));
                half3 lit = baseCol * (_ST_AmbientSky.rgb * 0.9 + main.color * ndl * 0.55);
                float3 h = normalize(main.direction + viewDir);
                half spec = pow(saturate(dot(n, h)), 90.0) * 1.6;
                lit += main.color * spec;
                lit += fres * _ST_AmbientSky.rgb * 0.5;
                lit = MixFog(lit, i.fog);
                return half4(lit, saturate(_Alpha + fres * 0.1));
            }
            ENDHLSL
        }
    }
}
