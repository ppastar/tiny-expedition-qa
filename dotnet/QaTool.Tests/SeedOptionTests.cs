using System;
using System.IO;
using NUnit.Framework;

namespace TinyExpedition.QaTool.Tests
{
    /// <summary>TC 번호를 테스트에 붙이는 속성 (Tests/Shared의 TCAttribute와 같은 "TC" 속성 이름).</summary>
    public sealed class TCAttribute : PropertyAttribute
    {
        public TCAttribute(string id) : base("TC", id) { }
    }

    /// <summary>
    /// BUG-003 회귀: 게임 시드는 ulong 전체 범위인데 qa-tool이 --seed를 long으로 읽어
    /// long 최대값보다 큰 시드로는 리플레이를 못 했다.
    /// </summary>
    public class SeedOptionTests
    {
        private static Options Opts(params string[] args) => Options.Parse(args);

        // 경계: 0 / long 최대 / long 최대 + 1 / ulong 최대
        [Test, TC("TOOL-001")]
        [TestCase("0", 0UL)]
        [TestCase("1", 1UL)]
        [TestCase("9223372036854775807", 9223372036854775807UL)]
        [TestCase("9223372036854775808", 9223372036854775808UL)]
        [TestCase("18446744073709551615", 18446744073709551615UL)]
        public void Seed_ReadsWholeULongRange(string text, ulong expected)
        {
            Assert.That(Opts("--seed", text).GetULong("seed", 1), Is.EqualTo(expected));
        }

        [Test, TC("TOOL-001")]
        public void Seed_Missing_UsesFallback()
        {
            Assert.That(Opts("--stage", "T-1").GetULong("seed", 7), Is.EqualTo(7UL));
        }

        // 음수는 예전에 (ulong) 캐스팅으로 조용히 큰 시드가 됐다 (-1 → 18446744073709551615)
        [Test, TC("TOOL-002")]
        [TestCase("-1")]
        [TestCase("-9223372036854775808")]
        [TestCase("18446744073709551616")] // ulong 최대 + 1
        [TestCase("abc")]
        [TestCase("12abc")]
        [TestCase("1.5")]
        [TestCase("0x10")]
        [TestCase("")]
        [TestCase("_")]
        public void Seed_Invalid_IsRejectedWithOptionError(string text)
        {
            var e = Assert.Throws<OptionException>(() => Opts("--seed", text).GetULong("seed", 1));
            Assert.That(e.Message, Does.Contain("--seed"));
            Assert.That(e.Message, Does.Contain("0 이상의 정수"));
        }

        [Test, TC("TOOL-003")]
        [TestCase("1_000", 1000UL)]
        [TestCase("9_969_530_186_747_707_812", 9969530186747707812UL)]
        [TestCase("18_446_744_073_709_551_615", 18446744073709551615UL)]
        public void Seed_UnderscoreSeparators_AreIgnored(string text, ulong expected)
        {
            Assert.That(Opts("--seed", text).GetULong("seed", 1), Is.EqualTo(expected));
        }

        // 실제 빌드 자동 플레이 결과 화면(docs/bug-reports/attachments/BUG-003-turn-result.png)에 찍힌 명령줄 그대로.
        // 이 시드(9969530186747707812)가 long 최대값보다 커서 BUG-003이 드러났다.
        private const string AutoplaySeed = "9969530186747707812";
        private const string AutoplayCommands =
            "A1,A1,S0,S0,A1,A1,A0,A0,A0,A1,S0,A1,A1,A1,S0,S0,A0,A0,A0,S0,A0,A0,A0,A0,A0,A0,S0,S0,S0,A0,A0,A0,A0,A0,A0,A0,S0,A0,S0,A0,S0";

        [Test, TC("TOOL-004")]
        public void TurnReplay_SeedAboveLongMax_ReproducesTheGameResult()
        {
            var (code, output) = Capture(() => TurnReplayCommand.Run(Opts(
                "--stage", "T-1", "--party", "c_firefly,c_mushroom,c_moss",
                "--seed", AutoplaySeed, "--auto", "false", "--commands", AutoplayCommands)));

            Assert.That(code, Is.EqualTo(0), output);
            Assert.That(output, Does.Contain("시드 " + AutoplaySeed));
            Assert.That(output, Does.Contain("결과: Victory · 웨이브 3/3 · 14라운드 · 별 3")); // 결과 화면과 같음
            Assert.That(output, Does.Contain("반딧불 궁수: HP 321/520"));                      // 화면 로그의 마지막 HP
            Assert.That(output, Does.Contain("명령: " + AutoplayCommands));                    // 자동으로 채운 명령 없음
        }

        [Test, TC("TOOL-004")]
        [TestCase("9223372036854775808")]
        [TestCase("18446744073709551615")]
        public void BattleReplay_SeedAboveLongMax_Runs(string seed)
        {
            var (code, output) = Capture(() => BattleReplayCommand.Run(Opts(
                "--stage", "1-1", "--party", "c_lumi,c_firefly,c_moss", "--seed", seed)));

            Assert.That(code, Is.EqualTo(0), output);
            Assert.That(output, Does.Contain("시드 " + seed));
            Assert.That(output, Does.Contain("결과: "));
        }

        [Test, TC("TOOL-004")]
        public void GachaVerify_SeedAboveLongMax_Runs()
        {
            // 판정(통과/실패)은 보지 않는다: 1000회로는 통계가 모자라다. 시드가 읽혀 실행되는지만 본다.
            var (_, output) = Capture(() => GachaVerifyCommand.Run(Opts(
                "--banner", "b_standard", "--pulls", "1000", "--seed", "18446744073709551615")));

            Assert.That(output, Does.Contain("[b_standard]"));
        }

        // 같은 판이어야 하는 두 시드가 다른 판으로 읽히지 않는지: 음수를 받아 주던 때는
        // --seed -1 이 ulong 최대값 시드의 전투를 돌렸다.
        [Test, TC("TOOL-002")]
        public void TurnReplay_NegativeSeed_IsRejectedInsteadOfWrapping()
        {
            Assert.Throws<OptionException>(() => Capture(() => TurnReplayCommand.Run(Opts(
                "--stage", "T-1", "--party", "c_firefly,c_mushroom,c_moss", "--seed", "-1"))));
        }

        private static (int Code, string Output) Capture(Func<int> run)
        {
            var before = Console.Out;
            var writer = new StringWriter();
            Console.SetOut(writer);
            try
            {
                int code = run();
                return (code, writer.ToString());
            }
            finally
            {
                Console.SetOut(before);
            }
        }
    }
}
