# 구조와 테스트 층

[← README](../README.md)

## 폴더 구조

```
UnityProject/Assets/
  TinyExpedition/
    Core/            ← 게임 규칙 전부 (엔진 참조 없음, noEngineReferences)
      Random/          PCG32 시드 난수 (런타임이 달라도 같은 수열)
      Data/            기획 데이터 로더 + DataValidator
      Gacha/           모집·천장·10연 보정, 천장 포함 실제 확률 계산
      Battle/          결정적 자동 전투 시뮬레이터 + 턴제 전투(시드 + 명령 목록으로 리플레이)
      MiniGame/        타이밍 탭 판정·콤보·리플레이
      Progress/        세이브 직렬화·변조 검사
      GameSession.cs   UI와 테스트가 함께 쓰는 입구
    Game/            ← 화면 (Core를 보여주고 입력만 넘김)
    Editor/          ← 에디터 메뉴, 빌드 전 데이터 검사 훅, CI용 진입점
    Tests/Shared/    ← NUnit 테스트 (Unity EditMode와 dotnet test가 같은 파일을 컴파일)
    Tests/PlayMode/  ← 실제 화면을 띄워 버튼을 누르는 UI 테스트
  StreamingAssets/GameData/   characters.json · banners.json · stages.json · skills.json · turn-stages.json
dotnet/              ← Core·테스트를 Unity 없이 빌드하는 프로젝트 + qa-tool(+ 도구 자체 테스트 QaTool.Tests)
docs/                ← TC 문서, 테스트 계획
  checklists/          빌드 스모크·아웃게임 릴리즈 체크리스트 (CSV)
  bug-reports/         버그 리포트 (BUG = 실제 결함, DEMO = 시연용)
  spec-review/         기획 리뷰 (모집·천장)
  reports/             확률 검증·TC 추적·커버리지 리포트, 자동 플레이 스크린샷
samples/broken-data/ ← 일부러 틀리게 만든 기획 데이터 (도구 시연용)
scripts/             ← 전체 파이프라인, Unity 테스트 실행, 뮤테이션 스모크
```

## 왜 이렇게 나눴나

게임 규칙을 엔진과 분리한 `Core`에 모았습니다. 그래서

1. **테스트가 빠릅니다.** 112개가 1초 안에 끝나고, Unity를 켜지 않고도 CI에서 돌 수 있습니다(Unity 라이선스 불필요).
2. **같은 테스트를 두 곳에서 돌립니다.** `Tests/Shared`는 Unity EditMode 테스트 어셈블리이면서, `dotnet/Core.Tests`가 같은 파일을 링크해 컴파일합니다. 난수를 PCG 공식 레퍼런스 값과 대조하는 테스트(RNG-001)가 .NET과 Unity(Mono) 양쪽에서 통과해, 런타임이 달라도 같은 시드면 같은 결과가 나옴을 확인했습니다.
3. **도구가 게임 코드를 그대로 씁니다.** 확률 검증기는 확률 로직을 따로 다시 짜지 않고, 게임이 쓰는 `GachaService`를 그대로 100만 번 돌립니다. 검증 대상과 검증 도구가 어긋날 일이 없습니다.

공유 소스는 Unity 6 컴파일러에 맞춰 C# 9 문법으로 제한했습니다(`Directory.Build.props`의 `LangVersion`). C# 10 이상 문법을 쓰면 dotnet 빌드에서 먼저 깨집니다.

## 테스트 전략

| 층 | 무엇을 | 어디서 | 수 |
|---|---|---|---|
| 규칙·리소스 단위 테스트 | 모집·천장·전투 공식·턴제 경계(순서·쿨타임·상태이상·전멸·보상)·턴제 화면 상태·판정 경계·세이브 | dotnet + Unity EditMode | 112 |
| QA 도구 테스트 | 리플레이 시드 옵션 경계(0 · long 최대 · long 최대+1 · ulong 최대 · 음수 · 숫자 아님), 게임이 찍은 명령줄 그대로 리플레이 | dotnet (qa-tool은 Unity에 없음) | 23 |
| 통계 테스트 | 등급 분포(카이제곱), 픽업 비율(천장 3성 포함), 천장 포함 실제 확률 | 위와 같음 (시드 고정이라 흔들리지 않음) | 4 (위 112에 포함) |
| 데이터 테스트 | 실제 테이블 검사 + 잘못된 테이블을 잡는지 | 위와 같음 + Unity 빌드 훅 | 18 (위 112에 포함) |
| 리소스 테스트 | 데이터의 캐릭터·적마다 그림 파일이 있는지, SVG 원본과 PNG가 맞는지, 부위 그림 크기가 같은지 | 위와 같음 | 5 (위 112에 포함) |
| UI E2E | 부팅 → 모집 → 결과, 미니게임 한 판, 그림 누락(모집 카드·전투 그림), 전투 일러스트 부위, 턴제 대상 선택·공격·자동·보상, 턴제 후퇴, 2.5D 전투 화면의 HP·쓰러짐이 Core와 같은지, 배속·건너뛰기 결과 동일, 연출 중 연타 | Unity PlayMode | 11 |
| 빌드 스모크 | 실제 Windows 개발 빌드가 홈→모집→전투→미니게임→2.5D 턴제 전투를 스스로 진행, 단계별 스크린샷 · 화면 HP 대조 · 프레임 시간 기록 | `TinyExpedition.exe -qaAutoplay <폴더>` | 1 흐름 |
| 수동 탐색 | 실기기 터치 지연, 화면 비율, 턴제 템포·연출·가독성 | 사람 | 3 |

TC 문서의 모든 자동화 TC는 테스트 코드에 `[TC("GACHA-004")]`처럼 번호가 붙어 있어,
리포트 도구가 **TC 문서 ↔ 테스트 ↔ 결과**를 자동으로 잇습니다. 문서에만 있고 테스트가 없는 TC, 테스트에만 있고 문서에 없는 번호는 리포트에서 실패로 잡힙니다.

자세한 기준은 [`TEST_PLAN.md`](TEST_PLAN.md)에 있습니다.
