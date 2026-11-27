# 작은 원정대 (Tiny Expedition) — 게임 QA 자동화 포트폴리오

[![QA](https://github.com/ppastar/tiny-expedition-qa/actions/workflows/qa.yml/badge.svg)](https://github.com/ppastar/tiny-expedition-qa/actions/workflows/qa.yml)

수집형 턴제 RPG를 작게 직접 만들고(Unity 6), 그 게임을 대상으로 **TC 설계 → 자동화 테스트 → QA 도구 → 버그 리포트 → CI**를 한 흐름으로 구현한 프로젝트입니다.
QA로 일을 시작해 SDET로 넘어온 경험을, "수집형 RPG를 출시·운영할 때 QA가 실제로 걱정하는 것"에 맞춰 옮겼습니다.

![2.5D 턴제 전투 — 실제 빌드가 스스로 플레이하며 찍은 화면](docs/reports/autoplay/10-turn-battle-attack.png)

| | 결과 | 근거 |
|---|---|---|
| TC 문서 | 99개 (자동화 96 · 수동 3) | [`docs/test-cases.csv`](docs/test-cases.csv) |
| 자동화 TC 통과 | **96 / 96** | [TC 추적 리포트](docs/reports/test-report.html) — 문서 ↔ 테스트 ↔ 결과를 TC 번호로 대조 |
| 자동화 테스트 | 규칙 107 · QA 도구 23 · 화면(Unity PlayMode) 9 | 규칙 107개는 dotnet과 Unity에서 같은 소스로 실행 |
| 뮤테이션 스모크 | **38 / 38** 검출 | 그럴듯한 버그 38개를 코드에 넣어 테스트가 잡는지 확인 |
| 모집 확률 검증 | 배너당 100만 회, 전부 통과 | [확률 검증 리포트](docs/reports/gacha-report.md) |
| 실제로 찾은 결함 | 3건 (모두 수정 · 회귀 TC 있음) | [버그 리포트](docs/bug-reports/README.md) |

> 게임의 캐릭터·이름·그림·규칙 수치는 모두 이 프로젝트용으로 새로 만든 것이며, 특정 게임을 복제한 것이 아닙니다. 그림은 전부 코드로 그렸습니다([출처](docs/ART.md)).

## 턴제 전투 QA에서 본 것

턴제 전투(속도 순서 · 스킬 쿨타임 · 기절/독 · 3웨이브 · 자동/배속/건너뛰기)에서 QA가 걱정할 것을 다섯 가지로 잡고, 각각 자동으로 확인합니다.

| 걱정 | 어떻게 확인하나 | TC |
|---|---|---|
| **"가끔 나는" 전투 버그를 재현할 수 있는가** | 전투는 **시드 + 아군 명령 목록**만 있으면 같은 결과가 나옵니다. 결과 화면에 리플레이 명령줄이 찍히고, `qa turn-replay`에 붙이면 같은 판이 다시 나옵니다. 실제 빌드에서 플레이한 판(명령 41개)을 .NET에서 다시 돌려 마지막 HP까지 같음을 확인했습니다. | TURN-012 · TOOL-004 |
| **화면에 보이는 HP가 진짜 HP인가** | 연출은 계산하지 않고 Core가 남긴 로그를 재생만 합니다. "여기까지 재생했을 때 보여야 하는 상태"를 엔진 밖 클래스로 분리해 300판(스테이지 3 × 파티 4 × 시드 25)을 명령마다 대조하고, 실제 화면의 HP 글자·쓰러짐도 Core와 대조합니다. | TURN-025~028 |
| **배속·건너뛰기를 하면 결과가 달라지지 않는가** | 배속·건너뛰기는 재생 속도만 바꿉니다. 같은 판을 시작하자마자 건너뛰기 / 30배속으로 끝까지 / 4배속 도중 건너뛰기로 돌려 결과가 같은지, 연출 중 연타로 명령이 잘못 들어가지 않는지 실제 화면에서 확인합니다. | TURN-029 · 030 |
| **경계에서 규칙이 맞는가** | 속도 동점 순서, 쿨타임이 풀리는 차례, 기절 갱신·독 최대 3중첩, 동시 전멸 = 패배, 보상 중복 지급 방지 등. 규칙을 [`TurnBattle.cs`](UnityProject/Assets/TinyExpedition/Core/Battle/TurnBattle.cs) 맨 위에 기획서처럼 적고 경계마다 TC를 붙였습니다. | TURN-001~022 |
| **이 테스트들이 정말 버그를 잡는가** | 쿨타임 off-by-one, 동시 전멸을 승리로 판정, 기절 합산 같은 버그를 코드에 하나씩 넣고 테스트가 실패하는지 봅니다. 턴제 규칙 11개 · 턴제 화면 상태 5개 포함 38개 모두 검출. | [뮤테이션 스모크](docs/QA_TOOLS.md#5-뮤테이션-스모크-scriptsmutation_smokepy) |

같은 방식으로 모집(확률표 = 실제 확률, 천장 경계, 재화 차감), 기획 데이터(한 줄 실수가 빌드에 들어가지 않게 빌드 전 검사), 미니게임 판정 경계, 세이브 변조도 다룹니다 — 영역별 위험과 대응은 [테스트 계획 1장](docs/TEST_PLAN.md#1-범위)에 있습니다.

## 실제로 찾은 결함

이 프로젝트를 만들면서 실제로 있었던 결함 3건입니다. 모두 수정했고 회귀 TC가 있습니다.

| ID | 요약 | 발견 · 재현 | 회귀 TC |
|---|---|---|---|
| [BUG-001](docs/bug-reports/BUG-001-gems-taken-on-failed-pull.md) | 모집이 데이터 오류로 실패해도 보석이 먼저 빠지고 결과는 없음 (Critical) | 첫 커밋(15562c3)에 있던 결함. 그 커밋을 받아 10/10 재현 | GACHA-015 |
| [BUG-002](docs/bug-reports/BUG-002-ten-pull-guarantee-without-two-star-pool.md) | "2성 0% + 2성 후보 없음 + 10연 보정" 배너가 데이터 검사를 통과하고, 10연이 시드 100개 중 76개에서 실패 (Major) | [모집 기획 리뷰](docs/spec-review/gacha.md)에서 규칙이 겹치는 곳을 질문으로 정리하다가 발견 | DATA-010 · GACHA-015 |
| [BUG-003](docs/bug-reports/BUG-003-replay-seed-above-long-max-rejected.md) | 게임이 찍어 준 시드가 long 최대값보다 크면 QA 도구가 거부해 리플레이 불가, 음수 시드는 오류 없이 다른 판을 돌림 (Major) | 실제 빌드 자동 플레이의 명령줄을 도구에 붙여 넣다가 발견 | TOOL-001~004 |

버그 리포트 폴더의 나머지 3건(DEMO-001~003)은 뮤테이션용 결함을 **일부러 넣고** 리포트 양식을 보여 주는 **시연용**이며, 실제 결함이 아닙니다. 각 문서 맨 위에도 그렇게 표시했습니다.

## 문서

| 문서 | 내용 |
|---|---|
| [TC 문서](docs/test-cases.csv) | 99개. 영역 · 우선순위(P0 48 · P1 39 · P2 12) · 유형 · 실행 환경 |
| [테스트 계획](docs/TEST_PLAN.md) | 위험별 대응, TC 작성 기준, 통계 판정 기준, 릴리스 기준, [개발 단계별 계획](docs/TEST_PLAN.md#7-개발-단계별-테스트-계획) |
| [버그 리포트](docs/bug-reports/README.md) | Jira 양식. 실제 결함 3건 + 시연용 3건, 시드·리플레이로 재현한 출력 첨부 |
| [기획 리뷰 (모집·천장)](docs/spec-review/gacha.md) | 규칙 11개 정리, 질문 13개 · 리스크 · TC 연결. 사후 작성임을 문서에 밝힘 |
| 체크리스트 | [빌드 스모크 15항목](docs/checklists/build-smoke.csv) · [아웃게임 릴리즈 30항목](docs/checklists/outgame-release.csv) (CSV, 항목마다 관련 TC) |
| [QA 도구](docs/QA_TOOLS.md) | 확률 검증기 · 데이터 검사기 · 전투/턴제/미니게임 리플레이 · TC 추적 리포트 · 뮤테이션 스모크 · 디버그 패널 · 빌드 자동 플레이 — 사용법과 실제 출력 |
| [구조와 테스트 층](docs/ARCHITECTURE.md) | 게임 규칙을 엔진과 분리한 이유, 폴더 구조, 테스트 층별 범위 |
| 리포트 | [TC 추적](docs/reports/test-report.html) · [확률 검증](docs/reports/gacha-report.md) · [커버리지 요약](docs/reports/coverage-summary.md)(규칙 코드 라인 95.7%) · [자동 플레이 스크린샷](docs/reports/autoplay/contact-sheet.png) |

## 실행

```bash
# 전체 파이프라인: 테스트 → 데이터 검사 → 확률 검증 → TC 추적 리포트 (Unity 없이, .NET 10)
bash scripts/test-all.sh

# Unity EditMode·PlayMode 테스트까지 (Unity 6000.5 + 라이선스 필요)
UNITY="/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Unity.exe" bash scripts/test-all.sh

# 뮤테이션 스모크
python scripts/mutation_smoke.py
```

Unity에서 열 때는 Unity Hub에서 `UnityProject` 폴더를 추가하고, 메뉴 **Tiny Expedition → Main 씬 만들기**를 한 번 실행한 뒤 Play 합니다.

## AI와 함께 만든 방식

Claude Code(AI 코딩 도구)와 함께 만들었고, 커밋의 `Co-Authored-By: Claude`가 그 기록입니다.

- **사람(저)**: 무엇이 위험한지, 어떤 TC가 필요한지, 판정 기준(카이제곱 유의수준 · 허용 오차 · 경계값 세 점), 릴리스 기준을 정했습니다.
- **AI**: 정한 기준에 맞춰 게임 코드 · 테스트 · QA 도구 · 문서 초안을 구현하고 실행 결과를 보고했습니다.
- **검증 장치**: AI가 쓴 테스트는 "통과한다"만으로 믿을 수 없어서(아무것도 확인하지 않는 테스트도 통과합니다), 테스트가 실제로 버그를 잡는지는 뮤테이션 스모크로, 문서와 테스트가 어긋나지 않는지는 TC 추적 리포트로 확인합니다.

이 저장소 문서의 출력·수치는 모두 실제로 실행해 받은 값입니다.

## 현재 상태와 한계

- 알려진 열린 결함 없음.
- CI(GitHub Actions, Ubuntu)는 Unity 없이 도는 부분(규칙 107 · QA 도구 23 · 데이터 검사 · 확률 검증 · 추적 리포트 · 뮤테이션)만 돌립니다. Unity EditMode · PlayMode 테스트와 빌드 자동 플레이는 라이선스가 필요해 로컬(Windows, Unity 6000.5.6f1)에서 돌린 결과입니다.
- 수동 TC 3개(실기기 터치 지연, 화면 비율, 턴제 템포·가독성)는 사람이 봐야 하는 항목으로 남겨 두었습니다.
- 서버 · 결제 · 네트워크는 범위 밖입니다(오프라인 게임). 성능은 전투 화면 프레임 시간만 자동 플레이가 기록합니다(1280×720 개발 빌드, 개발 PC 한 대 기준 평균 8.5ms · 최대 58ms).
- 실행 대상은 Windows · 에디터입니다. Android · WebGL은 데이터 읽기(`UnityWebRequest`)와 한글 폰트 포함을 바꿔야 합니다.
