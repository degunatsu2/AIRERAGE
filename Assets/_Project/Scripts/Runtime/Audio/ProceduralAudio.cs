using System;
using UnityEngine;

namespace Airerage.Runtime.Audio
{
    /// <summary>効果音の種類。</summary>
    public enum SoundId
    {
        /// <summary>攻撃を出した（風を切る音）。</summary>
        Swing,

        /// <summary>攻撃が当たった。</summary>
        Hit,

        /// <summary>強い攻撃が当たった（大きく吹き飛んだ）。</summary>
        HeavyHit,

        /// <summary>ふつうのガードで受けた（金属音）。</summary>
        Guard,

        /// <summary>ジャストガードに成功した（明るいチャイム）。</summary>
        Parry,

        /// <summary>ガードを構えた。</summary>
        GuardUp,

        /// <summary>撃破技のチャージ（上がっていく音）。</summary>
        Charge,

        Jump,
        WireFire,
        WireAttach,

        /// <summary>攻撃をワイヤーでキャンセルした。</summary>
        WireCancel,

        /// <summary>ステージの外に出た（撃破）。</summary>
        Knockout,

        /// <summary>FATAL（速く打った撃破技）が当たった（とても重い打撃音。ユーザーの要望、2026-10-13）。</summary>
        Fatal,

        /// <summary>のけぞり中に地形で跳ね返った（ドンという鈍い音）。</summary>
        Bounce,

        /// <summary>ジャンプ台で打ち上がった（ばねの音）。</summary>
        JumpPad,

        /// <summary>加速リングで加速した（風を切って加速する音）。</summary>
        Boost,

        /// <summary>始まる前の演出で、名前が滑り込む（風を切る柔らかい音）。</summary>
        IntroWhoosh,

        /// <summary>試合の開始（黒い帯を切り裂く鋭い音）。</summary>
        IntroSlash,

        /// <summary>試合の終了（FINISH!。下がっていく和音）。</summary>
        Finish,

        /// <summary>コインを拾った（「チャリン」）。</summary>
        CoinPickup,

        /// <summary>拠点で所持コインを確定した（上がっていく明るい和音と、コインの音）。</summary>
        Bank,

        /// <summary>拠点が 10 秒後に移る（呼び鈴の「ピンポーン」。ユーザーの要望、2026-10-14）。</summary>
        BankNotice,

        /// <summary>100 枚のコインの予告（警報。ユーザーの要望、2026-10-14）。</summary>
        JackpotAlarm,

        /// <summary>アディショナルタイムに入った（短く2回鳴る笛）。</summary>
        Overtime,

        /// <summary>COIN RUSH の始まり（残り 30 秒。短いファンファーレ。ユーザーの選択、2026-10-14）。</summary>
        CoinRush,

        /// <summary>残り 10 秒の、1 秒ごとの短い音（ユーザーの選択、2026-10-14）。</summary>
        CountdownTick,

        /// <summary>100 枚のコインを取った（派手な獲得音。ユーザーの要望、2026-10-15）。</summary>
        JackpotGet,

        /// <summary>特殊攻撃を出した（鋭く上がる風の音。docs/13 T5-4）。</summary>
        SpecialMove,

        /// <summary>必殺技を出した（暗転に合わせた、低い唸りと上がっていく光の音）。</summary>
        SuperStart,

        /// <summary>自分の特殊攻撃のクールタイムが終わった・必殺技のゲージがたまった（小さく明るい音）。</summary>
        SkillReady,

        /// <summary>ミラーボールを置いた（きらめく和音。RISK TAKER の Q）。</summary>
        MirrorBall,

        /// <summary>セーフティボックスを置いた（重い金属の音と、鍵の音）。</summary>
        SafePlace,

        /// <summary>セーフティボックスが壊れた・時間切れでばらまいた（割れる音と、たくさんのコインの音）。</summary>
        SafeBreak,

        /// <summary>コイントス（コインをはじく「キン」）。</summary>
        CoinToss,
    }

    /// <summary>
    /// 仮の効果音と BGM を、音声ファイルを使わずにその場で合成する（ユーザーの要望、2026-10-13）。
    /// 簡単な波形（正弦波、矩形波、三角波、のこぎり波、ノイズ）と減衰の組み合わせで作る。乱数は種を固定するので、毎回同じ音になる。
    /// 表示（演出）のためのもので、シミュレーションには関係しない。本番の音ができたら GameAudio の差し替えで置き換える。
    /// </summary>
    public static class ProceduralAudio
    {
        public const int SeSampleRate = 44100;
        public const int BgmSampleRate = 22050;

        /// <summary>BGM のテンポ（拍/分）と長さ（小節。4/4 拍子）。</summary>
        public const float BgmTempo = 140f;
        public const int BgmBars = 8;

        private enum Wave
        {
            Sine,
            Square,
            Pulse25,
            Triangle,
            Saw,
        }

        public static AudioClip CreateClip(SoundId id)
        {
            return ToClip("SE_" + id, Synthesize(id), SeSampleRate);
        }

        public static AudioClip CreateBgm()
        {
            return ToClip("BGM_Temporary", SynthesizeBgm(), BgmSampleRate);
        }

        /// <summary>緊張感のある BGM（100 枚のコインの予告から試合の終わりまで。ユーザーの要望、2026-10-14）。</summary>
        public static AudioClip CreateTenseBgm()
        {
            return ToClip("BGM_Tense_Temporary", SynthesizeTenseBgm(), BgmSampleRate);
        }

        /// <summary>緊張感のある BGM のテンポ（拍/分）。</summary>
        public const float TenseBgmTempo = 168f;

        /// <summary>効果音の波形（SeSampleRate、モノラル、-1〜1）。</summary>
        public static float[] Synthesize(SoundId id)
        {
            var rng = new System.Random(1000 + (int)id);
            const int r = SeSampleRate;
            float[] b;
            switch (id)
            {
                case SoundId.Swing:
                    b = new float[Samples(0.16f, r)];
                    AddNoise(b, r, rng, 0f, 0.16f, 0.5f, 0.05f, 0.05f, 900f, 3500f, 400f);
                    break;
                case SoundId.Hit:
                    b = new float[Samples(0.22f, r)];
                    AddNoise(b, r, rng, 0f, 0.12f, 0.9f, 0.001f, 0.03f, 5000f, 5000f, 200f);
                    AddTone(b, r, 0f, 0.2f, 190f, 55f, Wave.Sine, 0.9f, 0.002f, 0.07f);
                    AddTone(b, r, 0f, 0.03f, 1400f, 900f, Wave.Square, 0.15f, 0.001f, 0.01f);
                    break;
                case SoundId.HeavyHit:
                    b = new float[Samples(0.5f, r)];
                    AddNoise(b, r, rng, 0f, 0.35f, 1f, 0.001f, 0.08f, 3500f, 1200f, 60f);
                    AddTone(b, r, 0f, 0.45f, 140f, 38f, Wave.Sine, 1f, 0.002f, 0.16f);
                    AddTone(b, r, 0f, 0.05f, 900f, 300f, Wave.Saw, 0.25f, 0.001f, 0.02f);
                    break;
                case SoundId.Guard:
                    b = new float[Samples(0.3f, r)];
                    AddTone(b, r, 0f, 0.3f, 1250f, 1250f, Wave.Sine, 0.45f, 0.001f, 0.07f);
                    AddTone(b, r, 0f, 0.3f, 1873f, 1873f, Wave.Sine, 0.35f, 0.001f, 0.06f);
                    AddTone(b, r, 0f, 0.3f, 2461f, 2461f, Wave.Sine, 0.25f, 0.001f, 0.05f);
                    AddNoise(b, r, rng, 0f, 0.03f, 0.6f, 0.001f, 0.008f, 8000f, 8000f, 2000f);
                    break;
                case SoundId.Parry:
                    b = new float[Samples(0.9f, r)];
                    int[] notes = { 88, 92, 95, 100 }; // E6 G#6 B6 E7
                    for (int i = 0; i < notes.Length; i++)
                    {
                        float f = NoteFrequency(notes[i]);
                        AddTone(b, r, i * 0.05f, 0.7f, f, f, Wave.Sine, 0.45f, 0.002f, 0.22f);
                        AddTone(b, r, i * 0.05f, 0.5f, f * 2f, f * 2f, Wave.Sine, 0.12f, 0.002f, 0.12f);
                    }
                    AddNoise(b, r, rng, 0f, 0.5f, 0.25f, 0.005f, 0.15f, 12000f, 12000f, 6000f);
                    break;
                case SoundId.GuardUp:
                    b = new float[Samples(0.12f, r)];
                    AddNoise(b, r, rng, 0f, 0.1f, 0.35f, 0.005f, 0.03f, 9000f, 9000f, 3000f);
                    AddTone(b, r, 0f, 0.1f, 900f, 1100f, Wave.Sine, 0.3f, 0.003f, 0.04f);
                    break;
                case SoundId.Charge:
                    // 撃破技のチャージ（16 ティック = 約 0.27 秒）に合わせて上がっていく音。
                    b = new float[Samples(0.3f, r)];
                    AddTone(b, r, 0f, 0.3f, 220f, 1100f, Wave.Pulse25, 0.35f, 0.02f, 0.5f);
                    AddTone(b, r, 0f, 0.3f, 440f, 2200f, Wave.Sine, 0.2f, 0.02f, 0.5f);
                    ApplyTremolo(b, r, 28f, 0.5f);
                    break;
                case SoundId.Jump:
                    b = new float[Samples(0.12f, r)];
                    AddTone(b, r, 0f, 0.12f, 320f, 760f, Wave.Square, 0.3f, 0.002f, 0.05f);
                    break;
                case SoundId.WireFire:
                    b = new float[Samples(0.14f, r)];
                    AddTone(b, r, 0f, 0.14f, 1800f, 500f, Wave.Saw, 0.25f, 0.002f, 0.05f);
                    AddNoise(b, r, rng, 0f, 0.1f, 0.25f, 0.002f, 0.04f, 6000f, 2000f, 1000f);
                    break;
                case SoundId.WireAttach:
                    b = new float[Samples(0.12f, r)];
                    AddTone(b, r, 0f, 0.12f, 260f, 110f, Wave.Sine, 0.7f, 0.001f, 0.035f);
                    AddNoise(b, r, rng, 0f, 0.02f, 0.5f, 0.001f, 0.006f, 7000f, 7000f, 1500f);
                    break;
                case SoundId.WireCancel:
                    b = new float[Samples(0.3f, r)];
                    AddNoise(b, r, rng, 0f, 0.15f, 0.45f, 0.01f, 0.05f, 2000f, 7000f, 600f);
                    AddTone(b, r, 0.03f, 0.25f, NoteFrequency(79), NoteFrequency(79), Wave.Pulse25, 0.2f, 0.002f, 0.06f);
                    AddTone(b, r, 0.09f, 0.2f, NoteFrequency(86), NoteFrequency(86), Wave.Pulse25, 0.2f, 0.002f, 0.06f);
                    break;
                case SoundId.Knockout:
                    b = new float[Samples(1.1f, r)];
                    AddTone(b, r, 0f, 0.8f, 900f, 70f, Wave.Square, 0.35f, 0.005f, 0.4f);
                    AddNoise(b, r, rng, 0.25f, 0.85f, 0.9f, 0.01f, 0.25f, 1500f, 300f, 40f);
                    AddTone(b, r, 0.25f, 0.8f, 90f, 35f, Wave.Sine, 0.9f, 0.005f, 0.3f);
                    break;
                case SoundId.Fatal:
                    // 鋭い当たりの瞬間、深く長い低音、割れたような中音、重いノイズを重ねる。
                    b = new float[Samples(1.4f, r)];
                    AddNoise(b, r, rng, 0f, 0.06f, 1f, 0.0005f, 0.015f, 12000f, 12000f, 3000f);
                    AddTone(b, r, 0f, 1.3f, 130f, 24f, Wave.Sine, 1f, 0.002f, 0.45f);
                    AddTone(b, r, 0f, 0.5f, 420f, 70f, Wave.Saw, 0.45f, 0.002f, 0.15f);
                    AddTone(b, r, 0f, 0.35f, 2600f, 1900f, Wave.Sine, 0.25f, 0.001f, 0.08f);
                    AddNoise(b, r, rng, 0f, 1.2f, 1f, 0.002f, 0.3f, 2500f, 150f, 30f);
                    break;
                case SoundId.Bounce:
                    b = new float[Samples(0.3f, r)];
                    AddTone(b, r, 0f, 0.28f, 95f, 45f, Wave.Sine, 1f, 0.001f, 0.09f);
                    AddNoise(b, r, rng, 0f, 0.08f, 0.6f, 0.001f, 0.025f, 1800f, 600f, 80f);
                    AddTone(b, r, 0f, 0.04f, 600f, 300f, Wave.Triangle, 0.3f, 0.001f, 0.012f);
                    break;
                case SoundId.JumpPad:
                    // 低い音から一気に上がる「ビョン」に、揺れ（ばねの震え）を加える。
                    b = new float[Samples(0.4f, r)];
                    AddTone(b, r, 0f, 0.38f, 140f, 900f, Wave.Triangle, 0.6f, 0.002f, 0.15f);
                    AddTone(b, r, 0f, 0.3f, 280f, 1800f, Wave.Sine, 0.25f, 0.002f, 0.1f);
                    ApplyTremolo(b, r, 18f, 0.35f);
                    AddTone(b, r, 0f, 0.05f, 90f, 60f, Wave.Sine, 0.7f, 0.001f, 0.02f);
                    break;
                case SoundId.Boost:
                    // 風（帯域が上がるノイズ）と、上がっていく明るい音。
                    b = new float[Samples(0.45f, r)];
                    AddNoise(b, r, rng, 0f, 0.42f, 0.6f, 0.01f, 0.18f, 800f, 6000f, 500f);
                    AddTone(b, r, 0f, 0.35f, NoteFrequency(72), NoteFrequency(84), Wave.Pulse25, 0.18f, 0.005f, 0.12f);
                    AddTone(b, r, 0.06f, 0.3f, NoteFrequency(79), NoteFrequency(91), Wave.Pulse25, 0.14f, 0.005f, 0.1f);
                    break;
                case SoundId.IntroWhoosh:
                    b = new float[Samples(0.4f, r)];
                    AddNoise(b, r, rng, 0f, 0.38f, 0.45f, 0.06f, 0.14f, 500f, 3200f, 300f);
                    AddTone(b, r, 0f, 0.3f, 180f, 260f, Wave.Sine, 0.25f, 0.05f, 0.12f);
                    break;
                case SoundId.IntroSlash:
                    b = new float[Samples(0.6f, r)];
                    AddNoise(b, r, rng, 0f, 0.22f, 0.9f, 0.001f, 0.06f, 9000f, 4000f, 2500f);
                    AddTone(b, r, 0f, 0.5f, 2600f, 1700f, Wave.Sine, 0.25f, 0.001f, 0.18f);
                    AddTone(b, r, 0f, 0.35f, 110f, 60f, Wave.Sine, 0.6f, 0.002f, 0.12f);
                    break;
                case SoundId.Finish:
                    b = new float[Samples(1.3f, r)];
                    int[] chord = { 84, 79, 76, 72 }; // C6 G5 E5 C5 を順に下がる
                    for (int i = 0; i < chord.Length; i++)
                    {
                        float f = NoteFrequency(chord[i]);
                        AddTone(b, r, i * 0.12f, 0.9f, f, f, Wave.Triangle, 0.4f, 0.003f, 0.35f);
                        AddTone(b, r, i * 0.12f, 0.6f, f * 2f, f * 2f, Wave.Pulse25, 0.1f, 0.003f, 0.2f);
                    }
                    break;
                case SoundId.CoinPickup:
                    b = new float[Samples(0.35f, r)];
                    AddTone(b, r, 0f, 0.12f, NoteFrequency(88), NoteFrequency(88), Wave.Square, 0.22f, 0.001f, 0.04f);
                    AddTone(b, r, 0.06f, 0.28f, NoteFrequency(95), NoteFrequency(95), Wave.Sine, 0.4f, 0.001f, 0.1f);
                    AddTone(b, r, 0.06f, 0.2f, NoteFrequency(107), NoteFrequency(107), Wave.Sine, 0.12f, 0.001f, 0.06f);
                    break;
                case SoundId.Bank:
                    // C5 E5 G5 C6 を速く上がり、最後に高いコインの音を重ねる。
                    b = new float[Samples(0.8f, r)];
                    int[] rise = { 72, 76, 79, 84 };
                    for (int i = 0; i < rise.Length; i++)
                    {
                        float f = NoteFrequency(rise[i]);
                        AddTone(b, r, i * 0.06f, 0.5f, f, f, Wave.Triangle, 0.35f, 0.002f, 0.18f);
                        AddTone(b, r, i * 0.06f, 0.3f, f * 2f, f * 2f, Wave.Pulse25, 0.08f, 0.002f, 0.1f);
                    }
                    AddTone(b, r, 0.24f, 0.4f, NoteFrequency(96), NoteFrequency(96), Wave.Sine, 0.3f, 0.001f, 0.14f);
                    AddTone(b, r, 0.24f, 0.3f, NoteFrequency(103), NoteFrequency(103), Wave.Sine, 0.12f, 0.001f, 0.08f);
                    break;
                case SoundId.BankNotice:
                    // 「ピン」（E5）、「ポーン」（C5）。鐘のように、少しずれた倍音を重ねて長く残す。
                    b = new float[Samples(1.6f, r)];
                    int[] bell = { 76, 72 };
                    for (int i = 0; i < bell.Length; i++)
                    {
                        float f = NoteFrequency(bell[i]);
                        float start = i * 0.42f;
                        AddTone(b, r, start, 1.1f, f, f, Wave.Sine, 0.6f, 0.003f, 0.45f);
                        AddTone(b, r, start, 0.8f, f * 2f, f * 2f, Wave.Sine, 0.18f, 0.003f, 0.25f);
                        AddTone(b, r, start, 0.4f, f * 2.76f, f * 2.76f, Wave.Sine, 0.08f, 0.002f, 0.1f);
                    }
                    break;
                case SoundId.JackpotAlarm:
                    // 高い音と低い音を交互に速く鳴らす警報（4 往復）と、下から上がるうなり。
                    b = new float[Samples(1.8f, r)];
                    for (int i = 0; i < 8; i++)
                    {
                        float f = i % 2 == 0 ? 988f : 740f;
                        AddTone(b, r, i * 0.2f, 0.2f, f, f, Wave.Square, 0.22f, 0.004f, 0.2f);
                        AddTone(b, r, i * 0.2f, 0.2f, f * 0.5f, f * 0.5f, Wave.Saw, 0.14f, 0.004f, 0.2f);
                    }
                    AddTone(b, r, 0f, 1.7f, 110f, 330f, Wave.Saw, 0.2f, 0.05f, 1.2f);
                    break;
                case SoundId.Overtime:
                    // 笛（高い正弦波を少し揺らす）を短く2回、最後に下の和音。
                    b = new float[Samples(1f, r)];
                    for (int i = 0; i < 2; i++)
                    {
                        AddTone(b, r, i * 0.22f, 0.18f, 2350f, 2250f, Wave.Sine, 0.4f, 0.005f, 0.12f);
                        AddNoise(b, r, rng, i * 0.22f, 0.18f, 0.12f, 0.005f, 0.1f, 6000f, 6000f, 2000f);
                    }
                    AddTone(b, r, 0.44f, 0.5f, NoteFrequency(62), NoteFrequency(62), Wave.Saw, 0.25f, 0.005f, 0.3f);
                    AddTone(b, r, 0.44f, 0.5f, NoteFrequency(69), NoteFrequency(69), Wave.Saw, 0.2f, 0.005f, 0.3f);
                    break;
                case SoundId.CoinRush:
                    // G5 C6 E6 を速く上がり、最後の G6 を伸ばす（ファンファーレ）。下にコインのきらめき。
                    b = new float[Samples(1.2f, r)];
                    int[] fanfare = { 79, 84, 88, 91 };
                    for (int i = 0; i < fanfare.Length; i++)
                    {
                        float f = NoteFrequency(fanfare[i]);
                        float length = i == fanfare.Length - 1 ? 0.75f : 0.12f;
                        AddTone(b, r, i * 0.1f, length, f, f, Wave.Pulse25, 0.22f, 0.003f, i == fanfare.Length - 1 ? 0.45f : 0.06f);
                        AddTone(b, r, i * 0.1f, length, f * 0.5f, f * 0.5f, Wave.Triangle, 0.3f, 0.003f, i == fanfare.Length - 1 ? 0.45f : 0.06f);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        float f = NoteFrequency(100 + i * 2);
                        AddTone(b, r, 0.3f + i * 0.08f, 0.2f, f, f, Wave.Sine, 0.1f, 0.001f, 0.08f);
                    }
                    break;
                case SoundId.CountdownTick:
                    // 短く高い「ピッ」。
                    b = new float[Samples(0.15f, r)];
                    AddTone(b, r, 0f, 0.1f, 1760f, 1760f, Wave.Square, 0.25f, 0.002f, 0.05f);
                    AddTone(b, r, 0f, 0.1f, 880f, 880f, Wave.Sine, 0.2f, 0.002f, 0.06f);
                    break;
                case SoundId.JackpotGet:
                    // 2 オクターブを駆け上がるアルペジオ、明るい和音の伸ばし、たくさんのコインのきらめき。
                    b = new float[Samples(2f, r)];
                    int[] climb = { 72, 76, 79, 84, 88, 91, 96, 100 };
                    for (int i = 0; i < climb.Length; i++)
                    {
                        float f = NoteFrequency(climb[i]);
                        AddTone(b, r, i * 0.05f, 0.18f, f, f, Wave.Pulse25, 0.2f, 0.002f, 0.08f);
                        AddTone(b, r, i * 0.05f, 0.18f, f * 0.5f, f * 0.5f, Wave.Triangle, 0.2f, 0.002f, 0.08f);
                    }
                    int[] shine = { 84, 88, 91, 96 };
                    foreach (int note in shine)
                    {
                        float f = NoteFrequency(note);
                        AddTone(b, r, 0.42f, 1.4f, f, f, Wave.Square, 0.12f, 0.01f, 0.9f);
                        AddTone(b, r, 0.42f, 1.4f, f * 1.003f, f * 1.003f, Wave.Triangle, 0.18f, 0.01f, 0.9f);
                    }
                    for (int i = 0; i < 10; i++)
                    {
                        float f = NoteFrequency(98 + (i * 5) % 12);
                        AddTone(b, r, 0.45f + i * 0.09f, 0.25f, f, f, Wave.Sine, 0.14f, 0.001f, 0.1f);
                    }
                    break;
                case SoundId.SpecialMove:
                    // 風を切る音（通常攻撃より強く、高く上がる）に、短い電子音の上昇を重ねる。
                    b = new float[Samples(0.32f, r)];
                    AddNoise(b, r, rng, 0f, 0.28f, 0.7f, 0.01f, 0.09f, 1200f, 7000f, 500f);
                    AddTone(b, r, 0f, 0.22f, 330f, 1320f, Wave.Saw, 0.22f, 0.003f, 0.08f);
                    AddTone(b, r, 0.04f, 0.2f, 660f, 1980f, Wave.Pulse25, 0.12f, 0.003f, 0.07f);
                    break;
                case SoundId.SuperStart:
                    // 暗転の間（約 0.3 秒）に低い唸りが上がり、最後に明るい和音で光る。
                    b = new float[Samples(1.1f, r)];
                    AddTone(b, r, 0f, 0.45f, 55f, 220f, Wave.Saw, 0.45f, 0.01f, 0.3f);
                    AddTone(b, r, 0f, 0.45f, 110f, 440f, Wave.Square, 0.18f, 0.01f, 0.3f);
                    AddNoise(b, r, rng, 0f, 0.4f, 0.4f, 0.05f, 0.2f, 400f, 6000f, 200f);
                    ApplyTremolo(b, r, 18f, 0.35f);
                    int[] flash = { 79, 83, 86, 91 };
                    foreach (int note in flash)
                    {
                        float f = NoteFrequency(note);
                        AddTone(b, r, 0.3f, 0.8f, f, f, Wave.Triangle, 0.2f, 0.004f, 0.4f);
                        AddTone(b, r, 0.3f, 0.6f, f * 2f, f * 2f, Wave.Sine, 0.08f, 0.004f, 0.25f);
                    }
                    AddNoise(b, r, rng, 0.3f, 0.12f, 0.6f, 0.001f, 0.05f, 9000f, 9000f, 2000f);
                    break;
                case SoundId.SkillReady:
                    b = new float[Samples(0.25f, r)];
                    AddTone(b, r, 0f, 0.12f, NoteFrequency(88), NoteFrequency(88), Wave.Sine, 0.35f, 0.002f, 0.05f);
                    AddTone(b, r, 0.06f, 0.18f, NoteFrequency(95), NoteFrequency(95), Wave.Sine, 0.35f, 0.002f, 0.07f);
                    break;
                case SoundId.MirrorBall:
                    b = new float[Samples(0.8f, r)];
                    int[] sparkle = { 84, 88, 91, 95, 100 };
                    for (int i = 0; i < sparkle.Length; i++)
                    {
                        float f = NoteFrequency(sparkle[i]);
                        AddTone(b, r, i * 0.04f, 0.5f, f, f, Wave.Sine, 0.25f, 0.002f, 0.2f);
                        AddTone(b, r, i * 0.04f, 0.3f, f * 2f, f * 2f, Wave.Sine, 0.08f, 0.002f, 0.1f);
                    }
                    ApplyTremolo(b, r, 12f, 0.3f);
                    break;
                case SoundId.SafePlace:
                    b = new float[Samples(0.45f, r)];
                    AddTone(b, r, 0f, 0.3f, 110f, 70f, Wave.Sine, 0.9f, 0.001f, 0.12f);
                    AddNoise(b, r, rng, 0f, 0.06f, 0.6f, 0.001f, 0.02f, 3000f, 1200f, 200f);
                    AddTone(b, r, 0.18f, 0.08f, 2400f, 2400f, Wave.Square, 0.12f, 0.001f, 0.02f);
                    AddTone(b, r, 0.26f, 0.08f, 1800f, 1800f, Wave.Square, 0.12f, 0.001f, 0.02f);
                    break;
                case SoundId.SafeBreak:
                    b = new float[Samples(1.2f, r)];
                    AddNoise(b, r, rng, 0f, 0.3f, 1f, 0.001f, 0.1f, 6000f, 1500f, 300f);
                    AddTone(b, r, 0f, 0.4f, 160f, 50f, Wave.Saw, 0.5f, 0.002f, 0.15f);
                    for (int i = 0; i < 14; i++)
                    {
                        float f = NoteFrequency(93 + (i * 7) % 12);
                        AddTone(b, r, 0.15f + i * 0.05f, 0.2f, f, f, Wave.Sine, 0.16f, 0.001f, 0.08f);
                    }
                    break;
                case SoundId.CoinToss:
                    b = new float[Samples(0.3f, r)];
                    AddTone(b, r, 0f, 0.25f, NoteFrequency(98), NoteFrequency(98), Wave.Sine, 0.4f, 0.001f, 0.1f);
                    AddTone(b, r, 0f, 0.2f, NoteFrequency(105), NoteFrequency(105), Wave.Sine, 0.2f, 0.001f, 0.07f);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, "合成の仕方がない効果音");
            }
            Finish(b, 0.9f);
            return b;
        }

        /// <summary>
        /// BGM の波形（BgmSampleRate、モノラル、-1〜1。繰り返して鳴らす）。140 拍/分、8 小節のチップチューン風:
        /// ベース（三角波）、和音のアルペジオ（細い矩形波）、後半のメロディ（矩形波）、ドラム（キック、スネア、ハイハット）。
        /// </summary>
        public static float[] SynthesizeBgm()
        {
            const int r = BgmSampleRate;
            float step = 60f / BgmTempo / 4f; // 16 分音符
            int steps = BgmBars * 16;
            var b = new float[Samples(step * steps, r)];
            var rng = new System.Random(7);

            // コード（ルートの MIDI 番号と、短調・長調）: Am F C G | Am F G E。
            int[] roots = { 57, 53, 60, 55, 57, 53, 55, 52 };
            bool[] minor = { true, false, false, false, true, false, false, false };
            // 後半 4 小節のメロディ（4 分音符。0 は休み）。
            int[] melody =
            {
                76, 72, 69, 72,
                77, 76, 72, 69,
                79, 76, 72, 74,
                71, 74, 76, 0,
            };

            for (int s = 0; s < steps; s++)
            {
                int bar = s / 16;
                int inBar = s % 16;
                float t = s * step;
                int root = roots[bar];
                int third = root + (minor[bar] ? 3 : 4);
                int fifth = root + 7;

                // ベース: 8 分音符でルート（裏はオクターブ上）。
                if (inBar % 2 == 0)
                {
                    int note = root - 24 + (inBar % 4 == 2 ? 12 : 0);
                    AddTone(b, r, t, step * 2f, NoteFrequency(note), NoteFrequency(note), Wave.Triangle, 0.32f, 0.003f, 0.12f);
                }

                // アルペジオ: 16 分音符で和音の音を上がっていく。
                int[] arp = { root + 12, third + 12, fifth + 12, root + 24 };
                int arpNote = arp[inBar % 4];
                AddTone(b, r, t, step, NoteFrequency(arpNote), NoteFrequency(arpNote), Wave.Pulse25, 0.07f, 0.002f, 0.05f);

                // メロディ（後半）。
                if (bar >= 4 && inBar % 4 == 0)
                {
                    int note = melody[(bar - 4) * 4 + inBar / 4];
                    if (note > 0)
                    {
                        AddTone(b, r, t, step * 4f, NoteFrequency(note), NoteFrequency(note), Wave.Square, 0.1f, 0.005f, 0.25f);
                    }
                }

                // ドラム。
                bool kick = inBar == 0 || inBar == 8 || (bar % 2 == 1 && inBar == 10);
                if (kick)
                {
                    AddTone(b, r, t, 0.16f, 150f, 45f, Wave.Sine, 0.7f, 0.001f, 0.06f);
                }
                if (inBar == 4 || inBar == 12)
                {
                    AddNoise(b, r, rng, t, 0.14f, 0.3f, 0.001f, 0.04f, 6000f, 6000f, 900f);
                    AddTone(b, r, t, 0.08f, 200f, 170f, Wave.Triangle, 0.2f, 0.001f, 0.03f);
                }
                if (inBar % 2 == 0)
                {
                    float open = inBar == 14 ? 0.05f : 0.012f;
                    AddNoise(b, r, rng, t, 0.08f, 0.09f, 0.001f, open, 11000f, 11000f, 7000f);
                }
            }
            Finish(b, 0.8f);
            return b;
        }

        /// <summary>
        /// 緊張感のある BGM の波形（BgmSampleRate、モノラル、繰り返して鳴らす）。168 拍/分、8 小節。短調（Dm B♭ Gm A）で、
        /// 16 分音符で刻むベース（のこぎり波）、裏拍の和音、上がっていくアルペジオ、4 つ打ちのキック、16 分のハイハット、
        /// 4 小節ごとのうなり（警報の名残）。
        /// </summary>
        public static float[] SynthesizeTenseBgm()
        {
            const int r = BgmSampleRate;
            float step = 60f / TenseBgmTempo / 4f; // 16 分音符
            int steps = BgmBars * 16;
            var b = new float[Samples(step * steps, r)];
            var rng = new System.Random(11);

            int[] roots = { 50, 46, 43, 45, 50, 46, 43, 45 }; // D B♭ G A（2 回）
            bool[] minor = { true, false, true, false, true, false, true, false };

            for (int s = 0; s < steps; s++)
            {
                int bar = s / 16;
                int inBar = s % 16;
                float t = s * step;
                int root = roots[bar];
                int third = root + (minor[bar] ? 3 : 4);
                int fifth = root + 7;

                // ベース: 16 分音符で刻む（拍の頭はオクターブ下を強く）。
                int bassNote = root - 12 - (inBar % 4 == 0 ? 12 : 0);
                AddTone(b, r, t, step, NoteFrequency(bassNote), NoteFrequency(bassNote), Wave.Saw, inBar % 4 == 0 ? 0.26f : 0.16f, 0.002f, 0.06f);

                // 裏拍の和音（短く切る）。
                if (inBar % 4 == 2)
                {
                    foreach (int note in new[] { root + 12, third + 12, fifth + 12 })
                    {
                        AddTone(b, r, t, step * 1.5f, NoteFrequency(note), NoteFrequency(note), Wave.Pulse25, 0.06f, 0.002f, 0.05f);
                    }
                }

                // 上がっていくアルペジオ（後半 4 小節は 1 オクターブ上）。
                int[] arp = { root + 24, third + 24, fifth + 24, root + 36 };
                int arpNote = arp[inBar % 4] + (bar >= 4 ? 12 : 0);
                AddTone(b, r, t, step * 0.8f, NoteFrequency(arpNote), NoteFrequency(arpNote), Wave.Square, 0.045f, 0.002f, 0.04f);

                // ドラム: 4 つ打ちのキック、2・4 拍のスネア、16 分のハイハット（裏を強く）。
                if (inBar % 4 == 0)
                {
                    AddTone(b, r, t, 0.14f, 160f, 45f, Wave.Sine, 0.75f, 0.001f, 0.05f);
                }
                if (inBar == 4 || inBar == 12)
                {
                    AddNoise(b, r, rng, t, 0.12f, 0.32f, 0.001f, 0.035f, 7000f, 7000f, 1200f);
                }
                AddNoise(b, r, rng, t, 0.05f, inBar % 2 == 1 ? 0.1f : 0.05f, 0.001f, 0.01f, 11000f, 11000f, 7500f);

                // 4 小節ごとに、下から上がるうなり。
                if (inBar == 0 && bar % 4 == 0)
                {
                    AddTone(b, r, t, step * 16f, NoteFrequency(root), NoteFrequency(root + 12), Wave.Saw, 0.06f, 0.2f, 1.5f);
                }
            }
            Finish(b, 0.8f);
            return b;
        }

        /// <summary>MIDI のノート番号の周波数（69 = A4 = 440Hz）。</summary>
        public static float NoteFrequency(int note)
        {
            return 440f * Mathf.Pow(2f, (note - 69) / 12f);
        }

        private static int Samples(float seconds, int rate)
        {
            return Mathf.Max(1, Mathf.CeilToInt(seconds * rate));
        }

        private static AudioClip ToClip(string name, float[] samples, int rate)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // 周波数を f0 から f1 へ指数的に変えながら鳴らす。音量は attack 秒で立ち上がり、時定数 decay 秒で減っていく。
        private static void AddTone(float[] b, int rate, float start, float duration, float f0, float f1, Wave wave, float amp,
            float attack, float decay)
        {
            int from = Mathf.Max(0, (int)(start * rate));
            int to = Mathf.Min(b.Length, from + Samples(duration, rate));
            double phase = 0;
            for (int i = from; i < to; i++)
            {
                float t = (float)(i - from) / rate;
                float k = duration > 0f ? t / duration : 0f;
                float f = f0 * Mathf.Pow(f1 / f0, k);
                phase += f / rate;
                float p = (float)(phase - Math.Floor(phase));
                b[i] += WaveValue(wave, p) * amp * Envelope(t, attack, decay);
            }
        }

        // ノイズを鳴らす。ローパスのカットオフは lowpass0 から lowpass1 へ変わる。highpass より低い音を削る。
        private static void AddNoise(float[] b, int rate, System.Random rng, float start, float duration, float amp,
            float attack, float decay, float lowpass0, float lowpass1, float highpass)
        {
            int from = Mathf.Max(0, (int)(start * rate));
            int to = Mathf.Min(b.Length, from + Samples(duration, rate));
            float low = 0f;
            float lowForHigh = 0f;
            float highCoefficient = OnePole(highpass, rate);
            for (int i = from; i < to; i++)
            {
                float t = (float)(i - from) / rate;
                float k = duration > 0f ? t / duration : 0f;
                float cutoff = Mathf.Lerp(lowpass0, lowpass1, k);
                float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (x - low) * OnePole(cutoff, rate);
                lowForHigh += (low - lowForHigh) * highCoefficient;
                b[i] += (low - lowForHigh) * amp * Envelope(t, attack, decay);
            }
        }

        private static void ApplyTremolo(float[] b, int rate, float hz, float depth)
        {
            for (int i = 0; i < b.Length; i++)
            {
                float t = (float)i / rate;
                b[i] *= 1f - depth * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * hz * t));
            }
        }

        // 1 次のローパスの係数（カットオフ周波数から）。
        private static float OnePole(float cutoff, int rate)
        {
            return 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Clamp(cutoff, 10f, rate * 0.45f) / rate);
        }

        private static float Envelope(float t, float attack, float decay)
        {
            if (t < attack)
            {
                return attack > 0f ? t / attack : 1f;
            }
            return decay > 0f ? Mathf.Exp(-(t - attack) / decay) : 0f;
        }

        private static float WaveValue(Wave wave, float p)
        {
            switch (wave)
            {
                case Wave.Sine: return Mathf.Sin(2f * Mathf.PI * p);
                case Wave.Square: return p < 0.5f ? 0.8f : -0.8f;
                case Wave.Pulse25: return p < 0.25f ? 0.8f : -0.8f;
                case Wave.Triangle: return p < 0.5f ? 4f * p - 1f : 3f - 4f * p;
                default: return 2f * p - 1f;
            }
        }

        // 大きさを peak にそろえ、やわらかく丸める（音割れを防ぐ）。終わりの数ミリ秒を消していく（ぷつっと鳴らないように）。
        private static void Finish(float[] b, float peak)
        {
            float max = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                max = Mathf.Max(max, Mathf.Abs(b[i]));
            }
            float gain = max > 1e-6f ? 1.4f / max : 0f;
            float norm = peak / (float)Math.Tanh(1.4);
            int fade = Mathf.Min(b.Length, 64);
            for (int i = 0; i < b.Length; i++)
            {
                float v = (float)Math.Tanh(b[i] * gain) * norm;
                if (i >= b.Length - fade)
                {
                    v *= (float)(b.Length - 1 - i) / fade;
                }
                b[i] = v;
            }
        }
    }
}
