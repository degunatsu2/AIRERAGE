using System;
using System.Collections.Generic;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// 表示と効果音のための、シミュレーションの状態の読み取り口。オフライン（SimulationRunner）とオンライン（OnlineRunner）の
    /// どちらでも、同じ表示（ComboHud）と効果音（CombatSoundEvents）を使えるようにする（docs/11_combat_plan.md T3-8）。
    /// 読むだけで、状態は書き換えられない。
    /// </summary>
    public interface ISimulationFeed
    {
        /// <summary>状態を読めるキャラクターの数（始まっていなければ 0）。番号はワールドの番号（CombatState.LastAttacker と同じ）。</summary>
        int CharacterCount { get; }

        /// <summary>この PC で操作しているキャラクターの番号（いなければ -1）。</summary>
        int LocalCharacter { get; }

        CharacterState GetState(int index);

        CharacterConfig GetConfig(int index);

        /// <summary>表示用の名前（"DUMMY 1"、"P2" など）。</summary>
        string LabelOf(int index);

        /// <summary>表示用の色（プレイヤーの色）。</summary>
        Color ColorOf(int index);

        /// <summary>これまでに進めたティック数（試合の段階と残り時間を、Rules から求めるのに使う）。</summary>
        int CurrentTick { get; }

        /// <summary>試合のルール（始まっていなければ null）。</summary>
        MatchRules Rules { get; }

        /// <summary>アディショナルタイムと勝者（docs/12 T4-8。始まっていなければ MatchState.None）。段階は MatchFlow.PhaseAt で求める。</summary>
        MatchState Match { get; }

        /// <summary>フィールドのコイン（始まっていなければ null）。</summary>
        CoinField Coins { get; }

        /// <summary>コインの出現地点（番号の順。CoinField.SpawnReadyTick と FieldCoin.Source の番号。始まっていなければ空）。</summary>
        IReadOnlyList<CoinSpawnPoint> CoinSpawns { get; }

        /// <summary>拠点の候補の地点（番号の順。始まっていなければ空）。今の拠点は BankRules.SpotAt(Rules, BankSpots.Count, CurrentTick)。</summary>
        IReadOnlyList<BankSpot> BankSpots { get; }

        /// <summary>
        /// シミュレーションが進んだときに通知する。1回の通知で進んだティックの数は決まっていない（オンラインでは、
        /// 1フレームで複数のティックを進めたり、巻き戻して計算し直したりする）。状態の比べ方はそれに耐えるようにする。
        /// </summary>
        event Action Ticked;
    }
}
