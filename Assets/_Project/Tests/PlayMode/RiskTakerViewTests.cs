using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// RISK TAKER の表示（docs/14 1.3、docs/13 T5-6）: 一覧で選べる、ミラーボールとセーフティボックスの表示、長押しで預ける枚数、
    /// 金庫の「X/300」と金色・虹色の光、札の HOLD と ACTIVE、音。Sandbox_Wire で F3 の代わりに CycleCharacter を呼ぶ。
    /// </summary>
    public sealed class RiskTakerViewTests
    {
        [UnityTest]
        public IEnumerator RiskTaker_PlacesTheMirrorBallAndTheSafe_WithTheirDisplays()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var objectsView = Object.FindFirstObjectByType<WorldObjectsView>();
            var hold = Object.FindFirstObjectByType<HoldDepositHud>();
            var hud = Object.FindFirstObjectByType<SkillHud>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(objectsView, Is.Not.Null, "置かれた物の表示がない");
            Assert.That(hold, Is.Not.Null, "預ける枚数の表示がない");

            CharacterConfigAsset character = runner.CycleCharacter();
            Assert.That(character.DisplayName, Is.EqualTo(CharacterPresets.RiskTakerName), "一覧に RISK TAKER がない");
            Assert.That(runner.PlayerConfig.Combat.Special1.Name, Is.EqualTo("LIGHT"));
            yield return WaitSeconds(0.5f);

            // Q: ミラーボール。
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special1), default });
            yield return WaitSeconds(0.4f);
            Assert.That(objectsView.ShownCount, Is.EqualTo(1), "ミラーボールを表示しない");
            Assert.That(audio.History.Contains(SoundId.MirrorBall), Is.True, "ミラーボールの音が鳴らない");

            // E の長押し: 頭上と札に預ける枚数、離すと金庫。
            CharacterState state = runner.PlayerState;
            state.Coins.Held = 120;
            runner.SetPlayerState(state);
            var holdInputs = Enumerable.Repeat(new SimInput(0, 0, InputButtons.Special2), 90).ToList();
            holdInputs.Add(default);
            runner.InputOverride = new Script(holdInputs);
            int shownDeposit = -1;
            string slotText = string.Empty;
            float start = Time.time;
            while (runner.InputOverride != null && Time.time - start < 3f)
            {
                if (hold.DepositOf(0) > 0)
                {
                    shownDeposit = hold.DepositOf(0);
                    slotText = hud.SlotOf(1).Text;
                }
                yield return null;
            }
            Assert.That(shownDeposit, Is.GreaterThan(0), "長押し中に預ける枚数を出さない");
            Assert.That(slotText, Does.StartWith("-"), "札に預ける枚数を出さない");
            yield return WaitSeconds(0.2f);

            ISimulationFeed feed = runner;
            WorldObjects objects = feed.Objects;
            int safeIndex = Enumerable.Range(0, objects.Count).First(i => objects[i].Kind == WorldObjectKind.Safe);
            WorldObject safe = objects[safeIndex];
            Assert.That(objectsView.TryGetSafe(safe.Id, out string label, out WorldObjectsView.SafeGlow glow), Is.True, "金庫を表示しない");
            Assert.That(label, Is.EqualTo($"{safe.Stored}/300"), "「X/300」の形でない");
            Assert.That(glow, Is.EqualTo(WorldObjectsView.SafeGlow.None));
            Assert.That(hud.SlotOf(1).Text, Is.EqualTo("ACTIVE"), "置いている間は ACTIVE でない");
            Assert.That(audio.History.Contains(SoundId.SafePlace), Is.True);

            // 次のチャージまでの円形のゲージと、耐久の HP バー。
            Assert.That(objectsView.TryGetSafeState(safe.Id, out WorldObjectsView.SafeEvent safeEvent, out float health, out float ring), Is.True);
            Assert.That(health, Is.EqualTo(1f), "耐久が満タンでない");
            Assert.That(ring, Is.InRange(0f, 1f), "チャージのゲージを出さない");
            Assert.That(objectsView.SafeRingHasFrame(safe.Id), Is.True, "チャージのゲージに縁がない");

            // 150 枚で金色、300 枚で虹色（表示を確かめるため、チャージを書き換える）。チャージした瞬間の演出と音。
            safe.ChargeMilli = 150_000;
            safe.Damage = 15;
            objects[safeIndex] = safe;
            yield return null;
            yield return null;
            objectsView.TryGetSafe(safe.Id, out label, out glow);
            Assert.That(glow, Is.EqualTo(WorldObjectsView.SafeGlow.Gold), "150 枚で金色に光らない");
            objectsView.TryGetSafeState(safe.Id, out safeEvent, out health, out ring);
            Assert.That(safeEvent, Is.EqualTo(WorldObjectsView.SafeEvent.Gold), "150 枚の演出がない");
            Assert.That(health, Is.EqualTo(0.5f).Within(1e-4f), "HP バーが減らない");
            Assert.That(audio.History.Contains(SoundId.SafeGold), Is.True, "150 枚の音が鳴らない");
            safe.ChargeMilli = 300_000;
            objects[safeIndex] = safe;
            yield return null;
            yield return null;
            objectsView.TryGetSafe(safe.Id, out label, out glow);
            Assert.That(label, Is.EqualTo("300/300"));
            Assert.That(glow, Is.EqualTo(WorldObjectsView.SafeGlow.Rainbow), "300 枚で虹色に光らない");
            objectsView.TryGetSafeState(safe.Id, out safeEvent, out health, out ring);
            Assert.That(safeEvent, Is.EqualTo(WorldObjectsView.SafeEvent.Max), "300 枚の演出がない");
            Assert.That(ring, Is.EqualTo(-1f), "上限なのにチャージのゲージを出している");
            Assert.That(audio.History.Contains(SoundId.SafeMax), Is.True, "300 枚の音が鳴らない");

            // 一覧を一周して STANDARD に戻す（ほかのテストに影響しないように）。
            runner.CycleCharacter();
        }

        [UnityTest]
        public IEnumerator Fever_ShowsTheGauge_AndRouletteJackpotShowsTheBurst()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            runner.CycleCharacter();
            yield return WaitSeconds(0.3f);

            CharacterState state = runner.PlayerState;
            state.Combat.Gauge = CombatTuning.MaxGauge;
            runner.SetPlayerState(state);
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Super), default });
            yield return WaitSeconds(0.3f);
            Assert.That(runner.PlayerState.Combat.CoinBoostTicksLeft, Is.GreaterThan(0), "前提: フィーバータイム");
            Assert.That(runner.PlayerView.FeverGauge.IsShowing, Is.True, "フィーバーのゲージを出さない");
            Assert.That(runner.PlayerView.FeverGauge.ShowsFrame, Is.True, "円形のゲージに縁がない");

            // JACKPOT を受けたら（受けた数が増えたら）光る。
            state = runner.PlayerState;
            state.Combat.JackpotHitCount++;
            runner.SetPlayerState(state);
            bool sawBurst = false;
            float start = Time.time;
            while (Time.time - start < 0.5f)
            {
                sawBurst |= runner.PlayerView.JackpotBurst.IsPlaying;
                yield return null;
            }
            Assert.That(sawBurst, Is.True, "JACKPOT! の光が出ない");
            runner.CycleCharacter();
        }

        [UnityTest]
        public IEnumerator AllIn_ShowsTheAuraByChargeLevel_UpToRainbow()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var aura = Object.FindFirstObjectByType<ChargeAuraView>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(aura, Is.Not.Null, "ためのオーラの表示がない");
            runner.CycleCharacter();
            yield return WaitSeconds(0.6f);
            Assert.That(runner.PlayerState.Grounded, Is.True, "前提: 地上");

            var inputs = Enumerable.Repeat(new SimInput(0, 0, InputButtons.Attack), 200).ToList();
            inputs.Add(default);
            runner.InputOverride = new Script(inputs);
            var levels = new HashSet<int>();
            bool sawRainbow = false;
            float start = Time.time;
            while (runner.InputOverride != null && Time.time - start < 6f)
            {
                levels.Add(aura.LevelOf(0));
                sawRainbow |= aura.IsRainbow(0);
                yield return null;
            }
            Assert.That(levels.Contains(0), Is.True, "白（ため始め）のオーラを出さない");
            Assert.That(levels.Contains(3), Is.True, "途中の段階のオーラを出さない");
            Assert.That(sawRainbow, Is.True, "最後まで貯めても虹色にならない");
            Assert.That(audio.History.Contains(SoundId.ChargeUp), Is.True, "段階が上がった音が鳴らない");
            Assert.That(audio.History.Contains(SoundId.ChargeMax), Is.True, "虹の音が鳴らない");
            yield return WaitSeconds(1f);
            Assert.That(aura.LevelOf(0), Is.EqualTo(-1), "技が終わってもオーラが残る");

            // 2 回目のため: 白から始まる（虹色が残らない。ユーザーの指摘、2026-10-16）。技が出る向きに矢印。
            float wait = Time.time;
            while ((!runner.PlayerState.Grounded || !runner.PlayerState.Action.IsFree) && Time.time - wait < 3f)
            {
                yield return null;
            }
            Assert.That(runner.PlayerState.Grounded, Is.True, "前提: 地上に戻った");
            runner.InputOverride = new Script(Enumerable.Repeat(new SimInput(0, 0, InputButtons.Attack), 20).Append(default).ToList());
            yield return WaitSeconds(0.15f);
            Assert.That(aura.LevelOf(0), Is.EqualTo(0), "2 回目のためが白から始まらない");
            Assert.That(aura.IsRainbow(0), Is.False);
            Color white = aura.AuraColorOf(0);
            Assert.That(white.r, Is.GreaterThan(0.95f), "2 回目のためで白に戻らない");
            Assert.That(white.g, Is.GreaterThan(0.95f), "2 回目のためで白に戻らない");
            Assert.That(white.b, Is.GreaterThan(0.95f), "2 回目のためで白に戻らない");
            Assert.That(aura.ArrowFacingOf(0), Is.EqualTo(runner.PlayerState.Facing), "技が出る向きの矢印を出さない");
            yield return WaitSeconds(1f);
            runner.CycleCharacter();
        }

        [UnityTest]
        public IEnumerator RouletteSmash_SpinsAboveTheHead_AndStopsOnTheRoll()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var roulette = Object.FindFirstObjectByType<RouletteView>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(roulette, Is.Not.Null, "ルーレットの表示がない");
            runner.CycleCharacter();
            yield return WaitSeconds(0.5f);

            CharacterState state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(state.Position.X, state.Position.Y + 12f, 0f);
            state.Velocity = System.Numerics.Vector3.Zero;
            state.Grounded = false;
            runner.SetPlayerState(state);
            yield return null;
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, -127, InputButtons.Attack), default });
            bool spun = false;
            bool stopped = false;
            int roll = -1;
            float start = Time.time;
            while (Time.time - start < 1.5f && !stopped)
            {
                spun |= roulette.IsSpinning(0);
                if (roulette.IsStopped(0))
                {
                    stopped = true;
                    roll = roulette.RollOf(0);
                }
                yield return null;
            }
            Assert.That(spun, Is.True, "発生の間にルーレットが回らない");
            Assert.That(stopped, Is.True, "攻撃が出てもルーレットが止まらない");
            ISimulationFeed feed = runner;
            int expected = HitResolver.RouletteRoll(feed.Rules.Seed, 0, runner.PlayerState.Combat.AttackSerial);
            Assert.That(roll, Is.EqualTo(expected), "止まった目がシミュレーションの目と違う");
            Assert.That(audio.History.Contains(SoundId.RouletteSpin), Is.True, "回る音が鳴らない");
            Assert.That(audio.History.Contains(SoundId.RouletteStop), Is.True, "止まる音が鳴らない");
            Assert.That(runner.PlayerView.ChargeEffect == null || !runner.PlayerView.ChargeEffect.IsShowing, Is.True, "ルーレットの技で CHARGE! を出した");
            runner.CycleCharacter();
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }

        private sealed class Script : ITickInputSource
        {
            private readonly List<SimInput> _inputs;
            private int _next;

            public Script(List<SimInput> inputs)
            {
                _inputs = inputs;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _next < _inputs.Count ? _inputs[_next] : default;
                return _next++ < _inputs.Count;
            }
        }
    }
}
