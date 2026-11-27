# 그림 출처

이 게임의 그림은 **전부 이 프로젝트에서 직접 만든 것**입니다. 외부 그림·모델·이펙트·폰트 파일, 에셋 스토어 패키지, AI 생성 이미지는 쓰지 않았습니다.
캐릭터·적의 이름과 생김새도 이 프로젝트용으로 새로 만든 것이며, 특정 게임의 캐릭터·UI·이펙트를 옮기거나 본뜬 것이 아닙니다.

| 무엇 | 어떻게 | 어디 |
|---|---|---|
| 아군 9명 · 적 6종 (머리 큰 SD 비율) | 캐릭터마다 SVG 벡터 일러스트를 직접 작성(곡선 실루엣, 부위별 색 외곽선, 2단 명암, 눈 하이라이트, 머리카락 결). 빌드 전에 PNG로 바꿔 스프라이트로 씀 | 원본 [`art/units/*.svg`](../art/units) → [`StreamingAssets/BattleArt`](../UnityProject/Assets/StreamingAssets/BattleArt) |
| 모집 결과 카드 | 같은 SVG를 등급 색 배경 위에 올려 한 장으로 만듦 | [`StreamingAssets/Skins/<캐릭터 id>.png`](../UnityProject/Assets/StreamingAssets/Skins) |
| 그림이 빠졌을 때의 기본 그림 | 원·타원·다각형을 거리 함수로 칠해 `Texture2D`에 그림. 모르는 적 이름은 말랑한 덩어리 | [`BattleArt.cs`](../UnityProject/Assets/TinyExpedition/Game/Battle/BattleArt.cs) |
| 배경·바닥·소품 | 코드로 그림: 하늘 그라데이션, 봉우리, 버섯·수정·바위, 반복 바닥 무늬. 스테이지마다 색만 다름 | `BattleArt.Backdrop` 등 |
| 이펙트 (타격·베기·회복·별·독 방울·방패·그림자) | 코드로 그림 | `BattleArt` 아래쪽 |
| 셰이더 | Unity 내장 `Sprites/Default` | — |
| 글자 | OS 폰트(맑은 고딕 등)를 실행 중에 불러 씀. 폰트 파일을 저장소·빌드에 넣지 않음 | `UiKit.Font` |

## 캐릭터 그림 만드는 방법

```bash
cd art
pnpm install        # @resvg/resvg-js 2.6.2 (버전 고정, MPL-2.0 오픈소스 SVG 렌더러)
pnpm render         # art/units/*.svg → BattleArt/*.png, Skins/*.png, manifest.json
pnpm check          # SVG를 고치고 PNG를 다시 안 만든 것이 있으면 실패
```

- SVG 한 장이 캐릭터 한 명입니다. 맨 위 그룹 `L-back`(뒷머리) · `L-wings`(날개) · `L-body` · `L-head`가 부위이고, 같은 크기의 PNG로 한 장씩 뽑습니다. 눈은 뜬 것(`eyes-open`)과 감은 것(`eyes-closed`)을 같이 그려 두고 감은 눈으로 한 장 더 뽑습니다.
- 게임은 부위를 겹쳐 세우고 숨쉬기, 머리 까딱임, 눈 깜빡임, 날갯짓, 공격할 때 기울기를 붙입니다([`BattleStage.cs`](../UnityProject/Assets/TinyExpedition/Game/Battle/BattleStage.cs)). 이 움직임은 연출일 뿐이고 전투 결과와 무관합니다.
- 2배 크기로 뽑습니다(아군 576×704). `manifest.json`에는 부위 목록, 머리·날개 축, SVG 본문의 해시가 들어갑니다.

## 그림이 빠지면

- 전투: 그 유닛만 코드로 그린 기본 그림으로 대신하고 전투는 그대로 진행합니다(ART-006). 빠진 키는 `BattleArt.MissingArt`에 남습니다.
- 모집 카드: 등급 색 + 이름 첫 글자 카드로 대신합니다(UI-004).
- 화면이 멀쩡히 돌아가므로 누락은 테스트가 잡습니다: 데이터의 캐릭터 id·적 이름마다 그림 파일이 있는지(ART-001·002), SVG와 PNG가 맞는지(ART-004), 부위 크기가 같은지(ART-005). 에디터 메뉴 `Tiny Expedition/그림 리소스 누락 검사`로도 볼 수 있습니다.
