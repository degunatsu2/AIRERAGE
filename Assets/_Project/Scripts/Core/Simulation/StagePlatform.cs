using System;
using System.Collections.Generic;
using System.Numerics;

namespace Airerage.Core.Simulation
{
    /// <summary>動く足場の種類（StagePlatform。ユーザーの要望、2026-10-26）。</summary>
    public enum StagePlatformKind : byte
    {
        /// <summary>決まった道のりを動く足場（ロールケーキ、クレーンのコンテナ、エレベーター）。位置は試合のティック数だけで決まる。</summary>
        Path = 0,

        /// <summary>
        /// ワイヤーでつなぐと上に開き、つなぐのをやめると下に閉じる扉（研究所）。開き具合は状態（StageState）。閉じる先に人がいると
        /// 閉じない。
        /// </summary>
        Door = 1,
    }

    /// <summary>動く足場の道のりの 1 点（周期の中のティックと、元の位置からのずれ）。</summary>
    public readonly struct PlatformKey : IEquatable<PlatformKey>
    {
        public readonly int Tick;
        public readonly Vector2 Offset;

        public PlatformKey(int tick, Vector2 offset)
        {
            Tick = tick;
            Offset = offset;
        }

        public bool Equals(PlatformKey other)
        {
            return Tick == other.Tick && Offset.Equals(other.Offset);
        }

        public override bool Equals(object obj)
        {
            return obj is PlatformKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Tick, Offset);
        }
    }

    /// <summary>
    /// ステージの動く足場と扉（ユーザーの要望、2026-10-26）。軸平行の箱で、乗れる・ぶつかる（地形と同じ）。Attachable ならワイヤーを
    /// つなげて、つないだ点は足場と一緒に動く。足場に乗っている人と、横に張り付いている人は一緒に運ばれる（StagePlatforms.Carry）。
    /// - 道のり（Path）: 周期 Period の中で、点（Keys）の間をまっすぐ（Smooth なら、ゆっくり動き出して止まる）動く。最後の点の後は、
    ///   周期の終わりまで止まる。ActiveTo が ActiveFrom より大きければ、その間だけある（ほかの間は当たりも見た目もない）。
    /// - 扉（Door）: 閉じた位置（Origin）から OpenHeight だけ上に開く。開き具合は 0〜DoorScale（StageState）。
    /// 値は作るときに検査する（不正なら ArgumentException）。
    /// </summary>
    public sealed class StagePlatform
    {
        /// <summary>扉の開き具合の全体（DoorScale で全開）。</summary>
        public const int DoorScale = 1000;

        private readonly PlatformKey[] _keys;

        private StagePlatform(StagePlatformKind kind, Vector2 origin, Vector2 halfSize, bool attachable, PlatformKey[] keys, int period,
            int phaseTicks, int activeFrom, int activeTo, bool smooth, float openHeight, int openTicks, int closeTicks)
        {
            if (!(halfSize.X > 0f) || !(halfSize.Y > 0f) || float.IsInfinity(halfSize.X) || float.IsInfinity(halfSize.Y)
                || float.IsNaN(origin.X) || float.IsNaN(origin.Y) || float.IsInfinity(origin.X) || float.IsInfinity(origin.Y))
            {
                throw new ArgumentException($"動く足場の位置か大きさが不正です（{origin}、半分の大きさ {halfSize}）");
            }
            Kind = kind;
            Origin = origin;
            HalfSize = halfSize;
            Attachable = attachable;
            _keys = keys;
            Period = period;
            PhaseTicks = phaseTicks;
            ActiveFrom = activeFrom;
            ActiveTo = activeTo;
            Smooth = smooth;
            OpenHeight = openHeight;
            OpenTicks = openTicks;
            CloseTicks = closeTicks;
        }

        public StagePlatformKind Kind { get; }

        /// <summary>道のりの元の位置（ずれ 0 の中心）。扉は閉じたときの中心。</summary>
        public Vector2 Origin { get; }

        public Vector2 HalfSize { get; }
        public bool Attachable { get; }
        public IReadOnlyList<PlatformKey> Keys => _keys;
        public int Period { get; }
        public int PhaseTicks { get; }
        public int ActiveFrom { get; }
        public int ActiveTo { get; }
        public bool Smooth { get; }

        /// <summary>扉が開く高さ（m）と、全開まで・閉じきるまでのティック数。</summary>
        public float OpenHeight { get; }
        public int OpenTicks { get; }
        public int CloseTicks { get; }

        public bool IsDoor => Kind == StagePlatformKind.Door;

        /// <summary>道のりを動く足場を作る。keys は周期の中のティックの順（最初は 0）。</summary>
        public static StagePlatform Path(Vector2 origin, Vector2 halfSize, bool attachable, IReadOnlyList<PlatformKey> keys, int period,
            int phaseTicks = 0, int activeFrom = 0, int activeTo = 0, bool smooth = false)
        {
            if (keys == null || keys.Count == 0)
            {
                throw new ArgumentException("動く足場の道のりの点がありません");
            }
            if (period <= 0 || phaseTicks < 0)
            {
                throw new ArgumentException($"動く足場の周期が不正です（周期 {period}、ずれ {phaseTicks}）");
            }
            var copy = new PlatformKey[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                PlatformKey key = keys[i];
                bool ordered = i == 0 ? key.Tick == 0 : key.Tick > copy[i - 1].Tick;
                if (!ordered || key.Tick > period || float.IsNaN(key.Offset.X) || float.IsNaN(key.Offset.Y)
                    || float.IsInfinity(key.Offset.X) || float.IsInfinity(key.Offset.Y))
                {
                    throw new ArgumentException($"動く足場の道のりの {i} 番目の点が不正です（ティック {key.Tick}、ずれ {key.Offset}。最初は 0 で、増えていく）");
                }
                copy[i] = key;
            }
            if (activeFrom < 0 || activeTo > period || (activeTo > 0 && activeTo <= activeFrom))
            {
                throw new ArgumentException($"動く足場のある間が不正です（{activeFrom}〜{activeTo}、周期 {period}）");
            }
            return new StagePlatform(StagePlatformKind.Path, origin, halfSize, attachable, copy, period, phaseTicks % period, activeFrom, activeTo,
                smooth, 0f, 0, 0);
        }

        /// <summary>扉を作る（closedCenter は閉じたときの中心）。</summary>
        public static StagePlatform Door(Vector2 closedCenter, Vector2 halfSize, bool attachable, float openHeight, int openTicks, int closeTicks)
        {
            if (!(openHeight > 0f) || float.IsInfinity(openHeight) || openTicks <= 0 || closeTicks <= 0)
            {
                throw new ArgumentException($"扉の開く高さか速さが不正です（高さ {openHeight}、開く {openTicks}、閉じる {closeTicks} ティック）");
            }
            return new StagePlatform(StagePlatformKind.Door, closedCenter, halfSize, attachable, new[] { new PlatformKey(0, Vector2.Zero) }, 1,
                0, 0, 0, false, openHeight, openTicks, closeTicks);
        }

        /// <summary>
        /// 試合のティック tick（扉は開き具合 open）の中心。なければ（ある間の外）false。
        /// </summary>
        public bool TryGetCenter(int tick, int open, out Vector2 center)
        {
            if (Kind == StagePlatformKind.Door)
            {
                center = Origin + new Vector2(0f, OpenHeight * Math.Max(0, Math.Min(DoorScale, open)) / DoorScale);
                return true;
            }
            int t = (int)((((long)tick + PhaseTicks) % Period + Period) % Period);
            if (ActiveTo > ActiveFrom && (t < ActiveFrom || t >= ActiveTo))
            {
                center = Origin;
                return false;
            }
            center = Origin + OffsetAt(t);
            return true;
        }

        /// <summary>中心 center の箱の範囲。</summary>
        public void BoundsAt(Vector2 center, out Vector2 min, out Vector2 max)
        {
            min = center - HalfSize;
            max = center + HalfSize;
        }

        // 周期の中のティック t のずれ。
        private Vector2 OffsetAt(int t)
        {
            for (int i = 0; i + 1 < _keys.Length; i++)
            {
                PlatformKey from = _keys[i];
                PlatformKey to = _keys[i + 1];
                if (t < to.Tick)
                {
                    float f = (float)(t - from.Tick) / (to.Tick - from.Tick);
                    if (Smooth)
                    {
                        f = f * f * (3f - 2f * f);
                    }
                    return from.Offset + (to.Offset - from.Offset) * f;
                }
            }
            return _keys[_keys.Length - 1].Offset;
        }

        /// <summary>並べ替えの順（シーンから集めた順によらず、同じ番号をつけるため）。</summary>
        public int CompareTo(StagePlatform other)
        {
            int c = Kind.CompareTo(other.Kind);
            if (c == 0)
            {
                c = Origin.X.CompareTo(other.Origin.X);
            }
            if (c == 0)
            {
                c = Origin.Y.CompareTo(other.Origin.Y);
            }
            if (c == 0)
            {
                c = HalfSize.X.CompareTo(other.HalfSize.X);
            }
            if (c == 0)
            {
                c = HalfSize.Y.CompareTo(other.HalfSize.Y);
            }
            if (c == 0)
            {
                c = PhaseTicks.CompareTo(other.PhaseTicks);
            }
            if (c == 0)
            {
                c = Period.CompareTo(other.Period);
            }
            if (c == 0)
            {
                c = _keys.Length.CompareTo(other._keys.Length);
            }
            for (int i = 0; c == 0 && i < _keys.Length; i++)
            {
                c = _keys[i].Tick.CompareTo(other._keys[i].Tick);
                if (c == 0)
                {
                    c = _keys[i].Offset.X.CompareTo(other._keys[i].Offset.X);
                }
                if (c == 0)
                {
                    c = _keys[i].Offset.Y.CompareTo(other._keys[i].Offset.Y);
                }
            }
            return c;
        }

        /// <summary>同じ足場か（値がすべて同じ。表示が自分の番号を探すのに使う）。</summary>
        public bool SameAs(StagePlatform other)
        {
            return other != null && CompareTo(other) == 0 && Attachable == other.Attachable && ActiveFrom == other.ActiveFrom
                && ActiveTo == other.ActiveTo && Smooth == other.Smooth && OpenHeight.Equals(other.OpenHeight) && OpenTicks == other.OpenTicks
                && CloseTicks == other.CloseTicks;
        }

        public override string ToString()
        {
            return $"{Kind} at {Origin}, half {HalfSize}";
        }
    }

    /// <summary>
    /// ステージの状態のうち、キャラクター・コイン・物のどれでもないもの（扉の開き具合。ユーザーの要望、2026-10-26）。巻き戻しと
    /// チェックサムに乗る（WorldSnapshot、StateCodec.WorldChecksum）。番号は SimWorld.Platforms の番号（扉でない足場は 0 のまま）。
    /// 前のティックの開き具合も持つ（扉につないだワイヤーの接続点を、開いた分だけ動かす）。
    /// </summary>
    public sealed class StageState
    {
        private int[] _open = new int[0];
        private int[] _previous = new int[0];

        public int Count => _open.Length;

        /// <summary>扉 index の開き具合（0〜StagePlatform.DoorScale）と、前のティックの開き具合。</summary>
        public int OpenOf(int index) => index >= 0 && index < _open.Length ? _open[index] : 0;
        public int PreviousOf(int index) => index >= 0 && index < _previous.Length ? _previous[index] : 0;

        /// <summary>数を count にする（すべて閉じる）。</summary>
        public void Reset(int count)
        {
            _open = new int[count];
            _previous = new int[count];
        }

        /// <summary>扉 index の開き具合を open にする（前のティックの開き具合は、今の値になる）。</summary>
        public void Advance(int index, int open)
        {
            _previous[index] = _open[index];
            _open[index] = open;
        }

        public void CopyFrom(StageState other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            if (_open.Length != other._open.Length)
            {
                Reset(other._open.Length);
            }
            Array.Copy(other._open, _open, _open.Length);
            Array.Copy(other._previous, _previous, _previous.Length);
        }

        public bool SameAs(StageState other)
        {
            if (other == null || other._open.Length != _open.Length)
            {
                return false;
            }
            for (int i = 0; i < _open.Length; i++)
            {
                if (_open[i] != other._open[i] || _previous[i] != other._previous[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
