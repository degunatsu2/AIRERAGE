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

            // 150 枚で金色、300 枚で虹色（表示を確かめるため、チャージを書き換える）。
            safe.ChargeMilli = 150_000;
            objects[safeIndex] = safe;
            yield return null;
            yield return null;
            objectsView.TryGetSafe(safe.Id, out label, out glow);
            Assert.That(glow, Is.EqualTo(WorldObjectsView.SafeGlow.Gold), "150 枚で金色に光らない");
            safe.ChargeMilli = 300_000;
            objects[safeIndex] = safe;
            yield return null;
            yield return null;
            objectsView.TryGetSafe(safe.Id, out label, out glow);
            Assert.That(label, Is.EqualTo("300/300"));
            Assert.That(glow, Is.EqualTo(WorldObjectsView.SafeGlow.Rainbow), "300 枚で虹色に光らない");

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
