using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// JUGGERNAUT の技の表示（docs/14 1.7。ユーザーの案、2026-10-29）。状態を読むだけで、書き換えない。
    /// - 砲弾: 空中横は暗い鉄の球、フラックは大きい苔色の球と短い尾。爆発（Core の爆発の印）は、閃光・炎・煙を重ねた爆発
    ///   （LayeredExplosionView。ユーザーの要望、2026-10-29）。煙は Core の印が消えた後も残る。
    /// - 地雷: 暗い円盤と、赤く点滅する灯。張り付いたら、張り付いた面に沿って向く。
    /// - フラックの構え: 体の斜め後ろの上に、弾道の段階（弱・中・強・弩）。操作している人には、今の照準で撃ったときの弾道の予測線と、
    ///   落ちる所の爆発の範囲（JuggernautRules.PredictFlak。撃つときと同じ計算）。予測線は、照準の方向に出す技の矢印（SkillAimView）と
    ///   同じ見た目（水色の半透明の帯・芯・矢じり。ユーザーの要望、2026-10-29）で、着弾点まで曲がって伸びる。
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
        // 地雷はキャラクターの体より手前に出す（足もとや人に張り付いた地雷が、戦車や体の中に隠れないように。戦車の手前の履帯は約 -0.76）。
        private const float MineZ = -1f;
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
        private const float DropWidth = 0.12f;

        private static readonly string[] LevelNames = { "弱", "中", "強", "弩" };
        private static readonly Color[] LevelColors =
        {
            new Color(0.55f, 0.85f, 1f), new Color(0.55f, 1f, 0.5f), new Color(1f, 0.7f, 0.25f), new Color(1f, 0.3f, 0.25f),
        };
        private static readonly Color FreeColor = new Color(0.3f, 1f, 0.45f, 0.95f);
        private static readonly Color BlockedColor = new Color(1f, 0.25f, 0.2f, 0.95f);

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
        [Tooltip("爆発の見た目（閃光・炎・煙を重ねる）")]
        [SerializeField] private LayeredExplosionView _explosions;
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
        private readonly List<Vector3> _points = new List<Vector3>();
        private ISimulationFeed _feed;
        private ILocalAimSource _local;
        private bool _primed;
        private float _scale = 1f;

        private sealed class Shown
        {
            public WorldObjectKind Kind;
            public byte Variant;
            public GameObject Visual;
            public Renderer Lamp;
            // 爆発の印が消えるティック（それより前に消えたら、予測が外れた爆発なので、爆発の見た目も消す）。
            public int ExpireTick;
        }

        private sealed class Character
        {
            public TextMesh Level;
            public LineRenderer TrajectoryBand;
            public LineRenderer TrajectoryShaft;
            public LineRenderer TrajectoryHead;
            public LineRenderer TrajectoryEdges;
            public LineRenderer LandingFill;
            public LineRenderer LandingRing;
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

        /// <summary>出している爆発の数（煙が残っている間を含む。テスト用）。</summary>
        public int BlastCount => _explosions != null ? _explosions.ActiveCount : 0;

        /// <summary>出している地雷の数（爆発の印を除く。テスト用）。</summary>
        public int MineCount => Count(WorldObjectKind.Mine, false);

        /// <summary>キャラクター index の弾道の段階を出しているか（テスト用）。</summary>
        public bool ShowsLevel(int index) => index >= 0 && index < _characters.Count && _characters[index].Level.gameObject.activeSelf;

        /// <summary>キャラクター index の弾道の段階の文字（テスト用）。</summary>
        public string LevelText(int index) => index >= 0 && index < _characters.Count ? _characters[index].Level.text : string.Empty;

        /// <summary>キャラクター index の弾道の予測線を出しているか（テスト用）。</summary>
        public bool ShowsTrajectory(int index) => index >= 0 && index < _characters.Count && _characters[index].TrajectoryBand.enabled;

        /// <summary>キャラクター index の着弾点の爆発の範囲を出しているか（テスト用）。</summary>
        public bool ShowsLanding(int index) => index >= 0 && index < _characters.Count && _characters[index].LandingRing.enabled;

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
            GameObject[] templates = { _shellTemplate, _flakTemplate, _mineTemplate, _jetTemplate };
            if (_feed == null || _lineTemplate == null || _textTemplate == null || _explosions == null || System.Array.IndexOf(templates, null) >= 0)
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
                        // 爆発した（砲弾・地雷の見た目を消して、爆発を出す）。
                        DestroyVisual(shown);
                        _shown.Remove(item.Id);
                        shown = null;
                    }
                    if (shown == null)
                    {
                        shown = Create(item);
                        _shown.Add(item.Id, shown);
                        PlaySpawn(item, blast);
                        if (blast)
                        {
                            // 半径は Core の爆発の半径（End.X）。煙は印が消えた後も残る。
                            _explosions.Spawn(item.Id, new Vector3(item.Position.X, item.Position.Y, Z), item.End.X);
                        }
                    }
                    if (blast)
                    {
                        shown.Variant = item.Variant;
                        continue;
                    }
                    else if (shown.Kind == WorldObjectKind.Mine && shown.Variant == JuggernautRules.MineFlying && item.Variant != JuggernautRules.MineFlying)
                    {
                        Play(SoundId.MineSet, 0.8f);
                    }
                    shown.Variant = item.Variant;
                    Place(shown, item);
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
            int now = _feed.CurrentTick;
            foreach (int id in _gone)
            {
                Shown shown = _shown[id];
                if (IsBlast(shown) && now < shown.ExpireTick - 1)
                {
                    // 印が時間より前に消えた: 予測が外れて巻き戻った爆発なので、煙も残さない。
                    _explosions.Cancel(id);
                }
                DestroyVisual(shown);
                _shown.Remove(id);
            }
        }

        private static void DestroyVisual(Shown shown)
        {
            if (shown.Visual != null)
            {
                Destroy(shown.Visual);
            }
        }

        private static bool IsBlast(Shown shown)
        {
            return (shown.Kind == WorldObjectKind.Shell && shown.Variant == JuggernautRules.ShellBlast)
                || (shown.Kind == WorldObjectKind.Mine && shown.Variant == JuggernautRules.MineBlast);
        }

        private Shown Create(in WorldObject item)
        {
            if (JuggernautRules.IsBlast(item))
            {
                // 爆発は LayeredExplosionView が描く（ここでは印の消えるティックだけ覚える）。
                return new Shown { Kind = item.Kind, Variant = item.Variant, ExpireTick = item.ExpireTick };
            }
            GameObject template = item.Kind == WorldObjectKind.Mine ? _mineTemplate
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
            };
        }

        private void Place(Shown shown, in WorldObject item)
        {
            Transform visual = shown.Visual.transform;
            visual.position = new Vector3(item.Position.X, item.Position.Y, Z);
            if (item.Kind == WorldObjectKind.Mine)
            {
                visual.position = new Vector3(item.Position.X, item.Position.Y, MineZ);
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
            if (!predict)
            {
                SetTrajectoryShown(c, false, false);
            }
        }

        // 今の照準で撃ったときの弾道（撃つときと同じ計算）を、照準の方向の矢印（SkillAimView）と同じ見た目で描く: 水色の半透明の帯と芯が
        // 弾道に沿って曲がり、着弾点に矢じり。地形に落ちる所には、爆発の範囲（薄い塗りと縁）。点はカメラの前へ寄せて、地形に隠れないようにする。
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
            _points.Clear();
            for (int k = 0; k < count; k += 2)
            {
                _points.Add(new Vector3(_path[k].X, _path[k].Y, 0f));
            }
            var tip = new Vector3(_path[count - 1].X, _path[count - 1].Y, 0f);
            if (_points[_points.Count - 1] != tip)
            {
                _points.Add(tip);
            }

            // 矢じりの根元: 先から道のりに沿って矢じりの長さだけ戻った所。
            float total = 0f;
            for (int k = 1; k < _points.Count; k++)
            {
                total += Vector3.Distance(_points[k - 1], _points[k]);
            }
            float headLength = Mathf.Min(SkillAimView.MaxHeadLength * _scale, total * 0.35f);
            float remain = headLength;
            int bodyEnd = _points.Count - 1;
            Vector3 headBase = tip;
            for (int k = _points.Count - 1; k > 0; k--)
            {
                float segment = Vector3.Distance(_points[k - 1], _points[k]);
                bodyEnd = k - 1;
                if (segment >= remain)
                {
                    headBase = Vector3.Lerp(_points[k], _points[k - 1], segment > 0f ? remain / segment : 0f);
                    break;
                }
                remain -= segment;
                headBase = _points[k - 1];
            }
            Vector3 direction = (tip - headBase).sqrMagnitude > 1e-6f ? (tip - headBase).normalized : Vector3.right;
            var side = new Vector3(-direction.y, direction.x, 0f);

            var toward = new SkillAimView.TowardCamera(_camera != null ? _camera : Camera.main);
            float width = _scale * toward.Scale;
            float pulse = SkillAimView.Pulse;
            int bodyCount = bodyEnd + 2;
            c.TrajectoryBand.positionCount = bodyCount;
            c.TrajectoryShaft.positionCount = bodyCount;
            for (int k = 0; k <= bodyEnd; k++)
            {
                Vector3 p = toward.Place(_points[k]);
                c.TrajectoryBand.SetPosition(k, p);
                c.TrajectoryShaft.SetPosition(k, p);
            }
            c.TrajectoryBand.SetPosition(bodyCount - 1, toward.Place(headBase));
            c.TrajectoryShaft.SetPosition(bodyCount - 1, toward.Place(headBase + direction * 0.05f * _scale));
            c.TrajectoryBand.widthMultiplier = width;
            c.TrajectoryShaft.widthMultiplier = width;
            SkillAimView.Paint(c.TrajectoryBand, SkillAimView.BandStartAlpha * pulse, SkillAimView.BandEndAlpha * pulse);
            SkillAimView.Paint(c.TrajectoryShaft, SkillAimView.ShaftStartAlpha * pulse, SkillAimView.ShaftEndAlpha * pulse);

            c.TrajectoryHead.positionCount = 2;
            c.TrajectoryHead.SetPosition(0, toward.Place(headBase));
            c.TrajectoryHead.SetPosition(1, toward.Place(tip));
            c.TrajectoryHead.widthMultiplier = width;
            SkillAimView.Paint(c.TrajectoryHead, SkillAimView.HeadStartAlpha * pulse, SkillAimView.HeadEndAlpha * pulse);
            c.TrajectoryEdges.positionCount = 3;
            float wing = SkillAimView.HeadHalfWidth * _scale;
            c.TrajectoryEdges.SetPosition(0, toward.Place(headBase + side * wing - direction * 0.25f * _scale));
            c.TrajectoryEdges.SetPosition(1, toward.Place(tip));
            c.TrajectoryEdges.SetPosition(2, toward.Place(headBase - side * wing - direction * 0.25f * _scale));
            c.TrajectoryEdges.widthMultiplier = width;
            SkillAimView.Paint(c.TrajectoryEdges, SkillAimView.EdgeAlpha * pulse, SkillAimView.EdgeAlpha * pulse);

            if (hit)
            {
                // 爆発の範囲: 半径の半分の輪を半径の太さで描いて塗り、外側に縁。
                float radius = tuning.FlakExplosionRadius;
                DrawCircle(c.LandingFill, tip, radius * 0.5f, toward);
                c.LandingFill.widthMultiplier = radius * toward.Scale;
                SkillAimView.Paint(c.LandingFill, SkillAimView.BandStartAlpha * pulse, SkillAimView.BandStartAlpha * pulse);
                DrawCircle(c.LandingRing, tip, radius, toward);
                c.LandingRing.widthMultiplier = SkillAimView.EdgeWidth * width;
                SkillAimView.Paint(c.LandingRing, SkillAimView.EdgeAlpha * pulse, SkillAimView.EdgeAlpha * pulse);
            }
            SetTrajectoryShown(c, true, hit);
            return true;
        }

        private static void SetTrajectoryShown(Character c, bool shown, bool landing)
        {
            c.TrajectoryBand.enabled = shown;
            c.TrajectoryShaft.enabled = shown;
            c.TrajectoryHead.enabled = shown;
            c.TrajectoryEdges.enabled = shown;
            c.LandingFill.enabled = shown && landing;
            c.LandingRing.enabled = shown && landing;
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
                c.Drop.widthMultiplier = DropWidth * _scale;
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
                // 弾道の予測線（照準の方向の矢印と同じ見た目。帯は一定の太さ、芯は先ほど太く、矢じりは塗った三角形、縁は細い線）。
                TrajectoryBand = CreateAimLine($"FlakTrajectoryP{index + 1}Band", AnimationCurve.Constant(0f, 1f, SkillAimView.BandWidth)),
                TrajectoryShaft = CreateAimLine($"FlakTrajectoryP{index + 1}Shaft",
                    new AnimationCurve(new Keyframe(0f, SkillAimView.ShaftStartWidth), new Keyframe(1f, SkillAimView.ShaftEndWidth))),
                TrajectoryHead = CreateAimLine($"FlakTrajectoryP{index + 1}Head",
                    new AnimationCurve(new Keyframe(0f, SkillAimView.HeadHalfWidth * 2f), new Keyframe(1f, 0f))),
                TrajectoryEdges = CreateAimLine($"FlakTrajectoryP{index + 1}Edges", AnimationCurve.Constant(0f, 1f, SkillAimView.EdgeWidth)),
                LandingFill = CreateAimLine($"FlakLandingP{index + 1}Fill", AnimationCurve.Constant(0f, 1f, 1f)),
                LandingRing = CreateAimLine($"FlakLandingP{index + 1}Ring", AnimationCurve.Constant(0f, 1f, 1f)),
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
            SetTrajectoryShown(c, false, false);
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

        private LineRenderer CreateAimLine(string name, AnimationCurve width)
        {
            LineRenderer line = CreateLine(name, 1f, SkillAimView.Cyan);
            line.numCapVertices = 0;
            line.numCornerVertices = 2;
            line.widthCurve = width;
            line.enabled = false;
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

        // カメラの前へ寄せた円（toward。点はキャラクターの面で決める）。
        private static void DrawCircle(LineRenderer line, Vector3 center, float radius, SkillAimView.TowardCamera toward)
        {
            line.loop = true;
            line.positionCount = RingSegments;
            for (int k = 0; k < RingSegments; k++)
            {
                float angle = k * Mathf.PI * 2f / RingSegments;
                line.SetPosition(k, toward.Place(center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius));
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
