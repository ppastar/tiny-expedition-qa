#!/usr/bin/env bash
# 전체 QA 파이프라인: 단위 테스트 → 데이터 검사 → 확률 검증 → (Unity 테스트) → 추적 리포트
# 사용: scripts/test-all.sh            (Unity 라이선스가 있으면 UNITY=<Unity 실행 파일> 로 Unity 테스트까지)
set -uo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts
QA="dotnet run --project dotnet/QaTool -c Release --no-build --"
status=0

echo "== 1. 빌드"
dotnet build dotnet/TinyExpedition.slnx -c Release -v q --nologo || exit 1

echo "== 2. 단위 테스트 (dotnet, 엔진 없이)"
dotnet test dotnet/Core.Tests -c Release --no-build --nologo \
  --logger "nunit;LogFilePath=$PWD/artifacts/dotnet-results.xml" || status=1

echo "== 3. 기획 데이터 검사"
$QA validate-data || status=1

echo "== 4. 모집 확률 검증"
$QA gacha-verify --pulls "${GACHA_PULLS:-1000000}" --out artifacts/gacha-report.md || status=1

results="dotnet=artifacts/dotnet-results.xml"
if [ -n "${UNITY:-}" ]; then
  echo "== 5. Unity EditMode / PlayMode 테스트"
  scripts/run-unity-tests.sh || status=1
  results="$results,unity-editmode=artifacts/unity-editmode.xml,unity-playmode=artifacts/unity-playmode.xml"
else
  echo "== 5. Unity 테스트 건너뜀 (UNITY 환경변수 없음)"
fi

echo "== 6. TC 추적 리포트"
$QA report --results "$results" --cases docs/test-cases.csv --out artifacts/test-report.html || status=1

echo
[ $status -eq 0 ] && echo "모든 단계 통과" || echo "실패한 단계가 있습니다 (위 로그 확인)"
exit $status
