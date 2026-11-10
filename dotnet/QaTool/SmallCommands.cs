using System.Globalization;
using TinyExpedition.Core.Battle;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.MiniGame;
using TinyExpedition.Core.Random;

namespace TinyExpedition.QaTool;

public static class ValidateDataCommand
{
    public static int Run(Options o)
    {
        var dir = Repo.DataDir(o);
        Console.WriteLine("데이터 폴더: " + dir);
        GameDatabase db;
        try
        {
            db = GameDatabase.Load(f => File.ReadAllText(Path.Combine(dir, f)));
        }
        catch (GameDataException e)
        {
            Console.WriteLine("[Error] " + e.Message);
            return 1;
        }

        var issues = DataValidator.Validate(db);
        foreach (var i in issues) Console.WriteLine(i);
        int errors = issues.Count(i => i.Level == IssueLevel.Error);
        int warnings = issues.Count - errors;
        Console.WriteLine($"캐릭터 {db.Characters.Count} · 배너 {db.Banners.Count} · 스테이지 {db.Stages.Count} — 오류 {errors}, 경고 {warnings}");
        return errors > 0 ? 1 : 0;
    }
}

public static class BattleReplayCommand
{
    public static int Run(Options o)
    {
        var db = Repo.LoadDb(o);
        var stage = db.GetStage(o.Require("stage"));
        var party = o.Require("party").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(db.GetCharacter).ToList();
        ulong seed = (ulong)o.GetLong("seed", 1);

        var result = BattleSimulator.Run(party, stage, new Pcg32(seed));
        Console.WriteLine($"스테이지 {stage.Id} {stage.Name} · 파티 {string.Join(", ", party.Select(p => p.Name))} · 시드 {seed}");
        foreach (var e in result.Log) Console.WriteLine("  " + e);
        Console.WriteLine($"결과: {result.Outcome} · {result.Rounds}라운드 · 별 {result.Stars}");
        foreach (var a in result.Allies) Console.WriteLine($"  {a.Name}: HP {a.Hp}/{a.MaxHp}");
        return 0;
    }
}

public static class MiniGameReplayCommand
{
    public static int Run(Options o)
    {
        var times = o.Require("taps").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToList();
        var g = TimingTapGame.Replay(new TimingTapConfig(), times);
        foreach (var t in g.Taps)
            Console.WriteLine($"  t={t.Time,6:0.000}s  위치 {t.Position:0.000}  {t.Judgement,-7} 콤보 {t.Combo,2}  +{t.Gained}");
        Console.WriteLine($"점수 {g.Score} · 최대 콤보 {g.MaxCombo} · " + (g.IsFinished ? $"보상 보석 {g.RewardGems()}" : $"진행 중 ({g.Taps.Count}/{g.Config.Rounds})"));
        if (times.Count > g.Taps.Count) Console.WriteLine($"(판이 끝나 나머지 탭 {times.Count - g.Taps.Count}개는 무시)");
        return 0;
    }
}
