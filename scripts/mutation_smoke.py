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
MUTATIONS = [
    ("천장 off-by-one (90번째가 아니라 91번째에 확정)", "Gacha/GachaService.cs",
     "count >= banner.HardPity", "count > banner.HardPity"),
    ("3성이 나와도 천장 카운트가 초기화되지 않음", "Gacha/GachaService.cs",
     "pity.PullsSinceThreeStar = rarity == Rarity.ThreeStar ? 0 : count;", "pity.PullsSinceThreeStar = count;"),
    ("10연 보정이 항상 꺼짐", "Gacha/GachaService.cs",
     "bool needGuarantee = banner.TenPullGuarantee && lastSlot", "bool needGuarantee = false && lastSlot"),
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
]


def run_tests() -> tuple[str, list[str]]:
    """결과: ("pass" | "fail" | "build-error", 실패한 TC 번호 목록)"""
    xml = ROOT / "artifacts" / "mutation-run.xml"
    xml.unlink(missing_ok=True)
    p = subprocess.run(
        ["dotnet", "test", str(ROOT / "dotnet/Core.Tests"), "--nologo", "-v", "q",
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
    result, _ = run_tests()
    if result != "pass":
        print("원본 코드에서 이미 테스트가 실패합니다. 먼저 고치세요.")
        return 2

    survived = []
    for desc, rel, before, after in MUTATIONS:
        path = CORE / rel
        original = path.read_bytes()
        if b"\r\n" in original:  # 작업 폴더가 CRLF면 코드 조각도 CRLF로 맞춘다
            before, after = before.replace("\n", "\r\n"), after.replace("\n", "\r\n")
        if before.encode("utf-8") not in original:
            print(f"[건너뜀] {desc}: 원본 코드 조각을 찾지 못함 (코드가 바뀌었으면 목록을 갱신)")
            survived.append(desc)
            continue
        try:
            path.write_bytes(original.replace(before.encode("utf-8"), after.encode("utf-8"), 1))
            result, failed_tcs = run_tests()
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
