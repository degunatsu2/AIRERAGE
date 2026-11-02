using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 右上の全員の情報（ユーザーの要望、2026-10-14）。1 人 1 枚の札（PlayerStatusPlate）で、縁と斜めの線はプレイヤーの色。
    /// 左にプレイヤーの色の四角と番号、キャラクターのロゴ（docs/13 T5-8）とプレイヤーネーム、右を 4 つに分けて BANK（確定コイン）/
    /// COIN（所持コイン）/ K（撃破した数）/ F（撃破された数）。BANK の文字と数だけ黄色。
    /// いつも今の順位（MatchResults.Rank。確定コインの順）に並べ、順位が変わると札が滑って入れ替わる。確定コインが最も多い人
    /// （1 枚以上）には、札の左に王冠をつける（同じ数なら全員）。
    /// 撃破されたら（復帰を待っている間）: 札がひび割れて揺れ、灰色に暗くなり、「RESPAWN n」（復帰までの秒）を出す。復帰したら、
    /// 白く光ってからふつうの見た目に戻る。
    /// 100 枚のコインを取った人の札は、派手な音とともに虹色に光り「JACKPOT!」を出し、少しの後にふつうの見た目に戻る（ユーザーの要望、
    /// 2026-10-15）。取った人は、100 枚のコインが前のフレームにあった（または、この間に出た）のに今はなく、確定＋所持が 100 枚以上
    /// 増えた人（出たティックにその場にいれば、出たのと同じティックに拾うので、出ている間を見られないことがある）。
    /// WANTED（所持コインが MatchRules.WantedHeldCoins 以上。CatchUpRules。ユーザーの要望、2026-11-02）の人は、札のロゴの所に手配書の印を押す
    /// （印鑑を押すように落ちてきて、朱色の輪が広がり、札が揺れる）、札を赤く強調して斜めに大きな「WANTED!」のスタンプを押し、低めの
    /// サイレンを鳴らす。所持コインが減って外れたら、印が光の粒になって消え、赤い強調とスタンプは薄くなって消える。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class ScoreboardHud : MonoBehaviour
    {
        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [SerializeField] private int _width = 300;
        [SerializeField] private int _rowHeight = 52;
        [SerializeField] private float _rowGap = 6f;

        // キャラクターと重なった札を透かす（HudOcclusion）。
        private readonly HudOcclusion.Link _occlusion = new HudOcclusion.Link();
        [Tooltip("画面の右上からの余白（ピクセル）")]
        [SerializeField] private Vector2 _margin = new Vector2(12f, 10f);
        [Tooltip("順位が変わったときに、札が新しい位置へ滑る速さ（1 秒あたりの割合）")]
        [SerializeField] private float _slideSpeed = 10f;
        [SerializeField] private Color _bankColor = new Color(1f, 0.85f, 0.2f);
        [Tooltip("撃破されたときに、ひび割れてから灰色になるまでの秒数")]
        [SerializeField] private float _crackSeconds = 0.35f;
        [Tooltip("灰色になりきるまでの秒数")]
        [SerializeField] private float _graySeconds = 0.3f;
        [Tooltip("復帰したときに白く光る秒数")]
        [SerializeField] private float _flashSeconds = 0.45f;
        [Tooltip("100 枚のコインを取ったときに虹色に光る秒数（最後の 0.5 秒で消える）")]
        [SerializeField] private float _jackpotSeconds = 3f;
        [Tooltip("100 枚のコインを取ったときの音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("WANTED の印を押す動きの秒数")]
        [SerializeField] private float _stampSeconds = 0.6f;
        [Tooltip("WANTED の印が光の粒になって消える秒数")]
        [SerializeField] private float _dissolveSeconds = 0.9f;

        private readonly List<float> _rowY = new List<float>();
        private ISimulationFeed _feed;
        private PlayerStatusPlate _plate;
        private int[] _order = new int[0];
        private bool[] _waiting = new bool[0];
        private float[] _fellAt = new float[0];
        private float[] _backAt = new float[0];
        private float[] _jackpotAt = new float[0];
        private int[] _coinsTotal = new int[0];
        private bool[] _wanted = new bool[0];
        private float[] _wantedAt = new float[0];
        private float[] _unwantedAt = new float[0];
        private bool _jackpotWasPresent;
        private int _lastTick = -1;

        /// <summary>100 枚のコインを取って虹色に光った回数（テスト用）。</summary>
        public int JackpotCelebrations { get; private set; }

        /// <summary>WANTED の印を押した回数と、消した回数（テスト用）。</summary>
        public int WantedStamps { get; private set; }
        public int WantedDissolves { get; private set; }

        /// <summary>キャラクター index の札に WANTED の印を出しているか（テスト用）。</summary>
        public bool IsWanted(int index)
        {
            return index < _wanted.Length && _wanted[index];
        }

        /// <summary>キャラクター index の札が、100 枚のコインを取って虹色に光っているか（テスト用）。</summary>
        public bool IsCelebrating(int index)
        {
            return index < _jackpotAt.Length && Time.time - _jackpotAt[index] < _jackpotSeconds;
        }

        /// <summary>今の並び（上から、キャラクターの番号）と、王冠をつけている人の数（テスト用）。</summary>
        public IReadOnlyList<int> Order => _order;
        public int CrownCount { get; private set; }

        /// <summary>キャラクター index に王冠をつけているか（テスト用）。</summary>
        public bool HasCrown(int index)
        {
            return IsLeader(_feed, index);
        }

        /// <summary>キャラクター index の札に出すロゴ（キャラクターのアセットの Logo。なければ null）。結果の画面でも使う。</summary>
        public static Texture2D LogoOf(ISimulationFeed feed, int index)
        {
            CharacterConfigAsset character = index >= 0 && index < feed.CharacterCount ? feed.CharacterOf(index) : null;
            return character != null ? character.Logo : null;
        }

        /// <summary>キャラクター index の札の今の見た目（テスト用）。</summary>
        public PlayerStatusPlate.Look LookOf(int index)
        {
            return index < _waiting.Length ? CurrentLook(index) : default;
        }

        /// <summary>
        /// 確定コインが最も多い人（1 枚以上。同じ数なら全員。チーム対戦なら確定の合計が最も多いチームの全員。TeamRules.ScoreOf）か。
        /// 結果の画面でも使う。
        /// </summary>
        public static bool IsLeader(ISimulationFeed feed, int index)
        {
            int count = feed.CharacterCount;
            if (index < 0 || index >= count)
            {
                return false;
            }
            var states = new CharacterState[count];
            for (int i = 0; i < count; i++)
            {
                states[i] = feed.GetState(i);
            }
            int best = 0;
            for (int i = 0; i < count; i++)
            {
                best = Mathf.Max(best, TeamRules.ScoreOf(states, i));
            }
            return best > 0 && TeamRules.ScoreOf(states, index) == best;
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            _plate?.Dispose();
        }

        private void Update()
        {
            int count = _feed.CharacterCount;
            var states = new CharacterState[count];
            for (int i = 0; i < count; i++)
            {
                states[i] = _feed.GetState(i);
            }
            _order = MatchResults.Rank(states, _feed.Match.Winner);
            TrackFalls(states);
            TrackJackpot(states);
            TrackWanted(states);

            while (_rowY.Count < count)
            {
                _rowY.Add(-1f);
            }
            float blend = 1f - Mathf.Exp(-_slideSpeed * Time.unscaledDeltaTime);
            int crowns = 0;
            for (int rank = 0; rank < _order.Length; rank++)
            {
                int index = _order[rank];
                float target = _margin.y + rank * (_rowHeight + _rowGap);
                _rowY[index] = _rowY[index] < 0f ? target : Mathf.Lerp(_rowY[index], target, blend);
                crowns += IsLeader(_feed, index) ? 1 : 0;
            }
            CrownCount = crowns;
        }

        // 100 枚のコインを取った人を探す: 100 枚のコインが前のフレームにあった（またはこの間に出た）のに今はなく、
        // 確定＋所持が 100 枚以上増えた人。
        private void TrackJackpot(CharacterState[] states)
        {
            int tick = _feed.CurrentTick;
            int spawn = _feed.Rules != null ? FieldCoinRules.JackpotSpawnTick(_feed.Rules) : -1;
            bool appeared = _jackpotWasPresent || (spawn >= 0 && _lastTick >= 0 && _lastTick <= spawn && tick > spawn);
            _lastTick = tick;
            bool present = false;
            if (_feed.Coins != null)
            {
                foreach (FieldCoin coin in _feed.Coins.Coins)
                {
                    present |= CoinView.LookOf(coin.Value) == CoinLook.Rainbow;
                }
            }
            if (_coinsTotal.Length != states.Length)
            {
                _coinsTotal = new int[states.Length];
                _jackpotAt = new float[states.Length];
                for (int i = 0; i < states.Length; i++)
                {
                    _jackpotAt[i] = float.NegativeInfinity;
                }
            }
            for (int i = 0; i < states.Length; i++)
            {
                int total = states[i].Coins.Held + states[i].Coins.Banked;
                if (appeared && !present && total - _coinsTotal[i] >= CoinView.RainbowValue)
                {
                    _jackpotAt[i] = Time.time;
                    JackpotCelebrations++;
                    if (_audio != null)
                    {
                        _audio.Play(SoundId.JackpotGet);
                    }
                }
                _coinsTotal[i] = total;
            }
            _jackpotWasPresent = present;
        }

        // 撃破された瞬間（復帰を待ち始めた）と、復帰した瞬間を覚える（人数が変わったら、今の様子から数え直す）。
        private void TrackFalls(CharacterState[] states)
        {
            if (_waiting.Length != states.Length)
            {
                _waiting = new bool[states.Length];
                _fellAt = new float[states.Length];
                _backAt = new float[states.Length];
                for (int i = 0; i < states.Length; i++)
                {
                    _waiting[i] = states[i].Combat.IsWaitingToRespawn;
                    _fellAt[i] = float.NegativeInfinity;
                    _backAt[i] = float.NegativeInfinity;
                }
                return;
            }
            for (int i = 0; i < states.Length; i++)
            {
                bool waiting = states[i].Combat.IsWaitingToRespawn;
                if (waiting && !_waiting[i])
                {
                    _fellAt[i] = Time.time;
                }
                else if (!waiting && _waiting[i])
                {
                    _backAt[i] = Time.time;
                }
                _waiting[i] = waiting;
            }
        }

        // WANTED になった瞬間と、外れた瞬間を覚える（人数が変わったら、今の様子から数え直す。最初から WANTED なら押す動きは出さない）。
        private void TrackWanted(CharacterState[] states)
        {
            MatchRules rules = _feed.Rules;
            if (_wanted.Length != states.Length)
            {
                _wanted = new bool[states.Length];
                _wantedAt = new float[states.Length];
                _unwantedAt = new float[states.Length];
                for (int i = 0; i < states.Length; i++)
                {
                    _wanted[i] = CatchUpRules.IsWanted(rules, states[i].Coins.Held) && !states[i].Combat.HasLeft;
                    _wantedAt[i] = float.NegativeInfinity;
                    _unwantedAt[i] = float.NegativeInfinity;
                }
                return;
            }
            for (int i = 0; i < states.Length; i++)
            {
                bool wanted = CatchUpRules.IsWanted(rules, states[i].Coins.Held) && !states[i].Combat.HasLeft;
                if (wanted && !_wanted[i])
                {
                    _wantedAt[i] = Time.time;
                    WantedStamps++;
                    // 警戒させる低めのサイレン（2026-11-02 の要望）。
                    if (_audio != null)
                    {
                        _audio.Play(SoundId.WantedSiren);
                    }
                }
                else if (!wanted && _wanted[i])
                {
                    _unwantedAt[i] = Time.time;
                    WantedDissolves++;
                }
                _wanted[i] = wanted;
            }
        }

        private PlayerStatusPlate.Look CurrentLook(int index)
        {
            var look = new PlayerStatusPlate.Look();
            if (_feed.GetState(index).Combat.HasLeft)
            {
                // 試合から抜けた人（ネットワーク対戦。docs/15 T6-10）: 灰色のまま「LEFT」。
                look.Gray = 1f;
                look.Overlay = "LEFT";
            }
            else if (_waiting[index])
            {
                float t = Time.time - _fellAt[index];
                if (float.IsInfinity(t))
                {
                    t = _crackSeconds + _graySeconds;
                }
                look.Crack = Mathf.Clamp01(t / (_crackSeconds * 0.4f));
                float shake = Mathf.Clamp01(1f - t / _crackSeconds) * 4f;
                look.Shake = new Vector2(Mathf.Sin(Time.time * 90f) * shake, Mathf.Cos(Time.time * 70f) * shake * 0.5f);
                look.Gray = Mathf.Clamp01((t - _crackSeconds) / _graySeconds);
                float seconds = _feed.GetState(index).Combat.RespawnTicksLeft / (float)SimTime.TicksPerSecond;
                look.Overlay = $"RESPAWN {seconds:0.0}";
            }
            else
            {
                look.Flash = Mathf.Clamp01(1f - (Time.time - _backAt[index]) / _flashSeconds);
            }
            if (index < _jackpotAt.Length)
            {
                look.Jackpot = Mathf.Clamp01((_jackpotSeconds - (Time.time - _jackpotAt[index])) / 0.5f);
            }
            if (index < _wanted.Length)
            {
                if (_wanted[index])
                {
                    look.Wanted = true;
                    float since = Time.time - _wantedAt[index];
                    look.WantedStamp = float.IsInfinity(since) ? 1f : Mathf.Clamp01(since / Mathf.Max(0.01f, _stampSeconds));
                    // 押した瞬間に札が少し揺れる。
                    float impact = look.WantedStamp >= 0.35f ? Mathf.Clamp01(1f - (look.WantedStamp - 0.35f) / 0.3f) : 0f;
                    look.Shake += new Vector2(Mathf.Sin(Time.time * 80f), Mathf.Cos(Time.time * 95f)) * 3f * impact;
                    look.WantedEmphasis = 1f;
                }
                else
                {
                    float since = Time.time - _unwantedAt[index];
                    float t = float.IsInfinity(since) ? 1f : since / Mathf.Max(0.01f, _dissolveSeconds);
                    look.WantedDissolve = t < 1f ? Mathf.Max(0.001f, t) : 0f;
                    // 赤い強調とスタンプは、印が消えるのと同じ間に薄くなって消える（フェードアウト）。
                    look.WantedEmphasis = t < 1f ? 1f - Mathf.Max(0.001f, t) : 0f;
                }
            }
            return look;
        }

        private void OnGUI()
        {
            int count = _feed.CharacterCount;
            if (count == 0 || _order.Length != count || _waiting.Length != count)
            {
                return;
            }
            if (_plate == null)
            {
                _plate = new PlayerStatusPlate(_width, _rowHeight);
            }
            float x = Screen.width - _margin.x - _width;
            foreach (int index in _order)
            {
                var rect = new Rect(x, _rowY[index], _width, _rowHeight);
                // キャラクターと重なったら透かす（王冠の分を含めて調べる。HudOcclusion）。
                var area = new Rect(rect.x - _plate.CrownWidth, rect.y, rect.width + _plate.CrownWidth, rect.height);
                HudFade.Begin(_occlusion.AlphaFor(this, index, area));
                _plate.Draw(rect, index + 1, _feed.LabelOf(index), _feed.ColorOf(index), _feed.GetState(index),
                    IsLeader(_feed, index), CurrentLook(index), _bankColor, LogoOf(_feed, index));
                HudFade.End();
            }
        }
    }
}
