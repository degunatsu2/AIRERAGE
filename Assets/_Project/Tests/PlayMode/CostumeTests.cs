using System.Collections;
using System.Collections.Generic;
using Airerage.Runtime.Data;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// キャラクターの仮の見た目（衣装。ユーザーの要望、2026-10-20）: F3 でキャラクターを切り替えると、そのキャラクターの衣装の部品だけが
    /// 出る（STANDARD は箱のまま）。RISK TAKER・STORM RUNNER・SPACE TRAVELER・PASTRY CHEF はそれぞれ違う衣装。
    /// </summary>
    public sealed class CostumeTests
    {
        [UnityTest]
        public IEnumerator EachCharacter_WearsItsOwnCostume()
        {
            SceneManager.LoadScene("Sandbox_Combat");
            yield return null;
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            CostumeView costume = runner.PlayerView.Costume;
            Assert.That(costume, Is.Not.Null, "キャラクターの表示に衣装がない");
            var seen = new HashSet<CharacterCostume>();
            for (int i = 0; i < runner.Roster.Count; i++)
            {
                CharacterConfigAsset character = runner.CycleCharacter();
                yield return null;
                yield return null;
                Assert.That(costume.Current, Is.EqualTo(character.Costume), $"{character.DisplayName} の衣装になっていない");
                seen.Add(costume.Current);
                int shownSets = 0;
                foreach (Transform child in costume.transform)
                {
                    if (child.name.StartsWith("Costume") && child.gameObject.activeSelf)
                    {
                        shownSets++;
                    }
                }
                Assert.That(shownSets, Is.EqualTo(character.Costume == CharacterCostume.Standard ? 0 : 1),
                    $"{character.DisplayName} で出している衣装の数が違う");
            }
            Assert.That(seen, Is.EquivalentTo(new[]
            {
                CharacterCostume.Standard, CharacterCostume.RiskTaker, CharacterCostume.StormRunner, CharacterCostume.SpaceTraveler,
                CharacterCostume.PastryChef,
            }), "5 体の衣装がそろっていない");
        }
    }
}
