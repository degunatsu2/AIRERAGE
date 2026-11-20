using UnityEngine;
using UnityEngine.Rendering;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ギミックの粒（ParticleSystem）を作る（2026-11-20 の方針: ギミックを実際の現象・ゲームの演出に寄せる。溶岩の噴泉の塊・火の粉・煙・
    /// 湯気・しぶき）。粒は Airerage/SoftParticle の丸い柔らかい板。出す量は HazardEffectRig が状態に合わせて変える（ここでは 0）か、
    /// いつも出し続ける（Rate。悪魔の手の煙・つむじ風の塵）。
    /// 見た目だけ（当たりはない。巻き戻しとも関係ない）。
    /// </summary>
    internal sealed class ParticleFx
    {
        public string Name = "Fx";
        public float LifetimeMin = 1f;
        public float LifetimeMax = 2f;
        public float SpeedMin = 2f;
        public float SpeedMax = 4f;
        public float SizeMin = 0.3f;
        public float SizeMax = 0.6f;
        /// <summary>重力の倍率（1 で地球の重力）。</summary>
        public float Gravity;
        /// <summary>出す形の円錐の開き（度。0 なら真っすぐ）と、出す所の半径（m）。</summary>
        public float ConeAngle = 15f;
        public float ConeRadius = 0.3f;
        /// <summary>大きさの変化（生まれたときの倍率 → 消えるときの倍率）。</summary>
        public float GrowFrom = 1f;
        public float GrowTo = 1f;
        /// <summary>色の変化（HDR で光らせる。最後の透明度は消え方）。</summary>
        public Gradient Colors;
        /// <summary>重ねて光らせる（足し算）か。</summary>
        public bool Additive;
        /// <summary>縁の崩れ（煙・しぶきの塊）と柔らかさ。</summary>
        public float EdgeNoise;
        public float Softness = 0.5f;
        /// <summary>動く向きに伸ばす（飛ぶ塊・しぶきの筋）。</summary>
        public float Stretch;
        /// <summary>揺らぎ（煙が渦を巻く）。</summary>
        public float Turbulence;
        /// <summary>回転（度/秒の範囲）。</summary>
        public float Spin;
        public int MaxParticles = 300;
        /// <summary>いつも出し続ける量（個/秒。0 なら HazardEffectRig が状態に合わせて決める）。出し続ける粒は始まりから出そろっている（prewarm）。</summary>
        public float Rate;
        /// <summary>回転しながら昇る（つむじ風の塵）: 出す所の軸の周りを回る速さ（度/秒）と、軸へ寄せる速さ（負で外へ）。</summary>
        public float Orbit;
        public float Radial;
        /// <summary>色の明るさの倍率（粒の色は 1 までなので、光らせる粒はここで明るくする。仕上げのにじみが出る）。</summary>
        public float Intensity = 1f;

        /// <summary>粒の色の変化を作る（色と、その時の透明度。どちらも生まれた時 0 → 消える時 1 の位置）。</summary>
        public static Gradient Ramp((Color Color, float At)[] colors, (float Alpha, float At)[] alphas)
        {
            var gradient = new Gradient();
            var c = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                c[i] = new GradientColorKey(colors[i].Color, colors[i].At);
            }
            var a = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++)
            {
                a[i] = new GradientAlphaKey(alphas[i].Alpha, alphas[i].At);
            }
            gradient.SetKeys(c, a);
            gradient.mode = GradientMode.Blend;
            return gradient;
        }

        /// <summary>parent の子に作る（localRotation の +Z の向きへ出す）。</summary>
        public ParticleSystem Build(Transform parent, Vector3 localPosition, Quaternion localRotation, string folder)
        {
            var go = new GameObject(Name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            var system = go.AddComponent<ParticleSystem>();
            // 作った直後は再生を止める（HazardEffectRig が出す量を決めて再生する）。
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(LifetimeMin, LifetimeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(SpeedMin, SpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(SizeMin, SizeMax);
            main.gravityModifier = Gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaxParticles;
            main.startColor = Color.white;
            if (Spin > 0f)
            {
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            }
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = Rate;
            main.prewarm = Rate > 0f;
            if (!Mathf.Approximately(Orbit, 0f) || !Mathf.Approximately(Radial, 0f))
            {
                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.orbitalZ = new ParticleSystem.MinMaxCurve(Orbit * Mathf.Deg2Rad);
                velocity.radial = new ParticleSystem.MinMaxCurve(Radial);
            }
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = ConeAngle;
            shape.radius = Mathf.Max(0.001f, ConeRadius);
            if (Colors != null)
            {
                ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
                color.enabled = true;
                color.color = Colors;
            }
            if (!Mathf.Approximately(GrowFrom, 1f) || !Mathf.Approximately(GrowTo, 1f))
            {
                ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, GrowFrom, 1f, GrowTo));
            }
            if (Turbulence > 0f)
            {
                ParticleSystem.NoiseModule noise = system.noise;
                noise.enabled = true;
                noise.strength = Turbulence;
                noise.frequency = 0.4f;
                noise.scrollSpeed = 0.5f;
                noise.damping = true;
            }
            if (Spin > 0f)
            {
                ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
                rotation.enabled = true;
                rotation.z = new ParticleSystem.MinMaxCurve(-Spin * Mathf.Deg2Rad, Spin * Mathf.Deg2Rad);
            }
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Material(folder);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (Stretch > 0f)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = Stretch;
                renderer.lengthScale = 1.2f;
            }
            else
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
            return system;
        }

        private Material Material(string folder)
        {
            string name = $"Fx{(Additive ? "Add" : "Alpha")}{Mathf.RoundToInt(EdgeNoise * 100f)}_{Mathf.RoundToInt(Softness * 100f)}_{Mathf.RoundToInt(Intensity * 10f)}";
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/SoftParticle", m =>
            {
                m.SetFloat("_Softness", Softness);
                m.SetFloat("_Noise", EdgeNoise);
                m.SetFloat("_NoiseScale", 3f);
                m.SetFloat("_Intensity", Intensity);
                m.SetFloat("_SrcBlend", Additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", Additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            }, folder);
        }
    }
}
