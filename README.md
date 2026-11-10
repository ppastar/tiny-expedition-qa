# 작은 원정대 (Tiny Expedition) — 게임 QA 자동화 포트폴리오

모집(가챠)·자동 전투·미니게임이 있는 작은 Unity 게임을 직접 만들고,
**테스트 케이스 설계 → 자동화 테스트 → QA 도구 → CI**까지 한 흐름으로 구현한 프로젝트입니다.

QA로 일을 시작해 SDET·플랫폼 엔지니어로 넘어온 경험을 게임 도메인에 옮겨 본 것으로,
"수집형 RPG를 출시·운영할 때 QA가 실제로 걱정하는 것"을 기준으로 만들었습니다.

- 확률표와 실제 확률이 다르지 않은가 (확률형 아이템 정보 공개)
- 기획 데이터 한 줄 실수가 그대로 빌드에 들어가지 않는가
- 재화가 두 번 지급되거나 빠지지 않는가
- "가끔 일어나는" 전투·미니게임 버그를 그대로 재현할 수 있는가

> 게임의 캐릭터·이름·그림·규칙 수치는 모두 이 프로젝트용으로 새로 만든 것입니다.

## 한눈에 보기

| 항목 | 내용 |
|---|---|
| 게임 | Unity 6 (6000.5) · uGUI를 코드로 구성 · 모집 / 원정(자동 전투) / 미니게임 "별빛 타이밍" / QA 디버그 패널 |
| TC 문서 | [`docs/test-cases.csv`](docs/test-cases.csv) — 59개 (자동화 57 · 수동 2), 영역·우선순위·유형·실행 환경 포함 |
| 자동화 테스트 | NUnit 71개 (엔진 없이 dotnet에서도, Unity EditMode에서도 같은 소스로 실행) + Unity PlayMode UI 테스트 4개 |
| QA 도구 | `qa-tool` CLI — 데이터 검사 · 확률 검증 · 전투/미니게임 리플레이 · TC 추적 리포트 / 뮤테이션 스모크 / Unity 에디터 메뉴·빌드 훅 |
| CI | GitHub Actions — 테스트·데이터 검사·확률 검증(배너당 100만 회)·리포트를 PR마다 실행 |

## 구조

```
UnityProject/Assets/
  TinyExpedition/
    Core/            ← 게임 규칙 전부 (엔진 참조 없음, noEngineReferences)
      Random/          PCG32 시드 난수 (런타임이 달라도 같은 수열)
      Data/            기획 데이터 로더 + DataValidator
      Gacha/           모집·천장·10연 보정, 천장 포함 실제 확률 계산
      Battle/          결정적 자동 전투 시뮬레이터
      MiniGame/        타이밍 탭 판정·콤보·리플레이
      Progress/        세이브 직렬화·변조 검사
      GameSession.cs   UI와 테스트가 함께 쓰는 입구
    Game/            ← 화면 (Core를 보여주고 입력만 넘김)
    Editor/          ← 에디터 메뉴, 빌드 전 데이터 검사 훅, CI용 진입점
    Tests/Shared/    ← NUnit 테스트 (Unity EditMode와 dotnet test가 같은 파일을 컴파일)
    Tests/PlayMode/  ← 실제 화면을 띄워 버튼을 누르는 UI 테스트
  StreamingAssets/GameData/   characters.json · banners.json · stages.json
dotnet/              ← Core·테스트를 Unity 없이 빌드하는 프로젝트 + qa-tool
docs/                ← TC 문서, 테스트 계획
samples/broken-data/ ← 일부러 틀리게 만든 기획 데이터 (도구 시연용)
scripts/             ← 전체 파이프라인, Unity 테스트 실행, 뮤테이션 스모크
```

### 왜 이렇게 나눴나

게임 규칙을 엔진과 분리한 `Core`에 모았습니다. 그래서

1. **테스트가 빠릅니다.** 71개가 1초 안에 끝나고, Unity를 켜지 않고도 CI에서 돕니다(Unity 라이선스 불필요).
2. **같은 테스트를 두 곳에서 돌립니다.** `Tests/Shared`는 Unity EditMode 테스트이면서, `dotnet/Core.Tests`가 같은 파일을 링크해 컴파일합니다. 엔진 런타임(Mono)과 .NET에서 결과가 같은지도 함께 확인됩니다.
3. **도구가 게임 코드를 그대로 씁니다.** 확률 검증기는 확률 로직을 따로 다시 짜지 않고, 게임이 쓰는 `GachaService`를 그대로 100만 번 돌립니다. 검증 대상과 검증 도구가 어긋날 일이 없습니다.

공유 소스는 Unity 6 컴파일러에 맞춰 C# 9 문법으로 제한했습니다(`Directory.Build.props`의 `LangVersion`). C# 10 이상 문법을 쓰면 dotnet 빌드에서 먼저 깨집니다.

## 테스트 전략

| 층 | 무엇을 | 어디서 | 수 |
|---|---|---|---|
| 규칙 단위 테스트 | 모집·천장·전투 공식·판정 경계·세이브 | dotnet / Unity EditMode | 71 |
| 통계 테스트 | 등급 분포(카이제곱), 픽업 비율, 천장 포함 실제 확률 | dotnet / Unity EditMode (시드 고정이라 흔들리지 않음) | 3 (위 71에 포함) |
| 데이터 테스트 | 실제 테이블 검사 + 잘못된 테이블을 잡는지 | dotnet / 빌드 훅 | 9 (위 71에 포함) |
| UI E2E | 부팅 → 모집 → 결과, 미니게임 한 판, 그림 누락 | Unity PlayMode | 4 |
| 수동 탐색 | 실기기 터치 지연, 화면 비율 | 사람 | 2 |

TC 문서의 모든 자동화 TC는 테스트 코드에 `[TC("GACHA-004")]`처럼 번호가 붙어 있어,
리포트 도구가 **TC 문서 ↔ 테스트 ↔ 결과**를 자동으로 잇습니다. 문서에만 있고 테스트가 없는 TC, 테스트에만 있고 문서에 없는 번호는 리포트에서 실패로 잡힙니다.

자세한 기준은 [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md)에 있습니다.

## QA 도구

```bash
cd dotnet && dotnet build && cd ..
alias qa="dotnet run --project dotnet/QaTool --no-build --"
```

### 1. 확률 검증기 `qa gacha-verify`

게임의 모집 코드를 배너당 100만 번 돌려 공시한 확률표와 비교합니다.

- 기본 등급 확률: 카이제곱 적합도 검정 (p ≥ 0.001), 등급별 95% 신뢰구간
- 등급 안 캐릭터 분포: 픽업 50% / 나머지 균등이 맞는지
- **천장 포함 실제 3성 확률**: 기본 2.5% + 90회 천장이면 실제로는 약 2.79%. 공시값과 관측값을 비교
- 천장 동작: 3성 사이 간격이 90회를 넘지 않는지, 간격 분포 히스토그램
- 10연 보정: 10만 번의 10연 중 2성 이상이 없는 경우가 0번인지

```
[b_lumi] 달빛 픽업 모집
  PASS  기본 등급 확률 — χ²=5.71 (자유도 2), p=0.0575
  PASS  ★★★ 캐릭터 분포 — χ²=0.23, p=0.8912
  PASS  공시한 실제 3성 획득률 — 관측 2.787% vs 공시 2.79% (허용 ±0.071%)
  PASS  천장 동작 — 가장 긴 간격 90회 / 천장 90회
  PASS  10연 2성 이상 보정 — 10연 100,000번 중 위반 0번
```

### 2. 기획 데이터 검사기 `qa validate-data`

확률 합계 100%, 없는 캐릭터 참조, 풀 등급 불일치, 픽업 누락, 스탯 범위, 스테이지 선행 관계 순환,
**공시한 천장 포함 확률이 계산값과 맞는지**까지 검사합니다. 같은 검사가 Unity 빌드 직전 훅(`IPreprocessBuildWithReport`)에도 걸려 있어, 데이터가 틀리면 빌드가 실패합니다.

```
$ qa validate-data --data samples/broken-data
[Error] banners/b_standard: 등급 확률 합계가 100%가 아닙니다: 99.5%
[Error] banners/b_standard: 공시한 천장 포함 3성 확률 2.5%가 계산값 2.79%와 다릅니다.
[Error] banners/b_lumi: 없는 캐릭터를 참조합니다: c_lumii
[Error] stages/1-1: 선행 스테이지가 순환합니다.
...
캐릭터 10 · 배너 2 · 스테이지 4 — 오류 11, 경고 0
```

### 3. 버그 재현 도구 `qa battle-replay` / `qa minigame-replay`

전투와 미니게임은 시드·입력 기록만 있으면 언제든 같은 결과가 나오도록 만들었습니다.
버그 리포트에 시드(또는 미니게임 결과 화면의 리플레이 문자열)만 적으면 개발자가 그대로 재현할 수 있습니다.

```
$ qa battle-replay --stage 1-3 --party c_lumi,c_firefly,c_moss --seed 5
$ qa minigame-replay --taps 0.41,1.93,2.77,3.6,4.42,5.55,6.01,7.2,8.39,9.0
```

### 4. TC 추적 리포트 `qa report`

dotnet과 Unity가 내는 NUnit XML을 TC 문서와 이어 HTML 리포트를 만듭니다.
TC별 상태(통과·실패·자동화 누락·미실행·수동), 영역별 통과율, 실패 메시지를 한 페이지로 봅니다.

```
$ qa report --results dotnet=artifacts/dotnet-results.xml,unity-playmode=artifacts/unity-playmode.xml \
            --cases docs/test-cases.csv --out artifacts/test-report.html
```

### 5. 뮤테이션 스모크 `scripts/mutation_smoke.py`

"테스트가 다 통과한다"는 것만으로는 테스트가 버그를 잡는지 알 수 없어서,
실제로 생길 법한 버그 15개(천장 off-by-one, `<`/`<=` 경계 혼동, 보상 중복 지급, 콤보 초기화 누락 등)를 코드에 하나씩 넣고 테스트가 실패하는지 확인합니다.

```
[검출] 천장 off-by-one (90번째가 아니라 91번째에 확정)  ← GACHA-004, GACHA-005, GACHA-011
[검출] 미니게임 보상 중복 수령  ← MINI-010
[검출] Perfect 판정 경계 미포함 (< 와 <= 혼동)  ← MINI-002
...
검출 15/15
```

### 6. 게임 안 QA 디버그 패널

시드 고정, 보석 지급, 천장 직전(89회)으로 이동, 모든 스테이지 열기, 세이브 초기화.
`UNITY_EDITOR || DEVELOPMENT_BUILD`에서만 컴파일되어 출시 빌드에는 들어가지 않습니다.

## 실행

```bash
# 전체 파이프라인 (Unity 없이)
bash scripts/test-all.sh

# Unity 테스트까지 (Unity Hub 로그인 + 라이선스 활성화 필요)
UNITY="/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Unity.exe" bash scripts/test-all.sh

# 뮤테이션 스모크
python scripts/mutation_smoke.py
```

Unity에서 열 때는 Unity Hub에서 `UnityProject` 폴더를 추가하고, 메뉴 **Tiny Expedition → Main 씬 만들기**를 한 번 실행한 뒤 Play 합니다.

## 현재 상태와 한계

- **dotnet 쪽(규칙 테스트 71개·도구·뮤테이션 15/15)은 실행해서 통과를 확인했습니다.**
- **Unity EditMode/PlayMode 테스트와 에디터 도구는 코드만 작성했고 아직 실행하지 못했습니다.** 작성한 PC에서 Unity 라이선스가 활성화되어 있지 않았습니다. 리포트에서는 이 4개 UI TC가 "미실행"으로 따로 표시됩니다.
- 데이터를 `File` API로 읽으므로 Windows/에디터 기준입니다. Android·WebGL은 `UnityWebRequest`로 StreamingAssets를 읽도록 바꿔야 합니다.
- 한글 표시는 OS 폰트(맑은 고딕 등)에 기대고 있어 WebGL에서는 한글 폰트를 따로 넣어야 합니다.
