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
    }
}
