using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TinyExpedition.Core;
using TinyExpedition.Core.Battle;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.Gacha;
using TinyExpedition.Core.MiniGame;
using TinyExpedition.Core.Progress;
using UnityEngine;
using UnityEngine.UI;

namespace TinyExpedition.Game
{
    public sealed class GameAppOptions
    {
        public string DataDir = Path.Combine(Application.streamingAssetsPath, "GameData");
        public string SkinDir = Path.Combine(Application.streamingAssetsPath, "Skins");
        public string SavePath = Path.Combine(Application.persistentDataPath, "save.json");
        /// <summary>true면 세이브를 읽지 않고 새로 시작 (테스트용).</summary>
        public bool FreshStart;
        /// <summary>지정하면 난수 시드를 고정 (테스트·버그 재현용).</summary>
        public ulong? Seed;
    }

    /// <summary>
    /// 게임 화면 전체. 규칙은 전부 Core의 GameSession에 있고, 여기서는 보여주고 입력만 넘긴다.
    /// UI 요소 이름(Btn_*, Label_*)은 PlayMode 테스트가 찾는 계약이므로 바꾸면 테스트도 같이 바꾼다.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public const string MainBannerId = "b_lumi";

        public GameSession Session { get; private set; }
        public SkinLibrary Skins { get; private set; }
        public string CurrentScreen { get; private set; } = "";
        public TimingTapGame CurrentMiniGame => _miniGame;

        private GameAppOptions _opts;
        private RectTransform _root;
        private Text _gemsLabel;
        private TimingTapGame _miniGame;
        private MiniGameView _miniGameView;

        /// <summary>Main 씬에서 자동으로 시작한다. 테스트는 Create를 직접 부른다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Main" && FindAnyObjectByType<GameApp>() == null)
                Create(new GameAppOptions());
        }

        public static GameApp Create(GameAppOptions opts)
        {
            var go = new GameObject("GameApp");
            var app = go.AddComponent<GameApp>();
            app.Init(opts);
            return app;
        }

        private void Init(GameAppOptions opts)
        {
            _opts = opts;
            var db = GameDatabase.Load(f => File.ReadAllText(Path.Combine(opts.DataDir, f)));
            var errors = DataValidator.Validate(db).Where(i => i.Level == IssueLevel.Error).ToList();
            foreach (var e in errors) Debug.LogError("데이터 오류: " + e);

            PlayerState state = null;
            if (!opts.FreshStart && File.Exists(opts.SavePath))
            {
                try
                {
                    state = SaveSerializer.Deserialize(File.ReadAllText(opts.SavePath));
                }
                catch (SaveCorruptedException e)
                {
                    // 깨진 세이브는 지우지 않고 옆으로 옮겨 두고 새로 시작 (CS 문의 시 복구용)
                    Debug.LogWarning("세이브를 읽지 못해 새로 시작합니다: " + e.Message);
                    File.Copy(opts.SavePath, opts.SavePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
                }
            }
            Session = new GameSession(db, state ?? new PlayerState());
            if (opts.Seed.HasValue) Session.Reseed(opts.Seed.Value);
            Skins = new SkinLibrary(opts.SkinDir);

            var canvas = UiKit.CreateCanvas(transform);
            _root = UiKit.Panel_("Root", canvas.transform, UiKit.Bg);
            ShowHome();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_opts.SavePath));
            // 쓰다가 꺼져도 기존 세이브가 깨지지 않도록 임시 파일에 쓰고 바꿔 끼운다
            var tmp = _opts.SavePath + ".tmp";
            File.WriteAllText(tmp, SaveSerializer.Serialize(Session.State));
            if (File.Exists(_opts.SavePath)) File.Delete(_opts.SavePath);
            File.Move(tmp, _opts.SavePath);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && Session != null) Save();
        }

        private void OnApplicationQuit()
        {
            if (Session != null) Save();
        }

        // ---------------- 화면 ----------------

        private Transform BeginScreen(string name)
        {
            UiKit.Clear(_root);
            CurrentScreen = name;
            var col = UiKit.Column("Screen_" + name, _root);
            var header = UiKit.Row("Header", col, 16, 90);
            UiKit.Label("Label_Title", header, "작은 원정대", 40, UiKit.Muted, TextAnchor.MiddleLeft);
            _gemsLabel = UiKit.Label("Label_Gems", header, "", 44, UiKit.Gold, TextAnchor.MiddleRight);
            RefreshGems();
            return col;
        }

        private void RefreshGems()
        {
            if (_gemsLabel != null) _gemsLabel.text = "보석 " + Session.State.Gems.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        public void ShowHome()
        {
            var col = BeginScreen("Home");
            var banner = Session.Db.GetBanner(MainBannerId);
            Session.State.Pity.TryGetValue(banner.Id, out var pity);
            UiKit.Label("Label_Banner", col, banner.Name, 64, UiKit.TextColor, TextAnchor.MiddleCenter, 160);
            UiKit.Label("Label_Pity", col, "천장까지 " + (banner.HardPity - (pity?.PullsSinceThreeStar ?? 0)) + "회", 36, UiKit.Muted);
            UiKit.Button("Btn_Pull1", col, "1회 모집 (" + banner.CostPerPull + ")", () => DoPull(1));
            UiKit.Button("Btn_Pull10", col, "10회 모집 (" + banner.CostPerPull * 10 + ")", () => DoPull(10));
            UiKit.Button("Btn_Stages", col, "원정 떠나기", ShowStages, UiKit.Panel);
            UiKit.Button("Btn_MiniGame", col, "미니게임: 별빛 타이밍", ShowMiniGame, UiKit.Panel);
            UiKit.Button("Btn_Roster", col, "보유 캐릭터 (" + Session.State.Owned.Count + ")", ShowRoster, UiKit.Panel);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UiKit.Button("Btn_Debug", col, "QA 디버그 패널", ShowDebug, new Color(0.35f, 0.2f, 0.2f), 100);
#endif
        }

        private void DoPull(int count)
        {
            var r = Session.Pull(MainBannerId, count);
            if (!r.Ok)
            {
                Toast(r.Error == ActionError.NotEnoughGems ? "보석이 부족합니다." : "모집 데이터 오류로 모집할 수 없습니다. (보석은 차감되지 않았습니다)");
                return;
            }
            Save();
            ShowPullResult(r.Value);
        }

        public void ShowPullResult(IReadOnlyList<PullResult> results)
        {
            var col = BeginScreen("PullResult");
            UiKit.Label("Label_ResultTitle", col, "모집 결과", 56);
            var grid = new GameObject("Grid_Results", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            grid.transform.SetParent(col, false);
            var g = grid.GetComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(180, 260);
            g.spacing = new Vector2(12, 12);
            g.childAlignment = TextAnchor.UpperCenter;
            grid.GetComponent<LayoutElement>().preferredHeight = results.Count > 5 ? 540 : 270;
            for (int i = 0; i < results.Count; i++) CharacterCard("Card_" + i, grid.transform, results[i]);
            UiKit.Button("Btn_Back", col, "확인", ShowHome);
        }

        private void CharacterCard(string name, Transform parent, PullResult r)
        {
            var def = Session.Db.GetCharacter(r.CharacterId);
            var card = new GameObject(name, typeof(RectTransform), typeof(Image));
            card.transform.SetParent(parent, false);
            var sprite = Skins.Get(def.Id);
            var img = card.GetComponent<Image>();
            img.color = sprite != null ? Color.white : SkinLibrary.RarityColor(def.Rarity);
            img.sprite = sprite;
            if (sprite == null)
            {
                var initial = UiKit.Label("Initial", card.transform, def.Name.Substring(0, 1), 90, UiKit.Bg);
                UiKit.Stretch((RectTransform)initial.transform);
            }
            var stars = new string('★', (int)def.Rarity) + (r.IsPickup ? " 픽업" : "") + (r.Reason == PullReason.HardPity ? " 천장" : "");
            var label = UiKit.Label("Name", card.transform, stars + "\n" + def.Name, 24, UiKit.TextColor, TextAnchor.LowerCenter);
            var rt = (RectTransform)label.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0.4f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public void ShowRoster()
        {
            var col = BeginScreen("Roster");
            foreach (var def in Session.Db.Characters.OrderByDescending(c => c.Rarity))
            {
                Session.State.Owned.TryGetValue(def.Id, out int n);
                UiKit.Label("Roster_" + def.Id, col, new string('★', (int)def.Rarity) + " " + def.Name + (n > 0 ? "  ×" + n : "  (미보유)"),
                    38, n > 0 ? UiKit.TextColor : UiKit.Muted, TextAnchor.MiddleLeft);
            }
            UiKit.Button("Btn_Back", col, "돌아가기", ShowHome, UiKit.Panel);
        }

        public void ShowStages()
        {
            var col = BeginScreen("Stages");
            var party = AutoParty();
            UiKit.Label("Label_Party", col, party.Count == 0 ? "모집으로 캐릭터를 먼저 얻으세요." :
                "자동 편성: " + string.Join(", ", party.Select(id => Session.Db.GetCharacter(id).Name)), 32, UiKit.Muted, TextAnchor.MiddleCenter, 100);
            foreach (var stage in Session.Db.Stages)
            {
                bool unlocked = Session.IsStageUnlocked(stage.Id);
                Session.State.ClearedStages.TryGetValue(stage.Id, out int stars);
                var text = stage.Id + " " + stage.Name + (stars > 0 ? "  " + new string('★', stars) : "") + (unlocked ? "" : "  (잠김)");
                var b = UiKit.Button("Btn_Stage_" + stage.Id, col, text, () => DoBattle(stage.Id, party), unlocked ? UiKit.Accent : UiKit.Panel);
                b.interactable = unlocked && party.Count > 0;
            }
            UiKit.Button("Btn_Back", col, "돌아가기", ShowHome, UiKit.Panel);
        }

        /// <summary>보유 캐릭터 중 공격력 높은 순 3명.</summary>
        private List<string> AutoParty() =>
            Session.State.Owned.Keys.Select(Session.Db.GetCharacter)
                .OrderByDescending(c => c.Atk + c.Hp / 10).Take(BattleSimulator.MaxPartySize).Select(c => c.Id).ToList();

        private void DoBattle(string stageId, List<string> party)
        {
            var r = Session.Battle(stageId, party);
            if (!r.Ok)
            {
                Toast(r.Error.ToString());
                return;
            }
            Save();
            var col = BeginScreen("BattleResult");
            var res = r.Value;
            UiKit.Label("Label_Outcome", col, res.Outcome == BattleOutcome.Victory ? "승리! " + new string('★', res.Stars) : res.Outcome == BattleOutcome.Timeout ? "시간 초과" : "패배",
                72, res.Outcome == BattleOutcome.Victory ? UiKit.Gold : UiKit.Muted, TextAnchor.MiddleCenter, 160);
            foreach (var e in res.Log.Skip(Math.Max(0, res.Log.Count - 12)))
                UiKit.Label("Log", col, e.ToString(), 28, UiKit.Muted, TextAnchor.MiddleLeft, 44);
            UiKit.Button("Btn_Back", col, "확인", ShowStages);
        }

        public void ShowMiniGame()
        {
            var col = BeginScreen("MiniGame");
            _miniGame = new TimingTapGame(new TimingTapConfig());
            UiKit.Label("Label_Help", col, "막대가 가운데 금색 구간에 올 때 탭!  (스페이스바도 가능)", 32, UiKit.Muted);
            var area = new GameObject("MiniGameArea", typeof(RectTransform), typeof(LayoutElement));
            area.transform.SetParent(col, false);
            area.GetComponent<LayoutElement>().preferredHeight = 700;
            _miniGameView = area.AddComponent<MiniGameView>();
            _miniGameView.Build(_miniGame, OnMiniGameFinished);
            UiKit.Button("Btn_Back", col, "그만하기", ShowHome, UiKit.Panel, 100);
        }

        private void OnMiniGameFinished(TimingTapGame game)
        {
            int gems = Session.ClaimMiniGame(game);
            Save();
            var col = BeginScreen("MiniGameResult");
            UiKit.Label("Label_Score", col, "점수 " + game.Score, 72, UiKit.Gold, TextAnchor.MiddleCenter, 160);
            UiKit.Label("Label_Detail", col,
                "Perfect " + game.Taps.Count(t => t.Judgement == Judgement.Perfect) + " · Good " + game.Taps.Count(t => t.Judgement == Judgement.Good)
                + " · Miss " + game.Taps.Count(t => t.Judgement == Judgement.Miss) + " · 최대 콤보 " + game.MaxCombo, 36);
            UiKit.Label("Label_Reward", col, "보석 +" + gems, 52, UiKit.TextColor);
            UiKit.Label("Label_Replay", col, "리플레이: " + string.Join(",", game.Taps.Select(t => t.Time.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))),
                22, UiKit.Muted, TextAnchor.MiddleCenter, 90);
            UiKit.Button("Btn_Again", col, "한 판 더", ShowMiniGame);
            UiKit.Button("Btn_Back", col, "홈으로", ShowHome, UiKit.Panel);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>QA 디버그 패널. 개발 빌드에만 들어가고 출시 빌드에서는 컴파일되지 않는다.</summary>
        public void ShowDebug()
        {
            var col = BeginScreen("Debug");
            UiKit.Label("Label_Seed", col, "현재 시드 " + Session.State.RngSeed, 32, UiKit.Muted);
            UiKit.Button("Btn_Dbg_Seed", col, "시드 42로 고정", () => { Session.Reseed(42); ShowDebug(); }, UiKit.Panel, 110);
            UiKit.Button("Btn_Dbg_Gems", col, "보석 +10,000", () => { Session.State.Gems += 10000; ShowDebug(); }, UiKit.Panel, 110);
            UiKit.Button("Btn_Dbg_Pity", col, "천장 직전(89회)으로", () =>
            {
                Session.State.Pity[MainBannerId] = new PityState { PullsSinceThreeStar = 89 };
                Toast("다음 모집이 천장입니다.");
            }, UiKit.Panel, 110);
            UiKit.Button("Btn_Dbg_Unlock", col, "모든 스테이지 열기", () =>
            {
                foreach (var s in Session.Db.Stages)
                    if (!Session.State.ClearedStages.ContainsKey(s.Id)) Session.State.ClearedStages[s.Id] = 1;
                Toast("모든 스테이지를 열었습니다.");
            }, UiKit.Panel, 110);
            UiKit.Button("Btn_Dbg_Reset", col, "세이브 초기화", () =>
            {
                Session = new GameSession(Session.Db, new PlayerState());
                Save();
                ShowDebug();
            }, new Color(0.5f, 0.2f, 0.2f), 110);
            UiKit.Button("Btn_Back", col, "돌아가기", ShowHome);
        }
#endif

        private void Toast(string message)
        {
            Debug.Log("[Toast] " + message);
            var t = UiKit.Label("Toast", _root, message, 40, UiKit.Gold, TextAnchor.MiddleCenter);
            var rt = (RectTransform)t.transform;
            rt.anchorMin = new Vector2(0, 0.02f);
            rt.anchorMax = new Vector2(1, 0.08f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            Destroy(t.gameObject, 2f);
        }

        /// <summary>활성 상태인 UI 요소를 이름으로 찾는다 (테스트·자동화용).</summary>
        public T Find<T>(string name) where T : Component
        {
            return _root.GetComponentsInChildren<T>(false).FirstOrDefault(c => c.gameObject.name == name);
        }
    }
}
