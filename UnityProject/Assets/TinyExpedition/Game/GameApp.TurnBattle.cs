using System.Collections.Generic;
using System.Linq;
using TinyExpedition.Core;
using TinyExpedition.Core.Battle;
using TinyExpedition.Core.Data;
using TinyExpedition.Game.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace TinyExpedition.Game
{
    /// <summary>
    /// 턴제 전투 화면. 규칙은 Core의 TurnBattle에 있고, 여기서는 명령을 넘기고 2.5D 무대(BattleStage)에 재생을 맡긴다.
    ///
    /// 흐름: 명령 → Core가 다음 아군 차례까지 한 번에 계산 → 무대가 그 사이 로그를 연출로 재생 → 재생이 끝나면 다음 명령.
    /// 연출 중에는 공격·스킬·방어를 받지 않는다(대상 고르기, 자동·배속·건너뛰기·후퇴는 가능).
    /// 배속과 건너뛰기는 연출 속도만 바꾸므로 같은 시드·같은 명령이면 결과가 같다.
    ///
    /// 요소 이름 계약: Btn_TurnStage_&lt;id&gt;, Btn_Ally_&lt;i&gt;, Btn_Target_&lt;i&gt;, Btn_Attack, Btn_Skill, Btn_Guard,
    /// Btn_Auto(자동 켜기/끄기), Btn_Speed(배속), Btn_AutoAll(끝까지 건너뛰기), Btn_Retreat,
    /// Label_TurnInfo, Label_Prompt, Label_TurnLog, Label_Hp_A&lt;i&gt;/Label_Hp_E&lt;i&gt;, Order_&lt;i&gt;, Label_Outcome, Label_Reward, Label_Replay.
    /// </summary>
    public sealed partial class GameApp
    {
        private static readonly Color Danger = new Color32(0xc0, 0x4b, 0x5a, 0xff);
        private static readonly Color SkillBlue = new Color32(0x4f, 0x8c, 0xd9, 0xff);
        public static readonly float[] BattleSpeeds = { 1f, 2f, 4f };

        private TurnBattle _turn;
        private List<string> _turnParty;
        private int _enemyTarget;
        private int _allyTarget = -1;

        private BattleStage _stage;
        private Text _turnInfo, _turnPrompt, _turnLog;
        private Button _btnAttack, _btnSkill, _btnGuard, _btnAuto, _btnSpeed;
        private RectTransform _orderBar;
        private string _orderSignature;
        private float _autoDelay, _resultDelay = -1f;

        public TurnBattle CurrentTurnBattle => _turn;
        /// <summary>지금 전투의 2.5D 무대. 전투 화면이 아니면 null.</summary>
        public BattleStage Stage => _stage;
        /// <summary>연출 배속. 버튼은 1·2·4를 돌고, 테스트는 더 큰 값을 넣어 빨리 돌린다.</summary>
        public float BattleSpeed { get; set; } = 1f;
        /// <summary>켜면 아군 차례마다 자동 명령(TurnBattle.AutoCommand)을 넣는다.</summary>
        public bool AutoBattle { get; set; }
        /// <summary>연출 중이라 받지 않은 명령 수 (QA 확인용).</summary>
        public int IgnoredCommands { get; private set; }

        public void ShowTurnStages()
        {
            var col = BeginScreen("TurnStages");
            var party = AutoParty();
            UiKit.Label("Label_Party", col, party.Count == 0 ? "모집으로 캐릭터를 먼저 얻으세요." :
                "자동 편성: " + string.Join(", ", party.Select(id => Session.Db.GetCharacter(id).Name)), 32, UiKit.Muted, TextAnchor.MiddleCenter, 80);
            UiKit.Label("Label_TurnHelp", col, "속도 순서대로 한 명씩 행동합니다. 적을 눌러 대상을 고르고 공격 · 스킬(쿨타임) · 방어 중에서 고르세요.", 30, UiKit.Muted, TextAnchor.MiddleCenter, 70);
            foreach (var stage in Session.Db.TurnStages)
            {
                bool unlocked = Session.IsTurnStageUnlocked(stage.Id);
                Session.State.ClearedTurnStages.TryGetValue(stage.Id, out int stars);
                var text = stage.Id + " " + stage.Name + " · 웨이브 " + stage.Waves.Count
                    + (stars > 0 ? "  " + new string('★', stars) : "") + (unlocked ? "" : "  (잠김)");
                var b = UiKit.Button("Btn_TurnStage_" + stage.Id, col, text, () => StartTurn(stage.Id, party), unlocked ? UiKit.Accent : UiKit.Panel);
                b.interactable = unlocked && party.Count > 0;
            }
            UiKit.Button("Btn_Back", col, "돌아가기", ShowHome, UiKit.Panel);
        }

        private void StartTurn(string stageId, List<string> party)
        {
            var r = Session.StartTurnBattle(stageId, party);
            if (!r.Ok)
            {
                Toast(r.Error.ToString());
                return;
            }
            Save(); // 세션 시드가 넘어갔으니 저장 (후퇴 후 다시 들어와도 같은 판을 반복하지 않게)
            _turn = r.Value;
            _turnParty = party.ToList();
            _enemyTarget = 0;
            _allyTarget = -1;
            BuildTurnBattle();
        }

        public void ShowTurnBattle()
        {
            if (_turn == null) ShowTurnStages();
            else if (_stage == null) BuildTurnBattle();
            else RefreshTurnHud();
        }

        private void DestroyBattleStage()
        {
            if (_stage != null) Destroy(_stage.gameObject);
            _stage = null;
            _resultDelay = -1f;
        }

        // ---------------- 전투 화면 만들기 ----------------

        private static RectTransform Place(Component c, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static RectTransform Layer(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Stretch((RectTransform)go.transform);
            return (RectTransform)go.transform;
        }

        private RectTransform Pill(string name, Transform parent, Color color)
        {
            var rt = UiKit.Panel_(name, parent, color);
            UiKit.Round(rt.GetComponent<Image>());
            rt.GetComponent<Image>().raycastTarget = false;
            return rt;
        }

        private void BuildTurnBattle()
        {
            UiKit.Clear(_root);
            DestroyBattleStage();
            _root.GetComponent<Image>().enabled = false; // 뒤의 3D 무대가 보이게
            _gemsLabel = null;
            CurrentScreen = "TurnBattle";
            _orderSignature = null;
            _autoDelay = 0f;

            var unitLayer = Layer("UnitLayer", _root);
            _stage = BattleStage.Create(transform, _turn, _turnParty, unitLayer);
            _stage.Speed = BattleSpeed;
            _stage.OnEnemyClicked = slot => { _enemyTarget = slot; RefreshTurnHud(); };
            _stage.OnAllyClicked = slot => { _allyTarget = slot; RefreshTurnHud(); };
            _stage.OnPlaybackFinished = OnTurnPlaybackFinished;

            var hud = Layer("TurnHud", _root);
            var dark = new Color(0.09f, 0.09f, 0.18f, 0.8f);

            // 왼쪽 위: 스테이지·웨이브·라운드
            var info = Pill("InfoPanel", hud, dark);
            Place(info, new Vector2(0, 1), new Vector2(24, -20), new Vector2(560, 64));
            _turnInfo = UiKit.Label("Label_TurnInfo", info, "", 28, UiKit.TextColor, TextAnchor.MiddleCenter);
            UiKit.Stretch(_turnInfo.rectTransform);

            // 가운데 위: 행동 순서
            var order = Pill("OrderBar", hud, dark);
            Place(order, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(620, 92));
            var orderLayout = order.gameObject.AddComponent<HorizontalLayoutGroup>();
            orderLayout.spacing = 8;
            orderLayout.padding = new RectOffset(14, 14, 8, 8);
            orderLayout.childAlignment = TextAnchor.MiddleCenter;
            orderLayout.childControlWidth = orderLayout.childControlHeight = false;
            orderLayout.childForceExpandWidth = orderLayout.childForceExpandHeight = false;
            _orderBar = order;

            // 오른쪽 위: 자동·배속·건너뛰기·후퇴
            var tools = Layer("Tools", hud);
            Place(tools, new Vector2(1, 1), new Vector2(-24, -20), new Vector2(600, 64));
            var toolsLayout = tools.gameObject.AddComponent<HorizontalLayoutGroup>();
            toolsLayout.spacing = 10;
            toolsLayout.childControlWidth = toolsLayout.childControlHeight = true;
            toolsLayout.childForceExpandWidth = toolsLayout.childForceExpandHeight = true;
            _btnAuto = SmallButton("Btn_Auto", tools, "", () => { AutoBattle = !AutoBattle; _autoDelay = 0f; RefreshTurnHud(); }, UiKit.Panel);
            _btnSpeed = SmallButton("Btn_Speed", tools, "", CycleBattleSpeed, UiKit.Panel);
            SmallButton("Btn_AutoAll", tools, "건너뛰기", SkipTurnBattle, UiKit.Panel);
            SmallButton("Btn_Retreat", tools, "후퇴", RetreatTurn, Danger);

            // 아래: 차례 안내 + 명령
            var bottom = UiKit.Panel_("BottomBar", hud, new Color(0.07f, 0.07f, 0.15f, 0.72f));
            bottom.GetComponent<Image>().raycastTarget = false;
            bottom.anchorMin = new Vector2(0, 0);
            bottom.anchorMax = new Vector2(1, 0);
            bottom.pivot = new Vector2(0.5f, 0);
            bottom.sizeDelta = new Vector2(0, 176);
            bottom.anchoredPosition = Vector2.zero;

            _turnPrompt = UiKit.Label("Label_Prompt", bottom, "", 34, UiKit.Gold, TextAnchor.MiddleLeft);
            Place(_turnPrompt, new Vector2(0, 1), new Vector2(36, -18), new Vector2(980, 60));
            _turnLog = UiKit.Label("Label_TurnLog", bottom, "", 22, UiKit.Muted, TextAnchor.UpperLeft);
            Place(_turnLog, new Vector2(0, 0), new Vector2(36, 14), new Vector2(980, 78));

            var cmds = Layer("Commands", bottom);
            Place(cmds, new Vector2(1, 0.5f), new Vector2(-28, 0), new Vector2(820, 132));
            var cmdLayout = cmds.gameObject.AddComponent<HorizontalLayoutGroup>();
            cmdLayout.spacing = 16;
            cmdLayout.childControlWidth = cmdLayout.childControlHeight = true;
            cmdLayout.childForceExpandWidth = cmdLayout.childForceExpandHeight = true;
            _btnAttack = UiKit.Button("Btn_Attack", cmds, "공격", () => SubmitTurn(TurnCommand.Attack(_enemyTarget)));
            _btnSkill = UiKit.Button("Btn_Skill", cmds, "스킬", () =>
            {
                var skill = _turn?.CurrentActor?.Skill;
                SubmitTurn(TurnCommand.UseSkill(skill != null && skill.Target == SkillTarget.SingleAlly ? _allyTarget : _enemyTarget));
            }, SkillBlue);
            _btnSkill.GetComponentInChildren<Text>().fontSize = 34;
            _btnGuard = UiKit.Button("Btn_Guard", cmds, "방어", () => SubmitTurn(TurnCommand.Guard()), UiKit.Panel);

            _stage.Play(); // 적이 먼저 움직였으면 그 로그부터 재생
            RefreshTurnHud();
            if (!_stage.IsPlaying && _turn.IsOver) _resultDelay = 0.6f;
        }

        private static Button SmallButton(string name, Transform parent, string text, UnityEngine.Events.UnityAction onClick, Color color)
        {
            var b = UiKit.Button(name, parent, text, onClick, color, 64);
            b.GetComponentInChildren<Text>().fontSize = 26;
            return b;
        }

        // ---------------- 진행 ----------------

        private void Update()
        {
            if (_stage == null || _turn == null || CurrentScreen != "TurnBattle") return;
            _stage.Speed = BattleSpeed;
            RefreshTurnHud();
            if (_stage.IsPlaying) return;

            float dt = Time.deltaTime * Mathf.Max(1f, BattleSpeed);
            if (_turn.IsOver)
            {
                // 승패 연출을 잠깐 보여 주고 결과 화면으로
                if (_resultDelay < 0f) _resultDelay = 0.6f;
                _resultDelay -= dt;
                if (_resultDelay <= 0f) ShowTurnResult();
            }
            else if (AutoBattle)
            {
                _autoDelay += dt;
                if (_autoDelay >= 0.25f)
                {
                    _autoDelay = 0f;
                    SubmitTurn(_turn.AutoCommand());
                }
            }
        }

        private void OnTurnPlaybackFinished()
        {
            _autoDelay = 0f;
            if (CurrentScreen == "TurnBattle") RefreshTurnHud();
        }

        /// <summary>선택한 대상이 쓰러졌으면 살아 있는 대상으로 옮긴다. 회복 대상 기본값은 HP 비율이 가장 낮은 아군.</summary>
        private void FixTargets()
        {
            var b = _turn;
            if (_enemyTarget < 0 || _enemyTarget >= b.Enemies.Count || !b.Enemies[_enemyTarget].IsAlive)
                _enemyTarget = b.Enemies.First(u => u.IsAlive).Slot;
            if (_allyTarget < 0 || _allyTarget >= b.Allies.Count || !b.Allies[_allyTarget].IsAlive)
                _allyTarget = b.Allies.Where(u => u.IsAlive).OrderBy(u => (double)u.Hp / u.MaxHp).ThenBy(u => u.Slot).First().Slot;
        }

        private void RefreshTurnHud()
        {
            if (_turn == null || _stage == null || _turnInfo == null) return;
            var b = _turn;
            var shown = _stage.Display;
            bool playing = _stage.IsPlaying;
            bool waiting = !playing && !b.IsOver;

            // 위쪽 정보는 "재생한 곳까지"를 보여 준다 (연출보다 앞서가지 않게)
            SetText(_turnInfo, b.Stage.Id + " " + b.Stage.Name + "  ·  웨이브 " + (shown.WaveIndex + 1) + "/" + b.WaveCount + "  ·  라운드 " + shown.Round);
            SetText(_turnLog, string.Join("\n", b.Log.Take(shown.Cursor).Skip(System.Math.Max(0, shown.Cursor - 3)).Select(e => e.ToString())));

            var actor = b.CurrentActor;
            var skill = actor?.Skill;
            if (!b.IsOver) FixTargets();
            bool healSkill = skill != null && skill.Target == SkillTarget.SingleAlly;
            _stage.SelectedEnemy = waiting ? _enemyTarget : -1;
            _stage.SelectedAlly = waiting && healSkill ? _allyTarget : -1;

            if (b.IsOver) SetText(_turnPrompt, playing ? "…" : "전투 종료");
            else SetText(_turnPrompt, actor.Name + "의 차례 — " + (healSkill
                ? "공격 대상 " + b.Enemies[_enemyTarget].Name + " · 회복 대상 " + b.Allies[_allyTarget].Name
                : "대상 " + b.Enemies[_enemyTarget].Name));

            _btnAttack.interactable = waiting;
            _btnGuard.interactable = waiting;
            _btnSkill.interactable = waiting && actor.SkillReady;
            SetText(_btnSkill.GetComponentInChildren<Text>(), skill == null ? "스킬 없음" : actor.SkillReady ? skill.Name : skill.Name + " (쿨 " + actor.CooldownLeft + ")");
            SetText(_btnAuto.GetComponentInChildren<Text>(), AutoBattle ? "자동 ON" : "자동 OFF");
            _btnAuto.GetComponent<Image>().color = AutoBattle ? UiKit.Accent : UiKit.Panel;
            SetText(_btnSpeed.GetComponentInChildren<Text>(), "배속 ×" + BattleSpeed.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));

            if (!playing) RefreshOrderBar();
        }

        private static void SetText(Text t, string value)
        {
            if (t.text != value) t.text = value;
        }

        /// <summary>이번 라운드에 남은 행동 순서를 얼굴 칩으로. 맨 앞이 지금 차례.</summary>
        private void RefreshOrderBar()
        {
            var order = _turn.UpcomingOrder();
            string signature = _turn.Round + ":" + string.Join(",", order.Select(u => (u.Side == Side.Ally ? "A" : "E") + u.Slot));
            if (signature == _orderSignature) return;
            _orderSignature = signature;
            UiKit.Clear(_orderBar);
            for (int i = 0; i < order.Count && i < 7; i++)
            {
                var u = order[i];
                bool ally = u.Side == Side.Ally;
                float size = i == 0 ? 76 : 60;
                var chip = Pill("Order_" + i, _orderBar, ally ? SkillBlue : Danger);
                chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 0.5f);
                chip.sizeDelta = new Vector2(size, size);
                chip.gameObject.AddComponent<RectMask2D>();
                var face = new GameObject("Face", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                face.transform.SetParent(chip, false);
                face.raycastTarget = false;
                face.preserveAspect = true;
                face.sprite = ally ? BattleArt.Ally(_turnParty[u.Slot]) : BattleArt.Enemy(u.Name).Sprite;
                var rt = face.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                // 아군은 전신 그림이라 얼굴이 칩 가운데 오도록 크게 키워 아래로 내린다
                rt.sizeDelta = ally ? new Vector2(size * 1.9f, size * 2.3f) : new Vector2(size * 1.05f, size * 1.05f);
                rt.anchoredPosition = ally ? new Vector2(0, -size * 0.32f) : Vector2.zero;
            }
        }

        private void SubmitTurn(TurnCommand cmd)
        {
            if (_turn == null || _stage == null || _turn.IsOver) return;
            if (_stage.IsPlaying)
            {
                // 연출 중에는 명령을 받지 않는다 (연타로 다음 차례 명령이 잘못 들어가는 것을 막음)
                IgnoredCommands++;
                return;
            }
            var err = _turn.Submit(cmd);
            if (err != TurnCommandError.None) Toast(err == TurnCommandError.SkillOnCooldown ? "스킬 쿨타임 중입니다." : "지금은 그 명령을 쓸 수 없습니다: " + err);
            else _stage.Play();
            RefreshTurnHud();
        }

        private void CycleBattleSpeed()
        {
            int i = System.Array.IndexOf(BattleSpeeds, BattleSpeed);
            BattleSpeed = BattleSpeeds[(i + 1) % BattleSpeeds.Length]; // 목록에 없는 값(테스트용)이면 1배로 돌아간다
            RefreshTurnHud();
        }

        /// <summary>남은 전투를 자동 명령으로 끝까지 진행하고 연출 없이 결과로 간다.</summary>
        private void SkipTurnBattle()
        {
            if (_turn == null || _stage == null) return;
            _turn.RunAuto();
            _stage.SkipToEnd();
            ShowTurnResult();
        }

        private void RetreatTurn()
        {
            _turn = null; // 후퇴는 보상 없음
            ShowHome();
        }

        private void ShowTurnResult()
        {
            if (CurrentScreen == "TurnResult") return;
            var b = _turn;
            int gems = Session.ClaimTurnBattle(b);
            Save();

            // 무대는 그대로 두고(승리하면 아군이 뛴다) 그 위에 결과 카드를 올린다
            UiKit.Clear(_root);
            _root.GetComponent<Image>().enabled = false;
            _gemsLabel = null;
            _resultDelay = -1f;
            CurrentScreen = "TurnResult";
            var dim = UiKit.Panel_("Dim", _root, new Color(0.04f, 0.04f, 0.1f, 0.35f));
            var card = UiKit.Panel_("ResultCard", dim, new Color(0.11f, 0.12f, 0.2f, 0.94f));
            UiKit.Round(card.GetComponent<Image>());
            // 오른쪽에 둬서 왼쪽의 아군이 보이게
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(1f, 0.5f);
            card.anchoredPosition = new Vector2(-48, 0);
            card.sizeDelta = new Vector2(1020, 840);
            var col = UiKit.Column("Screen_TurnResult", card, 10, 32);

            UiKit.Label("Label_Outcome", col, b.Outcome == TurnOutcome.Victory ? "승리! " + new string('★', b.Stars) : b.Outcome == TurnOutcome.Timeout ? "시간 초과" : "패배",
                72, b.Outcome == TurnOutcome.Victory ? UiKit.Gold : UiKit.Muted, TextAnchor.MiddleCenter, 120);
            UiKit.Label("Label_TurnSummary", col, "웨이브 " + (b.WaveIndex + 1) + "/" + b.WaveCount + " · " + b.Round + "라운드 · 명령 " + b.Commands.Count + "개", 32);
            UiKit.Label("Label_Reward", col, "보석 +" + gems, 50, UiKit.TextColor);
            UiKit.Label("Label_TurnLog", col, string.Join("\n", b.Log.Skip(System.Math.Max(0, b.Log.Count - 6)).Select(e => e.ToString())),
                24, UiKit.Muted, TextAnchor.UpperLeft, 210);
            UiKit.Label("Label_Replay", col, "리플레이: " + TurnReplayCommand(b), 20, UiKit.Muted, TextAnchor.MiddleCenter, 110);
            // 화면 글자는 작아 옮겨 적다 틀리기 쉽다. 버그 리포트에는 Player.log의 이 줄을 그대로 붙인다.
            Debug.Log("[TurnReplay] " + b.Outcome + " · " + TurnReplayCommand(b));
            UiKit.Button("Btn_Back", col, "확인", () => { _turn = null; ShowTurnStages(); });
        }

        /// <summary>이 판을 그대로 다시 돌리는 qa-tool 명령줄.</summary>
        public string TurnReplayCommand(TurnBattle b) =>
            "qa turn-replay --stage " + b.Stage.Id + " --party " + string.Join(",", _turnParty)
            + " --seed " + b.Seed + " --auto false --commands " + string.Join(",", b.Commands);
    }
}
