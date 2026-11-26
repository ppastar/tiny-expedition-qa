# 그림 출처

이 게임의 그림은 **전부 이 프로젝트에서 코드로 그린 것**입니다. 외부 그림·모델·이펙트·폰트 파일, 에셋 스토어 패키지는 쓰지 않았습니다.
캐릭터·적의 이름과 생김새도 이 프로젝트용으로 새로 만든 것이며, 특정 게임의 캐릭터·UI·이펙트를 옮기거나 본뜬 것이 아닙니다.

| 무엇 | 어떻게 | 코드 |
|---|---|---|
| 아군 9명 (머리 큰 SD 비율) | 원·타원·둥근 사각형·다각형을 거리 함수로 칠해 `Texture2D`에 그림. 캐릭터마다 색·머리 모양·모자·소품을 다르게 | [`BattleArt.cs`](../UnityProject/Assets/TinyExpedition/Game/Battle/BattleArt.cs) `Ally` |
| 적 6종 (박쥐·거미·달팽이·개구리·요정·골렘) | 같은 방식. 데이터의 적 이름으로 종류를 고르고, 모르는 이름이면 기본 덩어리 그림 | `BattleArt.Enemy` |
| 배경·바닥·소품 | 하늘 그라데이션, 봉우리, 버섯·수정·바위, 반복 바닥 무늬. 스테이지마다 색만 다름 | `BattleArt.Backdrop` 등 |
| 이펙트 (타격·베기·회복·별·독 방울·방패·그림자) | 같은 방식 | `BattleArt` 아래쪽 |
| 그리기 도구 | 도형 채우기·외곽선 | [`Paint.cs`](../UnityProject/Assets/TinyExpedition/Game/Battle/Paint.cs) |
| 셰이더 | Unity 내장 `Sprites/Default` | — |
| 글자 | OS 폰트(맑은 고딕 등)를 실행 중에 불러 씀. 폰트 파일을 저장소·빌드에 넣지 않음 | `UiKit.Font` |

모집 결과 카드는 지금도 `StreamingAssets/Skins/<id>.png`가 있으면 그 그림을, 없으면 등급 색 + 이름 첫 글자 카드를 씁니다(UI-004). 저장소에는 Skins 그림이 없습니다.
