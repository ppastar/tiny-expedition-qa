#!/usr/bin/env bash
# Unity Test Runner를 배치 모드로 실행해 NUnit XML 결과를 artifacts/에 남긴다.
# 사용: UNITY="/c/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Unity.exe" scripts/run-unity-tests.sh
# 필요: Unity Hub 로그인 + Personal 이상 라이선스 활성화
set -uo pipefail
cd "$(dirname "$0")/.."
: "${UNITY:?UNITY 환경변수에 Unity 실행 파일 경로를 넣으세요}"
mkdir -p artifacts
status=0
for platform in EditMode PlayMode; do
  lower=$(echo "$platform" | tr '[:upper:]' '[:lower:]')
  echo "-- $platform"
  # -runTests는 끝나면 스스로 종료하므로 -quit를 붙이지 않는다
  "$UNITY" -batchmode -projectPath UnityProject \
    -runTests -testPlatform "$platform" \
    -testResults "$PWD/artifacts/unity-$lower.xml" \
    -enableCodeCoverage -coverageResultsPath "$PWD/artifacts/coverage" \
    -coverageOptions "generateHtmlReport;assemblyFilters:+TinyExpedition.Core,+TinyExpedition.Game" \
    -logFile "$PWD/artifacts/unity-$lower.log"
  code=$?
  # 0 = 전부 통과, 2 = 실패한 테스트 있음, 그 외 = 실행 자체 실패(라이선스·컴파일 오류 등)
  case $code in
    0) echo "   통과" ;;
    2) echo "   실패한 테스트 있음"; status=1 ;;
    *) echo "   Unity 실행 실패 (exit $code) — artifacts/unity-$lower.log 확인"; status=1 ;;
  esac
done
exit $status
