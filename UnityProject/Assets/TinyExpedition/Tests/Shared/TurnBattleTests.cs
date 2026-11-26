using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TinyExpedition.Core;
using TinyExpedition.Core.Battle;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.Progress;

namespace TinyExpedition.Tests
{
    /// <summary>턴제 전투용 작은 데이터 도우미. 치명타 0·상태이상 확률 100이라 시드와 상관없이 결과가 정해진다.</summary>
    internal static class Turn
    {
        public static CharacterDef Ally(string id, int hp = 500, int atk = 50, int def = 0, int spd = 100, string skill = null)
        {
            var c = TestData.Char(id, Rarity.OneStar, hp, atk, def, spd);
            c.SkillId = skill;
            return c;
        }

        public static SkillDef Skill(string id, SkillTarget target = SkillTarget.SingleEnemy, int power = 100, int cooldown = 0,
            StatusKind status = StatusKind.None, int turns = 0, int statusPower = 0) =>
            new SkillDef { Id = id, Name = id, Target = target, Power = power, Cooldown = cooldown, Status = status, StatusTurns = turns, StatusPower = statusPower };

        public static TurnEnemyDef Foe(string name, int hp = 100, int atk = 10, int def = 0, int spd = 50, string skill = null) =>
            new TurnEnemyDef { Name = name, Hp = hp, Atk = atk, Def = def, Spd = spd, SkillId = skill };

        public static TurnStageDef Stage(string id, string requires, int gems, params TurnEnemyDef[][] waves) =>
            new TurnStageDef
            {
                Id = id, Name = id, RequiresStageId = requires, FirstClearGems = gems,
                Waves = waves.Select(w => new WaveDef { Enemies = w.ToList() }).ToList(),
            };

        public static TurnStageDef Stage(params TurnEnemyDef[][] waves) => Stage("t1", null, 100, waves);

        public static TurnEnemyDef[] Wave(params TurnEnemyDef[] enemies) => enemies;

        public static GameDatabase Db(IEnumerable<CharacterDef> chars, IEnumerable<SkillDef> skills = null, params TurnStageDef[] stages) =>
            new GameDatabase(chars, new BannerDef[0], new StageDef[0], skills, stages);

        /// <summary>파티·스킬·웨이브로 바로 전투를 만든다.</summary>
        public static TurnBattle Battle(CharacterDef[] party, SkillDef[] skills, params TurnEnemyDef[][] waves)
        {
            var stage = Stage(waves);
            return new TurnBattle(party, stage, Db(party, skills, stage), 1);
        }

        public static int Count(TurnBattle b, TurnLogKind kind, string actor = null) =>
            b.Log.Count(e => e.Kind == kind && (actor == null || e.Actor == actor));
    }

    [Category("TurnBattle")]
    public class TurnBattleTests
    {
        [Test, TC("TURN-001")]
        public void Order_SpeedDesc_TieAllyFirst_ThenSlot()
        {
            var party = new[] { Turn.Ally("a0", spd: 50), Turn.Ally("a1", spd: 80), Turn.Ally("a2", spd: 50) };
            var b = Turn.Battle(party, null, Turn.Wave(Turn.Foe("e0", hp: 9999, atk: 1, spd: 80), Turn.Foe("e1", hp: 9999, atk: 1, spd: 90)));

            // e1(90) 이 먼저 행동하고, 속도 80 동점은 아군 a1 → 적 e0, 속도 50 동점은 슬롯 순 a0 → a2
            Assert.That(b.Log[0].Actor, Is.EqualTo("e1"));
            Assert.That(b.UpcomingOrder().Select(u => u.Name), Is.EqualTo(new[] { "a1", "e0", "a0", "a2" }));

            b.Submit(TurnCommand.Guard());
            Assert.That(b.Log.Last(e => e.Kind == TurnLogKind.Attack).Actor, Is.EqualTo("e0"));
            Assert.That(b.CurrentActor.Name, Is.EqualTo("a0"));
            b.Submit(TurnCommand.Guard());
            Assert.That(b.CurrentActor.Name, Is.EqualTo("a2"));
        }

        [Test, TC("TURN-002")]
        public void DeadUnit_TurnIsSkipped()
        {
            // 적: 라운드 앞쪽에서 쓰러진 slow0은 자기 차례가 와도 행동하지 않는다
            var hero = Turn.Ally("hero", atk: 1000, spd: 100);
            var b = Turn.Battle(new[] { hero }, null,
                Turn.Wave(Turn.Foe("slow0", hp: 100, spd: 50), Turn.Foe("slow1", hp: 5000, atk: 1, spd: 40)));
            b.Submit(TurnCommand.Attack(0));
            Assert.That(b.Enemies[0].IsAlive, Is.False);
            Assert.That(Turn.Count(b, TurnLogKind.Attack, "slow0"), Is.EqualTo(0));
            Assert.That(Turn.Count(b, TurnLogKind.Attack, "slow1"), Is.EqualTo(1));

            // 아군: 먼저 쓰러진 fragile을 건너뛰고 tank 차례가 된다
            var b2 = Turn.Battle(new[] { Turn.Ally("fragile", hp: 1, spd: 10), Turn.Ally("tank", hp: 9999, spd: 5) }, null,
                Turn.Wave(Turn.Foe("fast", hp: 9999, atk: 50, spd: 100)));
            Assert.That(b2.Allies[0].IsAlive, Is.False);
            Assert.That(b2.CurrentActor.Name, Is.EqualTo("tank"));
        }

        [Test, TC("TURN-003")]
        public void Cooldown_Boundary()
        {
            var skills = new[] { Turn.Skill("cd2", cooldown: 2), Turn.Skill("cd0", cooldown: 0) };
            var b = Turn.Battle(new[] { Turn.Ally("c", skill: "cd2") }, skills, Turn.Wave(Turn.Foe("wall", hp: 99999, atk: 0, spd: 1)));

            Assert.That(b.Submit(TurnCommand.UseSkill(0)), Is.EqualTo(TurnCommandError.None), "1번째 차례: 사용");
            Assert.That(b.CurrentActor.CooldownLeft, Is.EqualTo(2));
            Assert.That(b.Check(TurnCommand.UseSkill(0)), Is.EqualTo(TurnCommandError.SkillOnCooldown), "2번째 차례");
            b.Submit(TurnCommand.Attack(0));
            Assert.That(b.CurrentActor.CooldownLeft, Is.EqualTo(1));
            Assert.That(b.Check(TurnCommand.UseSkill(0)), Is.EqualTo(TurnCommandError.SkillOnCooldown), "3번째 차례 (경계 바로 안)");
            b.Submit(TurnCommand.Attack(0));
            Assert.That(b.CurrentActor.CooldownLeft, Is.EqualTo(0));
            Assert.That(b.Submit(TurnCommand.UseSkill(0)), Is.EqualTo(TurnCommandError.None), "4번째 차례 (경계): 다시 사용");

            var b0 = Turn.Battle(new[] { Turn.Ally("c", skill: "cd0") }, skills, Turn.Wave(Turn.Foe("wall", hp: 99999, atk: 0, spd: 1)));
            Assert.That(b0.Submit(TurnCommand.UseSkill(0)), Is.EqualTo(TurnCommandError.None));
            Assert.That(b0.Submit(TurnCommand.UseSkill(0)), Is.EqualTo(TurnCommandError.None), "쿨타임 0은 매 차례 사용");
        }

        [Test, TC("TURN-004")]
        public void StunnedTurn_StillCountsForCooldown()
        {
            var skills = new[] { Turn.Skill("cd2", cooldown: 2), Turn.Skill("bonk", power: 1, cooldown: 9, status: StatusKind.Stun, turns: 1) };
            var b = Turn.Battle(new[] { Turn.Ally("c", hp: 9999, spd: 100, skill: "cd2") }, skills,
                Turn.Wave(Turn.Foe("stunner", hp: 99999, atk: 1, spd: 50, skill: "bonk")));

            b.Submit(TurnCommand.UseSkill(0));            // R1: 사용 → 쿨 2, 적이 기절시킴
            Assert.That(Turn.Count(b, TurnLogKind.Stunned), Is.EqualTo(1), "R2 차례는 기절로 넘어감");
            Assert.That(b.Round, Is.EqualTo(3));
            Assert.That(b.CurrentActor.CooldownLeft, Is.EqualTo(1), "기절로 넘어간 차례에도 쿨타임이 1 줄어야 함");
            Assert.That(b.Check(TurnCommand.UseSkill(0)), Is.EqualTo(TurnCommandError.SkillOnCooldown));
            b.Submit(TurnCommand.Attack(0));
            Assert.That(b.Check(TurnCommand.UseSkill(0)), Is.EqualTo(TurnCommandError.None), "R4에 다시 사용 가능");
        }

        [Test, TC("TURN-005")]
        public void Stun_SkipsTurn_ReapplyRefreshesNotAdds()
        {
            var skills = new[] { Turn.Skill("stun2", power: 1, status: StatusKind.Stun, turns: 2) };
            var b = Turn.Battle(new[] { Turn.Ally("s", hp: 9999, atk: 1, spd: 100, skill: "stun2") }, skills,
                Turn.Wave(Turn.Foe("foe", hp: 9999, atk: 1, spd: 50)));
            var foe = b.Enemies[0];

            b.Submit(TurnCommand.UseSkill(0));   // 기절 2 → foe 차례 건너뜀 → 1
            Assert.That(foe.StunLeft, Is.EqualTo(1));
            b.Submit(TurnCommand.UseSkill(0));   // 다시 걸면 max(1, 2) = 2 (합산이면 3) → 건너뜀 → 1
            Assert.That(foe.StunLeft, Is.EqualTo(1), "재적용은 합산이 아니라 갱신");
            b.Submit(TurnCommand.Guard());       // 건너뜀 → 0
            Assert.That(Turn.Count(b, TurnLogKind.Attack, "foe"), Is.EqualTo(0));
            b.Submit(TurnCommand.Guard());       // 이제 행동
            Assert.That(Turn.Count(b, TurnLogKind.Stunned), Is.EqualTo(3));
            Assert.That(Turn.Count(b, TurnLogKind.Attack, "foe"), Is.EqualTo(1));
        }

        [Test, TC("TURN-006")]
        public void Poison_StacksToThree_TicksAtRoundEnd_Expires()
        {
            var skills = new[] { Turn.Skill("venom", power: 1, status: StatusKind.Poison, turns: 2, statusPower: 10) };
            var b = Turn.Battle(new[] { Turn.Ally("p", hp: 9999, atk: 1, spd: 100, skill: "venom") }, skills,
                Turn.Wave(Turn.Foe("foe", hp: 1000, atk: 0, spd: 50)));
            var foe = b.Enemies[0];

            // 스킬 피해 1 + 라운드 끝 독 (중첩 × 10)
            b.Submit(TurnCommand.UseSkill(0));
            Assert.That((foe.Hp, foe.PoisonStacks), Is.EqualTo((989, 1)));
            b.Submit(TurnCommand.UseSkill(0));
            Assert.That((foe.Hp, foe.PoisonStacks), Is.EqualTo((968, 2)));
            b.Submit(TurnCommand.UseSkill(0));
            Assert.That((foe.Hp, foe.PoisonStacks), Is.EqualTo((937, 3)));
            b.Submit(TurnCommand.UseSkill(0));
            Assert.That((foe.Hp, foe.PoisonStacks), Is.EqualTo((906, 3)), "최대 3중첩");
            b.Submit(TurnCommand.Guard());       // 남은 라운드 1 → 마지막 피해 후 해제
            Assert.That((foe.Hp, foe.PoisonStacks, foe.PoisonLeft), Is.EqualTo((876, 0, 0)));
            b.Submit(TurnCommand.Guard());
            Assert.That(foe.Hp, Is.EqualTo(876), "해제된 뒤에는 피해 없음");
        }

        [Test, TC("TURN-007")]
        public void Guard_HalvesDamage_UntilOwnNextTurn_MinOne()
        {
            var b = Turn.Battle(new[] { Turn.Ally("g", hp: 1000, spd: 50) }, null, Turn.Wave(Turn.Foe("hitter", hp: 99999, atk: 100, spd: 100)));
            var g = b.Allies[0];
            Assert.That(g.Hp, Is.EqualTo(900));
            b.Submit(TurnCommand.Guard());
            Assert.That(g.Hp, Is.EqualTo(850), "방어 중 피해 절반");
            b.Submit(TurnCommand.Attack(0));
            Assert.That(g.Hp, Is.EqualTo(750), "자기 차례가 오면 방어가 풀림");

            var weak = Turn.Battle(new[] { Turn.Ally("g", hp: 1000, spd: 50) }, null, Turn.Wave(Turn.Foe("tickler", hp: 99999, atk: 1, spd: 100)));
            weak.Submit(TurnCommand.Guard());
            Assert.That(weak.Allies[0].Hp, Is.EqualTo(998), "방어해도 최소 피해 1");
        }

        [Test, TC("TURN-008")]
        public void WaveClear_NextWave_KeepsHpAndCooldown_ClearsStatus()
        {
            var skills = new[] { Turn.Skill("big", cooldown: 3), Turn.Skill("venom", power: 1, status: StatusKind.Poison, turns: 3, statusPower: 50) };
            var b = Turn.Battle(new[] { Turn.Ally("w", hp: 1000, atk: 1000, spd: 100, skill: "big") }, skills,
                Turn.Wave(Turn.Foe("spider", hp: 10, atk: 1, spd: 200, skill: "venom")),
                Turn.Wave(Turn.Foe("bat", hp: 10, atk: 10, spd: 50)),
                Turn.Wave(Turn.Foe("boss", hp: 5000, atk: 10, spd: 50)));
            var w = b.Allies[0];
            Assert.That(w.PoisonStacks, Is.EqualTo(1), "전제: 웨이브 1에서 독에 걸림");

            b.Submit(TurnCommand.UseSkill(0));
            Assert.That(b.WaveIndex, Is.EqualTo(1));
            Assert.That(b.Round, Is.EqualTo(2), "웨이브가 바뀌면 다음 라운드부터");
            Assert.That(w.Hp, Is.EqualTo(999), "HP 유지, 웨이브 전환 때 독 피해 없음");
            Assert.That(w.PoisonStacks, Is.EqualTo(0), "상태이상 해제");
            Assert.That(w.CooldownLeft, Is.EqualTo(3), "쿨타임 유지");

            b.RunAuto();
            Assert.That(b.Outcome, Is.EqualTo(TurnOutcome.Victory));
            Assert.That(Turn.Count(b, TurnLogKind.WaveCleared), Is.EqualTo(3));
            Assert.That(b.Stars, Is.EqualTo(3));
        }

        [Test, TC("TURN-009")]
        public void AllAlliesDown_IsDefeat()
        {
            var b = Turn.Battle(new[] { Turn.Ally("a", hp: 10, spd: 1), Turn.Ally("b", hp: 10, spd: 1) }, null,
                Turn.Wave(Turn.Foe("big", hp: 9999, atk: 100, spd: 100), Turn.Foe("big2", hp: 9999, atk: 100, spd: 100)));
            Assert.That(b.Outcome, Is.EqualTo(TurnOutcome.Defeat));
            Assert.That(b.IsOver && b.CurrentActor == null, Is.True);
            Assert.That(b.Stars, Is.EqualTo(0));
            Assert.That(b.Submit(TurnCommand.Guard()), Is.EqualTo(TurnCommandError.BattleOver));
        }

        [Test, TC("TURN-010")]
        public void SimultaneousWipe_ByPoison_IsDefeat()
        {
            // 서로 독을 걸고, 라운드 끝 독 피해로 아군과 마지막 적이 같이 쓰러진다
            var skills = new[] { Turn.Skill("venom", power: 1, status: StatusKind.Poison, turns: 3, statusPower: 30) };
            var b = Turn.Battle(new[] { Turn.Ally("a", hp: 30, atk: 1, spd: 100, skill: "venom") }, skills,
                Turn.Wave(Turn.Foe("e", hp: 31, atk: 0, spd: 50, skill: "venom")));
            b.Submit(TurnCommand.UseSkill(0));

            Assert.That(b.Allies[0].IsAlive || b.Enemies[0].IsAlive, Is.False, "전제: 둘 다 쓰러짐");
            Assert.That(b.Outcome, Is.EqualTo(TurnOutcome.Defeat), "동시 전멸은 패배");
            Assert.That(b.Stars, Is.EqualTo(0));
        }

        [Test, TC("TURN-011")]
        public void NoResolution_IsTimeoutAfterMaxRounds()
        {
            var b = Turn.Battle(new[] { Turn.Ally("tank", hp: 99999, atk: 1, def: 99999) }, null,
                Turn.Wave(Turn.Foe("rock", hp: 99999, atk: 1, def: 99999)));
            b.RunAuto();
            Assert.That(b.Outcome, Is.EqualTo(TurnOutcome.Timeout));
            Assert.That(b.Round, Is.EqualTo(TurnBattle.MaxRounds));
            Assert.That(b.Commands.Count, Is.EqualTo(TurnBattle.MaxRounds));
            Assert.That(b.Stars, Is.EqualTo(0));
        }

        [Test, TC("TURN-012")]
        public void SameSeedAndCommands_SameLog_ReplayMatches()
        {
            var db = TestData.LoadReal();
            var party = new[] { "c_lumi", "c_kai", "c_reed" }.Select(db.GetCharacter).ToList();
            var stage = db.GetTurnStage("T-2");

            var a = new TurnBattle(party, stage, db, 7);
            a.RunAuto();
            var b = new TurnBattle(party, stage, db, 7);
            b.RunAuto();
            var replay = TurnBattle.Replay(party, stage, db, 7, a.Commands.Select(c => TurnCommand.Parse(c.ToString())));

            Assert.That(a.Log.Any(e => e.Crit), Is.True, "전제: 치명타 난수가 쓰인 전투");
            Assert.That(b.Log.Select(e => e.ToString()), Is.EqualTo(a.Log.Select(e => e.ToString())));
            Assert.That(replay.Log.Select(e => e.ToString()), Is.EqualTo(a.Log.Select(e => e.ToString())));
            Assert.That(replay.Outcome, Is.EqualTo(a.Outcome));
        }

        [Test, TC("TURN-013")]
        public void InvalidCommand_IsRejected_StateUnchanged()
        {
            var skills = new[] { Turn.Skill("cd5", cooldown: 5), Turn.Skill("heal", SkillTarget.SingleAlly) };
            var b = Turn.Battle(new[] { Turn.Ally("plain", atk: 1000, spd: 100), Turn.Ally("caster", spd: 90, skill: "cd5"), Turn.Ally("medic", hp: 9999, spd: 80, skill: "heal") }, skills,
                Turn.Wave(Turn.Foe("e0", hp: 10, atk: 0, spd: 1), Turn.Foe("e1", hp: 99999, atk: 0, spd: 1)));

            Assert.That(b.Submit(TurnCommand.UseSkill(0)), Is.EqualTo(TurnCommandError.NoSkill));
            Assert.That(b.Submit(TurnCommand.Attack(2)), Is.EqualTo(TurnCommandError.InvalidTarget), "없는 슬롯");
            Assert.That(b.Submit(TurnCommand.Attack(-1)), Is.EqualTo(TurnCommandError.InvalidTarget));
            Assert.That(b.Log, Is.Empty, "거부된 명령은 아무것도 바꾸지 않음");
            Assert.That(b.CurrentActor.Name, Is.EqualTo("plain"));

            b.Submit(TurnCommand.Attack(0));     // e0 쓰러짐
            Assert.That(b.Submit(TurnCommand.Attack(0)), Is.EqualTo(TurnCommandError.InvalidTarget), "쓰러진 적");
            b.Submit(TurnCommand.UseSkill(1));   // caster 사용 → 쿨 5
            Assert.That(b.Submit(TurnCommand.UseSkill(5)), Is.EqualTo(TurnCommandError.InvalidTarget), "medic: 없는 아군 슬롯");
            int logCount = b.Log.Count;
            b.Submit(TurnCommand.Guard());
            b.Submit(TurnCommand.Guard());       // 다음 라운드 plain
            Assert.That(b.CurrentActor.Name, Is.EqualTo("caster"));
            Assert.That(b.Submit(TurnCommand.UseSkill(1)), Is.EqualTo(TurnCommandError.SkillOnCooldown));
            Assert.That(b.Log.Count, Is.GreaterThan(logCount));
            Assert.That(b.Commands.Select(c => c.ToString()), Is.EqualTo(new[] { "A0", "S1", "G", "G" }), "거부된 명령은 리플레이 기록에도 없음");
            Assert.Throws<System.FormatException>(() => TurnCommand.Parse("X9"));
        }

        [Test, TC("TURN-017")]
        public void Heal_CappedAtMaxHp_DeadAllyNotTargetable()
        {
            var skills = new[] { Turn.Skill("mend", SkillTarget.SingleAlly, power: 100) };
            var b = Turn.Battle(new[] { Turn.Ally("fragile", hp: 1, spd: 10), Turn.Ally("healer", hp: 500, atk: 100, spd: 50, skill: "mend") }, skills,
                Turn.Wave(Turn.Foe("e", hp: 99999, atk: 30, spd: 100)));
            Assert.That(b.Allies[0].IsAlive, Is.False, "전제: fragile이 먼저 쓰러짐");
            Assert.That(b.Check(TurnCommand.UseSkill(0)), Is.EqualTo(TurnCommandError.InvalidTarget), "쓰러진 아군은 회복 대상 아님");

            b.Submit(TurnCommand.Attack(0));     // R2: 적이 healer를 때림 (500 → 470)
            var healer = b.Allies[1];
            Assert.That(healer.Hp, Is.EqualTo(470));
            b.Submit(TurnCommand.UseSkill(1));   // 회복량 100이지만 최대 HP까지만
            var heal = b.Log.Last(e => e.Kind == TurnLogKind.Heal);
            Assert.That(heal.Amount, Is.EqualTo(30));
            Assert.That(healer.Hp, Is.LessThanOrEqualTo(healer.MaxHp));
        }

        [Test, TC("TURN-018")]
        public void EnemyAi_AttacksLowestHp_HealsOnlyWhenHurt()
        {
            var b = Turn.Battle(new[] { Turn.Ally("a", hp: 500, atk: 1, spd: 1), Turn.Ally("b", hp: 300, atk: 1, spd: 1) }, null,
                Turn.Wave(Turn.Foe("e", hp: 9999, atk: 10, spd: 100)));
            Assert.That(b.Log[0].Target, Is.EqualTo("b"), "HP가 가장 낮은 아군");

            var skills = new[] { Turn.Skill("sap", SkillTarget.SingleAlly, power: 200) };
            var h = Turn.Battle(new[] { Turn.Ally("hero", hp: 9999, atk: 500, spd: 50) }, skills,
                Turn.Wave(Turn.Foe("fairy", hp: 9999, atk: 1, spd: 100, skill: "sap"), Turn.Foe("rock", hp: 1000, atk: 0, spd: 1)));
            Assert.That(Turn.Count(h, TurnLogKind.Heal), Is.EqualTo(0), "다친 아군이 없으면 회복 대신 공격");
            Assert.That(Turn.Count(h, TurnLogKind.Attack, "fairy"), Is.EqualTo(1));
            h.Submit(TurnCommand.Attack(1));     // rock 1000 → 500 (60% 미만)
            var heal = h.Log.First(e => e.Kind == TurnLogKind.Heal);
            Assert.That((heal.Actor, heal.Target), Is.EqualTo(("fairy", "rock")));
        }
    }

    [Category("TurnBattle")]
    public class TurnSessionTests
    {
        private static GameSession NewSession(out TurnStageDef easy, PlayerState state = null)
        {
            easy = Turn.Stage("t1", null, 100, Turn.Wave(Turn.Foe("weak", hp: 10, atk: 1, spd: 1)));
            var locked = Turn.Stage("t2", "t1", 200, Turn.Wave(Turn.Foe("weak", hp: 10, atk: 1, spd: 1)));
            var hard = Turn.Stage("t_hard", null, 500, Turn.Wave(Turn.Foe("giant", hp: 99999, atk: 9999, spd: 999)));
            var chars = new[]
            {
                Turn.Ally("hero", hp: 5000, atk: 60, spd: 1), Turn.Ally("front", hp: 1, spd: 1), Turn.Ally("stranger"),
            };
            state = state ?? new PlayerState { Gems = 0 };
            state.Owned["hero"] = 1;
            state.Owned["front"] = 1;
            return new GameSession(Turn.Db(chars, null, easy, locked, hard), state);
        }

        [Test, TC("TURN-014")]
        public void Reward_FirstClearOnce_NoDoubleClaim()
        {
            var s = NewSession(out _);
            var battle = s.StartTurnBattle("t1", new[] { "hero" }).Value;
            Assert.That(s.ClaimTurnBattle(battle), Is.EqualTo(0), "끝나지 않은 전투는 보상 없음");
            battle.RunAuto();
            Assert.That(battle.Outcome, Is.EqualTo(TurnOutcome.Victory));

            Assert.That(s.ClaimTurnBattle(battle), Is.EqualTo(100));
            Assert.That(s.ClaimTurnBattle(battle), Is.EqualTo(0), "같은 전투 두 번째 수령");
            Assert.That(s.State.Gems, Is.EqualTo(100));

            var again = s.StartTurnBattle("t1", new[] { "hero" }).Value;
            again.RunAuto();
            Assert.That(s.ClaimTurnBattle(again), Is.EqualTo(0), "첫 클리어 보상은 한 번만");

            var forged = new TurnBattle(new[] { s.Db.GetCharacter("hero") }, s.Db.GetTurnStage("t1"), s.Db, 1);
            forged.RunAuto();
            Assert.That(s.ClaimTurnBattle(forged), Is.EqualTo(0), "세션이 시작하지 않은 전투는 보상 없음");
            var other = NewSession(out _);
            var otherBattle = other.StartTurnBattle("t1", new[] { "hero" }).Value;
            otherBattle.RunAuto();
            Assert.That(s.ClaimTurnBattle(otherBattle), Is.EqualTo(0), "다른 세션의 전투는 보상 없음");
            Assert.That(s.State.Gems, Is.EqualTo(100));
        }

        [Test, TC("TURN-015")]
        public void Start_RejectsLockedStage_NotOwned_InvalidParty()
        {
            var s = NewSession(out _);
            Assert.That(s.StartTurnBattle("t2", new[] { "hero" }).Error, Is.EqualTo(ActionError.StageLocked));
            Assert.That(s.StartTurnBattle("t1", new[] { "stranger" }).Error, Is.EqualTo(ActionError.CharacterNotOwned));
            Assert.That(s.StartTurnBattle("t1", new string[0]).Error, Is.EqualTo(ActionError.InvalidParty));
            Assert.That(s.StartTurnBattle("t1", new[] { "hero", "hero" }).Error, Is.EqualTo(ActionError.InvalidParty));
            Assert.That(s.StartTurnBattle("t1", new[] { "hero", "front", "stranger", "x" }).Error, Is.EqualTo(ActionError.InvalidParty));

            var win = s.StartTurnBattle("t1", new[] { "hero" }).Value;
            win.RunAuto();
            s.ClaimTurnBattle(win);
            Assert.That(s.StartTurnBattle("t2", new[] { "hero" }).Ok, Is.True, "t1을 깨면 t2가 열림");
        }

        [Test, TC("TURN-016")]
        public void Defeat_NoReward_BestStarsKept()
        {
            var s = NewSession(out _);
            var lose = s.StartTurnBattle("t_hard", new[] { "hero" }).Value;
            lose.RunAuto();
            Assert.That(lose.Outcome, Is.EqualTo(TurnOutcome.Defeat));
            Assert.That(s.ClaimTurnBattle(lose), Is.EqualTo(0));
            Assert.That(s.State.ClearedTurnStages.ContainsKey("t_hard"), Is.False);

            var state = new PlayerState { Gems = 0 };
            state.ClearedTurnStages["t1"] = 3;
            var s2 = NewSession(out var easy, state);
            easy.Waves[0].Enemies[0] = Turn.Foe("biter", hp: 100, atk: 10, spd: 100); // 앞줄(front)이 먼저 쓰러져 별 1
            var win = s2.StartTurnBattle("t1", new[] { "front", "hero" }).Value;
            win.RunAuto();
            Assert.That((win.Outcome, win.Stars), Is.EqualTo((TurnOutcome.Victory, 1)), "전제: 별 1 승리");
            s2.ClaimTurnBattle(win);
            Assert.That(s2.State.ClearedTurnStages["t1"], Is.EqualTo(3), "더 낮은 별로 덮어쓰지 않음");
            Assert.That(s2.State.Gems, Is.EqualTo(0), "이미 깬 스테이지라 보석 없음");
        }

        [Test, TC("TURN-019")]
        public void Save_TurnRecords_RoundTrip_RejectBadStars_OldSaveLoads()
        {
            var state = new PlayerState();
            state.ClearedTurnStages["T-1"] = 2;
            var loaded = SaveSerializer.Deserialize(SaveSerializer.Serialize(state));
            Assert.That(loaded.ClearedTurnStages["T-1"], Is.EqualTo(2));

            state.ClearedTurnStages["T-1"] = 4;
            Assert.Throws<SaveCorruptedException>(() => SaveSerializer.Deserialize(SaveSerializer.Serialize(state)));

            // 턴제가 생기기 전 세이브(필드 없음)도 읽힌다
            var old = SaveSerializer.Deserialize("{ \"Version\": 1, \"Gems\": 10 }");
            Assert.That(old.ClearedTurnStages, Is.Empty);
            var nulled = SaveSerializer.Deserialize("{ \"Version\": 1, \"Gems\": 10, \"ClearedTurnStages\": null }");
            Assert.That(nulled.ClearedTurnStages, Is.Not.Null);
        }
    }

    [Category("Data")]
    public class TurnDataTests
    {
        private static List<string> Errors(GameDatabase db) =>
            DataValidator.Validate(db).Where(i => i.Level == IssueLevel.Error).Select(i => i.ToString()).ToList();

        private static GameDatabase Db(SkillDef[] skills, TurnStageDef[] stages, string allySkill = null) =>
            Turn.Db(new[] { Turn.Ally("a", skill: allySkill) }, skills, stages);

        [Test, TC("TURN-020")]
        public void MissingSkillReference_IsError()
        {
            var ok = Turn.Stage(Turn.Wave(Turn.Foe("e")));
            Assert.That(Errors(Db(new SkillDef[0], new[] { ok }, "nope")).Any(e => e.Contains("없는 스킬을 참조") && e.Contains("characters/a")), Is.True);
            var bad = Turn.Stage(Turn.Wave(Turn.Foe("e", skill: "ghost")));
            Assert.That(Errors(Db(new SkillDef[0], new[] { bad })).Any(e => e.Contains("없는 스킬을 참조합니다: ghost")), Is.True);
            Assert.That(Errors(Db(new[] { Turn.Skill("s") }, new[] { ok }, "s")), Is.Empty);
        }

        [TestCase(0, 0, false, "power")]
        [TestCase(1, 0, true, null)]
        [TestCase(100, 9, true, null)]
        [TestCase(100, 10, false, "cooldown")]
        [TestCase(100, -1, false, "cooldown")]
        [TC("TURN-021")]
        public void SkillNumbers_Range(int power, int cooldown, bool valid, string field)
        {
            var errors = Errors(Db(new[] { Turn.Skill("s", power: power, cooldown: cooldown) }, new TurnStageDef[0], "s"));
            if (valid) Assert.That(errors, Is.Empty, string.Join("\n", errors));
            else Assert.That(errors.Any(e => e.Contains(field)), Is.True, string.Join("\n", errors));
        }

        [Test, TC("TURN-021")]
        public void SkillStatus_Rules()
        {
            var noTurns = Turn.Skill("s", status: StatusKind.Stun, turns: 0);
            Assert.That(Errors(Db(new[] { noTurns }, new TurnStageDef[0], "s")).Any(e => e.Contains("statusTurns")), Is.True);
            var poisonNoPower = Turn.Skill("s", status: StatusKind.Poison, turns: 2, statusPower: 0);
            Assert.That(Errors(Db(new[] { poisonNoPower }, new TurnStageDef[0], "s")).Any(e => e.Contains("statusPower")), Is.True);
            var healStun = Turn.Skill("s", SkillTarget.SingleAlly, status: StatusKind.Stun, turns: 1);
            Assert.That(Errors(Db(new[] { healStun }, new TurnStageDef[0], "s")).Any(e => e.Contains("상태이상을 붙일 수 없습니다")), Is.True);
            var dup = new[] { Turn.Skill("s"), Turn.Skill("s") };
            Assert.That(Errors(Db(dup, new TurnStageDef[0], "s")).Any(e => e.Contains("중복")), Is.True);
        }

        [Test, TC("TURN-022")]
        public void TurnStage_WaveRules_AndUnlockCycle()
        {
            var e = Turn.Foe("e");
            Assert.That(Errors(Db(null, new[] { Turn.Stage() })).Any(x => x.Contains("웨이브가 없습니다")), Is.True);
            Assert.That(Errors(Db(null, new[] { Turn.Stage(new TurnEnemyDef[0]) })).Any(x => x.Contains("적이 없습니다")), Is.True);
            Assert.That(Errors(Db(null, new[] { Turn.Stage(Turn.Wave(e, e, e)) })), Is.Empty, "적 3마리 (경계)");
            Assert.That(Errors(Db(null, new[] { Turn.Stage(Turn.Wave(e, e, e, e)) })).Any(x => x.Contains("최대 3마리")), Is.True);
            var five = Enumerable.Repeat(Turn.Wave(e), 5).ToArray();
            Assert.That(Errors(Db(null, new[] { Turn.Stage(five) })), Is.Empty, "웨이브 5개 (경계)");
            var six = Enumerable.Repeat(Turn.Wave(e), 6).ToArray();
            Assert.That(Errors(Db(null, new[] { Turn.Stage(six) })).Any(x => x.Contains("웨이브는 최대")), Is.True);

            var a = Turn.Stage("ta", null, 0, Turn.Wave(e));
            var b = Turn.Stage("tb", "tc", 0, Turn.Wave(e));
            var c = Turn.Stage("tc", "tb", 0, Turn.Wave(e));
            Assert.That(Errors(Db(null, new[] { a, b, c })).Any(x => x.Contains("turn-stages/tb") && x.Contains("순환")), Is.True);
        }
    }
}
