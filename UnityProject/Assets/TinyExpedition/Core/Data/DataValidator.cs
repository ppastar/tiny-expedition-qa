using System;
using System.Collections.Generic;
using System.Linq;

namespace TinyExpedition.Core.Data
{
    public enum IssueLevel { Error, Warning }

    public sealed class DataIssue
    {
        public IssueLevel Level { get; }
        public string Table { get; }
        public string RowId { get; }
        public string Message { get; }

        public DataIssue(IssueLevel level, string table, string rowId, string message)
        {
            Level = level; Table = table; RowId = rowId; Message = message;
        }

        public override string ToString() => "[" + Level + "] " + Table + "/" + RowId + ": " + Message;
    }

    /// <summary>
    /// 기획 데이터 정합성 검사. 빌드 전에(그리고 CI에서) 돌려 잘못된 테이블이 게임에 들어가는 걸 막는다.
    /// </summary>
    public static class DataValidator
    {
        public const int MaxStat = 99999;

        public static List<DataIssue> Validate(GameDatabase db)
        {
            var issues = new List<DataIssue>();
            ValidateCharacters(db, issues);
            ValidateBanners(db, issues);
            ValidateStages(db, issues);
            return issues;
        }

        private static void ValidateCharacters(GameDatabase db, List<DataIssue> issues)
        {
            const string T = "characters";
            foreach (var dup in db.Characters.GroupBy(c => c.Id).Where(g => g.Count() > 1))
                issues.Add(Err(T, dup.Key, "id가 " + dup.Count() + "번 중복됩니다."));

            foreach (var c in db.Characters)
            {
                if (string.IsNullOrWhiteSpace(c.Id)) issues.Add(Err(T, "(빈 id)", "id가 비어 있습니다."));
                if (string.IsNullOrWhiteSpace(c.Name)) issues.Add(Err(T, c.Id, "이름이 비어 있습니다."));
                if (!Enum.IsDefined(typeof(Rarity), c.Rarity)) issues.Add(Err(T, c.Id, "알 수 없는 등급: " + (int)c.Rarity));
                CheckRange(issues, T, c.Id, "hp", c.Hp, 1, MaxStat);
                CheckRange(issues, T, c.Id, "atk", c.Atk, 1, MaxStat);
                CheckRange(issues, T, c.Id, "def", c.Def, 0, MaxStat);
                CheckRange(issues, T, c.Id, "spd", c.Spd, 1, 999);
                CheckRange(issues, T, c.Id, "critRate", c.CritRate, 0, 100);
            }
        }

        private static void ValidateBanners(GameDatabase db, List<DataIssue> issues)
        {
            const string T = "banners";
            foreach (var dup in db.Banners.GroupBy(b => b.Id).Where(g => g.Count() > 1))
                issues.Add(Err(T, dup.Key, "id가 중복됩니다."));

            foreach (var b in db.Banners)
            {
                int sum = b.RateBp.Values.Sum();
                if (sum != 10000)
                    issues.Add(Err(T, b.Id, "등급 확률 합계가 100%가 아닙니다: " + (sum / 100.0) + "%"));

                foreach (var kv in b.RateBp)
                {
                    if (kv.Value < 0) issues.Add(Err(T, b.Id, kv.Key + " 확률이 음수입니다."));
                    if (kv.Value > 0 && (!b.Pool.TryGetValue(kv.Key, out var pool) || pool.Count == 0))
                        issues.Add(Err(T, b.Id, kv.Key + " 확률이 있는데 후보 캐릭터가 없습니다."));
                }

                foreach (var kv in b.Pool)
                {
                    foreach (var id in kv.Value)
                    {
                        if (!db.HasCharacter(id))
                            issues.Add(Err(T, b.Id, "없는 캐릭터를 참조합니다: " + id));
                        else if (db.GetCharacter(id).Rarity != kv.Key)
                            issues.Add(Err(T, b.Id, id + "의 등급(" + db.GetCharacter(id).Rarity + ")이 풀 등급(" + kv.Key + ")과 다릅니다."));
                    }
                    foreach (var dup in kv.Value.GroupBy(x => x).Where(g => g.Count() > 1))
                        issues.Add(new DataIssue(IssueLevel.Warning, T, b.Id, kv.Key + " 풀에 " + dup.Key + "가 중복되어 확률이 의도와 달라집니다."));
                }

                if (b.PickupId != null)
                {
                    if (!b.Pool.TryGetValue(Rarity.ThreeStar, out var three) || !three.Contains(b.PickupId))
                        issues.Add(Err(T, b.Id, "픽업 캐릭터 " + b.PickupId + "가 3성 풀에 없습니다."));
                    CheckRange(issues, T, b.Id, "pickupShareBp", b.PickupShareBp, 0, 10000);
                }

                if (b.CostPerPull <= 0) issues.Add(Err(T, b.Id, "1회 비용은 1 이상이어야 합니다."));
                if (b.HardPity < 0) issues.Add(Err(T, b.Id, "천장 값이 음수입니다."));
                if (b.HardPity > 0 && b.DisclosedEffectiveThreeStarBp == 0)
                    issues.Add(new DataIssue(IssueLevel.Warning, T, b.Id, "천장이 있는데 천장 포함 실제 3성 확률이 공시되지 않았습니다."));
                if (b.DisclosedEffectiveThreeStarBp > 0 && b.RateBp.TryGetValue(Rarity.ThreeStar, out int threeBp))
                {
                    // 공시한 '천장 포함 실제 확률'이 기본 확률·천장으로 계산한 값과 맞는지 (반올림 오차 1bp 허용)
                    double expectedBp = Gacha.GachaMath.EffectiveThreeStarRate(threeBp / 10000.0, b.HardPity) * 10000;
                    if (Math.Abs(expectedBp - b.DisclosedEffectiveThreeStarBp) > 1.0)
                        issues.Add(Err(T, b.Id, "공시한 천장 포함 3성 확률 " + (b.DisclosedEffectiveThreeStarBp / 100.0)
                            + "%가 계산값 " + (expectedBp / 100.0).ToString("0.00") + "%와 다릅니다."));
                }
            }
        }

        private static void ValidateStages(GameDatabase db, List<DataIssue> issues)
        {
            const string T = "stages";
            var ids = new HashSet<string>();
            foreach (var s in db.Stages)
            {
                if (!ids.Add(s.Id)) issues.Add(Err(T, s.Id, "id가 중복됩니다."));
                if (s.Enemies.Count == 0) issues.Add(Err(T, s.Id, "적이 없습니다."));
                if (s.Enemies.Count > 5) issues.Add(Err(T, s.Id, "적은 최대 5마리입니다."));
                if (s.FirstClearGems < 0) issues.Add(Err(T, s.Id, "보상이 음수입니다."));
                foreach (var e in s.Enemies)
                {
                    CheckRange(issues, T, s.Id, e.Name + ".hp", e.Hp, 1, MaxStat);
                    CheckRange(issues, T, s.Id, e.Name + ".atk", e.Atk, 0, MaxStat);
                    CheckRange(issues, T, s.Id, e.Name + ".def", e.Def, 0, MaxStat);
                    CheckRange(issues, T, s.Id, e.Name + ".spd", e.Spd, 1, 999);
                }
            }

            var byId = db.Stages.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
            if (db.Stages.Count > 0 && db.Stages.All(s => s.RequiresStageId != null))
                issues.Add(Err(T, "-", "처음부터 열려 있는 스테이지가 없습니다."));

            foreach (var s in db.Stages)
            {
                if (s.RequiresStageId != null && !byId.ContainsKey(s.RequiresStageId))
                    issues.Add(Err(T, s.Id, "선행 스테이지가 없습니다: " + s.RequiresStageId));

                // 선행 관계 순환: 순환에 걸린 스테이지는 영원히 열리지 않는다.
                var seen = new HashSet<string>();
                var cur = s;
                while (cur != null && cur.RequiresStageId != null)
                {
                    if (!seen.Add(cur.Id))
                    {
                        issues.Add(Err(T, s.Id, "선행 스테이지가 순환합니다."));
                        break;
                    }
                    byId.TryGetValue(cur.RequiresStageId, out cur);
                }
            }
        }

        private static DataIssue Err(string table, string id, string message) =>
            new DataIssue(IssueLevel.Error, table, id, message);

        private static void CheckRange(List<DataIssue> issues, string table, string id, string field, int value, int min, int max)
        {
            if (value < min || value > max)
                issues.Add(Err(table, id, field + " 값 " + value + "이(가) 허용 범위 " + min + "~" + max + "를 벗어났습니다."));
        }
    }
}
