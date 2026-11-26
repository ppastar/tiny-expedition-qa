# BUG-001 [모집] 데이터 오류로 모집이 실패해도 보석이 먼저 빠지고 결과는 없음

> **구분: 실제 결함** — 첫 커밋(15562c3)에 있었고 4458613에서 고쳤습니다. 아래 출력은 15562c3을 받아 실제로 돌린 결과입니다.

| 항목 | 내용 |
|---|---|
| 상태 | 수정됨 · 회귀 TC 통과 |
| 심각도 | Critical — 재화가 빠지고 대가(캐릭터)가 지급되지 않음 |
| 우선순위 | P0 |
| 영역 | 모집 / 재화 |
| 발견 커밋 | `15562c3` |
| 수정 커밋 | `4458613` |
| 회귀 TC | [GACHA-015](../test-cases.csv) |
| 관련 이슈 | [BUG-002](BUG-002-ten-pull-guarantee-without-two-star-pool.md) (같은 결함이 검사기를 통과하는 데이터로도 터지는 경로) |

## 환경

| 항목 | 값 |
|---|---|
| 코드 | `15562c3` (수정 전) / `4458613` 이후 (수정 후) |
| OS | Windows 11 Pro 10.0.26300 |
| 런타임 | .NET SDK 10.0.402 (`dotnet test`로 Core 규칙을 엔진 없이 실행) |
| 엔진 | Unity 6000.5.6f1 — **게임 화면에서는 재현하지 않았습니다** (아래 "화면에서의 영향" 참고) |
| 데이터 | 테스트용 배너 `b_test` (아래 사전조건) |

## 사전조건

- 보석 3000
- 배너 `b_test`: 3성 0% / **2성 100% / 1성 0%**, 그런데 **2성 후보 목록이 비어 있음** (기획 데이터 실수)
- `b_test`의 천장 카운트 5

이 배너는 데이터 검사기(`DataValidator`)에서 "TwoStar 확률이 있는데 후보 캐릭터가 없습니다" Error로 걸리므로, 정상 빌드 절차라면 빌드 전 훅에서 막힙니다. 하지만 **검사기를 통과하면서 같은 오류가 나는 데이터가 있습니다** → [BUG-002](BUG-002-ten-pull-guarantee-without-two-star-pool.md).

## 재현 절차

1. 위 배너로 `GameSession`을 만든다 (보석 3000, 천장 5).
2. `b_test` 배너로 10회 모집을 요청한다.
3. 예외를 받은 뒤 보석·천장 카운트·보유 캐릭터를 확인한다.

## 기대 결과

- 모집이 실패로 끝나고 **보석 3000, 천장 5, 보유 캐릭터 없음** 그대로.
- 화면에는 "모집할 수 없음" 안내.

## 실제 결과 (15562c3)

- `InvalidOperationException: 배너 b_test에 TwoStar 후보가 없습니다.`가 `Pull`을 호출한 쪽까지 그대로 올라옴 (실패 결과가 아니라 예외).
- **보석 3000 → 1500** (10연 비용 1500이 빠짐)
- **천장 카운트 5 → 6** (첫 장을 뽑는 도중 카운트가 올라간 뒤 예외)
- 보유 캐릭터 0 (결과 없음)
- 1회 모집도 같음: 보석 3000 → 2850, 천장 5 → 6

## 재현율

| 조건 | 결과 |
|---|---|
| 10회 모집, 시드 1~10 | **10/10** 재현 (모두 보석 1500, 천장 6, 보유 0) |
| 1회 모집, 시드 1 | 1/1 재현 (보석 2850, 천장 6, 보유 0) |

2성이 100%라 첫 장에서 항상 빈 후보 목록을 만나므로 시드와 관계없이 재현됩니다.

## 첨부

### 재현 코드 (15562c3에 임시로 넣고 실행, 저장소에는 넣지 않음)

```csharp
var banner = TestData.Banner(0, 10000, 0);           // 3성 0% / 2성 100% / 1성 0%
banner.Pool[Rarity.TwoStar].Clear();                  // 2성 후보 없음
var s = new GameSession(TestData.Db(new[] { banner }), new PlayerState { Gems = 3000 });
s.State.Pity["b_test"] = new PityState { PullsSinceThreeStar = 5 };
s.Reseed(seed);
try { s.Pull("b_test", count); } catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
// gems, pity, owned 출력
```

```
$ git checkout 15562c3
$ dotnet test dotnet/Core.Tests --filter "FullyQualifiedName~ReproBug001"
```

### 실행 출력 (15562c3)

```
exception = InvalidOperationException: 배너 b_test에 TwoStar 후보가 없습니다.
count=10 seed=1 gems = 1500, pity = 6, owned = 0
count=10 seed=2 gems = 1500, pity = 6, owned = 0
...
count=10 seed=10 gems = 1500, pity = 6, owned = 0
count=1 seed=1 gems = 2850, pity = 6, owned = 0

  실패 BrokenBanner_TenPull_Repro
     실패한 모집은 보석을 가져가면 안 됨
  Expected: 3000
  But was:  1500
```

### 화면에서의 영향

15562c3의 화면 코드(`GameApp.DoPull`)는 `Pull`이 예외를 던지는 경우를 처리하지 않았습니다. 코드상으로는 버튼 콜백에서 예외가 나고 `Save()`가 호출되지 않지만, 메모리의 보석은 이미 줄어 있어 다음 저장 때 그대로 기록됩니다. **이 부분은 Unity 화면에서 실행해 확인하지 않았으므로** 스크린샷은 없습니다.

## 원인

`GameSession.Pull`(15562c3)이 **보석을 먼저 빼고** 그다음 뽑기를 돌렸고, 뽑기는 **저장된 천장 상태를 직접** 바꿨습니다. 뽑기 도중 예외가 나면 그 앞에서 바뀐 보석·천장이 되돌려지지 않았습니다.

```csharp
// 15562c3
State.Gems -= (int)cost;                                   // 1) 먼저 차감
IReadOnlyList<PullResult> results = count == 1
    ? new[] { _gacha.PullOne(banner, pity, _rng) }         // 2) 여기서 예외 → 1)은 그대로
    : _gacha.PullTen(banner, pity, _rng);
```

## 수정 (4458613)

- 천장 **사본**으로 뽑기를 끝까지 돌리고, 성공했을 때만 보석·천장·보유를 한 번에 반영.
- 뽑기가 `InvalidOperationException`을 내면 난수도 행동 전 상태로 되돌리고 `ActionError.DataError`를 돌려줌 (예외가 화면까지 올라가지 않음).
- 화면: `DataError`면 "모집 데이터 오류로 모집할 수 없습니다. (보석은 차감되지 않았습니다)" 토스트.

## 수정 확인

| 확인 | 결과 |
|---|---|
| 회귀 TC GACHA-015 ("데이터 오류로 모집이 실패하면 보석·천장 그대로") 추가 | 현재 코드에서 통과 (`BrokenBanner_FailsWithoutTakingGems`: DataError, 보석 3000, 천장 5, 보유 없음) |
| 뮤테이션 스모크에 "모집 실패해도 보석이 먼저 빠짐" 추가 | GACHA-015가 검출 |
| 전체 파이프라인 | 현재 코드(`1db01a0`)에서 `bash scripts/test-all.sh` 모든 단계 통과 (dotnet 테스트 72/72) |

참고: 뮤테이션 스모크의 이 항목은 수정된 코드의 실패 처리(`catch`) 안에 보석 차감을 끼워 넣어 **같은 증상을 흉내 낸 것**입니다. 이 결함의 1차 근거는 위의 15562c3 실행 출력입니다.
