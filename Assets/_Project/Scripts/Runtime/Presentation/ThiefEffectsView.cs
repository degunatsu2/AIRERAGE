using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// THIEF の技の表示（docs/14 1.6。ユーザーの案、2026-10-28）。状態を読むだけで、書き換えない。
    /// - 煙球（地上技）: 落ちていく暗い灰色の玉。
    /// - 煙（煙球の着弾、分身が消えたとき）: 不透明な灰色の雲。中の物を見えなくするので、カメラの前（_smokeDepth）に、z = 0 の面の円と
    ///   画面で重なる位置・大きさで出す。広がってから、消える前の 1 秒で縮む。出た瞬間に音。
    /// - タレントスナッチの弾（Q）: こげ茶の小さな弾と短い尾。撃った瞬間に音。
    /// - 置いたギミック（Q で奪ったもの）: 範囲の枠と、向きへ流れる印（種類ごとの色。効いていない間は薄く、予告の間は点滅）。
    /// - 置いた仕掛け: 発射装置の箱。赤外線は半透明の赤い板（触れられたら予告の点滅と警報、撃ったらミサイル、待ちの間は消える）。
    ///   ピッチャーマシンはボールを道のりの位置に出し、止まったら点滅させる（打ち出した瞬間に音）。
    /// - 透明（E）: 足元に薄い影だけを出す（体と周りの表示は CharacterView が隠す。分身は CharacterView.RenderClone）。
    /// - 技・ギミックを奪った瞬間に、こげ茶の光と「STEAL!」。バンクハイスト（R）でコインを得た瞬間に、金色の光と「HEIST! +枚数」。
    /// </summary>
    public sealed class ThiefEffectsView : MonoBehaviour
    {
        private const float Z = -0.5f;
        private const int SmokePuffs = 9;
        private const float SmokeGrowSeconds = 0.25f;
        private const float SmokeFadeSeconds = 1f;
        private const float ShotTrailLength = 1.6f;
        private const float BlinkPerSecond = 6f;
        private const float ShadowAlpha = 0.28f;

        private static readonly Color ShotColor = new Color(0.36f, 0.2f, 0.1f);
        private static readonly Color StealColor = new Color(0.62f, 0.4f, 0.2f);
        private static readonly Color BeamColor = new Color(1f, 0.15f, 0.15f, 0.3f);

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("煙を z = 0 の面に重ねるのに使うカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("線の見本（隠しておく）")]
        [SerializeField] private LineRenderer _lineTemplate;
        [Tooltip("煙のひとかたまりの見本（直径 1 の球。隠しておく）")]
        [SerializeField] private GameObject _puffTemplate;
        [Tooltip("煙球の見本（隠しておく）")]
        [SerializeField] private GameObject _ballTemplate;
        [Tooltip("タレントスナッチの弾の見本（隠しておく）")]
        [SerializeField] private GameObject _shotTemplate;
        [Tooltip("置いた仕掛けの発射装置の見本（隠しておく）")]
        [SerializeField] private GameObject _launcherTemplate;
        [Tooltip("置いた赤外線の見本（直径 1 の半透明の板。隠しておく）")]
        [SerializeField] private GameObject _beamTemplate;
        [Tooltip("置いた赤外線のミサイルの見本（隠しておく）")]
        [SerializeField] private GameObject _missileTemplate;
        [Tooltip("置いたピッチャーマシンのボールの見本（直径 1。隠しておく）")]
        [SerializeField] private GameObject _trapBallTemplate;
        [Tooltip("透明の間の足元の影の見本（隠しておく）")]
        [SerializeField] private GameObject _shadowTemplate;
        [Tooltip("奪った瞬間の光の見本（隠しておく。キャラクターごとに複製する）")]
        [SerializeField] private BurstEffectView _stealTemplate;
        [Tooltip("バンクハイストでコインを得た瞬間の光の見本（隠しておく。キャラクターごとに複製する）")]
        [SerializeField] private BurstEffectView _heistTemplate;
        [Tooltip("音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("煙を出すカメラの前の深さ（z。地形の手前の面より前）")]
        [SerializeField] private float _smokeDepth = -4f;

        private readonly Dictionary<int, Shown> _shown = new Dictionary<int, Shown>();
        private readonly List<int> _gone = new List<int>();
        private readonly HashSet<int> _present = new HashSet<int>();
        private readonly List<Character> _characters = new List<Character>();
        private ISimulationFeed _feed;
        private bool _primed;

        private sealed class Shown
        {
            public WorldObjectKind Kind;
            public GameObject Visual;
            public float BornAt;
            public Transform[] Puffs;
            public Vector3[] PuffOffsets;
            public float[] PuffSizes;
            public LineRenderer Trail;
            public LineRenderer Frame;
            public LineRenderer Marks;
            public GameObject Beam;
            public GameObject Missile;
            public bool Arming;
            public bool Flying;
        }

        private sealed class Character
        {
            public GameObject Shadow;
            public BurstEffectView Steal;
            public BurstEffectView Heist;
            public int Steals = -1;
            public int Payouts = -1;
        }

        /// <summary>出している煙の数（テスト用）。</summary>
        public int SmokeCount => CountOf(WorldObjectKind.Smoke);

        /// <summary>出している置いたギミックと仕掛けの数（テスト用）。</summary>
        public int PlacedCount => CountOf(WorldObjectKind.PlacedZone) + CountOf(WorldObjectKind.PlacedTrap);

        /// <summary>出しているタレントスナッチの弾の数（テスト用）。</summary>
        public int ShotCount => CountOf(WorldObjectKind.SnatchShot);

        /// <summary>キャラクター index の足元に、透明の影を出しているか（テスト用）。</summary>
        public bool ShowsShadow(int index) => index >= 0 && index < _characters.Count && _characters[index].Shadow.activeSelf;

        /// <summary>キャラクター index が奪って STEAL! を出した回数と、バンクハイストで HEIST! を出した回数（テスト用）。</summary>
        public int StealBursts { get; private set; }
        public int HeistBursts { get; private set; }

        /// <summary>最後に出した HEIST! の文字（テスト用）。</summary>
        public string LastHeistLabel { get; private set; } = string.Empty;

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            GameObject[] templates = { _puffTemplate, _ballTemplate, _shotTemplate, _launcherTemplate, _beamTemplate, _missileTemplate, _trapBallTemplate, _shadowTemplate };
            if (_feed == null || _lineTemplate == null || System.Array.IndexOf(templates, null) >= 0 || _stealTemplate == null || _heistTemplate == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、見本が設定されていません", this);
                enabled = false;
                return;
            }
            _lineTemplate.gameObject.SetActive(false);
            foreach (GameObject template in templates)
            {
                template.SetActive(false);
            }
            _stealTemplate.gameObject.SetActive(false);
            _heistTemplate.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            RenderObjects();
            RenderCharacters();
            _primed = true;
        }

        // ---------------------------------------------------------------- 置かれた物と投射物

        private void RenderObjects()
        {
            WorldObjects objects = _feed.Objects;
            _present.Clear();
            if (objects != null)
            {
                foreach (WorldObject item in objects.Items)
                {
                    if (item.Kind <= WorldObjectsView.LastKind || item.Ended)
                    {
                        continue;
                    }
                    _present.Add(item.Id);
                    if (!_shown.TryGetValue(item.Id, out Shown shown) || shown.Kind != item.Kind)
                    {
                        if (shown != null)
                        {
                            Destroy(shown.Visual);
                        }
                        shown = Create(item);
                        _shown[item.Id] = shown;
                        PlaySpawn(item.Kind, item);
                    }
                    UpdateShown(shown, item);
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

        private Shown Create(in WorldObject item)
        {
            var shown = new Shown { Kind = item.Kind, BornAt = Time.time };
            var root = new GameObject($"Thief{item.Kind}{item.Id}");
            root.transform.SetParent(transform, false);
            shown.Visual = root;
            switch (item.Kind)
            {
                case WorldObjectKind.SmokeBall:
                    Child(_ballTemplate, root.transform);
                    break;
                case WorldObjectKind.Smoke:
                    CreatePuffs(shown, item.Id);
                    break;
                case WorldObjectKind.SnatchShot:
                    Child(_shotTemplate, root.transform);
                    shown.Trail = Line(root.transform, "Trail", ShotColor, 0.12f);
                    break;
                case WorldObjectKind.PlacedZone:
                    shown.Frame = Line(root.transform, "Frame", Color.white, 0.08f);
                    shown.Frame.loop = true;
                    shown.Marks = Line(root.transform, "Marks", Color.white, 0.1f);
                    break;
                case WorldObjectKind.PlacedTrap:
                    Child(_launcherTemplate, root.transform);
                    shown.Beam = Child(_beamTemplate, root.transform);
                    shown.Missile = Child(_missileTemplate, root.transform);
                    shown.Frame = Line(root.transform, "Aim", new Color(1f, 1f, 1f, 0.5f), 0.05f);
                    break;
                case WorldObjectKind.TrapBall:
                    Child(_trapBallTemplate, root.transform);
                    break;
            }
            return shown;
        }

        private void UpdateShown(Shown shown, in WorldObject item)
        {
            var position = new Vector3(item.Position.X, item.Position.Y, Z);
            switch (item.Kind)
            {
                case WorldObjectKind.SmokeBall:
                {
                    shown.Visual.transform.position = position;
                    float radius = SmokeBallRadius(item.Owner);
                    shown.Visual.transform.localScale = Vector3.one * radius * 2f;
                    break;
                }
                case WorldObjectKind.Smoke:
                    UpdateSmoke(shown, item);
                    break;
                case WorldObjectKind.SnatchShot:
                    UpdateShot(shown, item, position);
                    break;
                case WorldObjectKind.PlacedZone:
                    UpdatePlacedZone(shown, item);
                    break;
                case WorldObjectKind.PlacedTrap:
                    UpdatePlacedTrap(shown, item, position);
                    break;
                case WorldObjectKind.TrapBall:
                    UpdateTrapBall(shown, item, position);
                    break;
            }
        }

        private float SmokeBallRadius(int owner)
        {
            return owner >= 0 && owner < _feed.CharacterCount ? _feed.GetConfig(owner).Skills.Thief.SmokeBallRadius : 0.3f;
        }

        // 煙: 大きさの違う球を、中心の周りに重ねる（並べ方は物の番号で決める）。
        private void CreatePuffs(Shown shown, int id)
        {
            var random = new System.Random(id * 7919 + 17);
            shown.Puffs = new Transform[SmokePuffs];
            shown.PuffOffsets = new Vector3[SmokePuffs];
            shown.PuffSizes = new float[SmokePuffs];
            for (int i = 0; i < SmokePuffs; i++)
            {
                float angle = (i - 1) * Mathf.PI * 2f / (SmokePuffs - 1) + (float)random.NextDouble() * 0.5f;
                // 中心の大きな球と、周りの球で、半径の円をすき間なく覆う（周りの球の外の縁は半径の 1.1〜1.2 倍）。
                float distance = i == 0 ? 0f : 0.5f + (float)random.NextDouble() * 0.12f;
                shown.PuffOffsets[i] = new Vector3(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance, (float)random.NextDouble() * 0.2f);
                shown.PuffSizes[i] = i == 0 ? 1.45f : 1.0f + (float)random.NextDouble() * 0.15f;
                shown.Puffs[i] = Child(_puffTemplate, shown.Visual.transform).transform;
            }
        }

        // 煙の位置: z = 0 の面の円（中心 center、半径 radius）と画面で重なるように、カメラの前（_smokeDepth）へ寄せて縮める。
        private void UpdateSmoke(Shown shown, in WorldObject smoke)
        {
            var center = new Vector3(smoke.Position.X, smoke.Position.Y, 0f);
            float radius = smoke.End.X;
            float scale = 1f;
            Vector3 placed = new Vector3(center.x, center.y, _smokeDepth);
            if (_camera != null && !_camera.orthographic && _camera.transform.position.z < _smokeDepth)
            {
                Vector3 eye = _camera.transform.position;
                scale = (_smokeDepth - eye.z) / (0f - eye.z);
                placed = eye + (center - eye) * scale;
            }
            float age = Time.time - shown.BornAt;
            float left = (smoke.ExpireTick - _feed.CurrentTick) * SimTime.DeltaTime;
            float grow = Mathf.Clamp01(age / SmokeGrowSeconds);
            float fade = Mathf.Clamp01(left / SmokeFadeSeconds);
            float size = radius * scale * (0.4f + 0.6f * grow) * Mathf.Lerp(0.2f, 1f, fade);
            shown.Visual.transform.position = placed;
            for (int i = 0; i < shown.Puffs.Length; i++)
            {
                float wobble = 1f + 0.06f * Mathf.Sin(Time.time * 2.3f + i * 1.7f);
                Vector3 offset = shown.PuffOffsets[i];
                shown.Puffs[i].localPosition = new Vector3(offset.x * size, offset.y * size, offset.z * size);
                shown.Puffs[i].localScale = Vector3.one * size * shown.PuffSizes[i] * wobble;
            }
        }

        private void UpdateShot(Shown shown, in WorldObject shot, Vector3 position)
        {
            Transform ball = shown.Visual.transform.GetChild(0);
            ball.position = position;
            float radius = shot.Owner >= 0 && shot.Owner < _feed.CharacterCount ? _feed.GetConfig(shot.Owner).Skills.Thief.SnatchRadius : 0.3f;
            ball.localScale = Vector3.one * radius * 2f;
            Vector3 back = new Vector3(shot.Velocity.X, shot.Velocity.Y, 0f).normalized;
            float length = Mathf.Min(ShotTrailLength, shot.ChargeMilli / 1000f);
            shown.Trail.positionCount = 2;
            shown.Trail.SetPosition(0, position);
            shown.Trail.SetPosition(1, position - back * length);
        }

        // 置いたギミック: 範囲の枠と、向きへ流れる 3 つの山形（効いていない間は薄く、予告の間は点滅）。
        private void UpdatePlacedZone(Shown shown, in WorldObject item)
        {
            IReadOnlyList<StageZone> zones = _feed.Zones;
            if (zones == null || item.Stored < 0 || item.Stored >= zones.Count)
            {
                shown.Visual.SetActive(false);
                return;
            }
            StageZone zone = ThiefRules.PlacedZone(zones[item.Stored], item);
            int tick = _feed.CurrentTick;
            int seed = _feed.Rules != null ? _feed.Rules.Seed : 0;
            bool active = zone.IsActive(tick, seed);
            bool warning = !active && InWarning(zone, tick, seed);
            float alpha = active ? 0.95f : warning ? (Mathf.Repeat(Time.time * BlinkPerSecond, 1f) < 0.5f ? 0.9f : 0.2f) : 0.3f;
            Color color = ZoneColor(zone.Kind);
            color.a = alpha;
            SetColor(shown.Frame, color);
            SetColor(shown.Marks, color);
            float z = Z + 0.1f;
            shown.Frame.positionCount = 4;
            shown.Frame.SetPosition(0, new Vector3(zone.MinX, zone.MinY, z));
            shown.Frame.SetPosition(1, new Vector3(zone.MaxX, zone.MinY, z));
            shown.Frame.SetPosition(2, new Vector3(zone.MaxX, zone.MaxY, z));
            shown.Frame.SetPosition(3, new Vector3(zone.MinX, zone.MaxY, z));

            // 向き（周りから中心へ集まるもの・向きのないものは上向き）。
            NVector2 direction = zone.Direction.LengthSquared() > 1e-4f ? NVector2.Normalize(zone.Direction) : new NVector2(0f, 1f);
            var along = new Vector3(direction.X, direction.Y, 0f);
            var side = new Vector3(-direction.Y, direction.X, 0f);
            var center = new Vector3((zone.MinX + zone.MaxX) * 0.5f, (zone.MinY + zone.MaxY) * 0.5f, z);
            float reach = Mathf.Abs(along.x) * (zone.MaxX - zone.MinX) * 0.5f + Mathf.Abs(along.y) * (zone.MaxY - zone.MinY) * 0.5f;
            float width = Mathf.Min(0.6f, Mathf.Abs(side.x) * (zone.MaxX - zone.MinX) * 0.3f + Mathf.Abs(side.y) * (zone.MaxY - zone.MinY) * 0.3f + 0.2f);
            float flow = Mathf.Repeat(Time.time * (active ? 1.2f : 0.3f), 1f);
            const int chevrons = 3;
            shown.Marks.positionCount = chevrons * 3;
            for (int i = 0; i < chevrons; i++)
            {
                float t = Mathf.Repeat((i + flow) / chevrons, 1f) * 2f - 1f;
                Vector3 tip = center + along * (t * reach * 0.8f + width * 0.5f);
                Vector3 tail = tip - along * width;
                shown.Marks.SetPosition(i * 3, tail + side * width);
                shown.Marks.SetPosition(i * 3 + 1, tip);
                shown.Marks.SetPosition(i * 3 + 2, tail - side * width);
            }
        }

        private static bool InWarning(in StageZone zone, int tick, int seed)
        {
            if (zone.Timing.Period <= 0 || zone.Timing.WarningTicks <= 0)
            {
                return false;
            }
            zone.Cycle(tick, seed, out _, out _, out int next);
            return next - tick <= zone.Timing.WarningTicks;
        }

        private static Color ZoneColor(StageZoneKind kind)
        {
            switch (kind)
            {
                case StageZoneKind.JumpPad: return new Color(0.3f, 1f, 0.45f);
                case StageZoneKind.Updraft: return new Color(0.55f, 0.85f, 1f);
                case StageZoneKind.BoostRing: return new Color(1f, 0.85f, 0.25f);
                case StageZoneKind.SpeedLane: return new Color(0.3f, 1f, 0.95f);
                case StageZoneKind.Launcher: return new Color(1f, 0.6f, 0.2f);
                case StageZoneKind.Bumper: return new Color(1f, 0.45f, 0.8f);
                case StageZoneKind.Gust: return new Color(0.85f, 0.95f, 1f);
                case StageZoneKind.Hazard: return new Color(1f, 0.3f, 0.1f);
                default: return Color.white;
            }
        }

        // 置いた仕掛け: 赤外線は板・予告・ミサイル、ピッチャーマシンは打ち出す向きの線。
        private void UpdatePlacedTrap(Shown shown, in WorldObject item, Vector3 position)
        {
            IReadOnlyList<StageTrap> traps = _feed.Traps;
            if (traps == null || item.Stored < 0 || item.Stored >= traps.Count)
            {
                shown.Visual.SetActive(false);
                return;
            }
            StageTrap source = traps[item.Stored];
            Transform launcher = shown.Visual.transform.GetChild(0);
            launcher.position = position;
            NVector2 aim = ThiefRules.PlacedTrapAim(source, item);
            launcher.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(aim.Y, aim.X) * Mathf.Rad2Deg);
            if (source.Kind == StageTrapKind.Pitcher)
            {
                shown.Beam.SetActive(false);
                shown.Missile.SetActive(false);
                Vector3 direction = new Vector3(aim.X, aim.Y, 0f).normalized;
                shown.Frame.positionCount = 2;
                shown.Frame.SetPosition(0, position);
                shown.Frame.SetPosition(1, position + direction * 1.2f);
                return;
            }
            shown.Frame.positionCount = 0;
            bool flying = ThiefRules.TryGetPlacedMissile(source, item, _feed.CurrentTick, out bool arming, out NVector2 missile);
            bool waiting = item.ChargeMilli == 0;
            // 赤外線: 待っている間は明滅し、予告の間は速く点滅する。撃った後（飛んでいる・待ちの間）は消す。
            shown.Beam.SetActive(waiting || arming);
            if (shown.Beam.activeSelf)
            {
                ThiefRules.PlacedBeam(source, item, out NVector2 min, out NVector2 max);
                shown.Beam.transform.position = new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, Z + 0.2f);
                shown.Beam.transform.localScale = new Vector3(Mathf.Max(0.05f, max.X - min.X), Mathf.Max(0.05f, max.Y - min.Y), 0.05f);
                shown.Beam.SetActive(!arming || Mathf.Repeat(Time.time * BlinkPerSecond, 1f) < 0.5f);
            }
            bool missileShown = flying && !arming;
            shown.Missile.SetActive(missileShown);
            if (missileShown)
            {
                shown.Missile.transform.position = new Vector3(missile.X, missile.Y, Z);
                // カプセルは縦向きなので、上を飛ぶ向きへ回す。
                shown.Missile.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(aim.Y, aim.X) * Mathf.Rad2Deg - 90f);
            }
            if (_primed && _audio != null)
            {
                if (arming && !shown.Arming)
                {
                    _audio.Play(SoundId.Alarm, 0.8f);
                }
                if (missileShown && !shown.Flying)
                {
                    _audio.Play(SoundId.MissileFire, 0.9f);
                }
            }
            shown.Arming = arming;
            shown.Flying = missileShown;
        }

        private void UpdateTrapBall(Shown shown, in WorldObject ball, Vector3 position)
        {
            IReadOnlyList<StageTrap> traps = _feed.Traps;
            float size = traps != null && ball.Variant < traps.Count ? traps[ball.Variant].HalfSize.X * 2f : 0.6f;
            shown.Visual.transform.position = position;
            shown.Visual.transform.localScale = Vector3.one * size;
            bool visible = !ThiefRules.IsBlinking(ball) || Mathf.Repeat(Time.time * BlinkPerSecond, 1f) < 0.6f;
            shown.Visual.SetActive(visible);
        }

        private void PlaySpawn(WorldObjectKind kind, in WorldObject item)
        {
            if (!_primed || _audio == null)
            {
                return;
            }
            switch (kind)
            {
                case WorldObjectKind.Smoke:
                    _audio.Play(SoundId.Smoke, 0.9f, 0.05f);
                    break;
                case WorldObjectKind.SnatchShot:
                    _audio.Play(SoundId.Snatch, 0.8f, 0.05f);
                    break;
                case WorldObjectKind.TrapBall:
                    _audio.Play(SoundId.Pitch, 0.8f, 0.04f);
                    break;
                case WorldObjectKind.PlacedZone:
                case WorldObjectKind.PlacedTrap:
                    _audio.Play(SoundId.SafePlace, 0.7f, 0.04f);
                    break;
            }
        }

        private int CountOf(WorldObjectKind kind)
        {
            int count = 0;
            foreach (Shown shown in _shown.Values)
            {
                count += shown.Kind == kind ? 1 : 0;
            }
            return count;
        }

        // ---------------------------------------------------------------- キャラクター

        private void RenderCharacters()
        {
            int count = _feed.CharacterCount;
            while (_characters.Count < count)
            {
                _characters.Add(CreateCharacter(_characters.Count));
            }
            for (int i = 0; i < _characters.Count; i++)
            {
                Character character = _characters[i];
                if (i >= count)
                {
                    character.Shadow.SetActive(false);
                    character.Steals = -1;
                    character.Payouts = -1;
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                Vector3 center = new Vector3(state.Position.X, state.Position.Y, 0f);

                bool invisible = state.Combat.InvisibleTicksLeft > 0 && !state.Combat.IsWaitingToRespawn;
                character.Shadow.SetActive(invisible);
                if (invisible)
                {
                    character.Shadow.transform.position = center + new Vector3(0f, -config.Movement.HalfHeight + 0.03f, 0f);
                    character.Shadow.transform.localScale = new Vector3(config.Movement.HalfWidth * 2.6f, 0.05f, config.Movement.HalfWidth * 1.6f);
                }

                if (character.Steals >= 0 && state.Combat.StealCount > character.Steals)
                {
                    character.Steal.Play(center + Vector3.up * (config.Movement.HalfHeight + 0.4f));
                    StealBursts++;
                }
                character.Steals = state.Combat.StealCount;
                if (character.Payouts >= 0 && state.Combat.HeistPayoutCount > character.Payouts)
                {
                    LastHeistLabel = $"HEIST! +{state.Combat.HeistLastPayout}";
                    character.Heist.Play(center, LastHeistLabel);
                    HeistBursts++;
                }
                character.Payouts = state.Combat.HeistPayoutCount;
            }
        }

        private Character CreateCharacter(int index)
        {
            var character = new Character
            {
                Shadow = Child(_shadowTemplate, transform),
                Steal = Instantiate(_stealTemplate, transform),
                Heist = Instantiate(_heistTemplate, transform),
            };
            character.Shadow.name = $"InvisibleShadowP{index + 1}";
            character.Shadow.SetActive(false);
            character.Steal.name = $"StealBurstP{index + 1}";
            character.Steal.gameObject.SetActive(true);
            character.Heist.name = $"HeistBurstP{index + 1}";
            character.Heist.gameObject.SetActive(true);
            return character;
        }

        // ---------------------------------------------------------------- 部品

        private static GameObject Child(GameObject template, Transform parent)
        {
            GameObject copy = Instantiate(template, parent);
            copy.SetActive(true);
            return copy;
        }

        private LineRenderer Line(Transform parent, string lineName, Color color, float width)
        {
            LineRenderer line = Instantiate(_lineTemplate, parent);
            line.name = lineName;
            line.gameObject.SetActive(true);
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.widthMultiplier = width;
            SetColor(line, color);
            return line;
        }

        private static void SetColor(LineRenderer line, Color color)
        {
            line.startColor = color;
            line.endColor = color;
        }

        /// <summary>透明の影の不透明さ（シーンのビルダーが材質を作るのに使う）。</summary>
        public static float ShadowOpacity => ShadowAlpha;

        /// <summary>奪った瞬間の光の色（シーンのビルダーが使う）。</summary>
        public static Color StealBurstColor => StealColor;

        /// <summary>置いた赤外線の板の色（シーンのビルダーが使う）。</summary>
        public static Color PlacedBeamColor => BeamColor;
    }
}
