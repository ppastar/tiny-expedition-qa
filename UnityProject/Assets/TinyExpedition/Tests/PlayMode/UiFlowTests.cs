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
    }
}
