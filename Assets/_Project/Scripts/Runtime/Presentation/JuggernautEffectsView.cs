using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// JUGGERNAUT の技の表示（docs/14 1.7。ユーザーの案、2026-10-29）。状態を読むだけで、書き換えない。
    /// - 砲弾: 空中横は暗い鉄の球、フラックは大きい苔色の球と短い尾。爆発（Core の爆発の印）は、オレンジの球が広がって薄れる。
    /// - 地雷: 暗い円盤と、赤く点滅する灯。張り付いたら、張り付いた面に沿って向く。
    /// - フラックの構え: 体の斜め後ろの上に、弾道の段階（弱・中・強・弩）。操作している人には、今の照準で撃ったときの弾道の予測線と、
    ///   落ちる所の爆発の輪（JuggernautRules.PredictFlak。撃つときと同じ計算）。
    /// - メーデー: ジェット機（R を出すと降りてきて、吊って上がり、選んだ場所へ運んで去る）。操作している人が場所を選ぶ間は、大きな
    ///   カーソル（置ける所は緑、地形と重なる所は赤）と、残りの時間の輪。選べない所を選んだら、赤い N/A とエラーの音。運ばれる間は、降りる
    ///   場所に輪。
    /// - カメラ: 操作している人が構えている間と、場所を選んでいる間は、ステージ全体を見せる（CameraZoom.SetOverview。ローカル対戦の
    ///   共通のカメラは SharedCamera が自分で決める）。
    /// 操作している人の表示は、ネットワーク対戦では自分だけ、ローカル対戦では CPU 以外の全員。
    /// </summary>
    public sealed class JuggernautEffectsView : MonoBehaviour
    {
        private const float Z = -0.3f;
        private const int PredictTicks = 240;
        private const int RingSegments = 40;
        private const float CursorRadius = 2.4f;
        private const float CursorWidth = 0.35f;
        private const float NoticeSeconds = 0.9f;
        private const float JetAbove = 0.25f;
        private const float JetArriveHeight = 22f;
        private const float JetLeaveSeconds = 1.2f;
        private const float OverviewMargin = 2f;

        // この距離（m。ふだんのカメラの距離）より引いたら、文字・線・カーソルをその割合で大きくする（全体を見せている間も読めるように）。
        private const float BaseCameraDistance = 30f;
        private const float LevelHeight = 1.1f;
        private const float NoticeHeight = 1.6f;
        private const float TrajectoryWidth = 0.12f;
        private const float RingWidth = 0.1f;

        private static readonly string[] LevelNames = { "弱", "中", "強", "弩" };
        private static readonly Color[] LevelColors =
        {
            new Color(0.55f, 0.85f, 1f), new Color(0.55f, 1f, 0.5f), new Color(1f, 0.7f, 0.25f), new Color(1f, 0.3f, 0.25f),
        };
        private static readonly Color TrajectoryColor = new Color(1f, 0.75f, 0.3f, 0.75f);
        private static readonly Color FreeColor = new Color(0.3f, 1f, 0.45f, 0.95f);
        private static readonly Color BlockedColor = new Color(1f, 0.25f, 0.2f, 0.95f);
        private static readonly Color BlastColor = new Color(1f, 0.55f, 0.15f, 0.8f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("状態を読む相手（SimulationRunner・LocalMatchRunner・OnlineRunner。ISimulationFeed と ILocalAimSource）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("画角と縦横比を読むカメラ（全体を見せる距離を決める）")]
        [SerializeField] private Camera _camera;
        [Tooltip("全体を見せるのに使う、追従カメラのズーム（なければ動かさない。ローカル対戦は SharedCamera）")]
        [SerializeField] private CameraZoom _zoom;
        [Tooltip("線の見本（隠しておく）")]
        [SerializeField] private LineRenderer _lineTemplate;
        [Tooltip("空中横の砲弾の見本（隠しておく）")]
        [SerializeField] private GameObject _shellTemplate;
        [Tooltip("フラックの砲弾の見本（隠しておく）")]
        [SerializeField] private GameObject _flakTemplate;
        [Tooltip("爆発の見本（半透明の球。隠しておく）")]
        [SerializeField] private GameObject _blastTemplate;
        [Tooltip("地雷の見本（子に \"Lamp\" の灯。隠しておく）")]
        [SerializeField] private GameObject _mineTemplate;
        [Tooltip("ジェット機の見本（隠しておく）")]
        [SerializeField] private GameObject _jetTemplate;
        [Tooltip("文字の見本（TextMesh。隠しておく）")]
        [SerializeField] private TextMesh _textTemplate;
        [Tooltip("音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;

        private readonly Dictionary<int, Shown> _shown = new Dictionary<int, Shown>();
        private readonly List<int> _gone = new List<int>();
        private readonly HashSet<int> _present = new HashSet<int>();
        private readonly List<Character> _characters = new List<Character>();
        private readonly System.Numerics.Vector2[] _path = new System.Numerics.Vector2[PredictTicks];
        private ISimulationFeed _feed;
        private ILocalAimSource _local;
        private MaterialPropertyBlock _block;
        private Font _japanese;
        private bool _primed;
        private float _scale = 1f;

        private sealed class Shown
        {
            public WorldObjectKind Kind;
            public byte Variant;
            public GameObject Visual;
            public Renderer Lamp;
            public Renderer[] Renderers;
        }

        private sealed class Character
        {
            public TextMesh Level;
            public LineRenderer Trajectory;
            public LineRenderer Landing;
            public LineRenderer Cursor;
            public LineRenderer CursorCross;
            public LineRenderer CursorTime;
            public LineRenderer Drop;
            public TextMesh Notice;
            public GameObject Jet;
            public float NoticeUntil;
            public int Rejects = -1;
            public byte Phase;
            public int Serial = -1;
            public float LeaveAt = -1f;
            public Vector3 LeaveFrom;
        }

        /// <summary>出している砲弾の数（爆発の印を除く。テスト用）。</summary>
        public int ShellCount => Count(WorldObjectKind.Shell, false);

        /// <summary>出している爆発の数（テスト用）。</summary>
        public int BlastCount => Count(WorldObjectKind.Shell, true) + Count(WorldObjectKind.Mine, true);

        /// <summary>出している地雷の数（爆発の印を除く。テスト用）。</summary>
        public int MineCount => Count(WorldObjectKind.Mine, false);

        /// <summary>キャラクター index の弾道の段階を出しているか（テスト用）。</summary>
        public bool ShowsLevel(int index) => index >= 0 && index < _characters.Count && _characters[index].Level.gameObject.activeSelf;

        /// <summary>キャラクター index の弾道の段階の文字（テスト用）。</summary>
        public string LevelText(int index) => index >= 0 && index < _characters.Count ? _characters[index].Level.text : string.Empty;

        /// <summary>キャラクター index の弾道の予測線を出しているか（テスト用）。</summary>
        public bool ShowsTrajectory(int index) => index >= 0 && index < _characters.Count && _characters[index].Trajectory.enabled;

        /// <summary>キャラクター index のメーデーの場所のカーソルを出しているか（テスト用）。</summary>
        public bool ShowsCursor(int index) => index >= 0 && index < _characters.Count && _characters[index].Cursor.enabled;

        /// <summary>キャラクター index のジェット機を出しているか（テスト用）。</summary>
        public bool ShowsJet(int index) => index >= 0 && index < _characters.Count && _characters[index].Jet.activeSelf;

        /// <summary>N/A を出した回数（テスト用）。</summary>
        public int Denials { get; private set; }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            _local = _source as ILocalAimSource;
            GameObject[] templates = { _shellTemplate, _flakTemplate, _blastTemplate, _mineTemplate, _jetTemplate };
            if (_feed == null || _lineTemplate == null || _textTemplate == null || System.Array.IndexOf(templates, null) >= 0)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、見本が設定されていません", this);
                enabled = false;
                return;
            }
            _lineTemplate.gameObject.SetActive(false);
            _textTemplate.gameObject.SetActive(false);
            foreach (GameObject template in templates)
            {
                template.SetActive(false);
            }
            _block = new MaterialPropertyBlock();
            _japanese = JapaneseFont.Create(64);
        }

        private void OnDestroy()
        {
            if (_japanese != null)
            {
                Destroy(_japanese);
            }
        }

        private void LateUpdate()
        {
            RenderObjects();
            RenderCharacters();
            _primed = true;
        }

        // ---------------------------------------------------------------- 砲弾・爆発・地雷

        private void RenderObjects()
        {
            WorldObjects objects = _feed.Objects;
            _present.Clear();
            if (objects != null)
            {
                int tick = _feed.CurrentTick;
                foreach (WorldObject item in objects.Items)
                {
                    if ((item.Kind != WorldObjectKind.Shell && item.Kind != WorldObjectKind.Mine) || item.Ended)
                    {
                        continue;
                    }
                    _present.Add(item.Id);
                    bool blast = JuggernautRules.IsBlast(item);
                    if (_shown.TryGetValue(item.Id, out Shown shown) && (blast != IsBlast(shown)))
                    {
                        // 爆発した（見た目を爆発に替える）。
                        Destroy(shown.Visual);
                        _shown.Remove(item.Id);
                        shown = null;
                    }
                    if (shown == null)
                    {
                        shown = Create(item);
                        _shown.Add(item.Id, shown);
                        PlaySpawn(item, blast);
                    }
                    else if (shown.Kind == WorldObjectKind.Mine && shown.Variant == JuggernautRules.MineFlying && item.Variant != JuggernautRules.MineFlying)
                    {
                        Play(SoundId.MineSet, 0.8f);
                    }
                    shown.Variant = item.Variant;
                    Place(shown, item, tick);
                }
            }
            _gone.Clear();
            foreach (KeyValuePair<int, Shown> pair in _shown)
            {
                if (!_present.Contains(pair.Key))
                {
                    _gone.Add(pair.Key);
                }
            }
            foreach (int id in _gone)
            {
                Destroy(_shown[id].Visual);
                _shown.Remove(id);
            }
        }

        private static bool IsBlast(Shown shown)
        {
            return (shown.Kind == WorldObjectKind.Shell && shown.Variant == JuggernautRules.ShellBlast)
                || (shown.Kind == WorldObjectKind.Mine && shown.Variant == JuggernautRules.MineBlast);
        }

        private Shown Create(in WorldObject item)
        {
            GameObject template = JuggernautRules.IsBlast(item) ? _blastTemplate
                : item.Kind == WorldObjectKind.Mine ? _mineTemplate
                : item.Variant == JuggernautRules.FlakShell ? _flakTemplate
                : _shellTemplate;
            GameObject visual = Instantiate(template, transform);
            visual.name = $"{item.Kind}#{item.Id}";
            visual.SetActive(true);
            Transform lamp = visual.transform.Find("Lamp");
            return new Shown
            {
                Kind = item.Kind,
                Variant = item.Variant,
                Visual = visual,
                Lamp = lamp != null ? lamp.GetComponent<Renderer>() : null,
                Renderers = visual.GetComponentsInChildren<Renderer>(),
            };
        }

        private void Place(Shown shown, in WorldObject item, int tick)
        {
            Transform visual = shown.Visual.transform;
            visual.position = new Vector3(item.Position.X, item.Position.Y, Z);
            if (JuggernautRules.IsBlast(item))
            {
                // 広がって薄れる（半径は Core の爆発の半径）。
                float life = Mathf.Max(1f, item.ExpireTick - item.SpawnTick);
                float t = Mathf.Clamp01((tick - item.SpawnTick) / life);
                float size = item.End.X * 2f * (0.45f + 0.55f * (1f - (1f - t) * (1f - t)));
                visual.localScale = Vector3.one * size;
                Color color = BlastColor;
                color.a *= 1f - t;
                _block.SetColor(BaseColorId, color);
                foreach (Renderer renderer in shown.Renderers)
                {
                    renderer.SetPropertyBlock(_block);
                }
                return;
            }
            if (item.Kind == WorldObjectKind.Mine)
            {
                // 張り付いた面に沿って向く（地形に張り付いた地雷の Velocity は面の法線）。
                var normal = item.Variant == JuggernautRules.MineOnTerrain ? new Vector3(item.Velocity.X, item.Velocity.Y, 0f) : Vector3.up;
                visual.rotation = normal.sqrMagnitude > 0.01f ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
                if (shown.Lamp != null)
                {
                    shown.Lamp.enabled = item.Variant == JuggernautRules.MineFlying || Mathf.Repeat(Time.time * 3f, 1f) < 0.5f;
                }
                return;
            }
            if (item.Velocity.LengthSquared() > 0.01f)
            {
                visual.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(item.Velocity.Y, item.Velocity.X) * Mathf.Rad2Deg);
            }
        }

        private void PlaySpawn(in WorldObject item, bool blast)
        {
            if (!_primed)
            {
                return;
            }
            if (blast)
            {
                Play(SoundId.MissileExplode, 0.9f);
            }
            else if (item.Kind == WorldObjectKind.Mine)
            {
                Play(SoundId.MineSet, 0.6f);
            }
            else
            {
                Play(SoundId.Cannon, item.Variant == JuggernautRules.FlakShell ? 0.9f : 0.7f);
            }
        }

        private int Count(WorldObjectKind kind, bool blast)
        {
            int count = 0;
            foreach (Shown shown in _shown.Values)
            {
                count += shown.Kind == kind && IsBlast(shown) == blast ? 1 : 0;
            }
            return count;
        }

        // ---------------------------------------------------------------- 構え・メーデー

        private void RenderCharacters()
        {
            int count = _feed.CharacterCount;
            while (_characters.Count < count)
            {
                _characters.Add(CreateCharacter(_characters.Count));
            }
            int local = _feed.LocalCharacter;
            Camera view = _camera != null ? _camera : Camera.main;
            _scale = view != null ? Mathf.Max(1f, Mathf.Abs(view.transform.position.z) / BaseCameraDistance) : 1f;
            bool overview = false;
            Vector3 overviewCenter = Vector3.zero;
            float overviewDistance = 0f;
            for (int i = 0; i < _characters.Count; i++)
            {
                Character c = _characters[i];
                if (i >= count)
                {
                    HideAll(c);
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                bool controlled = local < 0 ? !(_feed is ICpuRoster cpus && cpus.IsCpu(i)) : i == local;
                RenderStance(c, i, state, config, controlled);
                RenderMayday(c, i, state, config, controlled);
                if (controlled && local >= 0 && (state.Combat.FlakStance || state.Combat.MaydayPhase == JuggernautRules.MaydaySelect))
                {
                    overview = TryOverview(config, out overviewCenter, out overviewDistance);
                }
            }
            if (_zoom != null)
            {
                _zoom.SetOverview(overview, overviewCenter, overviewDistance);
            }
        }

        // フラックの構え: 斜め後ろの上に弾道の段階、操作している人には弾道の予測線と落ちる所の輪。
        private void RenderStance(Character c, int index, in CharacterState state, CharacterConfig config, bool controlled)
        {
            bool stance = state.Combat.FlakStance && !state.Combat.IsWaitingToRespawn;
            c.Level.gameObject.SetActive(stance);
            JuggernautTuning tuning = config.Skills.Juggernaut;
            if (stance)
            {
                int level = JuggernautRules.FlakLevel(state, tuning);
                c.Level.text = level < LevelNames.Length ? LevelNames[level] : (level + 1).ToString();
                c.Level.color = LevelColors[Mathf.Clamp(level, 0, LevelColors.Length - 1)];
                float back = -(state.Facing >= 0 ? 1f : -1f) * (config.Movement.HalfWidth + 0.55f);
                c.Level.transform.position = new Vector3(state.Position.X + back * _scale, state.Position.Y + config.Movement.HalfHeight + 0.6f * _scale, Z - 0.2f);
                c.Level.transform.localScale = Vector3.one * _scale;
            }
            bool predict = stance && controlled && _local != null && _local.Collision != null && _local.TryPeekAim(index, out System.Numerics.Vector2 aim)
                && Predict(c, state, config, aim);
            c.Trajectory.enabled = predict;
            if (!predict)
            {
                c.Landing.enabled = false;
            }
        }

        // 今の照準で撃ったときの弾道（撃つときと同じ計算）を線にし、地形に落ちる所に爆発の輪を出す。
        private bool Predict(Character c, in CharacterState state, CharacterConfig config, System.Numerics.Vector2 aim)
        {
            JuggernautTuning tuning = config.Skills.Juggernaut;
            JuggernautRules.FlakLaunch(state, config, aim, out System.Numerics.Vector2 origin, out System.Numerics.Vector2 lead,
                out System.Numerics.Vector2 velocity);
            int count = JuggernautRules.PredictFlak(_local.Collision, origin, lead, velocity, tuning.FlakGravity, _path, out bool hit);
            if (count < 2)
            {
                return false;
            }
            int shown = (count + 1) / 2 + 1;
            c.Trajectory.positionCount = shown;
            c.Trajectory.widthMultiplier = TrajectoryWidth * _scale;
            c.Landing.widthMultiplier = RingWidth * _scale;
            for (int k = 0; k < shown - 1; k++)
            {
                System.Numerics.Vector2 p = _path[Mathf.Min(k * 2, count - 1)];
                c.Trajectory.SetPosition(k, new Vector3(p.X, p.Y, Z));
            }
            System.Numerics.Vector2 end = _path[count - 1];
            c.Trajectory.SetPosition(shown - 1, new Vector3(end.X, end.Y, Z));
            c.Landing.enabled = hit;
            if (hit)
            {
                DrawCircle(c.Landing, new Vector3(end.X, end.Y, Z), tuning.FlakExplosionRadius);
            }
            return true;
        }

        // メーデー: ジェット機、操作している人の場所のカーソルと N/A、運ばれる間の降りる場所の輪。
        private void RenderMayday(Character c, int index, in CharacterState state, CharacterConfig config, bool controlled)
        {
            JuggernautTuning tuning = config.Skills.Juggernaut;
            byte phase = state.Combat.MaydayPhase;
            bool calling = !state.Combat.IsAway && state.Action.Kind == ActionKind.Attack && state.Action.Attack == AttackSlot.Super
                && config.Combat.Super.Effect == SkillEffect.Mayday;
            if (calling && state.Combat.AttackSerial != c.Serial && _primed)
            {
                Play(SoundId.Jet, 0.8f);
            }
            if (calling)
            {
                c.Serial = state.Combat.AttackSerial;
            }
            if (phase == JuggernautRules.MaydayCarry && c.Phase != JuggernautRules.MaydayCarry && _primed)
            {
                Play(SoundId.Jet, 0.8f);
            }

            var body = new Vector3(state.Position.X, state.Position.Y + CharacterView.AwayOffset(state, tuning), Z);
            float top = config.Movement.HalfHeight + JetAbove;
            int facing = state.Facing >= 0 ? 1 : -1;
            bool jet = true;
            Vector3 jetAt;
            if (calling)
            {
                // 降りてくる（向いている側の後ろの上から）。
                float t = Mathf.Clamp01(state.Action.Ticks / Mathf.Max(1f, config.Combat.Super.EffectTick));
                float rest = (1f - t) * (1f - t);
                jetAt = body + new Vector3(-facing * 14f * rest, top + JetArriveHeight * rest, 0f);
            }
            else if (phase == JuggernautRules.MaydayLift || phase == JuggernautRules.MaydayCarry)
            {
                jetAt = body + new Vector3(0f, top, 0f);
            }
            else if (c.Phase == JuggernautRules.MaydayCarry && phase == 0)
            {
                // 降ろして去る。
                c.LeaveAt = Time.time;
                c.LeaveFrom = body + new Vector3(0f, top, 0f);
                jetAt = c.LeaveFrom;
            }
            else if (c.LeaveAt >= 0f && Time.time - c.LeaveAt < JetLeaveSeconds)
            {
                float t = (Time.time - c.LeaveAt) / JetLeaveSeconds;
                jetAt = c.LeaveFrom + new Vector3(facing * 16f * t * t, JetArriveHeight * t * t, 0f);
            }
            else
            {
                jet = false;
                jetAt = Vector3.zero;
                c.LeaveAt = -1f;
            }
            c.Phase = phase;
            c.Jet.SetActive(jet);
            if (jet)
            {
                c.Jet.transform.position = jetAt;
                c.Jet.transform.localScale = new Vector3(facing, 1f, 1f);
            }

            // 運ばれる間の、降りる場所の輪。
            c.Drop.enabled = phase == JuggernautRules.MaydayCarry;
            if (c.Drop.enabled)
            {
                c.Drop.widthMultiplier = TrajectoryWidth * _scale;
                DrawCircle(c.Drop, new Vector3(state.Combat.MaydayTarget.X, state.Combat.MaydayTarget.Y, Z),
                    Mathf.Max(config.Movement.HalfWidth, config.Movement.HalfHeight) + 0.4f);
            }

            // 操作している人の、場所のカーソル。
            bool selecting = controlled && phase == JuggernautRules.MaydaySelect && _local != null
                && _local.TryGetTargetCursor(index, out System.Numerics.Vector2 cursor) && RenderCursor(c, state, config, cursor);
            c.Cursor.enabled = selecting;
            c.CursorCross.enabled = selecting;
            c.CursorTime.enabled = selecting;

            // 選べない場所を選んだ: N/A とエラーの音（操作している人だけ）。
            int rejects = state.Combat.MaydayRejectCount;
            if (c.Rejects >= 0 && rejects > c.Rejects && controlled && _primed)
            {
                c.NoticeUntil = Time.time + NoticeSeconds;
                Denials++;
                Play(SoundId.Denied, 0.9f);
                c.Notice.transform.position = c.Cursor.enabled ? c.Cursor.GetPosition(0) + new Vector3(-CursorRadius, CursorRadius * 1.4f, 0f) * _scale
                    : body + new Vector3(0f, top, 0f);
            }
            c.Rejects = rejects;
            bool notice = Time.time < c.NoticeUntil;
            c.Notice.gameObject.SetActive(notice);
            if (notice)
            {
                float t = 1f - (c.NoticeUntil - Time.time) / NoticeSeconds;
                c.Notice.transform.localScale = Vector3.one * _scale * (1f + 0.4f * (1f - t) * (1f - t));
            }
        }

        private bool RenderCursor(Character c, in CharacterState state, CharacterConfig config, System.Numerics.Vector2 cursor)
        {
            var center = new Vector3(cursor.X, cursor.Y, Z - 0.3f);
            var half = new System.Numerics.Vector2(config.Movement.HalfWidth, config.Movement.HalfHeight);
            bool free = _local.Collision == null || JuggernautRules.IsFreeTarget(_local.Collision, cursor, half);
            Color color = free ? FreeColor : BlockedColor;
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 9f);
            float radius = CursorRadius * _scale;
            c.Cursor.widthMultiplier = CursorWidth * _scale;
            c.CursorCross.widthMultiplier = CursorWidth * 0.6f * _scale;
            c.CursorTime.widthMultiplier = CursorWidth * 0.4f * _scale;
            SetColor(c.Cursor, color);
            DrawCircle(c.Cursor, center, radius * pulse);
            SetColor(c.CursorCross, color);
            c.CursorCross.positionCount = 5;
            float arm = radius * 1.5f;
            c.CursorCross.SetPosition(0, center + new Vector3(-arm, 0f, 0f));
            c.CursorCross.SetPosition(1, center + new Vector3(arm, 0f, 0f));
            c.CursorCross.SetPosition(2, center);
            c.CursorCross.SetPosition(3, center + new Vector3(0f, arm, 0f));
            c.CursorCross.SetPosition(4, center + new Vector3(0f, -arm, 0f));
            // 残りの時間（輪が減っていく）。
            float left = 1f - JuggernautRules.MaydayProgress(state, config.Skills.Juggernaut);
            SetColor(c.CursorTime, new Color(1f, 1f, 1f, 0.85f));
            DrawArc(c.CursorTime, center, radius * 1.3f, left);
            return true;
        }

        // ステージ全体（メーデーで選べる範囲）が入るカメラの中心と距離。
        private bool TryOverview(CharacterConfig config, out Vector3 center, out float distance)
        {
            StageBounds? bounds = _local != null ? _local.Bounds : null;
            JuggernautRules.TargetArea(bounds, config.Skills.Juggernaut, out System.Numerics.Vector2 c, out System.Numerics.Vector2 half);
            center = new Vector3(c.X, c.Y, 0f);
            Camera camera = _camera != null ? _camera : Camera.main;
            float fov = camera != null ? camera.fieldOfView : 40f;
            float aspect = camera != null ? camera.aspect : 16f / 9f;
            distance = SharedCamera.RequiredDistance(new Vector2(half.X + OverviewMargin, half.Y + OverviewMargin), fov, aspect);
            return true;
        }

        private Character CreateCharacter(int index)
        {
            var c = new Character
            {
                Level = CreateText($"FlakLevelP{index + 1}", LevelHeight),
                Notice = CreateText($"MaydayDeniedP{index + 1}", NoticeHeight),
                Trajectory = CreateLine($"FlakTrajectoryP{index + 1}", TrajectoryWidth, TrajectoryColor),
                Landing = CreateLine($"FlakLandingP{index + 1}", RingWidth, TrajectoryColor),
                Cursor = CreateLine($"MaydayCursorP{index + 1}", CursorWidth, FreeColor),
                CursorCross = CreateLine($"MaydayCrossP{index + 1}", CursorWidth * 0.6f, FreeColor),
                CursorTime = CreateLine($"MaydayTimeP{index + 1}", CursorWidth * 0.4f, Color.white),
                Drop = CreateLine($"MaydayDropP{index + 1}", 0.12f, new Color(1f, 1f, 1f, 0.7f)),
                Jet = Instantiate(_jetTemplate, transform),
            };
            c.Notice.text = "N/A";
            c.Notice.color = BlockedColor;
            c.Jet.name = $"MaydayJetP{index + 1}";
            HideAll(c);
            return c;
        }

        private static void HideAll(Character c)
        {
            c.Level.gameObject.SetActive(false);
            c.Notice.gameObject.SetActive(false);
            c.Trajectory.enabled = false;
            c.Landing.enabled = false;
            c.Cursor.enabled = false;
            c.CursorCross.enabled = false;
            c.CursorTime.enabled = false;
            c.Drop.enabled = false;
            c.Jet.SetActive(false);
        }

        private TextMesh CreateText(string name, float height)
        {
            TextMesh text = Instantiate(_textTemplate, transform);
            text.name = name;
            text.gameObject.SetActive(true);
            // TextMesh の 1 行の高さは、およそ characterSize × fontSize / 10（m）。
            text.characterSize = height * 10f / Mathf.Max(1, text.fontSize);
            if (_japanese != null)
            {
                text.font = _japanese;
                text.GetComponent<MeshRenderer>().sharedMaterial = _japanese.material;
            }
            return text;
        }

        private LineRenderer CreateLine(string name, float width, Color color)
        {
            LineRenderer line = Instantiate(_lineTemplate, transform);
            line.name = name;
            line.gameObject.SetActive(true);
            line.widthMultiplier = width;
            line.useWorldSpace = true;
            SetColor(line, color);
            return line;
        }

        private static void SetColor(LineRenderer line, Color color)
        {
            line.startColor = color;
            line.endColor = color;
        }

        private static void DrawCircle(LineRenderer line, Vector3 center, float radius)
        {
            line.loop = true;
            line.positionCount = RingSegments;
            for (int k = 0; k < RingSegments; k++)
            {
                float angle = k * Mathf.PI * 2f / RingSegments;
                line.SetPosition(k, center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
        }

        // 真上から時計回りに、割合 fraction の弧。
        private static void DrawArc(LineRenderer line, Vector3 center, float radius, float fraction)
        {
            line.loop = false;
            int points = Mathf.Max(2, Mathf.CeilToInt(RingSegments * Mathf.Clamp01(fraction)) + 1);
            line.positionCount = points;
            for (int k = 0; k < points; k++)
            {
                float angle = Mathf.PI * 0.5f - Mathf.PI * 2f * Mathf.Clamp01(fraction) * k / (points - 1);
                line.SetPosition(k, center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
        }

        private void Play(SoundId id, float volume)
        {
            if (_audio != null)
            {
                _audio.Play(id, volume);
            }
        }
    }
}
