// 撃破の線の見える化（D-97 の V19。2026-11-17 のユーザーの決定）: ステージの範囲（KnockoutBounds）の左右と下に、うっすらとした光の膜。
// ふだんは目立たず、キャラクターが近づくほど濃く出る（SurfaceImpactFeed がキャラクターの位置を渡す）。膜の線は細い走査線と、
// 境の線（撃破の線そのもの）。uv.x は膜の幅の向き（0 = 撃破の線、1 = 外の端）、uv.y は膜の長さの向き。足して描く。
Shader "Airerage/KillMembrane"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1.6, 0.35, 0.5, 1)
        _BaseAlpha ("Base strength", Range(0, 1)) = 0.12
        _NearDistance ("Near distance (m)", Float) = 14
        _ScanSpacing ("Scan spacing (m)", Float) = 0.9
        _ScanSpeed ("Scan speed (m/s)", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _BaseAlpha;
                float _NearDistance;
                float _ScanSpacing;
                float _ScanSpeed;
            CBUFFER_END

            #define AIRERAGE_MAX_ACTORS 8
            float4 _AirerageActors[AIRERAGE_MAX_ACTORS];
            float _AirerageActorCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // いちばん近いキャラクターまでの距離で濃くする。
                float nearest = 1e6;
                int count = (int)_AirerageActorCount;
                for (int i = 0; i < AIRERAGE_MAX_ACTORS; i++)
                {
                    if (i >= count)
                    {
                        break;
                    }
                    nearest = min(nearest, length(input.positionWS.xy - _AirerageActors[i].xy));
                }
                half near = 1.0 - smoothstep(0.0, _NearDistance, nearest);
                half strength = _BaseAlpha + (1.0 - _BaseAlpha) * near * near;
                // 撃破の線（膜の内側の端）は細くはっきり、外へ向かって薄く。
                half edge = 1.0 - smoothstep(0.0, 0.06, input.uv.x);
                half body = (1.0 - input.uv.x) * 0.35;
                float scan = frac((input.positionWS.x + input.positionWS.y) / _ScanSpacing + _Time.y * _ScanSpeed / _ScanSpacing);
                half scanLine = (1.0 - smoothstep(0.0, 0.12, abs(scan - 0.5))) * (1.0 - input.uv.x) * 0.5;
                // 膜の長さの両端は溶かす。
                half ends = smoothstep(0.0, 0.08, input.uv.y) * smoothstep(1.0, 0.92, input.uv.y);
                return half4(_Color.rgb * ((edge + body + scanLine) * strength * ends), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
