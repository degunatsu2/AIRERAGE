using System;
using System.Numerics;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Support
{
    /// <summary>
    /// キャラクター1人のシミュレーションを動かすテスト補助。
    /// </summary>
    public sealed class SimHarness
    {
        public SimHarness(BoxTerrain world, CharacterConfig config = null, Vector3? spawn = null, int facing = 1)
        {
            World = world;
            Config = config ?? new CharacterConfig();
            Sim = new SimWorld(world);
            Sim.AddCharacter(Config, spawn ?? new Vector3(0f, 0.91f, 0f), facing);
        }

        public BoxTerrain World { get; }
        public CharacterConfig Config { get; }
        public SimWorld Sim { get; }

        public CharacterState State
        {
            get => Sim.GetState(0);
            set => Sim.SetState(0, value);
        }

        public Vector3 HalfExtents => Config.Movement.HalfExtents;

        public void Step(PlayerInput input, int ticks = 1)
        {
            var inputs = new[] { input };
            for (int i = 0; i < ticks; i++)
            {
                Sim.Step(inputs);
            }
        }

        /// <summary>条件を満たすまで進め、かかったティック数を返す。maxTicks 以内に満たさなければテスト失敗。</summary>
        public int StepUntil(Func<CharacterState, bool> condition, PlayerInput input, int maxTicks)
        {
            var inputs = new[] { input };
            for (int i = 1; i <= maxTicks; i++)
            {
                Sim.Step(inputs);
                if (condition(State))
                {
                    return i;
                }
            }
            Assert.Fail($"{maxTicks} ティック以内に条件を満たしませんでした。最終状態: {State}");
            return -1;
        }
    }

    /// <summary>よく使う入力。</summary>
    public static class Inputs
    {
        public static readonly PlayerInput None = new PlayerInput(0, 0, InputButtons.None);
        public static readonly PlayerInput Right = PlayerInput.FromAxes(1f, 0f, InputButtons.None);
        public static readonly PlayerInput Left = PlayerInput.FromAxes(-1f, 0f, InputButtons.None);
        public static readonly PlayerInput Jump = new PlayerInput(0, 0, InputButtons.Jump);
        public static readonly PlayerInput RightJump = PlayerInput.FromAxes(1f, 0f, InputButtons.Jump);

        public static PlayerInput Wire(float x = 0f, float y = 0f)
        {
            return PlayerInput.FromAxes(x, y, InputButtons.Wire);
        }

        public static PlayerInput Stick(float x, float y)
        {
            return PlayerInput.FromAxes(x, y, InputButtons.None);
        }
    }
}
