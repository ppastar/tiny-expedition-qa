using System;
using System.Linq;
using NUnit.Framework;
using TinyExpedition.Core;
using TinyExpedition.Core.Analysis;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.Gacha;
using TinyExpedition.Core.Progress;
using TinyExpedition.Core.Random;

namespace TinyExpedition.Tests
{
    [Category("Gacha")]
    public class GachaTests
    {
        private static GameSession Session(int gems, params BannerDef[] banners)
        {
            var db = TestData.Db(banners.Length > 0 ? banners : new[] { TestData.Banner(250, 1800, 7950) });
            return new GameSession(db, new PlayerState { Gems = gems });
        }

        [Test, TC("GACHA-001")]
        public void SinglePull_Costs150()
        {
            var s = Session(3000);
            var r = s.Pull("b_test", 1);
            Assert.That(r.Ok, Is.True);
            Assert.That(r.Value.Count, Is.EqualTo(1));
            Assert.That(s.State.Gems, Is.EqualTo(2850));
        }

        [Test, TC("GACHA-002")]
        public void NotEnoughGems_FailsWithoutSideEffects()
        {
            var s = Session(149);
            var r = s.Pull("b_test", 1);
            Assert.That(r.Error, Is.EqualTo(ActionError.NotEnoughGems));
            Assert.That(s.State.Gems, Is.EqualTo(149));
            Assert.That(s.State.Owned, Is.Empty);
            Assert.That(s.State.Pity.ContainsKey("b_test"), Is.False, "실패한 모집이 천장 카운트를 만들면 안 됨");

            var exact = Session(150);
            Assert.That(exact.Pull("b_test", 1).Ok, Is.True, "보석이 정확히 비용과 같으면 성공해야 함");
            Assert.That(exact.State.Gems, Is.EqualTo(0));
        }

        [Test, TC("GACHA-003")]
        public void TenPull_Costs1500_Returns10()
        {
            var s = Session(1500);
            var r = s.Pull("b_test", 10);
            Assert.That(r.Ok, Is.True);
            Assert.That(r.Value.Count, Is.EqualTo(10));
            Assert.That(s.State.Gems, Is.EqualTo(0));
        }

        [Test, TC("GACHA-004"), TC("GACHA-005")]
        public void HardPity_At90_GuaranteesThreeStar_AndResets()
        {
            var banner = TestData.Banner(0, 0, 10000, hardPity: 90);
            var gacha = new GachaService(TestData.Db(new[] { banner }));
            var pity = new PityState { PullsSinceThreeStar = 89 };

            var r = gacha.PullOne(banner, pity, new Pcg32(1));

            Assert.That(r.Rarity, Is.EqualTo(Rarity.ThreeStar));
            Assert.That(r.Reason, Is.EqualTo(PullReason.HardPity));
            Assert.That(r.PityCount, Is.EqualTo(90));
            Assert.That(pity.PullsSinceThreeStar, Is.EqualTo(0));
        }

        [Test, TC("GACHA-012")]
        public void Pull89_IsNotGuaranteed()
        {
            var banner = TestData.Banner(0, 0, 10000, hardPity: 90);
            var gacha = new GachaService(TestData.Db(new[] { banner }));
            var pity = new PityState { PullsSinceThreeStar = 88 };

            var r = gacha.PullOne(banner, pity, new Pcg32(1));

            Assert.That(r.Rarity, Is.EqualTo(Rarity.OneStar));
            Assert.That(pity.PullsSinceThreeStar, Is.EqualTo(89));
        }

        [Test, TC("GACHA-006")]
        public void PityIsTrackedPerBanner()
        {
            var a = TestData.Banner(0, 0, 10000, id: "b_a");
            var b = TestData.Banner(0, 0, 10000, id: "b_b");
            var s = Session(100000, a, b);
            s.Pull("b_a", 10);
            s.Pull("b_b", 1);
            Assert.That(s.State.Pity["b_a"].PullsSinceThreeStar, Is.EqualTo(10));
            Assert.That(s.State.Pity["b_b"].PullsSinceThreeStar, Is.EqualTo(1));
        }

        [Test, TC("GACHA-007")]
        public void TenPull_LastSlotUpgradedWhenAllOneStar()
        {
            var banner = TestData.Banner(0, 0, 10000);
            var gacha = new GachaService(TestData.Db(new[] { banner }));
            var results = gacha.PullTen(banner, new PityState(), new Pcg32(3));

            Assert.That(results.Take(9).All(r => r.Rarity == Rarity.OneStar), Is.True);
            Assert.That(results[9].Rarity, Is.EqualTo(Rarity.TwoStar));
            Assert.That(results[9].Reason, Is.EqualTo(PullReason.TenPullGuarantee));
        }

        [Test, TC("GACHA-008")]
        public void TenPull_NoUpgradeWhenTwoStarAlreadyPresent()
        {
            var banner = TestData.Banner(0, 10000, 0);
            var gacha = new GachaService(TestData.Db(new[] { banner }));
            var results = gacha.PullTen(banner, new PityState(), new Pcg32(3));
            Assert.That(results.Count(r => r.Reason == PullReason.TenPullGuarantee), Is.EqualTo(0));
        }

        [Test, TC("GACHA-013")]
        public void Duplicates_IncreaseOwnedCount()
        {
            var banner = TestData.Banner(0, 0, 10000, tenGuarantee: false);
            banner.Pool[Rarity.OneStar] = new System.Collections.Generic.List<string> { "one_a" };
            var s = Session(1500, banner);
            s.Pull("b_test", 10);
            Assert.That(s.State.Owned["one_a"], Is.EqualTo(10));
        }

        [Test, TC("GACHA-014")]
        public void InvalidPullCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Session(10000).Pull("b_test", 5));
        }

        // ---------- 확률 검증 (통계) ----------
        // 시드를 고정했으므로 결과는 매번 같다. 즉 "가끔 실패하는(flaky)" 통계 테스트가 아니다.
        // 확률 표나 추첨 코드가 바뀌면 그때 검정이 깨진다.

        [Test, TC("GACHA-009"), Category("Probability")]
        public void BaseRarityDistribution_MatchesTable()
        {
            var banner = TestData.LoadReal().GetBanner("b_standard");
            var rng = new Pcg32(20261111);
            var counts = new long[3];
            const int N = 200000;
            for (int i = 0; i < N; i++)
                counts[(int)GachaService.RollRarity(banner, rng) - 1]++;

            var expected = new[] { Rarity.OneStar, Rarity.TwoStar, Rarity.ThreeStar }
                .Select(r => banner.RateBp[r] / 10000.0).ToArray();
            var chi = ChiSquare.GoodnessOfFit(counts, expected);
            Assert.That(chi.PValue, Is.GreaterThanOrEqualTo(0.001),
                "카이제곱 " + chi.Statistic.ToString("0.00") + ", p=" + chi.PValue.ToString("0.0000"));
        }

        [Test, TC("GACHA-010"), Category("Probability")]
        public void PickupShare_IsHalfOfThreeStars()
        {
            var banner = TestData.Banner(10000, 0, 0, hardPity: 0, pickup: "three_pick", pickupShareBp: 5000);
            var gacha = new GachaService(TestData.Db(new[] { banner }));
            var pity = new PityState();
            var rng = new Pcg32(99);
            const int N = 20000;
            int pickups = 0;
            for (int i = 0; i < N; i++)
                if (gacha.PullOne(banner, pity, rng).IsPickup) pickups++;
            Assert.That(pickups / (double)N, Is.EqualTo(0.5).Within(0.015));
        }

        [Test, TC("GACHA-011"), Category("Probability")]
        public void EffectiveThreeStarRate_MatchesDisclosure()
        {
            var banner = TestData.LoadReal().GetBanner("b_standard");
            var gacha = new GachaService(TestData.LoadReal());
            var pity = new PityState();
            var rng = new Pcg32(777);
            const int N = 300000;
            int three = 0, gap = 0, maxGap = 0;
            for (int i = 0; i < N; i++)
            {
                gap++;
                if (gacha.PullOne(banner, pity, rng).Rarity == Rarity.ThreeStar)
                {
                    three++;
                    maxGap = Math.Max(maxGap, gap);
                    gap = 0;
                }
            }
            Assert.That(maxGap, Is.LessThanOrEqualTo(banner.HardPity), "천장보다 긴 공백이 있으면 안 됨");
            Assert.That(three / (double)N * 10000, Is.EqualTo(banner.DisclosedEffectiveThreeStarBp).Within(10.0),
                "천장 포함 실제 3성 확률(bp)");
        }
    }
}
