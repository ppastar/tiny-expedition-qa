using System;
using System.Collections.Generic;
using System.Linq;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.Random;

namespace TinyExpedition.Core.Gacha
{
    /// <summary>배너별 천장 진행도. 세이브에 그대로 저장된다.</summary>
    public sealed class PityState
    {
        /// <summary>마지막 3성 이후 뽑은 횟수.</summary>
        public int PullsSinceThreeStar { get; set; }
    }

    public enum PullReason
    {
        /// <summary>기본 확률로 나옴.</summary>
        Normal,
        /// <summary>천장으로 3성 확정.</summary>
        HardPity,
        /// <summary>10연차 마지막 장 2성 이상 보정.</summary>
        TenPullGuarantee
    }

    public sealed class PullResult
    {
        public string CharacterId { get; }
        public Rarity Rarity { get; }
        public bool IsPickup { get; }
        public PullReason Reason { get; }
        /// <summary>이 결과가 나온 시점의 천장 카운트(1부터, 이 뽑기 포함).</summary>
        public int PityCount { get; }

        public PullResult(string characterId, Rarity rarity, bool isPickup, PullReason reason, int pityCount)
        {
            CharacterId = characterId; Rarity = rarity; IsPickup = isPickup; Reason = reason; PityCount = pityCount;
        }
    }

    /// <summary>
    /// 모집(가챠) 규칙.
    /// 1) 등급을 기본 확률로 뽑는다. 2) HardPity번째 뽑기면 3성 확정. 3) 10연차 마지막 장은 2성 이상 확정.
    /// 4) 3성이면 PickupShareBp 확률로 픽업, 아니면 나머지 3성 중 균등. 5) 같은 등급 안에서는 균등.
    /// </summary>
    public sealed class GachaService
    {
        public const int TenPullCount = 10;
        private readonly GameDatabase _db;

        public GachaService(GameDatabase db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public PullResult PullOne(BannerDef banner, PityState pity, IRandom rng)
        {
            return PullInternal(banner, pity, rng, applyTenPullGuarantee: false);
        }

        public IReadOnlyList<PullResult> PullTen(BannerDef banner, PityState pity, IRandom rng)
        {
            var results = new List<PullResult>(TenPullCount);
            for (int i = 0; i < TenPullCount; i++)
            {
                bool lastSlot = i == TenPullCount - 1;
                bool needGuarantee = banner.TenPullGuarantee && lastSlot && results.All(r => r.Rarity == Rarity.OneStar);
                results.Add(PullInternal(banner, pity, rng, needGuarantee));
            }
            return results;
        }

        private PullResult PullInternal(BannerDef banner, PityState pity, IRandom rng, bool applyTenPullGuarantee)
        {
            int count = pity.PullsSinceThreeStar + 1;
            Rarity rarity;
            PullReason reason = PullReason.Normal;

            if (banner.HardPity > 0 && count >= banner.HardPity)
            {
                rarity = Rarity.ThreeStar;
                reason = PullReason.HardPity;
            }
            else
            {
                rarity = RollRarity(banner, rng);
                if (applyTenPullGuarantee && rarity == Rarity.OneStar)
                {
                    rarity = Rarity.TwoStar;
                    reason = PullReason.TenPullGuarantee;
                }
            }

            pity.PullsSinceThreeStar = rarity == Rarity.ThreeStar ? 0 : count;

            bool isPickup = false;
            string id;
            if (rarity == Rarity.ThreeStar && banner.PickupId != null)
            {
                var others = PoolOf(banner, Rarity.ThreeStar).Where(x => x != banner.PickupId).ToList();
                isPickup = others.Count == 0 || rng.NextInt(10000) < banner.PickupShareBp;
                id = isPickup ? banner.PickupId : others[rng.NextInt(others.Count)];
            }
            else
            {
                var pool = PoolOf(banner, rarity);
                id = pool[rng.NextInt(pool.Count)];
            }

            return new PullResult(id, rarity, isPickup, reason, count);
        }

        /// <summary>천장·보정 없이 기본 확률로 등급만 뽑는다. 확률 검증 도구도 이 함수를 쓴다.</summary>
        public static Rarity RollRarity(BannerDef banner, IRandom rng)
        {
            int roll = rng.NextInt(10000);
            int acc = 0;
            // 높은 등급부터 누적
            foreach (var r in new[] { Rarity.ThreeStar, Rarity.TwoStar, Rarity.OneStar })
            {
                if (!banner.RateBp.TryGetValue(r, out int bp)) continue;
                acc += bp;
                if (roll < acc) return r;
            }
            throw new InvalidOperationException("배너 " + banner.Id + "의 확률 합계가 100% 미만입니다.");
        }

        private static List<string> PoolOf(BannerDef banner, Rarity rarity)
        {
            if (banner.Pool.TryGetValue(rarity, out var pool) && pool.Count > 0) return pool;
            throw new InvalidOperationException("배너 " + banner.Id + "에 " + rarity + " 후보가 없습니다.");
        }
    }

    public static class GachaMath
    {
        /// <summary>
        /// 천장 N회, 기본 3성 확률 p일 때 장기적으로 기대되는 3성 실제 획득률.
        /// 3성 1개당 기대 뽑기 수 E = (1-(1-p)^N)/p 이므로 실제 확률 = 1/E.
        /// </summary>
        public static double EffectiveThreeStarRate(double p, int hardPity)
        {
            if (p <= 0) return hardPity > 0 ? 1.0 / hardPity : 0;
            if (hardPity <= 0) return p;
            return p / (1 - Math.Pow(1 - p, hardPity));
        }
    }
}
