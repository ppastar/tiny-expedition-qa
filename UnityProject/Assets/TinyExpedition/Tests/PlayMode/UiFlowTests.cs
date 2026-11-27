using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TinyExpedition.Game;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Property = NUnit.Framework.PropertyAttribute;

namespace TinyExpedition.Tests.PlayMode
{
    /// <summary>
    /// 실제 화면을 띄워 버튼을 누르는 E2E 테스트. 규칙 자체는 공유 테스트(Shared)에서 검증하고,
    /// 여기서는 "화면과 규칙이 제대로 이어져 있는가"만 본다.
    /// </summary>
    public class UiFlowTests
    {
        private GameApp _app;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "tiny-expedition-test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            TinyExpedition.Game.Battle.BattleArt.ArtDir = null; // 그림 폴더를 바꾼 테스트가 있으면 원래대로
            if (_app != null) Object.Destroy(_app.gameObject);
            foreach (var es in Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>())
                Object.Destroy(es.gameObject);
            try { Directory.Delete(_tempDir, true); } catch (IOException) { }
        }

        private GameApp Launch(string skinDir = null)
        {
            _app = GameApp.Create(new GameAppOptions
            {
                SavePath = Path.Combine(_tempDir, "save.json"),
                SkinDir = skinDir ?? Path.Combine(Application.streamingAssetsPath, "Skins"),
                FreshStart = true,
                Seed = 42,
            });
            return _app;
        }

        private Button Btn(string name)
        {
            var b = _app.Find<Button>(name);
            Assert.That(b, Is.Not.Null, "버튼 없음: " + name + " (화면 " + _app.CurrentScreen + ")");
            return b;
        }

        private string LabelText(string name)
        {
            var t = _app.Find<Text>(name);
            Assert.That(t, Is.Not.Null, "라벨 없음: " + name);
            return t.text;
        }

        [UnityTest, Property("TC", "UI-001")]
        public IEnumerator Boot_ShowsHomeWithGems()
        {
            Launch();
            yield return null;
            Assert.That(_app.CurrentScreen, Is.EqualTo("Home"));
            Assert.That(LabelText("Label_Gems"), Is.EqualTo("보석 3,000"));
        }

        [UnityTest, Property("TC", "UI-002")]
        public IEnumerator PullButton_ShowsResult_AndSpendsGems()
        {
            Launch();
            yield return null;
            Btn("Btn_Pull1").onClick.Invoke();
            yield return null;

            Assert.That(_app.CurrentScreen, Is.EqualTo("PullResult"));
            Assert.That(_app.Find<Image>("Card_0"), Is.Not.Null);
            Assert.That(_app.Find<Image>("Card_1"), Is.Null);
            Assert.That(LabelText("Label_Gems"), Is.EqualTo("보석 2,850"));
            Assert.That(File.Exists(Path.Combine(_tempDir, "save.json")), Is.True, "모집 직후 저장되어야 함");
        }

        [UnityTest, Property("TC", "UI-003")]
        public IEnumerator MiniGame_TenTaps_ShowsResultAndReward()
        {
            Launch();
            yield return null;
            Btn("Btn_MiniGame").onClick.Invoke();
            yield return null;

            var view = Object.FindAnyObjectByType<MiniGameView>();
            Assert.That(view, Is.Not.Null);
            // 실제 시간 대신 정해진 시각을 넣어 결과를 결정적으로 만든다 (매번 Perfect 위치)
            double t = 0;
            view.Clock = () => t;
            for (int i = 0; i < 10; i++)
            {
                t = i * 1.6 + 0.4;
                Btn("Btn_Tap").onClick.Invoke();
                yield return null;
            }

            Assert.That(_app.CurrentScreen, Is.EqualTo("MiniGameResult"));
            // Perfect 10연속: 100+110+...+190 = 1450점 → 72 보석
            Assert.That(LabelText("Label_Score"), Is.EqualTo("점수 1450"));
            Assert.That(LabelText("Label_Reward"), Is.EqualTo("보석 +72"));
            Assert.That(LabelText("Label_Gems"), Is.EqualTo("보석 3,072"));
        }

        [UnityTest, Property("TC", "UI-004")]
        public IEnumerator MissingSkins_FallBackToPlaceholder()
        {
            var empty = Path.Combine(_tempDir, "no-skins");
            Directory.CreateDirectory(empty);
            Launch(empty);
            yield return null;
            Btn("Btn_Pull10").onClick.Invoke();
            yield return null;

            Assert.That(_app.CurrentScreen, Is.EqualTo("PullResult"));
            var cards = Enumerable.Range(0, 10).Select(i => _app.Find<Image>("Card_" + i)).ToList();
            Assert.That(cards.All(c => c != null && c.sprite == null), Is.True, "그림이 없으면 sprite 없이 기본 카드");
            Assert.That(_app.Skins.MissingCount, Is.GreaterThan(0));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>모집으로 파티를 만들고 턴제 T-1 전투 화면까지 들어간다.</summary>
        private IEnumerator EnterTurnBattle()
        {
            Launch();
            yield return null;
            Btn("Btn_Pull10").onClick.Invoke();
            yield return null;
            Btn("Btn_Back").onClick.Invoke();
            yield return null;
            Btn("Btn_TurnBattle").onClick.Invoke();
            yield return null;
            Assert.That(_app.CurrentScreen, Is.EqualTo("TurnStages"));
            Assert.That(Btn("Btn_TurnStage_T-2").interactable, Is.False, "T-1을 깨기 전에는 T-2 잠김");
            Btn("Btn_TurnStage_T-1").onClick.Invoke();
            yield return null;
            Assert.That(_app.CurrentScreen, Is.EqualTo("TurnBattle"));
        }

        [UnityTest, Property("TC", "TURN-023")]
        public IEnumerator TurnBattle_TargetAttack_ThenAuto_ShowsResultAndReward()
        {
            yield return EnterTurnBattle();
            var battle = _app.CurrentTurnBattle;
            Assert.That(battle.CurrentActor, Is.Not.Null);
            int gemsBefore = _app.Session.State.Gems;

            // 두 번째 적을 골라 공격 → 명령 기록에 대상 1번이 남고 다음 차례로 넘어간다
            Btn("Btn_Target_1").onClick.Invoke();
            yield return null;
            Assert.That(LabelText("Label_Prompt"), Does.Contain(battle.Enemies[1].Name));
            int logBefore = battle.Log.Count;
            Btn("Btn_Attack").onClick.Invoke();
            yield return null;
            Assert.That(battle.Commands.Select(c => c.ToString()), Is.EqualTo(new[] { "A1" }));
            Assert.That(battle.Log.Count, Is.GreaterThan(logBefore));

            var logs = new System.Collections.Generic.List<string>();
            Application.LogCallback capture = (msg, stack, type) => logs.Add(msg);
            Application.logMessageReceived += capture;
            Btn("Btn_AutoAll").onClick.Invoke();
            yield return null;
            Application.logMessageReceived -= capture;
            Assert.That(_app.CurrentScreen, Is.EqualTo("TurnResult"));
            Assert.That(battle.IsOver && battle.RewardClaimed, Is.True);
            int gained = _app.Session.State.Gems - gemsBefore;
            Assert.That(LabelText("Label_Reward"), Is.EqualTo("보석 +" + gained));
            if (battle.Outcome == TinyExpedition.Core.Battle.TurnOutcome.Victory)
                Assert.That(gained, Is.EqualTo(165), "T-1 첫 클리어 150 + 클리어 15");
            Assert.That(LabelText("Label_Replay"), Does.Contain("--seed " + battle.Seed).And.Contain("--commands A1,"));
            Assert.That(logs, Has.Member("[TurnReplay] " + battle.Outcome + " · " + _app.TurnReplayCommand(battle)),
                "버그 리포트용 리플레이 줄이 Player.log에도 남아야 함");
        }

        [UnityTest, Property("TC", "TURN-024")]
        public IEnumerator TurnBattle_Retreat_GivesNoReward()
        {
            yield return EnterTurnBattle();
            int gemsBefore = _app.Session.State.Gems;
            Btn("Btn_Guard").onClick.Invoke();
            yield return null;
            Btn("Btn_Retreat").onClick.Invoke();
            yield return null;
            Assert.That(_app.CurrentScreen, Is.EqualTo("Home"));
            Assert.That(_app.Session.State.Gems, Is.EqualTo(gemsBefore));
            Assert.That(_app.Session.State.ClearedTurnStages, Is.Empty);
            Assert.That(_app.CurrentTurnBattle, Is.Null);
        }

        // ---------------- 전투 그림 (일러스트 / 누락 시 기본 그림) ----------------

        [UnityTest, Property("TC", "ART-006")]
        public IEnumerator MissingBattleArt_FallsBackToDrawnArt_AndBattleStillRuns()
        {
            var empty = Path.Combine(_tempDir, "no-art");
            Directory.CreateDirectory(empty);
            TinyExpedition.Game.Battle.BattleArt.ArtDir = empty;

            yield return EnterTurnBattle();
            var battle = _app.CurrentTurnBattle;
            var stage = _app.Stage;
            for (int i = 0; i < battle.Allies.Count; i++)
            {
                Assert.That(stage.IsIllustrated(TinyExpedition.Core.Battle.Side.Ally, i), Is.False, "아군 " + i);
                Assert.That(stage.LayerCount(TinyExpedition.Core.Battle.Side.Ally, i), Is.EqualTo(1), "기본 그림은 한 장");
                Assert.That(stage.IsShownStanding(TinyExpedition.Core.Battle.Side.Ally, i), Is.True);
            }
            for (int i = 0; i < battle.Enemies.Count; i++)
            {
                Assert.That(stage.IsIllustrated(TinyExpedition.Core.Battle.Side.Enemy, i), Is.False, "적 " + i);
                Assert.That(stage.IsShownStanding(TinyExpedition.Core.Battle.Side.Enemy, i), Is.True);
            }
            Assert.That(TinyExpedition.Game.Battle.BattleArt.MissingArt, Has.Member("e_bat"), "빠진 그림은 목록에 남아야 함");

            // 그림이 없어도 전투는 끝까지 간다
            yield return null;
            Btn("Btn_AutoAll").onClick.Invoke();
            yield return null;
            Assert.That(_app.CurrentScreen, Is.EqualTo("TurnResult"));
            Assert.That(battle.IsOver, Is.True);
            // 오류·예외 로그가 있으면 테스트 러너가 실패시킨다 (결과 화면의 [TurnReplay] 안내 로그는 정상)
        }

        [UnityTest, Property("TC", "ART-007")]
        public IEnumerator BattleArt_UsesIllustrationWithParts()
        {
            yield return EnterTurnBattle();
            var battle = _app.CurrentTurnBattle;
            var stage = _app.Stage;
            for (int i = 0; i < battle.Allies.Count; i++)
            {
                Assert.That(stage.IsIllustrated(TinyExpedition.Core.Battle.Side.Ally, i), Is.True, "아군 " + i + " " + battle.Allies[i].Name);
                Assert.That(stage.LayerCount(TinyExpedition.Core.Battle.Side.Ally, i), Is.GreaterThanOrEqualTo(2), "몸과 머리가 따로 있어야 움직임이 붙는다");
            }
            for (int i = 0; i < battle.Enemies.Count; i++)
                Assert.That(stage.IsIllustrated(TinyExpedition.Core.Battle.Side.Enemy, i), Is.True, "적 " + i + " " + battle.Enemies[i].Name);
            Assert.That(TinyExpedition.Game.Battle.BattleArt.MissingArt, Is.Empty);

            // 부위 움직임(까딱임·깜빡임·날갯짓)이 도는 동안 오류가 없어야 한다
            for (int f = 0; f < 30; f++) yield return null;
            Assert.That(stage.MismatchCount, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------- 2.5D 전투 무대 ----------------

        private IEnumerator WaitIdle(float seconds = 30f)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (_app.Stage != null && _app.Stage.IsPlaying)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "연출이 제한 시간 안에 끝나지 않음");
                yield return null;
            }
        }

        /// <summary>화면 머리 위 HP 글자와 서 있는 그림이 Core 상태와 같은지.</summary>
        private void AssertShownMatchesCore(string when)
        {
            var battle = _app.CurrentTurnBattle;
            var stage = _app.Stage;
            foreach (var u in battle.Allies.Concat(battle.Enemies))
            {
                string who = when + " · " + u.Side + u.Slot + " " + u.Name;
                Assert.That(stage.ShownHpText(u.Side, u.Slot), Is.EqualTo(u.Hp + "/" + u.MaxHp), who + " HP 글자");
                Assert.That(stage.IsShownStanding(u.Side, u.Slot), Is.EqualTo(u.IsAlive), who + " 서 있음/쓰러짐");
            }
        }

        [UnityTest, Property("TC", "TURN-028")]
        public IEnumerator TurnStage_ShownHpAndDeaths_MatchCore_AfterEveryPlayback()
        {
            yield return EnterTurnBattle();
            var battle = _app.CurrentTurnBattle;
            var stage = _app.Stage;
            Assert.That(stage, Is.Not.Null);
            Assert.That(stage.Cam.orthographic, Is.False, "원근 카메라 (2.5D)");
            _app.BattleSpeed = 25f;
            yield return WaitIdle();
            AssertShownMatchesCore("시작");

            Btn("Btn_Target_1").onClick.Invoke();
            Btn("Btn_Attack").onClick.Invoke();
            // Core는 이미 계산을 끝냈지만 화면은 아직 한 줄도 재생하지 않았다
            Assert.That(stage.IsPlaying, Is.True);
            Assert.That(battle.Enemies[1].Hp, Is.LessThan(battle.Enemies[1].MaxHp));
            Assert.That(stage.Display.Enemies[1].Hp, Is.EqualTo(battle.Enemies[1].MaxHp), "맞기 전에 HP가 먼저 줄어 보이면 안 됨");
            yield return WaitIdle();
            AssertShownMatchesCore("첫 공격 뒤");

            Btn("Btn_Auto").onClick.Invoke();
            Assert.That(_app.AutoBattle, Is.True);
            int checks = 0;
            float deadline = Time.realtimeSinceStartup + 90f;
            while (_app.CurrentScreen == "TurnBattle")
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "자동 전투가 제한 시간 안에 끝나지 않음");
                if (!stage.IsPlaying)
                {
                    AssertShownMatchesCore("명령 " + battle.Commands.Count + "개 뒤");
                    checks++;
                }
                yield return null;
            }

            Assert.That(_app.CurrentScreen, Is.EqualTo("TurnResult"));
            Assert.That(battle.IsOver, Is.True);
            Assert.That(checks, Is.GreaterThan(5));
            Assert.That(stage.MismatchCount, Is.EqualTo(0));
            Assert.That(stage.SyncChecks, Is.GreaterThanOrEqualTo(battle.Commands.Count));
            Assert.That(stage.AnimatedEntries, Is.EqualTo(battle.Log.Count), "건너뛰지 않았으니 모든 로그 줄을 연출로 재생");
            Assert.That(LabelText("Label_Reward"), Does.StartWith("보석 +"));
            // 화면과 Core가 다르면 무대가 Debug.LogError([TurnView])를 남기고, Unity 테스트는 오류 로그가 있으면 실패한다
        }

        private sealed class TurnRun
        {
            public string Replay, Outcome, AllyHp;
            public int Gems, Animated, LogCount;
        }

        /// <summary>같은 시드로 T-1을 자동 명령으로 끝낸다. skipAfter번째 명령 뒤에 건너뛰기(-1이면 끝까지 연출).</summary>
        private IEnumerator PlayTurnBattle(float speed, int skipAfter, System.Action<TurnRun> done)
        {
            yield return EnterTurnBattle();
            var battle = _app.CurrentTurnBattle;
            var stage = _app.Stage;
            int gemsBefore = _app.Session.State.Gems;
            _app.BattleSpeed = speed;
            _app.AutoBattle = true;
            float deadline = Time.realtimeSinceStartup + 90f;
            while (_app.CurrentScreen == "TurnBattle")
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                if (skipAfter >= 0 && battle.Commands.Count >= skipAfter) Btn("Btn_AutoAll").onClick.Invoke();
                else yield return null;
            }
            Assert.That(_app.CurrentScreen, Is.EqualTo("TurnResult"));
            Assert.That(stage.MismatchCount, Is.EqualTo(0));
            done(new TurnRun
            {
                Replay = _app.TurnReplayCommand(battle), Outcome = battle.Outcome + " R" + battle.Round + " ★" + battle.Stars,
                AllyHp = string.Join(",", battle.Allies.Select(a => a.Hp)), Gems = _app.Session.State.Gems - gemsBefore,
                Animated = stage.AnimatedEntries, LogCount = battle.Log.Count,
            });
            Object.Destroy(_app.gameObject);
            _app = null;
            yield return null;
        }

        [UnityTest, Property("TC", "TURN-029")]
        public IEnumerator TurnStage_SpeedAndSkip_DoNotChangeResult()
        {
            TurnRun skipped = null, animated = null, mixed = null;
            yield return PlayTurnBattle(1f, 0, r => skipped = r);    // 시작하자마자 건너뛰기 (연출 0줄)
            yield return PlayTurnBattle(30f, -1, r => animated = r); // 30배속으로 끝까지 연출
            yield return PlayTurnBattle(4f, 3, r => mixed = r);      // 4배속으로 3명령, 그 뒤 건너뛰기

            Assert.That(skipped.Animated, Is.EqualTo(0));
            Assert.That(animated.Animated, Is.EqualTo(animated.LogCount));
            Assert.That(mixed.Animated, Is.GreaterThan(0).And.LessThan(mixed.LogCount));
            foreach (var other in new[] { animated, mixed })
            {
                Assert.That(other.Replay, Is.EqualTo(skipped.Replay), "같은 시드·같은 명령 목록");
                Assert.That(other.Outcome, Is.EqualTo(skipped.Outcome));
                Assert.That(other.AllyHp, Is.EqualTo(skipped.AllyHp));
                Assert.That(other.Gems, Is.EqualTo(skipped.Gems));
            }
        }

        [UnityTest, Property("TC", "TURN-030")]
        public IEnumerator TurnStage_CommandsLockedWhilePlaying()
        {
            yield return EnterTurnBattle();
            var battle = _app.CurrentTurnBattle;
            var stage = _app.Stage;
            yield return WaitIdle();
            Assert.That(Btn("Btn_Attack").interactable, Is.True);

            Btn("Btn_Attack").onClick.Invoke();
            yield return null;
            Assert.That(stage.IsPlaying, Is.True, "1배속이라 한 프레임 뒤에도 연출 중");
            Assert.That(Btn("Btn_Attack").interactable, Is.False);
            Assert.That(Btn("Btn_Guard").interactable, Is.False);
            Assert.That(Btn("Btn_Skill").interactable, Is.False);

            // 연타: 버튼이 잠겨 있어도 호출이 들어오면(자동화·키 입력) 명령으로 기록되면 안 된다
            for (int i = 0; i < 5; i++)
            {
                Btn("Btn_Attack").onClick.Invoke();
                Btn("Btn_Guard").onClick.Invoke();
            }
            Assert.That(battle.Commands.Select(c => c.ToString()), Is.EqualTo(new[] { "A0" }));
            Assert.That(_app.IgnoredCommands, Is.EqualTo(10));

            // 대상 고르기와 배속은 연출 중에도 된다
            Btn("Btn_Speed").onClick.Invoke();
            Assert.That(_app.BattleSpeed, Is.EqualTo(2f));
            _app.BattleSpeed = 30f;
            yield return WaitIdle();
            Assert.That(Btn("Btn_Attack").interactable, Is.True);
            Btn("Btn_Guard").onClick.Invoke();
            Assert.That(battle.Commands.Select(c => c.ToString()), Is.EqualTo(new[] { "A0", "G" }));
            yield return WaitIdle();
            Assert.That(stage.MismatchCount, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
