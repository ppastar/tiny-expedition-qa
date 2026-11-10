using System.Diagnostics;
using System.Text;
using TinyExpedition.Core.Analysis;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.Gacha;
using TinyExpedition.Core.Random;

namespace TinyExpedition.QaTool;

/// <summary>
/// 확률 검증기. 게임과 똑같은 GachaService 코드를 대량으로 돌려
/// "공시한 확률표대로 실제로 나오는가"를 통계로 확인한다.
/// </summary>
public static class GachaVerifyCommand
{
    private const double PValueThreshold = 0.001; // 이보다 작으면 "공시와 다르다"고 판정
    private const double Sigma = 4.0;             // 실제 획득률 허용 범위 (표준오차 배수)

    private sealed class Check(string name, bool pass, string detail)
    {
        public string Name { get; } = name;
        public bool Pass { get; } = pass;
        public string Detail { get; } = detail;
    }

    public static int Run(Options o)
    {
        var db = Repo.LoadDb(o);
        long pulls = o.GetLong("pulls", 1_000_000);
        ulong seed = (ulong)o.GetLong("seed", 1);
        var which = o.Get("banner", "all");
        var banners = which == "all" ? db.Banners.ToList() : new List<BannerDef> { db.GetBanner(which) };

        var md = new StringBuilder();
        md.AppendLine("# 모집 확률 검증 리포트");
        md.AppendLine();
        md.AppendLine($"- 실행: {DateTime.Now:yyyy-MM-dd HH:mm} · 배너당 {pulls:N0}회 · 시드 {seed}");
        md.AppendLine($"- 판정 기준: 카이제곱 p ≥ {PValueThreshold} · 실제 획득률은 공시값 ± {Sigma}σ 이내 · 천장 간격 ≤ 천장");
        md.AppendLine();

        bool allPass = true;
        foreach (var banner in banners)
        {
            var sw = Stopwatch.StartNew();
            var checks = VerifyBanner(db, banner, pulls, seed, md);
            sw.Stop();
            md.AppendLine("| 검사 | 결과 | 내용 |");
            md.AppendLine("|---|---|---|");
            foreach (var c in checks) md.AppendLine($"| {c.Name} | {(c.Pass ? "✅ 통과" : "❌ 실패")} | {c.Detail} |");
            md.AppendLine();
            md.AppendLine($"_소요 {sw.Elapsed.TotalSeconds:0.0}초_");
            md.AppendLine();
            allPass &= checks.All(c => c.Pass);

            Console.WriteLine($"[{banner.Id}] {banner.Name}");
            foreach (var c in checks) Console.WriteLine($"  {(c.Pass ? "PASS" : "FAIL")}  {c.Name} — {c.Detail}");
        }

        md.AppendLine(allPass ? "**종합: 모든 검사 통과**" : "**종합: 실패한 검사가 있습니다**");
        var outPath = o.Get("out");
        if (outPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, md.ToString(), new UTF8Encoding(false));
            Console.WriteLine("리포트: " + Path.GetFullPath(outPath));
        }
        Console.WriteLine(allPass ? "종합: 통과" : "종합: 실패");
        return allPass ? 0 : 1;
    }

    private static List<Check> VerifyBanner(GameDatabase db, BannerDef banner, long pulls, ulong seed, StringBuilder md)
    {
        var checks = new List<Check>();
        var rarities = new[] { Rarity.ThreeStar, Rarity.TwoStar, Rarity.OneStar };
        md.AppendLine($"## {banner.Name} (`{banner.Id}`)");
        md.AppendLine();

        // 1) 기본 등급 확률: 천장·10연 보정 없이 등급 추첨만
        var rng = new Pcg32(seed);
        var baseCounts = new long[rarities.Length];
        for (long i = 0; i < pulls; i++)
            baseCounts[Array.IndexOf(rarities, GachaService.RollRarity(banner, rng))]++;
        var expected = rarities.Select(r => banner.RateBp.GetValueOrDefault(r) / 10000.0).ToArray();
        var chi = ChiSquare.GoodnessOfFit(baseCounts, expected);

        md.AppendLine("### 1. 기본 등급 확률 (천장 제외)");
        md.AppendLine();
        md.AppendLine("| 등급 | 공시 | 관측 | 95% 신뢰구간 | 횟수 |");
        md.AppendLine("|---|---:|---:|---:|---:|");
        for (int i = 0; i < rarities.Length; i++)
        {
            double p = baseCounts[i] / (double)pulls;
            var (lo, hi) = Wilson(baseCounts[i], pulls);
            md.AppendLine($"| {Label(rarities[i])} | {expected[i]:0.00%} | {p:0.000%} | {lo:0.000%} ~ {hi:0.000%} | {baseCounts[i]:N0} |");
        }
        md.AppendLine();
        checks.Add(new Check("기본 등급 확률", chi.PValue >= PValueThreshold,
            $"χ²={chi.Statistic:0.00} (자유도 {chi.DegreesOfFreedom}), p={chi.PValue:0.0000}"));

        // 2) 실제 모집(천장 포함): 등급 내 캐릭터 분포, 실제 3성 획득률, 천장 간격
        rng = new Pcg32(seed + 1);
        var gacha = new GachaService(db);
        var pity = new PityState();
        var perChar = new Dictionary<string, long>();
        long three = 0, gap = 0, maxGap = 0, pityHits = 0;
        var gapHistogram = new long[(Math.Max(banner.HardPity, 100) + 9) / 10];
        for (long i = 0; i < pulls; i++)
        {
            var r = gacha.PullOne(banner, pity, rng);
            perChar[r.CharacterId] = perChar.GetValueOrDefault(r.CharacterId) + 1;
            gap++;
            if (r.Rarity == Rarity.ThreeStar)
            {
                three++;
                if (r.Reason == PullReason.HardPity) pityHits++;
                maxGap = Math.Max(maxGap, gap);
                gapHistogram[Math.Min(gapHistogram.Length - 1, (gap - 1) / 10)]++;
                gap = 0;
            }
        }

        md.AppendLine("### 2. 등급 안 캐릭터 분포 (실제 모집)");
        md.AppendLine();
        md.AppendLine("| 등급 | 캐릭터 | 기대 비율(등급 내) | 관측 비율 | 횟수 |");
        md.AppendLine("|---|---|---:|---:|---:|");
        foreach (var rarity in rarities)
        {
            if (!banner.Pool.TryGetValue(rarity, out var pool) || pool.Count == 0) continue;
            var ids = pool.Distinct().ToList();
            var shares = ExpectedShares(banner, rarity, ids);
            var counts = ids.Select(id => perChar.GetValueOrDefault(id)).ToArray();
            long total = counts.Sum();
            for (int i = 0; i < ids.Count; i++)
            {
                var name = db.HasCharacter(ids[i]) ? db.GetCharacter(ids[i]).Name : ids[i];
                md.AppendLine($"| {Label(rarity)} | {name}{(ids[i] == banner.PickupId ? " (픽업)" : "")} | {shares[i]:0.00%} | {(total == 0 ? 0 : counts[i] / (double)total):0.00%} | {counts[i]:N0} |");
            }
            if (total > 0 && ids.Count > 1)
            {
                var c = ChiSquare.GoodnessOfFit(counts, shares);
                checks.Add(new Check($"{Label(rarity)} 캐릭터 분포", c.PValue >= PValueThreshold, $"χ²={c.Statistic:0.00}, p={c.PValue:0.0000}"));
            }
        }
        md.AppendLine();

        double observedRate = three / (double)pulls;
        double theoretical = GachaMath.EffectiveThreeStarRate(banner.RateBp.GetValueOrDefault(Rarity.ThreeStar) / 10000.0, banner.HardPity);
        double se = Math.Sqrt(theoretical * (1 - theoretical) / pulls);
        md.AppendLine("### 3. 천장 포함 실제 3성 획득률");
        md.AppendLine();
        md.AppendLine($"- 관측 {observedRate:0.000%} ({three:N0}회) · 이론값 {theoretical:0.000%}" +
                      (banner.DisclosedEffectiveThreeStarBp > 0 ? $" · 공시값 {banner.DisclosedEffectiveThreeStarBp / 10000.0:0.00%}" : " · 공시값 없음"));
        md.AppendLine($"- 천장으로 나온 3성 {pityHits:N0}회 ({(three == 0 ? 0 : pityHits / (double)three):0.0%}) · 가장 긴 3성 간격 {maxGap}회");
        md.AppendLine();
        md.AppendLine("3성까지 걸린 횟수 분포:");
        md.AppendLine();
        md.AppendLine("```");
        long maxBucket = Math.Max(1, gapHistogram.Max());
        for (int i = 0; i < gapHistogram.Length; i++)
        {
            if (i * 10 >= Math.Max(banner.HardPity, 1) && gapHistogram[i] == 0) continue;
            int bar = (int)(gapHistogram[i] * 40 / maxBucket);
            md.AppendLine($"{i * 10 + 1,3}~{i * 10 + 10,3}회 | {new string('█', bar)} {gapHistogram[i]:N0}");
        }
        md.AppendLine("```");
        md.AppendLine();

        if (banner.DisclosedEffectiveThreeStarBp > 0)
        {
            double disclosed = banner.DisclosedEffectiveThreeStarBp / 10000.0;
            // 공시값은 0.01% 단위 반올림이므로 그만큼 여유를 더한다
            double tolerance = Sigma * se + 0.00005;
            checks.Add(new Check("공시한 실제 3성 획득률", Math.Abs(observedRate - disclosed) <= tolerance,
                $"관측 {observedRate:0.000%} vs 공시 {disclosed:0.00%} (허용 ±{tolerance:0.000%})"));
        }
        if (banner.HardPity > 0)
            checks.Add(new Check("천장 동작", maxGap <= banner.HardPity, $"가장 긴 간격 {maxGap}회 / 천장 {banner.HardPity}회"));

        // 4) 10연 보정: 모든 10연에 2성 이상이 1장 이상
        if (banner.TenPullGuarantee)
        {
            rng = new Pcg32(seed + 2);
            pity = new PityState();
            long tens = Math.Max(1, pulls / 10), violations = 0;
            for (long i = 0; i < tens; i++)
                if (gacha.PullTen(banner, pity, rng).All(r => r.Rarity == Rarity.OneStar)) violations++;
            checks.Add(new Check("10연 2성 이상 보정", violations == 0, $"10연 {tens:N0}번 중 위반 {violations}번"));
        }
        return checks;
    }

    /// <summary>등급 안에서 캐릭터별 기대 비율. 3성 픽업이 있으면 픽업이 PickupShareBp, 나머지가 남은 비율을 균등.</summary>
    private static double[] ExpectedShares(BannerDef banner, Rarity rarity, List<string> ids)
    {
        if (rarity == Rarity.ThreeStar && banner.PickupId != null && ids.Contains(banner.PickupId) && ids.Count > 1)
        {
            double pick = banner.PickupShareBp / 10000.0;
            return ids.Select(id => id == banner.PickupId ? pick : (1 - pick) / (ids.Count - 1)).ToArray();
        }
        return ids.Select(_ => 1.0 / ids.Count).ToArray();
    }

    private static (double lo, double hi) Wilson(long k, long n, double z = 1.96)
    {
        double p = k / (double)n, z2 = z * z;
        double center = (p + z2 / (2 * n)) / (1 + z2 / n);
        double half = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / (1 + z2 / n);
        return (center - half, center + half);
    }

    private static string Label(Rarity r) => r switch
    {
        Rarity.ThreeStar => "★★★",
        Rarity.TwoStar => "★★",
        _ => "★",
    };
}
