using System.Text;
using TinyExpedition.QaTool;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

var opts = Options.Parse(args.Skip(1).ToArray());
try
{
    return args[0] switch
    {
        "validate-data" => ValidateDataCommand.Run(opts),
        "gacha-verify" => GachaVerifyCommand.Run(opts),
        "battle-replay" => BattleReplayCommand.Run(opts),
        "minigame-replay" => MiniGameReplayCommand.Run(opts),
        "report" => ReportCommand.Run(opts),
        _ => Unknown(args[0]),
    };
}
catch (OptionException e)
{
    Console.Error.WriteLine("옵션 오류: " + e.Message);
    return 2;
}

static int Unknown(string cmd)
{
    Console.Error.WriteLine("알 수 없는 명령: " + cmd);
    PrintHelp();
    return 2;
}

static void PrintHelp()
{
    Console.WriteLine("""
        qa-tool — Tiny Expedition QA 도구

        명령
          validate-data   [--data <dir>]
              기획 데이터(JSON) 정합성 검사. 오류가 있으면 종료 코드 1.

          gacha-verify    [--data <dir>] [--banner <id>|all] [--pulls 1000000] [--seed 1] [--out report.md]
              실제 모집 코드를 대량으로 돌려 공시 확률과 비교 (카이제곱·신뢰구간·천장 간격).

          battle-replay   --stage <id> --party <id,id,id> [--seed 1] [--data <dir>]
              시드로 전투를 다시 돌려 행동 로그 출력 (버그 리포트 재현용).

          minigame-replay --taps <t1,t2,...>
              미니게임 탭 시각 기록으로 판정·점수를 다시 계산.

          report          --results <a.xml,b.xml> --cases <test-cases.csv> --out <report.html>
              NUnit 결과(dotnet·Unity)를 TC 문서와 이어 추적 매트릭스 HTML 생성.
              TC 실패 또는 자동화 예정(Y)인데 테스트 없는 TC가 있으면 종료 코드 1.
        """);
}
