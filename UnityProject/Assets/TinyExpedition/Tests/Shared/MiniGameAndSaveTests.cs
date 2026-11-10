using System;
using System.Linq;
using NUnit.Framework;
using TinyExpedition.Core;
using TinyExpedition.Core.MiniGame;
using TinyExpedition.Core.Progress;

namespace TinyExpedition.Tests
{
    [Category("MiniGame")]
    public class MiniGameTests
    {
        // 주기 1.6초 → 위치 p(0~1)에 오는 시각(올라가는 구간) = p * 0.8
        private static double TimeAt(double position) => position * 0.8;

        [TestCase(0.0, 0.0)]
        [TestCase(0.4, 0.5)]
        [TestCase(0.8, 1.0)]
        [TestCase(1.2, 0.5)]
        [TestCase(1.6, 0.0)]
        [TC("MINI-001")]
        public void BarMovesBackAndForth(double t, double expected)
        {
            Assert.That(new TimingTapGame(new TimingTapConfig()).PositionAt(t), Is.EqualTo(expected).Within(1e-9));
        }

        [TestCase(0.46, Judgement.Perfect)]
        [TestCase(0.54, Judgement.Perfect)]
        [TestCase(0.4599, Judgement.Good)]
        [TC("MINI-002")]
        public void PerfectBoundary(double pos, Judgement expected)
        {
            Assert.That(new TimingTapGame(new TimingTapConfig()).Judge(pos), Is.EqualTo(expected));
        }

        [TestCase(0.38, Judgement.Good)]
        [TestCase(0.3799, Judgement.Miss)]
        [TestCase(0.62, Judgement.Good)]
        [TestCase(0.6201, Judgement.Miss)]
        [TC("MINI-003")]
        public void GoodMissBoundary(double pos, Judgement expected)
        {
            Assert.That(new TimingTapGame(new TimingTapConfig()).Judge(pos), Is.EqualTo(expected));
        }

        [Test, TC("MINI-004")]
        public void ComboBonus_AndResetOnMiss()
        {
            var g = new TimingTapGame(new TimingTapConfig());
            var gained = new[]
            {
                g.Tap(TimeAt(0.5)),        // Perfect, 콤보1 → 100
                g.Tap(1.6 + TimeAt(0.5)),  // Perfect, 콤보2 → 110
                g.Tap(3.2 + TimeAt(0.1)),  // Miss → 0
                g.Tap(4.8 + TimeAt(0.5)),  // Perfect, 콤보1 → 100
            };
            Assert.That(gained.Select(x => x.Gained), Is.EqualTo(new[] { 100, 110, 0, 100 }));
            Assert.That(gained.Select(x => x.Combo), Is.EqualTo(new[] { 1, 2, 0, 1 }));
            Assert.That(g.Score, Is.EqualTo(310));
            Assert.That(g.MaxCombo, Is.EqualTo(2));
        }

        [Test, TC("MINI-005")]
        public void ComboBonus_IsCapped()
        {
            var g = new TimingTapGame(new TimingTapConfig { Rounds = 15 });
            var gains = Enumerable.Range(0, 15).Select(i => g.Tap(i * 1.6 + TimeAt(0.5)).Gained).ToList();
            // 콤보 n의 보너스 = min(100, (n-1)*10)% → 11콤보부터 200점
            Assert.That(gains[9], Is.EqualTo(190));
            Assert.That(gains[10], Is.EqualTo(200));
            Assert.That(gains.Skip(10).All(x => x == 200), Is.True);
        }

        [Test, TC("MINI-006")]
        public void EndsAfterRounds_ExtraTapThrows()
        {
            var g = new TimingTapGame(new TimingTapConfig());
            for (int i = 0; i < 10; i++)
            {
                Assert.That(g.IsFinished, Is.False);
                g.Tap(i);
            }
            Assert.That(g.IsFinished, Is.True);
            Assert.Throws<InvalidOperationException>(() => g.Tap(11));
        }

        [Test, TC("MINI-007")]
        public void TapGoingBackInTime_Throws()
        {
            var g = new TimingTapGame(new TimingTapConfig());
            g.Tap(1.0);
            Assert.Throws<ArgumentException>(() => g.Tap(0.5));
        }

        [Test, TC("MINI-008")]
        public void Replay_ReproducesOriginal()
        {
            var times = new[] { 0.41, 1.93, 2.77, 3.6, 4.42, 5.55, 6.01, 7.2, 8.39, 9.0 };
            var original = new TimingTapGame(new TimingTapConfig());
            foreach (var t in times) original.Tap(t);

            var replay = TimingTapGame.Replay(new TimingTapConfig(), original.Taps.Select(x => x.Time));

            Assert.That(replay.Score, Is.EqualTo(original.Score));
            Assert.That(replay.Taps.Select(x => x.Judgement), Is.EqualTo(original.Taps.Select(x => x.Judgement)));
            Assert.That(replay.Taps.Select(x => x.Combo), Is.EqualTo(original.Taps.Select(x => x.Combo)));
        }

        [Test, TC("MINI-009")]
        public void RewardConversion_AndCap()
        {
            // 점수 1990 → 99 보석, 3000 → 상한 100
            Assert.That(RewardFor(1990), Is.EqualTo(99));
            Assert.That(RewardFor(3000), Is.EqualTo(100));
        }

        private static int RewardFor(int targetScore)
        {
            // 1회 탭 = Rounds 1, PerfectScore를 목표 점수로 두어 원하는 점수를 만든다
            var g = new TimingTapGame(new TimingTapConfig { Rounds = 1, PerfectScore = targetScore });
            g.Tap(TimeAt(0.5));
            return g.RewardGems();
        }

        [Test, TC("MINI-010")]
        public void Reward_CannotBeClaimedTwice()
        {
            var session = new GameSession(TestData.Db(), new PlayerState { Gems = 0 });
            var g = new TimingTapGame(new TimingTapConfig());
            for (int i = 0; i < 10; i++) g.Tap(i * 1.6 + TimeAt(0.5));

            int first = session.ClaimMiniGame(g);
            int second = session.ClaimMiniGame(g);

            Assert.That(first, Is.GreaterThan(0));
            Assert.That(second, Is.EqualTo(0));
            Assert.That(session.State.Gems, Is.EqualTo(first));
        }

        [Test, TC("MINI-011")]
        public void UnfinishedGame_GivesNoReward()
        {
            var session = new GameSession(TestData.Db(), new PlayerState { Gems = 0 });
            var g = new TimingTapGame(new TimingTapConfig());
            for (int i = 0; i < 5; i++) g.Tap(i * 1.6 + TimeAt(0.5));
            Assert.That(session.ClaimMiniGame(g), Is.EqualTo(0));
            Assert.That(session.State.Gems, Is.EqualTo(0));
        }
    }

    [Category("Save")]
    public class SaveTests
    {
        [Test, TC("SAVE-001")]
        public void RoundTrip_PreservesState()
        {
            var session = new GameSession(TestData.LoadReal(), new PlayerState());
            session.Pull("b_lumi", 10);
            session.State.ClearedStages["1-1"] = 2;
            session.State.MiniGameBestScore = 1234;

            var json = SaveSerializer.Serialize(session.State);
            var loaded = SaveSerializer.Deserialize(json);

            Assert.That(SaveSerializer.Serialize(loaded), Is.EqualTo(json));
            Assert.That(loaded.Gems, Is.EqualTo(session.State.Gems));
            Assert.That(loaded.Pity["b_lumi"].PullsSinceThreeStar, Is.EqualTo(session.State.Pity["b_lumi"].PullsSinceThreeStar));
        }

        [TestCase("")]
        [TestCase("{ \"Version\": 1, \"Gems\": ")]
        [TestCase("null")]
        [TC("SAVE-002")]
        public void BrokenSave_Throws(string json)
        {
            Assert.Throws<SaveCorruptedException>(() => SaveSerializer.Deserialize(json));
        }

        [Test, TC("SAVE-003")]
        public void FutureVersion_Throws()
        {
            Assert.Throws<SaveCorruptedException>(() => SaveSerializer.Deserialize("{ \"Version\": 99 }"));
        }

        [TestCase("{ \"Version\": 1, \"Gems\": -1 }")]
        [TestCase("{ \"Version\": 1, \"ClearedStages\": { \"1-1\": 4 } }")]
        [TestCase("{ \"Version\": 1, \"Owned\": { \"c_lumi\": 0 } }")]
        [TC("SAVE-004")]
        public void TamperedValues_Throw(string json)
        {
            Assert.Throws<SaveCorruptedException>(() => SaveSerializer.Deserialize(json));
        }

        [Test, TC("SAVE-005")]
        public void SaveLoad_DoesNotChangeNextResults()
        {
            var db = TestData.LoadReal();
            var a = new GameSession(db, new PlayerState());
            a.Pull("b_standard", 10);
            var b = new GameSession(db, SaveSerializer.Deserialize(SaveSerializer.Serialize(a.State)));

            var nextA = a.Pull("b_standard", 10).Value.Select(r => r.CharacterId);
            var nextB = b.Pull("b_standard", 10).Value.Select(r => r.CharacterId);
            Assert.That(nextB, Is.EqualTo(nextA));
        }
    }
}
