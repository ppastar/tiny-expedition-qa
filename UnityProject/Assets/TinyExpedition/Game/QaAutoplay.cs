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
    /// 홈(2.5D 로비) → 10연 모집 → 로비 파티·탭 반응 → 보유 캐릭터 → 원정 1-1 전투 → 미니게임 한 판 → 턴제 T-1 전투(2.5D 무대)를 실제 빌드에서 진행하고
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
            Expect(_app.Lobby != null && !_app.Lobby.Cam.orthographic && _app.Lobby.IsShowcase, "홈 2.5D 로비 (보유 캐릭터 없음 → 픽업 캐릭터)");
            yield return Shot("home");

            Click("Btn_Pull10");
            yield return Wait(0.5f);
            Expect(_app.CurrentScreen == "PullResult", "10연 모집 결과 화면");
            yield return Shot("pull10-result");

            // 모집 뒤 홈: 로비에 원정 자동 편성과 같은 파티가 서고, 누르면 반응하되 세이브는 그대로
            Click("Btn_Back");
            yield return Wait(0.6f);
            var lobby = _app.Lobby;
            Expect(lobby != null && string.Join(",", lobby.ShownIds) == string.Join(",", _app.AutoPartyIds()), "로비 파티 = 자동 편성 (" + string.Join(",", lobby.ShownIds) + ")");
            string saveBefore = TinyExpedition.Core.Progress.SaveSerializer.Serialize(_app.Session.State);
            Click("Btn_LobbyUnit_1");
            yield return Wait(0.25f);
            Expect(lobby.Reactions == 1, "로비 캐릭터 탭 반응");
            yield return Shot("home-lobby");
            string saveAfter = TinyExpedition.Core.Progress.SaveSerializer.Serialize(_app.Session.State);
            if (saveAfter != saveBefore) Log("세이브 차이 · 전: " + saveBefore.Replace("\n", " ") + " · 후: " + saveAfter.Replace("\n", " "));
            Expect(saveAfter == saveBefore, "로비 탭은 게임 상태를 바꾸지 않음");
            yield return Wait(1.8f);
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

            // 턴제 전투(2.5D): 대상을 골라 직접 공격 → 스킬 → 4배속 자동으로 끝까지. 연출 도중의 화면도 찍는다.
            Click("Btn_Back");
            yield return Wait(0.3f);
            Click("Btn_TurnBattle");
            yield return Wait(0.3f);
            Expect(_app.CurrentScreen == "TurnStages", "턴제 스테이지 화면");
            yield return Shot("turn-stages");
            Click("Btn_TurnStage_T-1");
            yield return Wait(1.2f);
            Expect(_app.CurrentScreen == "TurnBattle", "턴제 전투 화면");
            var stage = _app.Stage;
            Expect(stage != null && stage.Cam != null && !stage.Cam.orthographic, "2.5D 전투 무대 (원근 카메라)");
            yield return WaitIdle();
            yield return Shot("turn-battle");

            Click("Btn_Target_1");
            yield return Wait(0.3f);
            Click("Btn_Attack");
            yield return Wait(0.24f);
            Expect(stage.IsPlaying, "공격 연출 재생 중");
            Expect(!_app.Find<Button>("Btn_Attack").interactable, "연출 중에는 공격 버튼 잠김");
            yield return Shot("turn-battle-attack");
            yield return WaitIdle();
            Expect(_app.CurrentTurnBattle.Commands.Count == 1, "턴제 명령 1개 기록");
            Expect(stage.ShownHpText(TinyExpedition.Core.Battle.Side.Enemy, 1) == _app.CurrentTurnBattle.Enemies[1].Hp + "/" + _app.CurrentTurnBattle.Enemies[1].MaxHp,
                "공격받은 적의 화면 HP = 실제 HP");

            if (_app.CurrentScreen == "TurnBattle" && !_app.CurrentTurnBattle.IsOver)
            {
                bool skill = _app.Find<Button>("Btn_Skill").interactable;
                Click(skill ? "Btn_Skill" : "Btn_Guard");
                yield return Wait(0.62f);
                yield return Shot(skill ? "turn-battle-skill" : "turn-battle-guard");
                yield return WaitIdle();
            }

            if (_app.CurrentScreen == "TurnBattle")
            {
                Click("Btn_Speed");
                Click("Btn_Speed");
                Expect(_app.BattleSpeed == 4f, "배속 ×4");
                Click("Btn_Auto");
                bool shotWave = false;
                float turnDeadline = Time.realtimeSinceStartup + 180f;
                while (_app.CurrentScreen == "TurnBattle")
                {
                    if (Time.realtimeSinceStartup > turnDeadline) throw new Exception("턴제 전투가 제한 시간 안에 끝나지 않음");
                    if (!shotWave && stage.Display.WaveIndex >= 1 && !stage.IsPlaying)
                    {
                        shotWave = true;
                        yield return Shot("turn-battle-wave2");
                    }
                    yield return null;
                }
            }
            Expect(_app.CurrentScreen == "TurnResult", "턴제 결과 화면");
            Expect(stage.MismatchCount == 0, "화면 상태 = 전투 상태 (대조 " + stage.SyncChecks + "번, 불일치 0)");
            Expect(stage.AnimatedEntries > 20, "로그를 연출로 재생함 (" + stage.AnimatedEntries + "줄)");
            stage.FrameStats(out int frames, out float avgMs, out float p95Ms, out float maxMs);
            Log("전투 화면 프레임 시간 · " + frames + "프레임 · 평균 " + avgMs.ToString("0.0") + "ms · 95% " + p95Ms.ToString("0.0") + "ms · 최대 " + maxMs.ToString("0.0") + "ms");
            Expect(frames > 100 && avgMs < 50f, "전투 화면 평균 프레임 시간 50ms 미만");
            Log("턴제 결과 " + _app.CurrentTurnBattle.Outcome + " · 시드 " + _app.CurrentTurnBattle.Seed);
            yield return Wait(0.4f);
            yield return Shot("turn-result");
        }

        /// <summary>전투 연출이 끝나 명령을 받을 수 있을 때까지 기다린다.</summary>
        private IEnumerator WaitIdle()
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (_app.Stage != null && _app.Stage.IsPlaying)
            {
                if (Time.realtimeSinceStartup > deadline) throw new Exception("전투 연출이 30초 안에 끝나지 않음");
                yield return null;
            }
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
