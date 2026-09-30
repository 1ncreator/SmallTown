Shader "SmallTown/Glow"
{
    // Additive unlit glow for lamp light pools, festival bulbs and emergency lights.
    // UV (0,0)-(1,1) quads get a soft radial falloff; geometry with UV 0.5 glows uniformly.
    Properties
    {
        _Color("Color", Color) = (1, 0.8, 0.5, 1)
        _Intensity("Intensity", Float) = 1
        _NightOnly("Night Only", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _NightOnly;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(GlowProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstColorA)
            UNITY_INSTANCING_BUFFER_END(GlowProps)

            half _ST_Night;

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V vert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                half4 c = v.color * _Color;
            #if defined(UNITY_INSTANCING_ENABLED)
                c *= UNITY_ACCESS_INSTANCED_PROP(GlowProps, _InstColorA);
            #endif
                o.color = c;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 d = i.uv * 2.0 - 1.0;
                half a = saturate(1.0 - dot(d, d));
                a *= a;
                half night = lerp(1.0h, _ST_Night, _NightOnly);
                return half4(i.color.rgb * a * _Intensity * night * i.color.a, 0);
            }
            ENDHLSL
        }
    }
}
