using System;
using System.Collections.Generic;
using System.IO;
using TinyExpedition.Core.Data;

namespace TinyExpedition.Tests
{
    /// <summary>TC 번호를 테스트에 붙이는 속성. 리포트 도구가 이 값으로 TC 문서와 결과를 잇는다.</summary>
    public sealed class TCAttribute : NUnit.Framework.PropertyAttribute
    {
        public TCAttribute(string id) : base("TC", id) { }
    }

    public static class TestData
    {
        private const string Relative = "Assets/StreamingAssets/GameData";

        /// <summary>
        /// 실제 기획 데이터 폴더를 찾는다.
        /// Unity(현재 경로 = 프로젝트 루트)와 dotnet test(bin 폴더) 둘 다에서 동작하도록 위로 올라가며 찾는다.
        /// </summary>
        public static string GameDataDir()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    foreach (var candidate in new[] { Path.Combine(dir.FullName, Relative), Path.Combine(dir.FullName, "UnityProject", Relative) })
                        if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("GameData 폴더를 찾지 못했습니다.");
        }

        public static GameDatabase LoadReal()
        {
            var dir = GameDataDir();
            return GameDatabase.Load(f => File.ReadAllText(Path.Combine(dir, f)));
        }

        public static CharacterDef Char(string id, Rarity rarity, int hp = 500, int atk = 50, int def = 20, int spd = 100, int crit = 0) =>
            new CharacterDef { Id = id, Name = id, Rarity = rarity, Hp = hp, Atk = atk, Def = def, Spd = spd, CritRate = crit };

        /// <summary>테스트용 미니 DB: 등급마다 캐릭터 2명.</summary>
        public static List<CharacterDef> SmallRoster() => new List<CharacterDef>
        {
            Char("one_a", Rarity.OneStar), Char("one_b", Rarity.OneStar),
            Char("two_a", Rarity.TwoStar), Char("two_b", Rarity.TwoStar),
            Char("three_a", Rarity.ThreeStar), Char("three_pick", Rarity.ThreeStar),
        };

        /// <summary>확률을 원하는 대로 바꿔 경계 상황을 강제로 만드는 배너.</summary>
        public static BannerDef Banner(int threeBp, int twoBp, int oneBp, int hardPity = 90, bool tenGuarantee = true,
            string pickup = null, int pickupShareBp = 0, string id = "b_test")
        {
            return new BannerDef
            {
                Id = id,
                Name = id,
                CostPerPull = 150,
                RateBp = new Dictionary<Rarity, int> { { Rarity.ThreeStar, threeBp }, { Rarity.TwoStar, twoBp }, { Rarity.OneStar, oneBp } },
                Pool = new Dictionary<Rarity, List<string>>
                {
                    { Rarity.ThreeStar, new List<string> { "three_a", "three_pick" } },
                    { Rarity.TwoStar, new List<string> { "two_a", "two_b" } },
                    { Rarity.OneStar, new List<string> { "one_a", "one_b" } },
                },
                PickupId = pickup,
                PickupShareBp = pickupShareBp,
                HardPity = hardPity,
                TenPullGuarantee = tenGuarantee,
            };
        }

        public static StageDef Stage(string id, string requires = null, int gems = 100, int enemyHp = 100, int enemyAtk = 10) =>
            new StageDef
            {
                Id = id,
                Name = id,
                RequiresStageId = requires,
                FirstClearGems = gems,
                Enemies = new List<EnemyDef> { new EnemyDef { Name = "dummy", Hp = enemyHp, Atk = enemyAtk, Def = 0, Spd = 50 } },
            };

        public static GameDatabase Db(IEnumerable<BannerDef> banners = null, IEnumerable<StageDef> stages = null) =>
            new GameDatabase(SmallRoster(), banners ?? new[] { Banner(250, 1800, 7950) }, stages ?? new[] { Stage("s1"), Stage("s2", "s1") });
    }
}
