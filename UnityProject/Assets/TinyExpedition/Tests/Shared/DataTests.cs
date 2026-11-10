using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.Random;

namespace TinyExpedition.Tests
{
    [Category("Data")]
    public class DataTests
    {
        private static List<DataIssue> Errors(GameDatabase db) =>
            DataValidator.Validate(db).Where(i => i.Level == IssueLevel.Error).ToList();

        [Test, TC("DATA-001")]
        public void RealTables_HaveNoErrors()
        {
            var errors = Errors(TestData.LoadReal());
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test, TC("DATA-002")]
        public void RateSumNot100_IsError()
        {
            var db = TestData.Db(new[] { TestData.Banner(250, 1800, 7949) });
            Assert.That(Errors(db).Any(e => e.Message.Contains("합계가 100%가 아닙니다")), Is.True);
        }

        [Test, TC("DATA-003")]
        public void MissingCharacterReference_IsError()
        {
            var b = TestData.Banner(250, 1800, 7950);
            b.Pool[Rarity.OneStar].Add("ghost");
            Assert.That(Errors(TestData.Db(new[] { b })).Any(e => e.Message.Contains("ghost")), Is.True);
        }

        [Test, TC("DATA-004")]
        public void RarityMismatchInPool_IsError()
        {
            var b = TestData.Banner(250, 1800, 7950);
            b.Pool[Rarity.ThreeStar].Add("one_a");
            Assert.That(Errors(TestData.Db(new[] { b })).Any(e => e.Message.Contains("one_a") && e.Message.Contains("등급")), Is.True);
        }

        [Test, TC("DATA-005")]
        public void PickupNotInThreeStarPool_IsError()
        {
            var b = TestData.Banner(250, 1800, 7950, pickup: "two_a", pickupShareBp: 5000);
            Assert.That(Errors(TestData.Db(new[] { b })).Any(e => e.Message.Contains("픽업")), Is.True);
        }

        [Test, TC("DATA-006")]
        public void StatOutOfRange_IsError()
        {
            var roster = TestData.SmallRoster();
            roster[0].Hp = 0;
            roster[1].CritRate = 101;
            var db = new GameDatabase(roster, new[] { TestData.Banner(250, 1800, 7950) }, new[] { TestData.Stage("s1") });
            var rangeErrors = Errors(db).Where(e => e.Message.Contains("허용 범위")).ToList();
            Assert.That(rangeErrors.Count, Is.EqualTo(2));
        }

        [Test, TC("DATA-007")]
        public void StagePrerequisiteCycle_IsError()
        {
            var stages = new[] { TestData.Stage("root"), TestData.Stage("a", "b"), TestData.Stage("b", "a") };
            var db = TestData.Db(stages: stages);
            Assert.That(Errors(db).Any(e => e.Message.Contains("순환")), Is.True);
        }

        [Test, TC("DATA-008")]
        public void DisclosedEffectiveRateMismatch_IsError()
        {
            var b = TestData.Banner(250, 1800, 7950, hardPity: 90);
            b.DisclosedEffectiveThreeStarBp = 250; // 천장 효과를 빼먹고 기본 확률을 그대로 공시한 실수
            Assert.That(Errors(TestData.Db(new[] { b })).Any(e => e.Message.Contains("공시")), Is.True);

            b.DisclosedEffectiveThreeStarBp = 279; // 계산값 2.785%
            Assert.That(Errors(TestData.Db(new[] { b })).Any(e => e.Message.Contains("공시")), Is.False);
        }

        [Test, TC("DATA-009")]
        public void BrokenJson_ReportsFileName()
        {
            var files = new Dictionary<string, string>
            {
                { GameDatabase.CharactersFile, "[ { \"Id\": \"x\", " },
                { GameDatabase.BannersFile, "[]" },
                { GameDatabase.StagesFile, "[]" },
            };
            var ex = Assert.Throws<GameDataException>(() => GameDatabase.Load(f => files[f]));
            Assert.That(ex.Message, Does.Contain(GameDatabase.CharactersFile));
        }
    }

    [Category("Random")]
    public class RandomTests
    {
        [Test, TC("RNG-001")]
        public void SameSeed_SameSequence()
        {
            var a = new Pcg32(42);
            var b = new Pcg32(42);
            for (int i = 0; i < 1000; i++) Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt()));
        }

        [Test, TC("RNG-001")]
        public void KnownSequence_IsStableAcrossRuntimes()
        {
            // 런타임(Unity Mono / .NET)이 바뀌어도 같은 수열이어야 기존 버그 리포트의 시드를 재현할 수 있다.
            var r = new Pcg32(42);
            var first = new[] { r.NextUInt(), r.NextUInt(), r.NextUInt() };
            Assert.That(first, Is.EqualTo(PcgGolden.Seed42First3));
        }

        [TestCase(1), TestCase(2), TestCase(7), TestCase(10000)]
        [TC("RNG-002")]
        public void NextInt_StaysInRange(int n)
        {
            var r = new Pcg32(7);
            for (int i = 0; i < 100000; i++)
            {
                int v = r.NextInt(n);
                Assert.That(v >= 0 && v < n, "범위 밖: " + v);
            }
        }

        [Test, TC("RNG-003")]
        public void NextInt_Zero_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new Pcg32(1).NextInt(0));
        }
    }

    internal static class PcgGolden
    {
        // PCG 공식 레퍼런스 구현(pcg32-demo, seed 42 / stream 54)의 첫 출력값.
        // 구현이 레퍼런스와 같은지, 그리고 dotnet·Unity 어디서든 같은 값인지 함께 확인한다.
        public static readonly uint[] Seed42First3 = { 0xa15c02b7u, 0x7b47f409u, 0xba1d3330u };
    }
}
