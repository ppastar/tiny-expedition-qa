#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace TinyExpedition.Game
{
    /// <summary>
    /// 개발 빌드 전용 자동 플레이(빌드 스모크 테스트).
    /// 실행: TinyExpedition.exe -qaAutoplay &lt;결과 폴더&gt;
    /// 홈 → 10연 모집 → 보유 캐릭터 → 원정 1-1 전투 → 미니게임 한 판을 실제 빌드에서 진행하고
    /// 단계마다 스크린샷을 남긴 뒤 종료한다. 실패하면 종료 코드 1.
    /// 출시 빌드에는 컴파일되지 않는다.
    /// </summary>
    public sealed class QaAutoplay : MonoBehaviour
    {
        public const string Arg = "-qaAutoplay";

        private GameApp _app;
        private string _outDir;
        private int _step;

        public static string OutDirFromArgs()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == Arg) return args[i + 1];
            return null;
        }

        public static void Attach(GameApp app, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var a = app.gameObject.AddComponent<QaAutoplay>();
            a._app = app;
            a._outDir = outDir;
        }

        private IEnumerator Start()
        {
            Log("시작 · 결과 폴더 " + _outDir);
            bool ok = true;
            var run = Run();
            while (true)
            {
                object current;
                try
                {
                    if (!run.MoveNext()) break;
                    current = run.Current;
                }
                catch (Exception e)
                {
                    Log("실패: " + e);
                    ok = false;
                    break;
                }
                yield return current;
            }
            Log(ok ? "완료 (모든 단계 통과)" : "중단됨");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(ok ? 0 : 1);
        }

        private IEnumerator Run()
        {
            yield return Wait(1.0f);
            Expect(_app.CurrentScreen == "Home", "홈 화면");
            yield return Shot("home");

            Click("Btn_Pull10");
            yield return Wait(0.5f);
            Expect(_app.CurrentScreen == "PullResult", "10연 모집 결과 화면");
            yield return Shot("pull10-result");

            Click("Btn_Back");
            yield return Wait(0.3f);
            Click("Btn_Roster");
            yield return Wait(0.3f);
            yield return Shot("roster");

            Click("Btn_Back");
            yield return Wait(0.3f);
            Click("Btn_Stages");
            yield return Wait(0.3f);
            yield return Shot("stages");
            Click("Btn_Stage_1-1");
            yield return Wait(0.5f);
            Expect(_app.CurrentScreen == "BattleResult", "전투 결과 화면");
            yield return Shot("battle-1-1");

            Click("Btn_Back");
            yield return Wait(0.3f);
            Click("Btn_Back");
            yield return Wait(0.3f);
            Click("Btn_MiniGame");
            yield return Wait(0.3f);
            var view = FindAnyObjectByType<MiniGameView>();
            Expect(view != null, "미니게임 화면");
            var game = _app.CurrentMiniGame;

            // 실제 시간으로 막대를 보고 탭한다: 사람처럼 목표 근처에서 누르되, 일부는 일부러 빗나가게.
            int taps = 0;
            bool shotMid = false;
            float deadline = Time.realtimeSinceStartup + 40f;
            while (!game.IsFinished)
            {
                if (Time.realtimeSinceStartup > deadline) throw new Exception("미니게임이 제한 시간 안에 끝나지 않음");
                double pos = game.PositionAt(view.Clock());
                double target = taps % 4 == 3 ? 0.15 : 0.5; // 4번째마다 Miss 위치
                if (Math.Abs(pos - target) < 0.02)
                {
                    view.Tap();
                    taps++;
                    if (taps == 5 && !shotMid)
                    {
                        shotMid = true;
                        yield return Shot("minigame-playing");
                    }
                    yield return Wait(0.25f); // 같은 구간에서 두 번 누르지 않도록
                }
                else
                {
                    yield return null;
                }
            }
            yield return Wait(0.5f);
            Expect(_app.CurrentScreen == "MiniGameResult", "미니게임 결과 화면");
            yield return Shot("minigame-result");
        }

        private void Click(string name)
        {
            var b = _app.Find<Button>(name);
            Expect(b != null, "버튼 " + name + " (화면 " + _app.CurrentScreen + ")");
            Expect(b.interactable, "버튼 " + name + " 활성 상태");
            Log("클릭 " + name);
            b.onClick.Invoke();
        }

        private static void Expect(bool condition, string what)
        {
            if (!condition) throw new Exception("확인 실패: " + what);
            Log("확인 " + what);
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(_outDir, (++_step).ToString("00") + "-" + name + ".png");
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Log("스크린샷 " + path);
        }

        private static IEnumerator Wait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }

        private static void Log(string message) => Debug.Log("[QA-AUTOPLAY] " + message);
    }
}
#endif
