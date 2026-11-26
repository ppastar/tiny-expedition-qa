"""
뮤테이션 스모크 테스트: 게임 코드에 '그럴듯한 버그'를 하나씩 일부러 넣고,
테스트가 그 버그를 잡는지(=실패하는지) 확인한다. 테스트가 통과해 버리면 그 버그는 테스트 구멍이다.

사용: python scripts/mutation_smoke.py
"""
import pathlib
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
CORE = ROOT / "UnityProject/Assets/TinyExpedition/Core"

# (설명, 파일, 원래 코드, 버그 코드)
# 파일이 "dotnet/"으로 시작하면 저장소 루트 기준(qa-tool)이고 qa-tool 테스트로 확인한다. 나머지는 Core 기준.
MUTATIONS = [
    ("천장 off-by-one (90번째가 아니라 91번째에 확정)", "Gacha/GachaService.cs",
     "count >= banner.HardPity", "count > banner.HardPity"),
    ("3성이 나와도 천장 카운트가 초기화되지 않음", "Gacha/GachaService.cs",
     "pity.PullsSinceThreeStar = rarity == Rarity.ThreeStar ? 0 : count;", "pity.PullsSinceThreeStar = count;"),
    ("10연 보정이 항상 꺼짐", "Gacha/GachaService.cs",
     "bool needGuarantee = banner.TenPullGuarantee && lastSlot", "bool needGuarantee = false && lastSlot"),
    ("10연 안에서는 3성이 나와도 천장 카운트가 초기화되지 않음 (1회 모집만 초기화)", "Gacha/GachaService.cs",
     "results.Add(PullInternal(banner, pity, rng, needGuarantee));",
     "{ int prev = pity.PullsSinceThreeStar; var r = PullInternal(banner, pity, rng, needGuarantee);"
     " if (r.Rarity == Rarity.ThreeStar) pity.PullsSinceThreeStar = prev + 1; results.Add(r); }"),
    ("10연 보정 대상 장에서는 천장 판정을 건너뜀 (보정이 천장보다 먼저)", "Gacha/GachaService.cs",
     "if (banner.HardPity > 0 && count >= banner.HardPity)",
     "if (banner.HardPity > 0 && count >= banner.HardPity && !applyTenPullGuarantee)"),
    ("천장 3성은 픽업 판정 없이 픽업 확정", "Gacha/GachaService.cs",
     "isPickup = others.Count == 0 || rng.NextInt(10000) < banner.PickupShareBp;",
     "isPickup = others.Count == 0 || reason == PullReason.HardPity || rng.NextInt(10000) < banner.PickupShareBp;"),
    ("등급 확률 누적 순서 오류 (2성 구간을 두 번 더함)", "Gacha/GachaService.cs",
     "acc += bp;", "acc += r == Rarity.TwoStar ? bp * 2 : bp;"),
    ("모집 실패해도 보석이 먼저 빠짐 (차감 순서 오류)", "GameSession.cs",
     "_rng = new Pcg32(State.RngSeed); // 난수도 행동 전 상태로", "State.Gems -= (int)cost; _rng = new Pcg32(State.RngSeed);"),
    ("보석 부족 판정 경계 오류 (같으면 실패)", "GameSession.cs",
     "if (State.Gems < cost)", "if (State.Gems <= cost)"),
    ("첫 클리어 보상 중복 지급", "GameSession.cs",
     "if (firstClear) State.Gems += stage.FirstClearGems;", "State.Gems += stage.FirstClearGems;"),
    ("별 기록이 낮은 값으로 덮어써짐", "GameSession.cs",
     "if (result.Stars > prevStars) State.ClearedStages[stageId] = result.Stars;", "State.ClearedStages[stageId] = result.Stars;"),
    ("미니게임 보상 중복 수령", "GameSession.cs",
     "|| game.RewardClaimed) return 0;", ") return 0;"),
    ("Perfect 판정 경계 미포함 (< 와 <= 혼동)", "MiniGame/TimingTapGame.cs",
     "if (d <= _cfg.PerfectHalfWidth + eps)", "if (d < _cfg.PerfectHalfWidth)"),
    ("Miss 후 콤보가 끊기지 않음", "MiniGame/TimingTapGame.cs",
     "                Combo = 0;\n", "\n"),
    ("콤보 보너스 상한 없음", "MiniGame/TimingTapGame.cs",
     "Math.Min(_cfg.MaxComboBonusPercent, (Combo - 1) * _cfg.ComboBonusPercent)", "(Combo - 1) * _cfg.ComboBonusPercent"),
    ("최소 피해 1 보장 누락", "Battle/BattleSimulator.cs",
     "return Math.Max(1, baseDmg);", "return baseDmg;"),
    ("속도 동률일 때 적이 먼저 행동", "Battle/BattleSimulator.cs",
     ".ThenBy(u => u.Side == Side.Ally ? 0 : 1)", ".ThenBy(u => u.Side == Side.Ally ? 1 : 0)"),
    ("음수 보석 세이브를 그대로 읽음", "Progress/SaveSerializer.cs",
     "if (state.Gems < 0) throw", "if (false) throw"),
    ("확률표 합계 검사 누락", "Data/DataValidator.cs",
     "if (sum != 10000)", "if (false)"),
    ("10연 보정 대상(2성) 후보 검사 누락", "Data/DataValidator.cs",
     "if (b.TenPullGuarantee && (", "if (false && ("),
    # --- 턴제 전투 ---
    ("[턴제] 속도 동점일 때 적이 먼저 행동", "Battle/TurnBattle.cs",
     ".ThenBy(u => u.Side == Side.Ally ? 0 : 1)", ".ThenBy(u => u.Side == Side.Ally ? 1 : 0)"),
    ("[턴제] 라운드 도중 쓰러진 유닛도 차례를 받음", "Battle/TurnBattle.cs",
     "if (!unit.IsAlive) continue; // 이번 라운드에 먼저 쓰러짐", "// 사망 확인 누락"),
    ("[턴제] 쿨타임 경계 off-by-one (마지막 1턴 일찍 사용 가능)", "Battle/TurnBattle.cs",
     "if (actor.CooldownLeft > 0) return TurnCommandError.SkillOnCooldown;", "if (actor.CooldownLeft > 1) return TurnCommandError.SkillOnCooldown;"),
    ("[턴제] 기절로 넘어간 차례에 쿨타임이 줄지 않음", "Battle/TurnBattle.cs",
     "                    EndTurn(unit, false);\n                    continue;", "                    continue;"),
    ("[턴제] 기절 재적용 시 남은 횟수가 합산됨", "Battle/TurnBattle.cs",
     "target.StunLeft = Math.Max(target.StunLeft, skill.StatusTurns);", "target.StunLeft += skill.StatusTurns;"),
    ("[턴제] 독 중첩 상한 없음", "Battle/TurnBattle.cs",
     "target.PoisonStacks = Math.Min(MaxPoisonStacks, target.PoisonStacks + 1);", "target.PoisonStacks++;"),
    ("[턴제] 방어가 피해를 줄이지 않음", "Battle/TurnBattle.cs",
     "if (target.Guarding) dmg = Math.Max(1, dmg / 2);", ""),
    ("[턴제] 동시 전멸을 승리로 판정", "Battle/TurnBattle.cs",
     "if (!_allies.Any(u => u.IsAlive))", "if (!_allies.Any(u => u.IsAlive) && _enemies.Any(u => u.IsAlive))"),
    ("[턴제] 웨이브가 바뀌어도 아군 상태이상 유지", "Battle/TurnBattle.cs",
     "foreach (var a in _allies) a.ClearStatuses();", ""),
    ("[턴제] 같은 전투 보상 중복 수령", "GameSession.cs",
     "battle.RewardClaimed = true;\n            _turnBattles.Remove(battle);", ""),
    ("[턴제] 첫 클리어가 아니어도 보석 지급", "GameSession.cs",
     "(firstClear ? battle.Stage.FirstClearGems : 0)", "battle.Stage.FirstClearGems"),
    # --- 턴제 전투 화면 상태 (2.5D 무대가 그리는 값) ---
    ("[턴제 화면] 로그에 대상 슬롯 대신 항상 0번을 적음 (이름이 같은 적 중 엉뚱한 쪽 HP가 깎여 보임)", "Battle/TurnBattle.cs",
     "TargetSide = target?.Side ?? Side.Ally; TargetSlot = target?.Slot ?? -1;", "TargetSide = target?.Side ?? Side.Ally; TargetSlot = target != null ? 0 : -1;"),
    ("[턴제 화면] 로그의 독 중첩 칸에 남은 라운드 수를 적음 (화면의 독 표시가 실제와 다름)", "Battle/TurnBattle.cs",
     "PoisonAfter = subject?.PoisonStacks ?? -1;", "PoisonAfter = subject?.PoisonLeft ?? -1;"),
    ("[턴제 화면] 방어 표시가 다음 차례에도 안 사라짐", "Battle/TurnBattleDisplay.cs",
     "actor.Guarding = e.Kind == TurnLogKind.Guard;", "actor.Guarding |= e.Kind == TurnLogKind.Guard;"),
    ("[턴제 화면] 웨이브가 바뀌어도 화면의 아군 상태이상 표시가 남음", "Battle/TurnBattleDisplay.cs",
     "foreach (var a in _allies) { a.StunLeft = 0; a.PoisonStacks = 0; a.Guarding = false; }", ""),
    ("[턴제 화면] 다음 웨이브 적을 세우지 않음 (이전 웨이브 적이 그대로 보임)", "Battle/TurnBattleDisplay.cs",
     "_enemies = Spawn(WaveIndex);", ""),
    # --- qa-tool (BUG-003) ---
    ("[qa-tool] 시드를 long으로 읽음 (long 최대값보다 큰 시드 거부, 음수는 큰 시드로 바뀜)", "dotnet/QaTool/Common.cs",
     "return ulong.TryParse(v.Replace(\"_\", \"\"), out var n) ? n :",
     "return long.TryParse(v.Replace(\"_\", \"\"), out var n) ? (ulong)n :"),
    ("[qa-tool] battle-replay만 시드를 long으로 읽음 (호출부 되돌림)", "dotnet/QaTool/SmallCommands.cs",
     'ulong seed = o.GetULong("seed", 1);', 'ulong seed = (ulong)o.GetLong("seed", 1);'),
]

CORE_TESTS = "dotnet/Core.Tests"
TOOL_TESTS = "dotnet/QaTool.Tests"


def run_tests(project: str = CORE_TESTS) -> tuple[str, list[str]]:
    """결과: ("pass" | "fail" | "build-error", 실패한 TC 번호 목록)"""
    xml = ROOT / "artifacts" / "mutation-run.xml"
    xml.unlink(missing_ok=True)
    p = subprocess.run(
        ["dotnet", "test", str(ROOT / project), "--nologo", "-v", "q",
         "--logger", f"nunit;LogFilePath={xml}"],
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    if "error CS" in p.stdout + p.stderr or not xml.exists():
        return "build-error", []
    tree = ET.parse(xml)
    failed_tcs = set()
    for case in tree.iter("test-case"):
        if case.get("result") != "Failed":
            continue
        for prop in case.iter("property"):
            if prop.get("name") == "TC":
                failed_tcs.add(prop.get("value"))
    return ("pass" if p.returncode == 0 else "fail"), sorted(failed_tcs)


def main() -> int:
    if any(run_tests(project)[0] != "pass" for project in (CORE_TESTS, TOOL_TESTS)):
        print("원본 코드에서 이미 테스트가 실패합니다. 먼저 고치세요.")
        return 2

    survived = []
    for desc, rel, before, after in MUTATIONS:
        is_tool = rel.startswith("dotnet/")
        path = ROOT / rel if is_tool else CORE / rel
        original = path.read_bytes()
        if b"\r\n" in original:  # 작업 폴더가 CRLF면 코드 조각도 CRLF로 맞춘다
            before, after = before.replace("\n", "\r\n"), after.replace("\n", "\r\n")
        if before.encode("utf-8") not in original:
            print(f"[건너뜀] {desc}: 원본 코드 조각을 찾지 못함 (코드가 바뀌었으면 목록을 갱신)")
            survived.append(desc)
            continue
        try:
            path.write_bytes(original.replace(before.encode("utf-8"), after.encode("utf-8"), 1))
            result, failed_tcs = run_tests(TOOL_TESTS if is_tool else CORE_TESTS)
        finally:
            path.write_bytes(original)
        if result == "pass":
            print(f"[생존] {desc}  ← 이 버그를 잡는 테스트가 없음")
            survived.append(desc)
        elif result == "build-error":
            print(f"[무효] {desc}  ← 버그 코드가 컴파일되지 않음 (목록 수정 필요)")
            survived.append(desc)
        else:
            print(f"[검출] {desc}  ← {', '.join(failed_tcs)}")

    total = len(MUTATIONS)
    print(f"\n검출 {total - len(survived)}/{total}")
    return 1 if survived else 0


if __name__ == "__main__":
    sys.exit(main())
