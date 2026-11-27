# BUG-003 [QA 도구/리플레이] 게임이 찍어 준 시드가 long 최대값보다 크면 qa-tool이 "숫자여야 합니다"로 거부해 리플레이를 못 함

> **구분: 실제 결함 (수정됨)** — 턴제 전투 작업(PR #4) 중, 실제 빌드 자동 플레이의 결과 화면에 찍힌 리플레이 명령줄을 qa-tool에 그대로 붙여 넣다가 찾았습니다. '실제 결과'의 출력은 수정 직전 커밋(`8709f77`)을 별도 폴더에 받아 빌드해 실제로 돌린 결과이고, 수정 후 결과는 맨 아래 '수정 확인'에 있습니다.

| 항목 | 내용 |
|---|---|
| 상태 | 수정됨 · 회귀 TC 통과 |
| 심각도 | Major — 플레이어 피해는 없지만, 버그 재현 도구가 **전체 시드의 절반**에서 막히고 우회 방법이 없음. 음수 시드는 오류 없이 다른 판을 돌림 |
| 우선순위 | P1 — 재화·확률·진행은 멀쩡함. 다만 "가끔 나는" 전투 버그를 리포트에 적힌 시드로 재현할 수 없게 되므로, 다음 전투 테스트 전에 고쳐야 함 |
| 영역 | QA 도구(`qa-tool`) / 전투·턴제 리플레이, 확률 검증기 |
| 발견 커밋 | `8709f77` (PR #4 작업 브랜치) — `--seed` 옵션은 첫 커밋(`15562c3`)부터 같은 방식이었음 |
| 수정 커밋 | `ae98211` (PR #4 안의 커밋, main에는 `109786f`로 스쿼시 머지) |
| 회귀 TC | [TOOL-001 ~ TOOL-004](../test-cases.csv) |

## 환경

| 항목 | 값 |
|---|---|
| 코드 | `8709f77` (수정 전) / `ae98211` 이후 (수정 후) |
| OS | Windows 11 Pro 10.0.26300 |
| 런타임 | .NET SDK 10.0.402 (`qa-tool`은 .NET 콘솔 프로그램) |
| 엔진 | Unity 6000.5.6f1 — 시드와 명령줄은 실제 Windows 개발 빌드의 자동 플레이(`-qaAutoplay`)가 남긴 결과 화면에서 가져옴. 이 리포트를 쓰면서 게임을 다시 실행하지는 않았습니다 |
| 데이터 | 저장소의 `StreamingAssets/GameData` 그대로 |

## 사전조건

- 턴제 T-1을 한 판 끝낸 결과 화면(또는 `Player.log`의 `[TurnReplay]` 줄)에 찍힌 리플레이 명령줄.
- 그 판의 시드가 long 최대값(9223372036854775807)보다 큼. 여기서는 당시 자동 플레이 스크린샷 [`BUG-003-turn-result.png`](attachments/BUG-003-turn-result.png)의 시드 **9969530186747707812**.

게임은 행동이 끝날 때마다 32비트 난수 두 개를 이어 붙여 다음 시드를 만듭니다(`GameSession.AdvanceSeed`). 즉 시드는 ulong 전체 범위에 고르게 퍼지고, 맨 위 비트가 1인 시드(= long 최대값 초과)는 **절반**입니다. 특별한 조건이 아니라 두 판에 한 판꼴로 만나는 값입니다.

## 재현 절차

1. `8709f77`을 받아 `dotnet build dotnet/TinyExpedition.slnx -c Release`로 빌드한다.
2. 결과 화면의 명령줄을 그대로 붙여 넣어 실행한다.
3. 같은 스테이지·파티로 시드만 경계값(9223372036854775807 / 9223372036854775808 / 18446744073709551615 / -1)으로 바꿔 실행한다.
4. `battle-replay`, `gacha-verify`에도 같은 시드를 넣어 본다.

## 기대 결과

- 2번: 결과 화면과 같은 전투가 다시 나옴 — 승리, 웨이브 3/3, 14라운드, 마지막 줄의 반딧불 궁수 HP 321.
- 3번: 0 ~ 18446744073709551615는 모두 그 시드의 전투를 돌림. `-1`은 게임에 없는 시드이므로 옵션 오류.
- 4번: 세 명령 모두 같은 범위를 받음.

## 실제 결과 (`8709f77`)

| 명령 | `--seed` | 결과 |
|---|---|---|
| turn-replay (화면의 명령줄 그대로) | 9969530186747707812 | **옵션 오류, 종료 코드 2** — 리플레이 불가 |
| turn-replay | 9223372036854775807 (long 최대) | 실행됨 |
| turn-replay | 9223372036854775808 (long 최대 + 1) | **옵션 오류, 종료 코드 2** |
| turn-replay | 18446744073709551615 (ulong 최대) | **옵션 오류, 종료 코드 2** |
| turn-replay | -1 | **오류 없이 실행됨** — 승리·15라운드·반딧불 궁수 HP 281. 수정 후 확인해 보니 시드 18446744073709551615의 전투와 같은 판 |
| battle-replay | 9969530186747707812 | **옵션 오류, 종료 코드 2** |
| gacha-verify | 9969530186747707812 | **옵션 오류, 종료 코드 2** |

문제는 둘입니다.

1. **큰 시드를 못 읽음.** 오류 문구가 "`--seed 는 숫자여야 합니다: 9969530186747707812`"라, 받는 사람은 숫자를 넣었는데 숫자가 아니라는 말을 듣습니다. 원인을 짐작할 단서가 없습니다.
2. **음수를 조용히 받아 다른 시드로 바꿈.** `--seed -1`이 ulong 최대값 시드의 전투를 돌립니다. 정작 그 시드를 그대로 적으면 거부됩니다. 리플레이 도구가 "입력한 것과 다른 판"을 오류 없이 보여 주는 것은, 못 돌리는 것보다 나쁠 수 있습니다(재현 안 됨으로 잘못 결론 내리기 쉬움).

## 재현율

| 조건 | 결과 |
|---|---|
| 시드 > 9223372036854775807, 명령 3종(turn-replay · battle-replay · gacha-verify) | **5/5** 재현 (위 표의 옵션 오류 5건) |
| 시드 ≤ 9223372036854775807 | 0/2 (정상 실행) |

조건(시드 범위)만 맞으면 항상 재현됩니다. 게임이 만드는 시드의 절반이 이 조건에 듭니다.

## 첨부

### 결과 화면 (자동 플레이 스크린샷)

[`attachments/BUG-003-turn-result.png`](attachments/BUG-003-turn-result.png) (발견 당시 자동 플레이가 `docs/reports/autoplay/11-turn-result.png`로 남긴 화면. 이후 2.5D 화면으로 바뀌며 그 폴더의 스크린샷이 교체되어 여기에 보관) — 화면 아래쪽 "리플레이:" 줄:

```
qa turn-replay --stage T-1 --party c_firefly,c_mushroom,c_moss --seed 9969530186747707812 --commands A1,A1,S0,S0,A1,A1,A0,A0,A0,A1,S0,A1,A1,A1,S0,S0,A0,A0,A0,S0,A0,A0,A0,A0,A0,A0,S0,S0,S0,A0,A0,A0,A0,A0,A0,A0,S0,A0,S0,A0,S0
```

(스크린샷을 찍은 시점의 화면은 `--auto false`를 찍지 않았고, 이후 커밋에서 명령줄에 넣었습니다. 아래 실행에서는 지금 화면이 찍는 형식대로 `--auto false`를 붙였습니다. 명령 41개를 다 주므로 결과는 같습니다.)

### 실행 출력 (`8709f77`, `qa` = `dotnet run --project dotnet/QaTool -c Release --no-build --`)

```
$ qa turn-replay --stage T-1 --party c_firefly,c_mushroom,c_moss --seed 9969530186747707812 --commands A1,A1,S0,S0,A1,A1,A0,A0,A0,A1,S0,A1,A1,A1,S0,S0,A0,A0,A0,S0,A0,A0,A0,A0,A0,A0,S0,S0,S0,A0,A0,A0,A0,A0,A0,A0,S0,A0,S0,A0,S0 --auto false
옵션 오류: --seed 는 숫자여야 합니다: 9969530186747707812
(종료 코드 2)

$ qa turn-replay --stage T-1 --party c_firefly,c_mushroom,c_moss --seed 9223372036854775807
  W3 R15 승리 (별 3)
결과: Victory · 웨이브 3/3 · 15라운드 · 별 3
  반딧불 궁수: HP 354/520
  버섯 약사: HP 700/700
  이끼 방패병: HP 620/620
(종료 코드 0)

$ qa turn-replay --stage T-1 --party c_firefly,c_mushroom,c_moss --seed 9223372036854775808
옵션 오류: --seed 는 숫자여야 합니다: 9223372036854775808
(종료 코드 2)

$ qa turn-replay --stage T-1 --party c_firefly,c_mushroom,c_moss --seed 18446744073709551615
옵션 오류: --seed 는 숫자여야 합니다: 18446744073709551615
(종료 코드 2)

$ qa turn-replay --stage T-1 --party c_firefly,c_mushroom,c_moss --seed -1
  W3 R15 승리 (별 3)
결과: Victory · 웨이브 3/3 · 15라운드 · 별 3
  반딧불 궁수: HP 281/520
  버섯 약사: HP 700/700
  이끼 방패병: HP 620/620
(종료 코드 0)

$ qa battle-replay --stage 1-1 --party c_lumi,c_firefly,c_moss --seed 9969530186747707812
옵션 오류: --seed 는 숫자여야 합니다: 9969530186747707812
(종료 코드 2)

$ qa gacha-verify --pulls 1000 --seed 9969530186747707812
옵션 오류: --seed 는 숫자여야 합니다: 9969530186747707812
(종료 코드 2)
```

(성공한 실행은 로그가 길어 마지막 몇 줄만 붙였습니다. `(종료 코드 n)` 줄은 셸에서 따로 찍은 것입니다.)

## 원인

`dotnet/QaTool/Common.cs`의 옵션 읽기에 숫자용 함수가 `GetLong` 하나뿐이었고, 시드를 읽는 세 곳이 모두 이것을 쓴 뒤 `ulong`으로 바꿨습니다.

```csharp
ulong seed = (ulong)o.GetLong("seed", 1);   // SmallCommands.cs(battle-replay, turn-replay), GachaVerifyCommand.cs
```

- `long.TryParse`는 9223372036854775807까지만 읽으므로 그보다 큰 시드는 "숫자가 아님"으로 처리됩니다.
- 음수는 `long`으로 읽힌 뒤 `(ulong)` 캐스팅에서 그대로 비트가 넘어가 큰 양수가 됩니다 (-1 → 18446744073709551615).
- 게임 쪽 타입은 처음부터 `ulong`이었습니다(`PlayerState.RngSeed`, `Pcg32(ulong)`, `TurnBattle.Seed`). **만드는 쪽과 읽는 쪽의 타입이 달랐던 것**이 원인입니다.

### 왜 그동안 안 걸렸나

- 문서·테스트에 쓰던 시드가 전부 사람이 고른 작은 수(1, 3, 5, 42)였습니다.
- 게임이 스스로 만든 시드를 도구에 넣어 볼 일이 턴제 전투 전에는 없었습니다. 자동 전투는 화면에 리플레이 명령줄을 찍지 않았고, 결과 화면에 명령줄을 그대로 찍는 것은 PR #4에서 처음 넣었습니다.
- `qa-tool` 자체를 대상으로 한 테스트가 없었습니다. 테스트 104개는 모두 게임 규칙(Core) 대상이었습니다.

## 수정

1. `Options.GetULong(key, fallback)` 추가 — `ulong.TryParse`로 읽고, 실패하면 "`--seed 는 0 이상의 정수여야 합니다: <입력>`" 옵션 오류. 밑줄 구분자(`1_000`)는 `GetLong`과 같이 허용.
2. 시드를 읽는 세 곳(`battle-replay`, `turn-replay`, `gacha-verify`)을 `o.GetULong("seed", 1)`로 교체. 캐스팅이 없어져 음수가 다른 시드로 바뀌는 길도 함께 사라짐.
3. `--pulls`(모집 횟수)는 `GetLong` 그대로 — 횟수는 long 범위로 충분.

게임 코드는 바꾸지 않았습니다. 틀린 쪽은 도구였습니다.

## 수정 확인

수정 후(`109786f`) 같은 명령을 다시 돌린 출력:

```
$ qa turn-replay --stage T-1 --party c_firefly,c_mushroom,c_moss --seed 9969530186747707812 --commands A1,A1,S0,...(41개) --auto false
턴제 T-1 반딧불 동굴 · 파티 반딧불 궁수, 버섯 약사, 이끼 방패병 · 시드 9969530186747707812
  ...
  W3 R14 돌껍질 달팽이 독 피해 20 (1중첩) [남은 HP 0]
  W3 R14 돌껍질 달팽이 쓰러짐
  W3 R14 웨이브 3 정리
  W3 R14 승리 (별 3)
결과: Victory · 웨이브 3/3 · 14라운드 · 별 3
  반딧불 궁수: HP 321/520
  버섯 약사: HP 700/700
  이끼 방패병: HP 620/620
명령: A1,A1,S0,S0,A1,A1,A0,A0,A0,A1,S0,A1,A1,A1,S0,S0,A0,A0,A0,S0,A0,A0,A0,A0,A0,A0,S0,S0,S0,A0,A0,A0,A0,A0,A0,A0,S0,A0,S0,A0,S0
(종료 코드 0)

$ qa turn-replay --stage T-1 --party c_firefly,c_mushroom,c_moss --seed 18446744073709551615
결과: Victory · 웨이브 3/3 · 15라운드 · 별 3
  반딧불 궁수: HP 281/520
  버섯 약사: HP 700/700
  이끼 방패병: HP 620/620
(종료 코드 0)

$ qa turn-replay --stage T-1 --party c_firefly,c_mushroom,c_moss --seed -1
옵션 오류: --seed 는 0 이상의 정수여야 합니다: -1
(종료 코드 2)

$ qa turn-replay --stage T-1 --party c_firefly,c_mushroom,c_moss --seed abc
옵션 오류: --seed 는 0 이상의 정수여야 합니다: abc
(종료 코드 2)
```

- 화면의 명령줄이 이제 그대로 돌고, 결과가 스크린샷과 같습니다: 승리 · 웨이브 3/3 · 14라운드, 마지막 로그 네 줄, 반딧불 궁수 HP 321. Unity(Mono) 빌드에서 한 전투를 .NET 도구가 똑같이 다시 만든 것입니다.
- 시드 18446744073709551615의 결과(15라운드, HP 281)가 수정 전 `--seed -1`의 결과와 같습니다. 수정 전의 `-1`이 실제로는 이 시드를 돌리고 있었다는 확인입니다.

| 확인 | 결과 |
|---|---|
| 회귀 TC TOOL-001 (시드 경계값: 0 · 1 · long 최대 · long 최대+1 · ulong 최대, 생략 시 기본값) | 통과 (`Seed_ReadsWholeULongRange` 5건, `Seed_Missing_UsesFallback`) |
| 회귀 TC TOOL-002 (음수 · long 최소 · ulong 최대+1 · `abc` · `12abc` · `1.5` · `0x10` · 빈 값 · `_` → 옵션 오류, `turn-replay --seed -1` 거부) | 통과 (`Seed_Invalid_IsRejectedWithOptionError` 9건, `TurnReplay_NegativeSeed_IsRejectedInsteadOfWrapping`) |
| 회귀 TC TOOL-003 (밑줄 구분자) | 통과 (`Seed_UnderscoreSeparators_AreIgnored` 3건) |
| 회귀 TC TOOL-004 (위 스크린샷의 명령줄로 turn-replay → 화면과 같은 결과, 큰 시드로 battle-replay · gacha-verify 실행) | 통과 (`TurnReplay_SeedAboveLongMax_ReproducesTheGameResult` 외 3건) |
| 뮤테이션 "[qa-tool] 시드를 long으로 읽음" (`GetULong` 안을 `long.TryParse` + 캐스팅으로 되돌림 = 이 결함 그대로) | TOOL-001 · TOOL-002 · TOOL-003 · TOOL-004가 검출 |
| 뮤테이션 "[qa-tool] battle-replay만 시드를 long으로 읽음" (호출부 하나만 되돌림) | TOOL-004가 검출 — 전체 33/33 |
| 전체 파이프라인 | `bash scripts/test-all.sh`(Unity 포함) 모든 단계 통과 — dotnet 104/104 · qa-tool 23/23 · Unity EditMode 104/104 · PlayMode 6/6, 추적 리포트 TC 93개 중 자동화 90 통과 · 수동 3 |

테스트는 새 프로젝트 [`dotnet/QaTool.Tests`](../../dotnet/QaTool.Tests/SeedOptionTests.cs)에 있습니다. `qa-tool`은 Unity에 들어가지 않으므로 공유 테스트 폴더(`Tests/Shared`)가 아니라 dotnet 전용으로 두었고, TC 문서의 실행환경은 `qa-tool`입니다.

## 발견 경위

PR #4에서 턴제 결과 화면에 리플레이 명령줄을 찍게 한 뒤, **실제 빌드에서 나온 줄이 정말 그대로 도는지** 확인하려고 자동 플레이 스크린샷의 명령줄을 qa-tool에 붙여 넣었습니다. 목적은 "Unity(Mono)에서 한 전투가 .NET 도구에서 같은 결과로 다시 나오는가"였는데, 전투 결과를 비교하기도 전에 옵션 오류로 멈췄습니다. 그 전까지 손으로 넣던 시드(1, 3, 5)로는 한 번도 볼 수 없던 오류입니다. (수정 커밋 `ae98211`에 턴제 자동 플레이 스크린샷과 이 수정이 함께 들어 있습니다.)

돌아보면 두 가지가 겹쳐야 보이는 결함이었습니다.

- **테스트 데이터가 "사람이 고른 값"뿐이었다.** 작은 시드만 쓰면 시드 범위의 위쪽 절반은 한 번도 지나가지 않습니다. 경계값(타입의 최대값 근처)을 TC로 두지 않았습니다.
- **도구는 테스트 대상이 아니라고 여겼다.** 게임 규칙에는 TC·뮤테이션까지 걸어 두고, 그 규칙을 재현하는 도구의 입력 처리는 아무도 확인하지 않았습니다.

그래서 수정과 별도로 ① 도구 입력의 경계값 TC(TOOL-001~003), ② **게임이 실제로 만든 명령줄**을 그대로 넣는 회귀 TC(TOOL-004), ③ 이 결함을 그대로 되살리는 뮤테이션을 추가했습니다. 빌드 스모크 체크리스트의 SMK-14("`Player.log`의 `[TurnReplay]` 줄을 qa turn-replay에 붙여 넣어 같은 결과인지 확인")도 같은 경로를 사람이 한 번 더 지나가게 합니다.
