// 灯台の回る光の筋（2026-11-20 の要望: 灯台の光をモデルに合わせた光源として精巧に）。円錐の網（先が原点、+Y へ伸びる）に足し算で
// 塗る。根元ほど明るく先へ消え、円錐の真ん中の軸に近いほど明るい（縁は柔らかい）。光の向きがカメラの方を向くほど薄くする（回って
// 手前へ来たときに画面いっぱいに光らないように）。光は使わない。
Shader "Airerage/LightBeam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1.6, 1.5, 1.1, 1)
        _Falloff ("Falloff along the beam", Float) = 1.6
        _FacingFade ("Fade when pointing at the camera", Range(0, 1)) = 1
        _NearFade ("Fade within this distance from the camera (m)", Float) = 30
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Falloff;
                half _FacingFade;
                float _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float along : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                float3 axisWS : TEXCOORD3;
                float cameraDistance : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                // 網の高さ（+Y）が光の長さ。0 が根元、1 が先。
                output.along = saturate(input.positionOS.y);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceNormalizeViewDir(position.positionWS);
                output.axisWS = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                output.cameraDistance = distance(position.positionWS, GetCameraPositionWS());
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                // 縁（視線とすれ違う面）ほど薄く、真ん中ほど濃い。
                float core = pow(saturate(abs(dot(n, v))), 1.5);
                float along = pow(1.0 - input.along, _Falloff);
                // 光の向きがカメラの方を向くほど早めに消し（2026-11-20 の要望: 手前を向くと画面がまぶしい）、カメラの近くの部分も消す。
                float facing = abs(dot(input.axisWS, v));
                float fade = 1.0 - _FacingFade * smoothstep(0.15, 0.55, facing);
                fade *= smoothstep(_NearFade * 0.5, _NearFade, input.cameraDistance);
                return half4(_Color.rgb * core * along * fade, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
