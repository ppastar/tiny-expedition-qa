using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TinyExpedition.Core.Progress;
using TinyExpedition.Game;
using TinyExpedition.Game.Battle;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Property = NUnit.Framework.PropertyAttribute;

namespace TinyExpedition.Tests.PlayMode
{
    /// <summary>
    /// 홈 화면의 2.5D 로비. 무대에 선 캐릭터가 Core의 자동 편성과 같은지, 탭 반응이 게임 상태를 건드리지 않는지,
    /// 화면을 오가도 3D 무대가 하나만 남는지 본다.
    /// </summary>
    public class LobbyTests
    {
        private GameApp _app;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "tiny-expedition-lobby-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            BattleArt.ArtDir = null;
            if (_app != null) Object.Destroy(_app.gameObject);
            foreach (var es in Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>())
                Object.Destroy(es.gameObject);
            try { Directory.Delete(_tempDir, true); } catch (IOException) { }
        }

        private IEnumerator Launch()
        {
            _app = GameApp.Create(new GameAppOptions
            {
                SavePath = Path.Combine(_tempDir, "save.json"),
                SkinDir = Path.Combine(Application.streamingAssetsPath, "Skins"),
                FreshStart = true,
                Seed = 42,
            });
            yield return null;
        }

        private Button Btn(string name)
        {
            var b = _app.Find<Button>(name);
            Assert.That(b, Is.Not.Null, "버튼 없음: " + name + " (화면 " + _app.CurrentScreen + ")");
            return b;
        }

        private IEnumerator Click(string name)
        {
            Btn(name).onClick.Invoke();
            yield return null; // Destroy는 프레임 끝에 일어나므로 한 프레임 기다린다
        }

        private static int Count<T>() where T : Object => Object.FindObjectsByType<T>().Length;

        [UnityTest, Property("TC", "LOBBY-001")]
        public IEnumerator Lobby_ShowsAutoParty_OrPickupWhenEmpty()
        {
            yield return Launch();
            var lobby = _app.Lobby;
            Assert.That(_app.CurrentScreen, Is.EqualTo("Home"));
            Assert.That(lobby, Is.Not.Null);
            Assert.That(lobby.Cam.orthographic, Is.False, "전투와 같은 원근 카메라");

            // 새 세이브: 보유 캐릭터가 없으니 픽업 캐릭터를 세운다
            Assert.That(_app.AutoPartyIds(), Is.Empty);
            Assert.That(lobby.IsShowcase, Is.True);
            Assert.That(lobby.ShownIds, Is.EqualTo(new[] { _app.Session.Db.GetBanner(GameApp.MainBannerId).PickupId }));
            Assert.That(_app.Find<Text>("Label_LobbyName_0").text, Does.Contain("모집 중"));

            // 10연 모집 뒤 홈: 원정 자동 편성과 같은 캐릭터가 같은 순서로 선다
            yield return Click("Btn_Pull10");
            yield return Click("Btn_Back");
            lobby = _app.Lobby;
            var party = _app.AutoPartyIds();
            Assert.That(party.Count, Is.EqualTo(3));
            Assert.That(lobby.IsShowcase, Is.False);
            Assert.That(lobby.ShownIds, Is.EqualTo(party));
            for (int i = 0; i < party.Count; i++)
            {
                Assert.That(_app.Find<Text>("Label_LobbyName_" + i).text, Is.EqualTo(_app.Session.Db.GetCharacter(party[i]).Name));
                Assert.That(_app.Find<Button>("Btn_LobbyUnit_" + i), Is.Not.Null);
            }
            Assert.That(_app.Find<Button>("Btn_LobbyUnit_3"), Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Property("TC", "LOBBY-002")]
        public IEnumerator LobbyTap_Reacts_WithoutChangingGameState()
        {
            yield return Launch();
            yield return Click("Btn_Pull10");
            yield return Click("Btn_Back");
            var lobby = _app.Lobby;
            string before = SaveSerializer.Serialize(_app.Session.State);

            for (int round = 0; round < 2; round++)
                for (int i = 0; i < lobby.ShownIds.Count; i++)
                {
                    yield return Click("Btn_LobbyUnit_" + i);
                    Assert.That(lobby.LastReacted, Is.EqualTo(i));
                    var speech = _app.Find<Text>("Label_Speech");
                    Assert.That(speech, Is.Not.Null, "탭하면 말풍선이 보여야 함");
                    Assert.That(speech.text, Is.Not.Empty);
                }
            Assert.That(lobby.Reactions, Is.EqualTo(lobby.ShownIds.Count * 2));
            for (int f = 0; f < 30; f++) yield return null;

            // 세이브(보석·보유·천장·게임 난수 시드 포함)가 한 글자도 바뀌지 않아야 한다
            Assert.That(SaveSerializer.Serialize(_app.Session.State), Is.EqualTo(before));
            Assert.That(_app.CurrentScreen, Is.EqualTo("Home"));
        }

        [UnityTest, Property("TC", "LOBBY-003")]
        public IEnumerator Stages_OnlyOnePerScreen_WhenMovingBetweenScreens()
        {
            yield return Launch();
            Assert.That((Count<LobbyStage>(), Count<BattleStage>()), Is.EqualTo((1, 0)), "홈");
            yield return Click("Btn_Pull10");
            Assert.That((Count<LobbyStage>(), Count<BattleStage>()), Is.EqualTo((0, 0)), "모집 결과");
            Assert.That(_app.Find<Image>("Root").enabled, Is.True, "평면 화면은 배경을 다시 깐다");
            yield return Click("Btn_Back");
            Assert.That((Count<LobbyStage>(), Count<BattleStage>()), Is.EqualTo((1, 0)), "홈으로 돌아옴");
            yield return Click("Btn_Roster");
            Assert.That((Count<LobbyStage>(), Count<BattleStage>()), Is.EqualTo((0, 0)), "보유 캐릭터");
            yield return Click("Btn_Back");
            yield return Click("Btn_TurnBattle");
            yield return Click("Btn_TurnStage_T-1");
            Assert.That(_app.CurrentScreen, Is.EqualTo("TurnBattle"));
            Assert.That((Count<LobbyStage>(), Count<BattleStage>()), Is.EqualTo((0, 1)), "턴제 전투");
            yield return Click("Btn_Retreat");
            Assert.That(_app.CurrentScreen, Is.EqualTo("Home"));
            Assert.That((Count<LobbyStage>(), Count<BattleStage>()), Is.EqualTo((1, 0)), "후퇴 후 홈");
            Assert.That(Object.FindObjectsByType<Camera>().Count(c => c.depth >= 10), Is.EqualTo(1), "무대 카메라는 하나");
        }

        [UnityTest, Property("TC", "LOBBY-004")]
        public IEnumerator Lobby_MissingArt_FallsBack_AndMenuStillWorks()
        {
            var empty = Path.Combine(_tempDir, "no-art");
            Directory.CreateDirectory(empty);
            BattleArt.ArtDir = empty;
            yield return Launch();
            Assert.That(_app.Lobby, Is.Not.Null);
            Assert.That(BattleArt.MissingArt, Has.Member("p_campfire").And.Member("c_lumi"));
            yield return Click("Btn_LobbyUnit_0");
            yield return Click("Btn_Pull1");
            Assert.That(_app.CurrentScreen, Is.EqualTo("PullResult"));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
