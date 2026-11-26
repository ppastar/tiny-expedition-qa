using System.Collections.Generic;
using System.Linq;
using TinyExpedition.Core;
using TinyExpedition.Core.Battle;
using TinyExpedition.Core.Data;
using UnityEngine;
using UnityEngine.UI;

namespace TinyExpedition.Game
{
    /// <summary>
    /// 턴제 전투 화면. 규칙은 Core의 TurnBattle에 있고, 여기서는 상태를 그리고 명령만 넘긴다.
    /// 명령을 하나 넘길 때마다 화면을 통째로 다시 그린다(적 차례는 Core가 다음 아군 차례까지 진행해 둔다).
    /// 요소 이름 계약: Btn_TurnStage_&lt;id&gt;, Btn_Ally_&lt;i&gt;, Btn_Target_&lt;i&gt;, Btn_Attack, Btn_Skill, Btn_Guard,
    /// Btn_AutoTurn, Btn_AutoAll, Btn_Retreat, Label_TurnInfo, Label_Order, Label_Prompt, Label_TurnLog, Label_Outcome, Label_Reward, Label_Replay.
    /// </summary>
    public sealed partial class GameApp
    {
        private static readonly Color Danger = new Color32(0xc0, 0x4b, 0x5a, 0xff);
        private static readonly Color Dead = new Color32(0x20, 0x22, 0x2c, 0xff);
        private static readonly Color HpGreen = new Color32(0x5c, 0xc8, 0x7a, 0xff);

        private TurnBattle _turn;
        private List<string> _turnParty;
        private int _enemyTarget;
        private int _allyTarget = -1;

        public TurnBattle CurrentTurnBattle => _turn;

        public void ShowTurnStages()
        {
            var col = BeginScreen("TurnStages");
            var party = AutoParty();
            UiKit.Label("Label_Party", col, party.Count == 0 ? "모집으로 캐릭터를 먼저 얻으세요." :
                "자동 편성: " + string.Join(", ", party.Select(id => Session.Db.GetCharacter(id).Name)), 32, UiKit.Muted, TextAnchor.MiddleCenter, 100);
            UiKit.Label("Label_TurnHelp", col, "속도 순서대로 한 명씩 행동합니다. 공격 · 스킬(쿨타임) · 방어 중에서 고르세요.", 30, UiKit.Muted, TextAnchor.MiddleCenter, 90);
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
            ShowTurnBattle();
        }

        public void ShowTurnBattle()
        {
            if (_turn == null)
            {
                ShowTurnStages();
                return;
            }
            if (_turn.IsOver)
            {
                ShowTurnResult();
                return;
            }
            FixTargets();
            var b = _turn;
            var actor = b.CurrentActor;
            var col = BeginScreen("TurnBattle");

            UiKit.Label("Label_TurnInfo", col, b.Stage.Id + " " + b.Stage.Name + "   웨이브 " + (b.WaveIndex + 1) + "/" + b.WaveCount + " · 라운드 " + b.Round,
                36, UiKit.TextColor, TextAnchor.MiddleCenter, 60);
            UiKit.Label("Label_Order", col, "순서 ▸ " + string.Join(" ▸ ", b.UpcomingOrder().Select(u => (u.Side == Side.Ally ? "" : "·") + u.Name)),
                26, UiKit.Muted, TextAnchor.MiddleCenter, 70);

            var field = UiKit.Row("Field", col, 24, 780);
            field.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            field.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            var allyCol = UiKit.Column("AllyColumn", field, 16, 0);
            var enemyCol = UiKit.Column("EnemyColumn", field, 16, 0);
            UiKit.Label("Label_AllySide", allyCol, "아군", 30, UiKit.Muted, TextAnchor.MiddleCenter, 44);
            UiKit.Label("Label_EnemySide", enemyCol, "적", 30, UiKit.Muted, TextAnchor.MiddleCenter, 44);
            for (int i = 0; i < b.Allies.Count; i++)
            {
                int slot = i;
                var u = b.Allies[i];
                UnitCard("Btn_Ally_" + i, allyCol, u, u == actor, actor?.Skill != null && actor.Skill.Target == SkillTarget.SingleAlly && i == _allyTarget,
                    () => { _allyTarget = slot; ShowTurnBattle(); });
            }
            for (int i = 0; i < b.Enemies.Count; i++)
            {
                int slot = i;
                UnitCard("Btn_Target_" + i, enemyCol, b.Enemies[i], false, i == _enemyTarget, () => { _enemyTarget = slot; ShowTurnBattle(); });
            }

            var skill = actor.Skill;
            string targetName = skill != null && skill.Target == SkillTarget.SingleAlly
                ? "공격 대상 " + b.Enemies[_enemyTarget].Name + " · 회복 대상 " + b.Allies[_allyTarget].Name
                : "대상 " + b.Enemies[_enemyTarget].Name;
            UiKit.Label("Label_Prompt", col, actor.Name + "의 차례 — " + targetName, 34, UiKit.Gold, TextAnchor.MiddleCenter, 60);

            var cmds = UiKit.Row("Commands", col, 16, 130);
            UiKit.Button("Btn_Attack", cmds, "공격", () => SubmitTurn(TurnCommand.Attack(_enemyTarget)));
            var skillBtn = UiKit.Button("Btn_Skill", cmds, skill == null ? "스킬 없음" : actor.SkillReady ? skill.Name : skill.Name + " (쿨 " + actor.CooldownLeft + ")",
                () => SubmitTurn(TurnCommand.UseSkill(skill.Target == SkillTarget.SingleAlly ? _allyTarget : _enemyTarget)), (Color)new Color32(0x4f, 0x8c, 0xd9, 0xff));
            skillBtn.interactable = actor.SkillReady;
            skillBtn.GetComponentInChildren<Text>().fontSize = 34;
            UiKit.Button("Btn_Guard", cmds, "방어", () => SubmitTurn(TurnCommand.Guard()), UiKit.Panel);

            var autoRow = UiKit.Row("AutoRow", col, 16, 100);
            UiKit.Button("Btn_AutoTurn", autoRow, "자동 1턴", () => SubmitTurn(_turn.AutoCommand()), UiKit.Panel, 100);
            UiKit.Button("Btn_AutoAll", autoRow, "끝까지 자동", () => { _turn.RunAuto(); ShowTurnBattle(); }, UiKit.Panel, 100);
            UiKit.Button("Btn_Retreat", autoRow, "후퇴", RetreatTurn, Danger, 100);

            UiKit.Label("Label_TurnLog", col, string.Join("\n", b.Log.Skip(System.Math.Max(0, b.Log.Count - 7)).Select(e => e.ToString())),
                26, UiKit.Muted, TextAnchor.UpperLeft, 300);
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

        private void UnitCard(string name, Transform parent, TurnUnit u, bool acting, bool selected, UnityEngine.Events.UnityAction onClick)
        {
            var color = !u.IsAlive ? Dead : acting ? UiKit.Accent : selected ? (Color)new Color32(0x6b, 0x55, 0x2a, 0xff) : UiKit.Panel;
            var statuses = new List<string>();
            if (u.Guarding) statuses.Add("방어");
            if (u.StunLeft > 0) statuses.Add("기절" + u.StunLeft);
            if (u.PoisonStacks > 0) statuses.Add("독×" + u.PoisonStacks);
            string skillLine = u.Side == Side.Ally && u.Skill != null ? (u.SkillReady ? "스킬 준비" : "스킬 쿨 " + u.CooldownLeft) : "";
            string text = (selected ? "▶ " : "") + u.Name + "\nHP " + u.Hp + "/" + u.MaxHp
                + (statuses.Count > 0 ? "\n" + string.Join(" ", statuses) : "") + (skillLine != "" ? "\n" + skillLine : "");

            var btn = UiKit.Button(name, parent, text, onClick, color, 200);
            btn.interactable = u.IsAlive;
            var label = btn.GetComponentInChildren<Text>();
            label.fontSize = 28;
            label.color = u.IsAlive ? UiKit.TextColor : UiKit.Muted;

            // 아래쪽 HP 막대
            var bar = new GameObject("HpBar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(btn.transform, false);
            bar.GetComponent<Image>().color = u.Hp * 10 < u.MaxHp * 3 ? Danger : HpGreen;
            bar.GetComponent<Image>().raycastTarget = false;
            var rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2((float)u.Hp / u.MaxHp, 0.05f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private void SubmitTurn(TurnCommand cmd)
        {
            var err = _turn.Submit(cmd);
            if (err != TurnCommandError.None) Toast(err == TurnCommandError.SkillOnCooldown ? "스킬 쿨타임 중입니다." : "지금은 그 명령을 쓸 수 없습니다: " + err);
            ShowTurnBattle();
        }

        private void RetreatTurn()
        {
            _turn = null; // 후퇴는 보상 없음
            ShowHome();
        }

        private void ShowTurnResult()
        {
            var b = _turn;
            int gems = Session.ClaimTurnBattle(b);
            Save();
            var col = BeginScreen("TurnResult");
            UiKit.Label("Label_Outcome", col, b.Outcome == TurnOutcome.Victory ? "승리! " + new string('★', b.Stars) : b.Outcome == TurnOutcome.Timeout ? "시간 초과" : "패배",
                72, b.Outcome == TurnOutcome.Victory ? UiKit.Gold : UiKit.Muted, TextAnchor.MiddleCenter, 160);
            UiKit.Label("Label_TurnSummary", col, "웨이브 " + (b.WaveIndex + 1) + "/" + b.WaveCount + " · " + b.Round + "라운드 · 명령 " + b.Commands.Count + "개", 34);
            UiKit.Label("Label_Reward", col, "보석 +" + gems, 52, UiKit.TextColor);
            UiKit.Label("Label_TurnLog", col, string.Join("\n", b.Log.Skip(System.Math.Max(0, b.Log.Count - 10)).Select(e => e.ToString())),
                26, UiKit.Muted, TextAnchor.UpperLeft, 420);
            UiKit.Label("Label_Replay", col, "리플레이: qa turn-replay --stage " + b.Stage.Id + " --party " + string.Join(",", _turnParty)
                + " --seed " + b.Seed + " --commands " + string.Join(",", b.Commands), 22, UiKit.Muted, TextAnchor.MiddleCenter, 120);
            UiKit.Button("Btn_Back", col, "확인", () => { _turn = null; ShowTurnStages(); });
        }
    }
}
