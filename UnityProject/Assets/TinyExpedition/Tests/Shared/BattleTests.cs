using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TinyExpedition.Core;
using TinyExpedition.Core.Battle;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.Progress;
using TinyExpedition.Core.Random;

namespace TinyExpedition.Tests
{
    [Category("Battle")]
    public class BattleTests
    {
        [TestCase(100, 0, false, 100)]
        [TestCase(100, 100, false, 50)]
        [TestCase(100, 0, true, 150)]
        [TC("BATTLE-001")]
        public void DamageFormula(int atk, int def, bool crit, int expected)
        {
            Assert.That(BattleSimulator.CalcDamage(atk, def, crit), Is.EqualTo(expected));
        }

        [Test, TC("BATTLE-002")]
        public void Damage_IsAtLeastOne()
        {
            Assert.That(BattleSimulator.CalcDamage(1, 9999, false), Is.EqualTo(1));
        }

        [Test, TC("BATTLE-003")]
        public void SameSeed_SameBattleLog()
        {
            var db = TestData.LoadReal();
            var party = new[] { "c_lumi", "c_firefly", "c_moss" }.Select(db.GetCharacter).ToList();
            var stage = db.GetStage("1-3");

            var a = BattleSimulator.Run(party, stage, new Pcg32(5));
            var b = BattleSimulator.Run(party, stage, new Pcg32(5));

            Assert.That(a.Outcome, Is.EqualTo(b.Outcome));
            Assert.That(a.Log.Select(e => e.ToString()), Is.EqualTo(b.Log.Select(e => e.ToString())));
        }

        [Test, TC("BATTLE-004")]
        public void SpeedTie_AllyActsFirst()
        {
            var ally = TestData.Char("ally", Rarity.OneStar, spd: 50);
            var stage = TestData.Stage("s", enemyHp: 9999);
            stage.Enemies[0].Spd = 50;
            var result = BattleSimulator.Run(new[] { ally }, stage, new Pcg32(1));
            Assert.That(result.Log[0].Attacker, Is.EqualTo("ally"));
        }

        [Test, TC("BATTLE-005")]
        public void NoResolutionIn30Rounds_IsTimeout()
        {
            var tank = TestData.Char("tank", Rarity.OneStar, hp: 99999, atk: 1, def: 99999);
            var stage = TestData.Stage("s", enemyHp: 99999, enemyAtk: 1);
            stage.Enemies[0].Def = 99999;
            var result = BattleSimulator.Run(new[] { tank }, stage, new Pcg32(1));
            Assert.That(result.Outcome, Is.EqualTo(BattleOutcome.Timeout));
            Assert.That(result.Rounds, Is.EqualTo(BattleSimulator.MaxRounds));
            Assert.That(result.Stars, Is.EqualTo(0));
        }

        [Test, TC("BATTLE-006")]
        public void Stars_AllAlive3_OneAlive1()
        {
            var strong = TestData.Char("strong", Rarity.ThreeStar, hp: 5000, atk: 500, spd: 200);
            var allAlive = BattleSimulator.Run(new[] { strong }, TestData.Stage("s", enemyHp: 100), new Pcg32(1));
            Assert.That(allAlive.Stars, Is.EqualTo(3));

            // 약한 두 명은 먼저 쓰러지고 튼튼한 한 명만 남아 이긴다
            var weak1 = TestData.Char("weak1", Rarity.OneStar, hp: 1, atk: 1, spd: 1);
            var weak2 = TestData.Char("weak2", Rarity.OneStar, hp: 1, atk: 1, spd: 1);
            var hero = TestData.Char("hero", Rarity.ThreeStar, hp: 5000, atk: 60, spd: 1);
            var stage = TestData.Stage("s", enemyHp: 300, enemyAtk: 10);
            stage.Enemies[0].Spd = 100;
            var oneAlive = BattleSimulator.Run(new[] { weak1, weak2, hero }, stage, new Pcg32(1));
            Assert.That(oneAlive.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(oneAlive.Stars, Is.EqualTo(1));
        }

        [Test, TC("BATTLE-007")]
        public void InvalidParty_Throws()
        {
            var stage = TestData.Stage("s");
            var c = TestData.SmallRoster();
            Assert.Throws<ArgumentException>(() => BattleSimulator.Run(new List<CharacterDef>(), stage, new Pcg32(1)));
            Assert.Throws<ArgumentException>(() => BattleSimulator.Run(c.Take(4).ToList(), stage, new Pcg32(1)));
            Assert.Throws<ArgumentException>(() => BattleSimulator.Run(new[] { c[0], c[0] }, stage, new Pcg32(1)));
        }
    }

    [Category("Stage")]
    public class StageTests
    {
        private static GameSession NewSession()
        {
            var state = new PlayerState { Gems = 0 };
            state.Owned["three_a"] = 1;
            var roster = TestData.SmallRoster();
            roster.First(c => c.Id == "three_a").Atk = 9999;
            roster.First(c => c.Id == "three_a").Spd = 999;
            var db = new GameDatabase(roster, new[] { TestData.Banner(250, 1800, 7950) },
                new[] { TestData.Stage("s1", gems: 100), TestData.Stage("s2", "s1") });
            return new GameSession(db, state);
        }

        [Test, TC("STAGE-001")]
        public void LockedStage_CannotBattle()
        {
            var s = NewSession();
            Assert.That(s.Battle("s2", new[] { "three_a" }).Error, Is.EqualTo(ActionError.StageLocked));
            s.Battle("s1", new[] { "three_a" });
            Assert.That(s.Battle("s2", new[] { "three_a" }).Ok, Is.True);
        }

        [Test, TC("STAGE-002")]
        public void FirstClearReward_OnlyOnce()
        {
            var s = NewSession();
            s.Battle("s1", new[] { "three_a" });
            s.Battle("s1", new[] { "three_a" });
            Assert.That(s.State.Gems, Is.EqualTo(100));
        }

        [Test, TC("STAGE-003")]
        public void BestStars_AreKept()
        {
            // 앞줄(HP 1)이 먼저 쓰러져 별 1로 이기는 상황을 만든다
            var front = TestData.Char("front", Rarity.OneStar, hp: 1, spd: 1);
            var hero = TestData.Char("hero", Rarity.ThreeStar, hp: 5000, atk: 60, spd: 1);
            var stage = TestData.Stage("s1", enemyHp: 100, enemyAtk: 10);
            stage.Enemies[0].Spd = 100;
            var db = new GameDatabase(new[] { front, hero }, new BannerDef[0], new[] { stage });
            var state = new PlayerState();
            state.Owned["front"] = 1;
            state.Owned["hero"] = 1;
            state.ClearedStages["s1"] = 3;
            var s = new GameSession(db, state);

            var r = s.Battle("s1", new[] { "front", "hero" });

            Assert.That(r.Value.Stars, Is.EqualTo(1), "전제: 이번 판은 별 1");
            Assert.That(s.State.ClearedStages["s1"], Is.EqualTo(3));
        }

        [Test, TC("STAGE-004")]
        public void NotOwnedCharacter_CannotJoin()
        {
            var s = NewSession();
            Assert.That(s.Battle("s1", new[] { "three_pick" }).Error, Is.EqualTo(ActionError.CharacterNotOwned));
        }
    }
}
