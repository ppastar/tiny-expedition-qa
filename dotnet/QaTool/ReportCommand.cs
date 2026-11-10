using System.Net;
using System.Text;
using System.Xml.Linq;

namespace TinyExpedition.QaTool;

/// <summary>
/// 테스트 결과(NUnit 3 XML)와 TC 문서(CSV)를 이어 추적 매트릭스 HTML을 만든다.
/// dotnet test(NunitXml.TestLogger)와 Unity Test Runner가 같은 NUnit 3 형식을 내므로 둘 다 읽는다.
/// </summary>
public static class ReportCommand
{
    public sealed record TestCase(string Name, string FullName, string Result, double Duration, string Message, string Source, List<string> Tcs);
    public sealed record TcRow(string Id, string Area, string Title, string Pre, string Steps, string Expected, string Priority, string Type, bool Automated, string Env);

    private enum Status { Pass, Fail, Missing, Manual, Skipped, NotRun }

    public static int Run(Options o)
    {
        var resultSpecs = o.Require("results").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cases = ReadCases(o.Require("cases"));
        var tests = new List<TestCase>();
        var missingFiles = new List<string>();
        foreach (var spec in resultSpecs)
        {
            // "라벨=경로" 또는 "경로"
            var parts = spec.Split('=', 2);
            var path = parts.Length == 2 ? parts[1] : parts[0];
            var label = parts.Length == 2 ? parts[0] : Path.GetFileNameWithoutExtension(path);
            if (!File.Exists(path)) { missingFiles.Add(path); continue; }
            tests.AddRange(ReadNUnit(path, label));
        }

        // 이번에 결과를 넣은 실행 환경 (예: dotnet, unity-playmode). 결과 파일이 없는 환경은 빠진다.
        var ranSources = tests.Select(t => t.Source).ToHashSet();
        var rows = cases.Select(c =>
        {
            var linked = tests.Where(t => t.Tcs.Contains(c.Id)).ToList();
            var status = StatusOf(c, linked);
            // 자동화 TC인데 그 실행 환경의 결과가 아예 없으면 '누락'이 아니라 '미실행'
            if (status == Status.Missing && c.Env.Length > 0 && !ranSources.Contains(c.Env)) status = Status.NotRun;
            return (Case: c, Tests: linked, Status: status);
        }).ToList();

        var known = cases.Select(c => c.Id).ToHashSet();
        var orphanTcs = tests.SelectMany(t => t.Tcs).Where(id => !known.Contains(id)).Distinct().OrderBy(x => x).ToList();
        var untagged = tests.Where(t => t.Tcs.Count == 0).ToList();

        var html = Render(rows, tests, orphanTcs, untagged, missingFiles, resultSpecs);
        var outPath = o.Require("out");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, html, new UTF8Encoding(false));

        int fail = rows.Count(r => r.Status == Status.Fail), missing = rows.Count(r => r.Status == Status.Missing);
        Console.WriteLine($"TC {rows.Count}개 — 통과 {rows.Count(r => r.Status == Status.Pass)}, 실패 {fail}, 자동화 누락 {missing}, 미실행(환경 없음) {rows.Count(r => r.Status == Status.NotRun)}, 수동 {rows.Count(r => r.Status == Status.Manual)}, 건너뜀 {rows.Count(r => r.Status == Status.Skipped)}");
        Console.WriteLine($"테스트 {tests.Count}개 (실패 {tests.Count(t => t.Result == "Failed")}) · 문서에 없는 TC {orphanTcs.Count} · TC 없는 테스트 {untagged.Count}");
        foreach (var f in missingFiles) Console.WriteLine("결과 파일 없음(건너뜀): " + f);
        Console.WriteLine("리포트: " + Path.GetFullPath(outPath));
        return fail > 0 || missing > 0 || orphanTcs.Count > 0 ? 1 : 0;
    }

    private static Status StatusOf(TcRow c, List<TestCase> linked)
    {
        if (linked.Any(t => t.Result == "Failed")) return Status.Fail;
        if (linked.Count > 0 && linked.All(t => t.Result == "Passed")) return Status.Pass;
        if (linked.Count > 0) return Status.Skipped;
        return c.Automated ? Status.Missing : Status.Manual;
    }

    public static List<TestCase> ReadNUnit(string path, string source)
    {
        var doc = XDocument.Load(path);
        return doc.Descendants("test-case").Select(tc =>
        {
            // TC 속성은 test-case 자신 또는 (TestCase 묶음인 경우) 부모 test-suite에 붙는다
            var tcs = tc.AncestorsAndSelf()
                .Select(e => e.Element("properties"))
                .Where(p => p != null)
                .SelectMany(p => p!.Elements("property"))
                .Where(p => (string?)p.Attribute("name") == "TC")
                .Select(p => (string)p.Attribute("value")!)
                .Distinct().ToList();
            var msg = tc.Element("failure")?.Element("message")?.Value ?? "";
            double.TryParse((string?)tc.Attribute("duration"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var dur);
            return new TestCase((string?)tc.Attribute("name") ?? "", (string?)tc.Attribute("fullname") ?? "",
                (string?)tc.Attribute("result") ?? "", dur, msg.Trim(), source, tcs);
        }).ToList();
    }

    public static List<TcRow> ReadCases(string path)
    {
        var lines = Csv.Parse(File.ReadAllText(path));
        var header = lines[0];
        int Col(string name) => header.IndexOf(name) is var i and >= 0 ? i : throw new OptionException("TC 문서에 '" + name + "' 열이 없습니다.");
        int id = Col("TC_ID"), area = Col("영역"), title = Col("제목"), pre = Col("사전조건"), steps = Col("절차"),
            exp = Col("기대결과"), pri = Col("우선순위"), type = Col("유형"), auto = Col("자동화");
        int env = header.IndexOf("실행환경");
        return lines.Skip(1).Where(l => l.Count > 1 && l[id].Length > 0)
            .Select(l => new TcRow(l[id], l[area], l[title], l[pre], l[steps], l[exp], l[pri], l[type], l[auto].Trim().ToUpperInvariant() == "Y", env >= 0 && env < l.Count ? l[env].Trim() : ""))
            .ToList();
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string Render(List<(TcRow Case, List<TestCase> Tests, Status Status)> rows, List<TestCase> tests,
        List<string> orphanTcs, List<TestCase> untagged, List<string> missingFiles, string[] specs)
    {
        int total = rows.Count;
        int pass = rows.Count(r => r.Status == Status.Pass);
        int fail = rows.Count(r => r.Status == Status.Fail);
        int missing = rows.Count(r => r.Status == Status.Missing);
        int manual = rows.Count(r => r.Status == Status.Manual);
        int notRun = rows.Count(r => r.Status == Status.NotRun);
        int planned = rows.Count(r => r.Case.Automated);
        int covered = rows.Count(r => r.Case.Automated && r.Tests.Count > 0);
        bool green = fail == 0 && missing == 0 && orphanTcs.Count == 0;

        var sb = new StringBuilder();
        sb.Append("""
<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Tiny Expedition — 테스트 추적 리포트</title>
<style>
:root { color-scheme: light dark; --bg:#f7f7f5; --card:#fff; --fg:#1d1d1f; --muted:#6b6b70; --line:#e3e3e0;
  --pass:#1a7f37; --pass-bg:#dafbe1; --fail:#cf222e; --fail-bg:#ffebe9; --warn:#9a6700; --warn-bg:#fff8c5; --man:#57606a; --man-bg:#eaeef2; --accent:#6d4aff; }
@media (prefers-color-scheme: dark) { :root { --bg:#0f1115; --card:#171a21; --fg:#e6e6e6; --muted:#9aa0aa; --line:#2a2f3a;
  --pass:#3fb950; --pass-bg:#12261a; --fail:#ff7b72; --fail-bg:#2d1416; --warn:#d29922; --warn-bg:#2b230f; --man:#9aa0aa; --man-bg:#222731; --accent:#a38bff; } }
* { box-sizing: border-box; }
body { margin:0; font: 14px/1.55 -apple-system, "Segoe UI", "Malgun Gothic", sans-serif; background:var(--bg); color:var(--fg); }
main { max-width: 1180px; margin: 0 auto; padding: 32px 20px 64px; }
h1 { font-size: 24px; margin: 0 0 4px; } h2 { font-size: 17px; margin: 32px 0 12px; }
.sub { color: var(--muted); margin-bottom: 20px; }
.verdict { display:inline-block; padding:4px 12px; border-radius:999px; font-weight:600; margin-left:8px; font-size:13px; vertical-align: middle; }
.cards { display:grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap:12px; }
.card { background:var(--card); border:1px solid var(--line); border-radius:12px; padding:14px 16px; }
.card .n { font-size: 26px; font-weight: 700; } .card .l { color: var(--muted); font-size: 12px; }
.bar { height: 8px; border-radius: 4px; background: var(--man-bg); overflow: hidden; display:flex; }
.bar span { display:block; height:100%; }
table { width:100%; border-collapse: collapse; background:var(--card); border:1px solid var(--line); border-radius:12px; overflow:hidden; }
th, td { padding: 8px 10px; border-bottom: 1px solid var(--line); text-align:left; vertical-align: top; }
th { font-size: 12px; color: var(--muted); font-weight: 600; background: var(--card); position: sticky; top: 0; }
tr:last-child td { border-bottom: 0; }
.badge { display:inline-block; padding:1px 8px; border-radius:999px; font-size:12px; font-weight:600; white-space:nowrap; }
.Pass { color:var(--pass); background:var(--pass-bg); } .Fail { color:var(--fail); background:var(--fail-bg); }
.Missing, .Skipped { color:var(--warn); background:var(--warn-bg); } .Manual, .NotRun { color:var(--man); background:var(--man-bg); }
.tests { font-family: ui-monospace, Consolas, monospace; font-size: 12px; color: var(--muted); }
.tests .f { color: var(--fail); }
.id { font-family: ui-monospace, Consolas, monospace; font-weight: 600; white-space: nowrap; }
td:nth-child(2), td:nth-child(4), td:nth-child(5), td:nth-child(6) { white-space: nowrap; }
th { white-space: nowrap; }
.filters { display:flex; gap:8px; flex-wrap: wrap; margin-bottom: 12px; }
.filters button { border:1px solid var(--line); background:var(--card); color:var(--fg); padding:4px 12px; border-radius:999px; cursor:pointer; font: inherit; font-size: 13px; }
.filters button.on { border-color: var(--accent); color: var(--accent); font-weight: 600; }
.areas { display:grid; grid-template-columns: 110px 1fr 90px; gap: 8px 12px; align-items:center; background:var(--card); border:1px solid var(--line); border-radius:12px; padding: 14px 16px; }
pre { white-space: pre-wrap; background: var(--fail-bg); color: var(--fg); padding: 10px; border-radius: 8px; font-size: 12px; }
.muted { color: var(--muted); }
</style>
</head>
<body><main>
""");
        sb.Append($"<h1>Tiny Expedition 테스트 추적 리포트<span class=\"verdict {(!green ? "Fail" : notRun > 0 ? "NotRun" : "Pass")}\">{(!green ? "확인 필요" : notRun > 0 ? "실행한 범위 통과 · 미실행 " + notRun + "건" : "전체 통과")}</span></h1>");
        sb.Append($"<div class=\"sub\">생성 {DateTime.Now:yyyy-MM-dd HH:mm} · 결과 파일: {E(string.Join(", ", specs))}</div>");

        sb.Append("<div class=\"cards\">");
        Card(sb, total.ToString(), "TC 전체");
        Card(sb, $"{covered}/{planned}", "자동화 예정 TC 중 구현");
        Card(sb, pass.ToString(), "통과", "var(--pass)");
        Card(sb, fail.ToString(), "실패", fail > 0 ? "var(--fail)" : null);
        Card(sb, missing.ToString(), "자동화 누락", missing > 0 ? "var(--warn)" : null);
        Card(sb, notRun.ToString(), "미실행 (환경 없음)");
        Card(sb, manual.ToString(), "수동 TC");
        Card(sb, tests.Count.ToString(), "실행된 테스트");
        sb.Append("</div>");

        sb.Append("<h2>영역별</h2><div class=\"areas\">");
        foreach (var g in rows.GroupBy(r => r.Case.Area))
        {
            int n = g.Count(), p = g.Count(r => r.Status == Status.Pass), f = g.Count(r => r.Status == Status.Fail),
                m = g.Count(r => r.Status is Status.Missing or Status.Skipped);
            sb.Append($"<div>{E(g.Key)}</div><div class=\"bar\"><span style=\"width:{100.0 * p / n:0.#}%;background:var(--pass)\"></span><span style=\"width:{100.0 * f / n:0.#}%;background:var(--fail)\"></span><span style=\"width:{100.0 * m / n:0.#}%;background:var(--warn)\"></span></div><div class=\"muted\">{p}/{n} 통과</div>");
        }
        sb.Append("</div>");

        var failedTests = tests.Where(t => t.Result == "Failed").ToList();
        if (failedTests.Count > 0)
        {
            sb.Append("<h2>실패한 테스트</h2>");
            foreach (var t in failedTests)
                sb.Append($"<div><b class=\"id\">{E(string.Join(", ", t.Tcs))}</b> <span class=\"tests\">[{E(t.Source)}] {E(t.FullName)}</span><pre>{E(t.Message)}</pre></div>");
        }

        sb.Append("<h2>추적 매트릭스</h2>");
        sb.Append("<div class=\"filters\" id=\"f\"><button class=\"on\" data-s=\"all\">전체</button><button data-s=\"Fail\">실패</button><button data-s=\"Missing\">자동화 누락</button><button data-s=\"NotRun\">미실행</button><button data-s=\"Manual\">수동</button><button data-s=\"Pass\">통과</button></div>");
        sb.Append("<table><thead><tr><th>TC</th><th>영역</th><th>제목 / 기대 결과</th><th>우선순위</th><th>유형</th><th>실행 환경</th><th>상태</th><th>연결된 자동화 테스트</th></tr></thead><tbody>");
        foreach (var (c, linked, status) in rows)
        {
            var label = status switch { Status.Pass => "통과", Status.Fail => "실패", Status.Missing => "자동화 누락", Status.Manual => "수동", Status.NotRun => "미실행", _ => "건너뜀" };
            var testList = linked.Count == 0 ? "<span class=\"muted\">—</span>" :
                string.Join("<br>", linked.Select(t => $"<span class=\"{(t.Result == "Failed" ? "f" : "")}\">[{E(t.Source)}] {E(t.Name)} · {E(t.Result)}</span>"));
            sb.Append($"<tr data-s=\"{status}\"><td class=\"id\">{E(c.Id)}</td><td>{E(c.Area)}</td><td><b>{E(c.Title)}</b><div class=\"muted\">{E(c.Steps)} → {E(c.Expected)}</div></td><td>{E(c.Priority)}</td><td>{E(c.Type)}</td><td class=\"muted\">{E(c.Env)}</td><td><span class=\"badge {status}\">{label}</span></td><td class=\"tests\">{testList}</td></tr>");
        }
        sb.Append("</tbody></table>");

        if (orphanTcs.Count > 0 || untagged.Count > 0 || missingFiles.Count > 0)
        {
            sb.Append("<h2>정리할 것</h2><ul>");
            if (orphanTcs.Count > 0) sb.Append($"<li>TC 문서에 없는 TC 번호를 쓰는 테스트: <span class=\"id\">{E(string.Join(", ", orphanTcs))}</span></li>");
            foreach (var t in untagged) sb.Append($"<li>TC 번호가 없는 테스트: <span class=\"tests\">[{E(t.Source)}] {E(t.FullName)}</span></li>");
            foreach (var f in missingFiles) sb.Append($"<li>결과 파일 없음: <span class=\"tests\">{E(f)}</span></li>");
            sb.Append("</ul>");
        }

        sb.Append("""
<script>
document.getElementById('f').addEventListener('click', e => {
  const b = e.target.closest('button'); if (!b) return;
  document.querySelectorAll('#f button').forEach(x => x.classList.toggle('on', x === b));
  document.querySelectorAll('tbody tr').forEach(tr => tr.style.display = (b.dataset.s === 'all' || tr.dataset.s === b.dataset.s) ? '' : 'none');
});
</script>
</main></body></html>
""");
        return sb.ToString();
    }

    private static void Card(StringBuilder sb, string n, string label, string? color = null) =>
        sb.Append($"<div class=\"card\"><div class=\"n\"{(color != null ? $" style=\"color:{color}\"" : "")}>{E(n)}</div><div class=\"l\">{E(label)}</div></div>");
}

/// <summary>따옴표·쉼표·줄바꿈을 처리하는 작은 CSV 파서 (RFC 4180).</summary>
public static class Csv
{
    public static List<List<string>> Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
