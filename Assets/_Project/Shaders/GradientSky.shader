// ステージの空（D-97 の V15。2026-11-17 のユーザーの決定）: 上から下へのグラデーション、地平線の明るいもや、太陽の光。
// 地平線より下も空と同じもやの色にして、茶色の地面を見せない（空中のステージなので、下にも空が続いて見える）。色は StageLook。
// 試合の終盤（残り 1 分）は MatchAtmosphere が _Tense を上げ、空を赤い色に寄せる（2026-10-14 の要望の「空を少し赤く」）。
//
// 空模様（2026-11-18 の要望: 最奥の空をステージに合ったリアルな空模様に。商業ゲームの空を参考に）。どれも強さ 0 なら描かない:
// - 積雲（_CloudCover）: 地平線から湧き上がる雲。横から見るカメラ（ほぼ水平に前を向く）に合わせ、空を円筒に見立てて
//   （横 = 方位、縦 = 見上げる角度の正接）雲を描く。地平線の近くに小さく霞んだ遠い雲の列、その手前に大きな雲。雲の頂は場所ごとに
//   高さが変わり、濃さの勾配から作った表面の向きで、太陽（夜は月）の側が明るく、反対と濃い芯と底が陰る。太陽のすぐ近くの縁は光る。
//   下から照らす光（_CloudUnder。夜の街明かり・溶岩）。地平線の近くはもやに溶ける。ゆっくり流れる。
// - 巻雲（_Cirrus）: 高い所の、横に引き伸ばした薄い筋雲。
// - 月（_MoonColor）: 海（暗い模様）と縁の暗さのある円盤と、光のかさ。
// - 星（_Stars）: 色と大きさにばらつき、ゆっくりまたたく。天の川（_MilkyWay）: 斜めの帯の淡い光と、帯の中に多い星。
// - 地平線の光（_HorizonGlow）: 街明かり（夜の街）・溶岩の照り返し。地平線の下から上へ薄れる。
// - 星雲（_Nebula）と惑星（_Planet）: 宇宙。惑星は昼の側と夜の側、海と陸と雲、縁の大気の光。
// - 火の粉（_Embers）: 溶岩の空を舞い上がる光の粒。
Shader "Airerage/GradientSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.16, 0.36, 0.72, 1)
        _SkyColor ("Sky", Color) = (0.42, 0.62, 0.88, 1)
        _HorizonColor ("Horizon", Color) = (0.84, 0.92, 0.98, 1)
        _BelowColor ("Below horizon", Color) = (0.56, 0.7, 0.86, 1)
        _HorizonHeight ("Horizon height", Range(-0.3, 0.3)) = 0.02
        _HorizonSharpness ("Horizon sharpness", Range(1, 40)) = 9
        [HDR] _SunColor ("Sun", Color) = (1.6, 1.45, 1.2, 1)
        _SunDirection ("Sun direction", Vector) = (-0.45, 0.35, 1, 0)
        _SunSize ("Sun size", Range(16, 4096)) = 2600
        _SunGlow ("Sun glow", Range(0, 1)) = 0.25
        _Stars ("Stars", Range(0, 1)) = 0
        _TenseColor ("Tense color", Color) = (1, 0.42, 0.34, 1)
        _Tense ("Tense", Range(0, 1)) = 0

        _CloudCover ("Cloud cover", Range(0, 1)) = 0
        _CloudLit ("Cloud lit", Color) = (0.97, 0.97, 0.96, 1)
        _CloudShade ("Cloud shade", Color) = (0.62, 0.68, 0.78, 1)
        [HDR] _CloudUnder ("Cloud under light", Color) = (0, 0, 0, 1)
        _CloudTop ("Cloud band top (tan of elevation)", Range(0.05, 1.5)) = 0.4
        _CloudScale ("Cloud scale", Range(0.3, 4)) = 1
        _CloudSpeed ("Cloud drift (radians per second)", Float) = 0.004
        _CloudSoftness ("Cloud softness", Range(0.02, 0.4)) = 0.06
        _Cirrus ("Cirrus", Range(0, 1)) = 0
        _CirrusColor ("Cirrus color", Color) = (1, 1, 1, 1)
        [HDR] _MoonColor ("Moon", Color) = (0, 0, 0, 1)
        _MoonDirection ("Moon direction", Vector) = (0.45, 0.4, 1, 0)
        _MoonSize ("Moon size (degrees)", Range(0.5, 12)) = 3
        _MilkyWay ("Milky way", Range(0, 1)) = 0
        [HDR] _HorizonGlow ("Horizon glow", Color) = (0, 0, 0, 1)
        _HorizonGlowHeight ("Horizon glow height", Range(0.01, 1)) = 0.18
        _Nebula ("Nebula", Range(0, 1)) = 0
        [HDR] _NebulaA ("Nebula A", Color) = (0.35, 0.12, 0.6, 1)
        [HDR] _NebulaB ("Nebula B", Color) = (0.1, 0.4, 0.8, 1)
        _Planet ("Planet", Range(0, 1)) = 0
        _PlanetOcean ("Planet ocean", Color) = (0.03, 0.12, 0.35, 1)
        _PlanetLand ("Planet land", Color) = (0.2, 0.3, 0.14, 1)
        [HDR] _PlanetAtmosphere ("Planet atmosphere", Color) = (0.35, 0.65, 1.6, 1)
        _PlanetDirection ("Planet direction", Vector) = (0, -0.88, 0.47, 0)
        _PlanetRadius ("Planet radius (degrees)", Range(5, 80)) = 50
        _Embers ("Embers", Range(0, 1)) = 0
        [HDR] _EmberColor ("Ember color", Color) = (3, 1.2, 0.3, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _SkyColor;
                half4 _HorizonColor;
                half4 _BelowColor;
                float _HorizonHeight;
                float _HorizonSharpness;
                half4 _SunColor;
                float4 _SunDirection;
                float _SunSize;
                half _SunGlow;
                half _Stars;
                half4 _TenseColor;
                half _Tense;
                half _CloudCover;
                half4 _CloudLit;
                half4 _CloudShade;
                half4 _CloudUnder;
                float _CloudTop;
                float _CloudScale;
                float _CloudSpeed;
                half _CloudSoftness;
                half _Cirrus;
                half4 _CirrusColor;
                half4 _MoonColor;
                float4 _MoonDirection;
                float _MoonSize;
                half _MilkyWay;
                half4 _HorizonGlow;
                float _HorizonGlowHeight;
                half _Nebula;
                half4 _NebulaA;
                half4 _NebulaB;
                half _Planet;
                half4 _PlanetOcean;
                half4 _PlanetLand;
                half4 _PlanetAtmosphere;
                float4 _PlanetDirection;
                float _PlanetRadius;
                half _Embers;
                half4 _EmberColor;
            CBUFFER_END

            // 乱数（大きな値でも精度の落ちにくい形）。
            float Hash21(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            float Hash31(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            // 値ノイズ（0〜1）と、それを重ねた fbm（0〜1 に正規化。octaves 回）。
            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p, int octaves)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float total = 0.0;
                const float2x2 turn = float2x2(0.8, -0.6, 0.6, 0.8);
                [unroll(6)]
                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * Noise(p);
                    total += amplitude;
                    p = mul(turn, p) * 2.03 + 17.1;
                    amplitude *= 0.5;
                }
                return value / total;
            }

            // 星: 向きを細かい升目に分け、升目ごとに乱数で星を置く（位置・大きさ・色・明るさにばらつき）。ゆっくりまたたく。
            half3 StarField(float3 d, float density)
            {
                float3 g = d * 260.0;
                float3 cell = floor(g);
                float3 local = frac(g) - 0.5;
                float h = Hash31(cell);
                float h2 = Hash31(cell + 17.3);
                float present = step(1.0 - 0.02 * density, h);
                float3 offset = (float3(Hash31(cell + 3.1), Hash31(cell + 5.7), Hash31(cell + 9.2)) - 0.5) * 0.5;
                float size = lerp(0.05, 0.16, h2 * h2);
                float s = saturate(1.0 - length(local - offset) / size);
                s *= s * present;
                half3 tint = lerp(half3(0.72, 0.82, 1.0), half3(1.0, 0.86, 0.68), h2);
                half twinkle = 0.65 + 0.35 * sin(_Time.y * (1.3 + h * 3.0) + h * 50.0);
                return tint * s * twinkle * (0.5 + 1.6 * h2);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            // 空を円筒に見立てた座標（x = 方位〈ラジアン〉、y = 見上げる角度の正接）。
            float2 Cylinder(float3 d)
            {
                return float2(atan2(d.x, d.z), d.y / max(length(d.xz), 1e-3));
            }

            // 3 次元の値ノイズと fbm（惑星の表面。向きのまま使うので極で筋にならない）。
            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float x00 = lerp(Hash31(i), Hash31(i + float3(1, 0, 0)), u.x);
                float x10 = lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), u.x);
                float x01 = lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), u.x);
                float x11 = lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), u.x);
                return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
            }

            float Fbm3(float3 p, int octaves)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float total = 0.0;
                [unroll(6)]
                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * Noise3(p);
                    total += amplitude;
                    p = p * 2.02 + float3(5.3, 1.7, 9.1);
                    amplitude *= 0.5;
                }
                return value / total;
            }

            // 積雲の帯（手前の大きな雲）: 不透明さを alpha、雲の中の明るさを lit、帯の中の高さを height に。濃さの勾配から雲の表面の
            // 向きを作り、太陽（夜は月）の向きで陰影をつける（光の側が明るく、反対と濃い芯と底が陰る）。
            void Cumulus(float2 c, float2 sunC, out half alpha, out half lit, out half height)
            {
                float drift = _Time.y * _CloudSpeed;
                float2 p = float2((c.x + drift) * 2.4, c.y * 3.4) / _CloudScale;
                float2 warp = float2(Fbm(p * 0.5 + 3.1, 3), Fbm(p * 0.5 - 1.7, 3)) - 0.5;
                float2 q = p + warp * 0.9;
                float n = Fbm(q, 6);
                // 雲の頂の高さは方位ごとに変わる（かたまりごとの湧き上がり）。
                float top = _CloudTop * (0.5 + 0.8 * Fbm(float2((c.x + drift) * 1.6, 3.7) / _CloudScale, 3));
                float rel = c.y - _HorizonHeight;
                height = saturate(rel / max(top, 1e-3));
                // 地平線の下にも少し雲（雲海）。上ほど雲になりにくい。
                float below = saturate(-rel / 0.22);
                float base = 1.0 - _CloudCover;
                float threshold = rel >= 0.0 ? lerp(base, 1.12, pow(height, 2.2)) : lerp(base, 1.12, below);
                float density = n - threshold;
                alpha = smoothstep(0.0, _CloudSoftness, density);
                const float e = 0.06;
                float2 gradient = float2(Fbm(q + float2(e, 0.0), 6), Fbm(q + float2(0.0, e), 6)) - n;
                float2 surface = normalize(-gradient + float2(0.0, 1e-4));
                float2 toward = sunC - c;
                toward.x = toward.x - 6.2831853 * round(toward.x / 6.2831853);
                float2 light = normalize(float2(toward.x * 2.4, toward.y * 3.4 + 0.6));
                half diffuse = saturate(dot(surface, light) * 0.5 + 0.5);
                half thick = saturate(density / 0.22);
                lit = saturate((0.42 + 0.58 * diffuse) * (1.0 - 0.25 * thick) * (0.82 + 0.22 * height));
            }

            // 遠い雲（地平線の近くの、小さく霞んだ雲の列）。陰影は高さだけ。
            half FarClouds(float2 c, out half height)
            {
                float2 p = float2((c.x + _Time.y * _CloudSpeed * 0.6) * 9.0, c.y * 12.0) / _CloudScale + float2(40.0, 0.0);
                float n = Fbm(p, 5);
                float rel = c.y - _HorizonHeight;
                height = saturate(rel / 0.09);
                float base = 1.0 - _CloudCover * 0.9;
                return smoothstep(0.0, 0.08, n - lerp(base, 1.1, pow(height, 1.5))) * step(-0.02, rel);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float h = d.y - _HorizonHeight;
                // 地平線から上: もや → 空 → 天頂。下: もや → 下の色。
                half3 color;
                if (h >= 0.0)
                {
                    half t = saturate(h * 2.2);
                    color = lerp(_SkyColor.rgb, _ZenithColor.rgb, t * t);
                }
                else
                {
                    color = lerp(_HorizonColor.rgb, _BelowColor.rgb, saturate(-h * 3.0));
                }
                // 地平線のもやは、上は薄く狭く（奥の山やビルの根元まで空に見えるように。2026-11-18 の要望）、下は今までどおり。
                half haze = h >= 0.0 ? exp(-h * _HorizonSharpness * 2.2) * 0.85 : exp(h * _HorizonSharpness);
                color = lerp(color, _HorizonColor.rgb, haze);
                float2 c = Cylinder(d);
                float3 sun = normalize(_SunDirection.xyz);
                float2 sunC = Cylinder(sun);
                half facing = saturate(dot(d, sun));

                // 星雲（宇宙）は星よりも奥: 淡く色の濃い光の雲と暗い塵の筋だけにして、星はその上にくっきり重ねる（2026-11-18 の要望:
                // 銀河のようなもやが星より手前に見える。前は明るい灰色の煙のようで星を覆って見えた）。星雲の濃い所ほど星を多く。
                half nebulaMask = 0.0;
                if (_Nebula > 0.0)
                {
                    float2 nq = c * 1.6 + 7.0;
                    float n1 = Fbm(nq + Fbm(nq * 0.7, 3) * 1.5, 5);
                    float n2 = Fbm(nq * 1.7 + 3.3, 4);
                    nebulaMask = pow(saturate((n1 - 0.42) * 2.6), 2.2);
                    half3 tint = lerp(_NebulaA.rgb, _NebulaB.rgb, smoothstep(0.35, 0.7, n2));
                    half dust = smoothstep(0.52, 0.72, Fbm(nq * 2.3 - 4.1, 4));
                    color += tint * nebulaMask * _Nebula * 0.45 * (1.0 - dust * 0.85);
                }
                // 星・天の川（夜と宇宙）。
                if (_Stars > 0.0)
                {
                    float3 band = normalize(float3(-0.75, 0.62, 0.12));
                    half milky = exp(-pow(dot(d, band) / 0.16, 2.0)) * _MilkyWay;
                    half3 stars = StarField(d, 1.0 + milky * 2.5 + nebulaMask * 2.0) * _Stars;
                    half mist = Fbm(c * float2(7.0, 7.0) + 11.0, 4);
                    stars += half3(0.45, 0.5, 0.8) * milky * smoothstep(0.4, 0.85, mist) * 0.1;
                    color += stars * saturate(h * 4.0 + 0.3);
                }

                // 地平線の光（街明かり・溶岩の照り返し）。
                color += _HorizonGlow.rgb * exp(-max(h, 0.0) / max(_HorizonGlowHeight, 1e-3));

                // 太陽: 円盤、まわりのかさ、広い光。
                color += _SunColor.rgb * (pow(facing, _SunSize) + _SunGlow * (0.9 * pow(facing, 420.0) + pow(facing, 12.0) * 0.35 + pow(facing, 3.0) * 0.08));

                // 月: 海の模様と縁の暗さのある円盤と、光のかさ。
                if (_MoonColor.r + _MoonColor.g + _MoonColor.b > 0.0)
                {
                    float3 moon = normalize(_MoonDirection.xyz);
                    float md = dot(d, moon);
                    float radius = radians(_MoonSize);
                    float angle = acos(clamp(md, -1.0, 1.0));
                    half disc = 1.0 - smoothstep(radius * 0.94, radius, angle);
                    float3 right = normalize(cross(float3(0.0, 1.0, 0.0), moon));
                    float3 up = cross(moon, right);
                    float3 offset = d - moon * md;
                    float2 local = float2(dot(offset, right), dot(offset, up)) / max(sin(radius), 1e-4);
                    half mare = smoothstep(0.42, 0.62, Fbm(local * 2.2 + 5.0, 4));
                    half craters = smoothstep(0.7, 0.9, Noise(local * 9.0 + 2.0)) * 0.12;
                    half limb = sqrt(saturate(1.0 - dot(local, local)));
                    half3 face = _MoonColor.rgb * (0.86 - mare * 0.24 + craters) * (0.7 + 0.3 * limb);
                    half glow = pow(saturate(md), 6000.0) * 0.35 + pow(saturate(md), 600.0) * 0.08 + pow(saturate(md), 60.0) * 0.02;
                    color = lerp(color + _MoonColor.rgb * glow, face, disc);
                }

                // 惑星（宇宙）: 光の当たる側と夜の側、海と陸と雲、縁の大気。
                if (_Planet > 0.0)
                {
                    float3 center = normalize(_PlanetDirection.xyz);
                    float pd = dot(d, center);
                    float r = sin(radians(_PlanetRadius));
                    float edge = pd - cos(radians(_PlanetRadius));
                    half inside = smoothstep(-0.0012, 0.0012, edge) * _Planet;
                    float under = r * r - (1.0 - pd * pd);
                    float t = pd - sqrt(max(under, 0.0));
                    float3 normal = normalize(d * t - center);
                    // 表面は向きのまま 3 次元のノイズで（ゆっくり自転）。
                    float spin = _Time.y * 0.0015;
                    float3 turned = float3(normal.x * cos(spin) - normal.z * sin(spin), normal.y, normal.x * sin(spin) + normal.z * cos(spin));
                    half land = smoothstep(0.5, 0.56, Fbm3(turned * 2.2 + 3.0, 5));
                    half clouds = smoothstep(0.5, 0.74, Fbm3(turned * float3(4.5, 7.0, 4.5) + 10.0, 5));
                    half day = saturate(dot(normal, sun) * 1.4 + 0.15);
                    half3 ground = lerp(_PlanetOcean.rgb, _PlanetLand.rgb, land);
                    ground = lerp(ground, half3(0.95, 0.96, 0.98), clouds * 0.85);
                    half3 body = ground * (day * 1.05 + 0.015);
                    half rim = pow(1.0 - saturate(dot(normal, -d)), 3.0);
                    body += _PlanetAtmosphere.rgb * rim * (day * 0.9 + 0.1) * 0.6;
                    half halo = exp(-max(-edge, 0.0) * 70.0) * (1.0 - inside) * _Planet;
                    color = lerp(color, body, inside) + _PlanetAtmosphere.rgb * halo * 0.5 * saturate(dot(d, sun) * 0.6 + 0.6);
                }

                // 巻雲（高い所の薄い筋雲）。
                if (_Cirrus > 0.0)
                {
                    float rel = c.y - _HorizonHeight;
                    float2 pc = float2((c.x + _Time.y * _CloudSpeed * 1.6) * 2.6, rel * 15.0) / _CloudScale;
                    pc.x += Fbm(pc * 0.35, 3) * 2.0;
                    half streak = smoothstep(0.5, 0.82, Fbm(pc, 5));
                    half band = smoothstep(0.08, 0.3, rel) * (1.0 - smoothstep(0.9, 1.5, rel));
                    half3 cirrus = _CirrusColor.rgb + _SunColor.rgb * pow(facing, 6.0) * 0.25;
                    color = lerp(color, cirrus, streak * band * _Cirrus * 0.55);
                }

                // 積雲（遠い列と手前の大きな雲）。雲の帯の外では計算しない。
                float cloudRel = c.y - _HorizonHeight;
                if (_CloudCover > 0.0 && cloudRel > -0.26 && cloudRel < _CloudTop * 1.35)
                {
                    half3 haze = _HorizonColor.rgb + _HorizonGlow.rgb * 0.5;
                    half farHeight;
                    half farAlpha = FarClouds(c, farHeight);
                    half3 far = lerp(_CloudShade.rgb, _CloudLit.rgb, 0.45 + 0.55 * farHeight) + _CloudUnder.rgb * (1.0 - farHeight) * 0.6;
                    far = lerp(far, haze, 0.45);
                    color = lerp(color, far, farAlpha * 0.9);

                    half alpha;
                    half lit;
                    half height;
                    Cumulus(c, sunC, alpha, lit, height);
                    half3 cloud = lerp(_CloudShade.rgb, _CloudLit.rgb, lit);
                    // 下から照らす光（街明かり・溶岩）は底ほど強い。
                    cloud += _CloudUnder.rgb * (1.0 - height) * (1.0 - lit * 0.6);
                    // 太陽（夜は月）のすぐ近くの縁が光る。
                    half3 rimLight = _SunColor.rgb + _MoonColor.rgb * 0.35;
                    cloud += rimLight * pow(facing, 40.0) * saturate((1.0 - alpha) * alpha * 4.0) * 0.8;
                    // 地平線の近くはもやに溶ける。
                    half fade = exp(-abs(cloudRel) * _HorizonSharpness * 0.55);
                    cloud = lerp(cloud, haze, fade * 0.6);
                    color = lerp(color, cloud, alpha * 0.97);
                }

                // 火の粉（舞い上がる光の粒）。
                if (_Embers > 0.0)
                {
                    float2 g = float2(c.x * 30.0, c.y * 30.0 - _Time.y * 1.4);
                    g.x += sin(g.y * 0.7 + floor(g.x) * 1.3) * 0.25;
                    float2 cell = floor(g);
                    float hash = Hash21(cell);
                    float2 offset = float2(Hash21(cell + 4.1), Hash21(cell + 8.3)) - 0.5;
                    half ember = saturate(1.0 - length(frac(g) - 0.5 - offset * 0.6) / 0.09);
                    ember *= ember * step(0.9, hash) * (0.5 + 0.5 * sin(_Time.y * (3.0 + hash * 5.0) + hash * 30.0));
                    color += _EmberColor.rgb * ember * _Embers * (1.0 - smoothstep(0.2, 0.9, c.y - _HorizonHeight));
                }

                half3 tense = dot(color, half3(0.2126, 0.7152, 0.0722)) * _TenseColor.rgb * 1.45 + color * 0.12;
                color = lerp(color, tense, _Tense);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
