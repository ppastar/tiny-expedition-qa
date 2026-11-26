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

        [Test, TC("GACHA-017")]
        public void TenPull_CrossingHardPity_ResetsMidPull()
        {
            // 천장 카운트 85에서 10연: 5번째 장이 90번째 모집 → 3성 확정, 이후 카운트는 1부터 다시
            var banner = TestData.Banner(0, 0, 10000, hardPity: 90);
            var gacha = new GachaService(TestData.Db(new[] { banner }));
            var pity = new PityState { PullsSinceThreeStar = 85 };

            var results = gacha.PullTen(banner, pity, new Pcg32(5));

            Assert.That(results.Select(r => r.PityCount), Is.EqualTo(new[] { 86, 87, 88, 89, 90, 1, 2, 3, 4, 5 }));
            Assert.That(results[4].Rarity, Is.EqualTo(Rarity.ThreeStar));
            Assert.That(results[4].Reason, Is.EqualTo(PullReason.HardPity));
            Assert.That(results.Where((r, i) => i != 4).All(r => r.Rarity != Rarity.ThreeStar), Is.True,
                "천장 3성은 5번째 장 하나뿐이어야 함");
            Assert.That(pity.PullsSinceThreeStar, Is.EqualTo(5), "10연 뒤 카운트 = 천장 이후 5장");
        }

        [Test, TC("GACHA-018")]
        public void TenPull_LastSlotHardPity_WinsOverGuarantee()
        {
            // 천장 카운트 80에서 10연: 앞 9장은 1성(2·3성 0%), 10번째 장이 90번째 모집
            // → 10연 보정(2성)이 아니라 천장(3성)이 적용되어야 함
            var banner = TestData.Banner(0, 0, 10000, hardPity: 90);
            var gacha = new GachaService(TestData.Db(new[] { banner }));
            var pity = new PityState { PullsSinceThreeStar = 80 };

            var results = gacha.PullTen(banner, pity, new Pcg32(7));

            Assert.That(results.Take(9).All(r => r.Rarity == Rarity.OneStar && r.Reason == PullReason.Normal), Is.True);
            Assert.That(results[9].PityCount, Is.EqualTo(90));
            Assert.That(results[9].Rarity, Is.EqualTo(Rarity.ThreeStar));
            Assert.That(results[9].Reason, Is.EqualTo(PullReason.HardPity), "보정(TenPullGuarantee)이 아니라 천장");
            Assert.That(pity.PullsSinceThreeStar, Is.EqualTo(0));
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

        [Test, TC("GACHA-015")]
        public void BrokenBanner_FailsWithoutTakingGems()
        {
            // 2성 확률 100%인데 2성 후보가 비어 있는 잘못된 배너 (검증기를 거치지 않고 들어왔다고 가정)
            var banner = TestData.Banner(0, 10000, 0);
            banner.Pool[Rarity.TwoStar].Clear();
            var s = Session(3000, banner);
            s.State.Pity["b_test"] = new PityState { PullsSinceThreeStar = 5 };

            var r = s.Pull("b_test", 10);

            Assert.That(r.Error, Is.EqualTo(ActionError.DataError));
            Assert.That(s.State.Gems, Is.EqualTo(3000), "실패한 모집은 보석을 가져가면 안 됨");
            Assert.That(s.State.Pity["b_test"].PullsSinceThreeStar, Is.EqualTo(5), "천장 카운트도 그대로");
            Assert.That(s.State.Owned, Is.Empty);
        }

        [Test, TC("GACHA-015")]
        public void TenPullGuaranteeWithoutTwoStarPool_FailsWithoutSideEffects()
        {
            // BUG-002 배너가 검사기를 거치지 않고 들어왔을 때: 보정이 걸린 10연은 DataError로 실패하고 상태는 그대로여야 한다.
            var banner = TestData.Banner(250, 0, 9750);
            banner.Pool[Rarity.TwoStar].Clear();
            int failed = 0;
            for (ulong seed = 1; seed <= 100; seed++)
            {
                var s = Session(3000, banner);
                s.Reseed(seed);
                s.State.Pity["b_test"] = new PityState { PullsSinceThreeStar = 5 };

                var r = s.Pull("b_test", 10);
                if (r.Ok) continue;

                failed++;
                Assert.That(r.Error, Is.EqualTo(ActionError.DataError), "시드 " + seed);
                Assert.That(s.State.Gems, Is.EqualTo(3000), "시드 " + seed + ": 보석 그대로");
                Assert.That(s.State.Pity["b_test"].PullsSinceThreeStar, Is.EqualTo(5), "시드 " + seed + ": 천장 그대로");
                Assert.That(s.State.Owned, Is.Empty, "시드 " + seed + ": 보유 그대로");
            }
            Assert.That(failed, Is.GreaterThan(0), "보정이 걸리는 시드가 하나도 없으면 이 테스트는 아무것도 확인하지 않음");
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

        [Test, TC("GACHA-016"), Category("Probability")]
        public void HardPityThreeStar_AlsoRollsPickupShare()
        {
            // 천장으로 나온 3성도 픽업 50% 판정을 한다 (천장 = 픽업 확정이 아님). 매번 카운트 89에서 1회 모집.
            var banner = TestData.Banner(0, 0, 10000, hardPity: 90, pickup: "three_pick", pickupShareBp: 5000);
            var gacha = new GachaService(TestData.Db(new[] { banner }));
            var rng = new Pcg32(2026);
            const int N = 20000;
            int pickups = 0;
            for (int i = 0; i < N; i++)
            {
                var r = gacha.PullOne(banner, new PityState { PullsSinceThreeStar = 89 }, rng);
                Assert.That(r.Reason, Is.EqualTo(PullReason.HardPity));
                if (r.IsPickup) pickups++;
            }
            Assert.That(pickups / (double)N, Is.EqualTo(0.5).Within(0.015), "천장 3성 중 픽업 비율");
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
