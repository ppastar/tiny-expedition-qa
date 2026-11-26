using System;
using System.Collections.Generic;
using System.Linq;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.Random;

namespace TinyExpedition.Core.Battle
{
    public sealed class TurnUnit
    {
        public string Name { get; }
        public Side Side { get; }
        public int Slot { get; }
        public int MaxHp { get; }
        public int Hp { get; internal set; }
        public int Atk { get; }
        public int Def { get; }
        public int Spd { get; }
        public int CritRate { get; }
        /// <summary>없으면 null (일반 공격·방어만).</summary>
        public SkillDef Skill { get; }
        /// <summary>0이면 스킬 사용 가능. 쓴 뒤 Cooldown으로 올라가 자기 차례가 끝날 때마다 1씩 준다.</summary>
        public int CooldownLeft { get; internal set; }
        /// <summary>방어 중이면 받는 피해 절반. 자기 다음 차례가 시작될 때 풀린다.</summary>
        public bool Guarding { get; internal set; }
        /// <summary>남은 기절 차례 수. 0보다 크면 다음 자기 차례를 건너뛴다.</summary>
        public int StunLeft { get; internal set; }
        public int PoisonStacks { get; internal set; }
        /// <summary>독이 남은 라운드 수.</summary>
        public int PoisonLeft { get; internal set; }
        /// <summary>독 중첩 1개당 라운드 끝 피해.</summary>
        public int PoisonPower { get; internal set; }

        public bool IsAlive => Hp > 0;
        public bool SkillReady => Skill != null && CooldownLeft == 0;

        internal TurnUnit(string name, Side side, int slot, int hp, int atk, int def, int spd, int critRate, SkillDef skill)
        {
            Name = name; Side = side; Slot = slot; MaxHp = hp; Hp = hp; Atk = atk; Def = def; Spd = spd; CritRate = critRate; Skill = skill;
        }

        internal void ClearStatuses()
        {
            StunLeft = 0; PoisonStacks = 0; PoisonLeft = 0; PoisonPower = 0; Guarding = false;
        }
    }

    public enum TurnActionKind { Attack, Skill, Guard }

    /// <summary>아군 한 명의 명령. Target은 단일 대상일 때만 쓰는 슬롯 번호(상대 대상이면 적 슬롯, 회복이면 아군 슬롯).</summary>
    public struct TurnCommand
    {
        public TurnActionKind Kind;
        public int Target;

        public TurnCommand(TurnActionKind kind, int target = 0) { Kind = kind; Target = target; }

        public static TurnCommand Attack(int target) => new TurnCommand(TurnActionKind.Attack, target);
        public static TurnCommand UseSkill(int target = 0) => new TurnCommand(TurnActionKind.Skill, target);
        public static TurnCommand Guard() => new TurnCommand(TurnActionKind.Guard);

        /// <summary>리플레이 문자열 한 칸: A0 = 0번 적 공격, S1 = 스킬(대상 1번), G = 방어.</summary>
        public override string ToString() =>
            Kind == TurnActionKind.Guard ? "G" : (Kind == TurnActionKind.Attack ? "A" : "S") + Target;

        public static TurnCommand Parse(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s == "G") return Guard();
            if (s.Length >= 2 && (s[0] == 'A' || s[0] == 'S') && int.TryParse(s.Substring(1), out int t) && t >= 0)
                return new TurnCommand(s[0] == 'A' ? TurnActionKind.Attack : TurnActionKind.Skill, t);
            throw new FormatException("명령 형식이 아닙니다 (A0 / S1 / G): " + s);
        }
    }

    public enum TurnCommandError { None, BattleOver, NoSkill, SkillOnCooldown, InvalidTarget }

    public enum TurnOutcome { Ongoing, Victory, Defeat, Timeout }

    public enum TurnLogKind { Attack, Skill, Heal, Guard, Stunned, StatusApplied, PoisonTick, Defeated, WaveCleared, End }

    public sealed class TurnLogEntry
    {
        public int Round { get; }
        public int Wave { get; }
        public TurnLogKind Kind { get; }
        public string Actor { get; }
        public string Target { get; }
        public int Amount { get; }
        public bool Crit { get; }
        public string Text { get; }

        internal TurnLogEntry(int round, int wave, TurnLogKind kind, string actor, string target, int amount, bool crit, string text)
        {
            Round = round; Wave = wave; Kind = kind; Actor = actor; Target = target; Amount = amount; Crit = crit; Text = text;
        }

        public override string ToString() => "W" + Wave + " R" + Round + " " + Text;
    }

    /// <summary>
    /// 턴제 전투. 같은 파티·스테이지·시드·명령 목록이면 항상 같은 결과가 나온다 (리플레이 = 시드 + 명령 목록).
    /// 규칙:
    /// - 라운드 시작 때 살아 있는 유닛의 행동 순서를 확정한다: 속도(Spd) 내림차순, 동률이면 아군 먼저, 그다음 슬롯 번호 순.
    ///   라운드 도중 쓰러진 유닛은 자기 차례가 와도 건너뛴다.
    /// - 아군 차례에는 명령(공격 / 스킬 / 방어)을 기다리고, 적 차례·기절로 넘어가는 차례는 자동으로 진행한다.
    /// - 피해 = BattleSimulator.CalcDamage(공격력 × 위력%, 방어). 치명타는 아군만. 방어 중이면 절반(내림, 최소 1).
    /// - 방어는 자기 다음 차례가 시작될 때 풀린다.
    /// - 쿨타임 N: 스킬을 쓴 차례가 끝날 때 N이 되고, 이후 자기 차례가 끝날 때마다 1씩 준다(기절로 넘어간 차례도 센다).
    ///   즉 쓴 뒤 자기 차례 N번 동안 못 쓰고, N+1번째 차례에 다시 쓸 수 있다.
    /// - 기절: 다음 자기 차례를 건너뛴다. 다시 걸리면 남은 횟수를 더하지 않고 큰 값으로 갱신한다.
    /// - 독: 라운드 끝에 살아 있는 모든 유닛이 한꺼번에 (중첩 × 위력) 피해를 받고 남은 라운드가 1 준다.
    ///   다시 걸리면 중첩 +1(최대 3), 남은 라운드는 큰 값으로 갱신.
    /// - 판정: 행동 하나 또는 라운드 끝 독 피해가 끝날 때마다 한다. 아군 전멸이면 패배 — 같은 처리에서 적도 전멸했어도 패배(동시 전멸 = 패배).
    ///   적 웨이브가 전멸하면 그 라운드는 거기서 끝나고, 마지막 웨이브였으면 승리. 아니면 다음 라운드부터 다음 웨이브.
    ///   웨이브가 바뀔 때 아군의 상태이상·방어는 풀리고 HP·쿨타임은 이어진다.
    /// - MaxRounds 라운드가 끝나도 결판이 안 나면 Timeout(패배 취급).
    /// - 별: 아군 전원 생존 3, 1명만 생존 1, 그 외 2.
    /// - 적 AI: 스킬이 준비됐고 쓸 만하면(회복은 다친 아군이 있을 때만) 스킬, 아니면 HP가 가장 낮은 아군을 공격(동률이면 앞 슬롯).
    /// </summary>
    public sealed class TurnBattle
    {
        public const int MaxRounds = 30;
        public const int MaxPartySize = 3;
        public const int MaxPoisonStacks = 3;

        public ulong Seed { get; }
        public TurnStageDef Stage { get; }
        public IReadOnlyList<TurnUnit> Allies => _allies;
        /// <summary>현재 웨이브의 적.</summary>
        public IReadOnlyList<TurnUnit> Enemies => _enemies;
        /// <summary>0부터.</summary>
        public int WaveIndex { get; private set; }
        public int WaveCount => Stage.Waves.Count;
        public int Round { get; private set; }
        public TurnOutcome Outcome { get; private set; } = TurnOutcome.Ongoing;
        public bool IsOver => Outcome != TurnOutcome.Ongoing;
        /// <summary>명령을 기다리는 아군. 전투가 끝났으면 null.</summary>
        public TurnUnit CurrentActor { get; private set; }
        public IReadOnlyList<TurnLogEntry> Log => _log;
        /// <summary>지금까지 받은 아군 명령 (리플레이용).</summary>
        public IReadOnlyList<TurnCommand> Commands => _commands;
        /// <summary>승리 시 1~3. 그 외 0.</summary>
        public int Stars { get; private set; }
        /// <summary>GameSession이 보상을 지급했는지. 같은 전투로 두 번 받지 못하게 한다.</summary>
        public bool RewardClaimed { get; internal set; }

        private readonly List<TurnUnit> _allies;
        private List<TurnUnit> _enemies;
        private readonly GameDatabase _db;
        private readonly IRandom _rng;
        private readonly Queue<TurnUnit> _queue = new Queue<TurnUnit>();
        private readonly List<TurnLogEntry> _log = new List<TurnLogEntry>();
        private readonly List<TurnCommand> _commands = new List<TurnCommand>();

        public TurnBattle(IReadOnlyList<CharacterDef> party, TurnStageDef stage, GameDatabase db, ulong seed)
        {
            if (party == null || party.Count == 0) throw new ArgumentException("파티가 비어 있습니다.", nameof(party));
            if (party.Count > MaxPartySize) throw new ArgumentException("파티는 최대 " + MaxPartySize + "명입니다.", nameof(party));
            if (party.Select(p => p.Id).Distinct().Count() != party.Count) throw new ArgumentException("같은 캐릭터를 두 번 넣을 수 없습니다.", nameof(party));
            if (stage == null || stage.Waves.Count == 0 || stage.Waves.Any(w => w.Enemies.Count == 0))
                throw new ArgumentException("웨이브가 없거나 빈 웨이브가 있습니다.", nameof(stage));

            Stage = stage;
            Seed = seed;
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _rng = new Pcg32(seed);
            _allies = party.Select((c, i) => new TurnUnit(c.Name, Side.Ally, i, c.Hp, c.Atk, c.Def, c.Spd, c.CritRate, FindSkill(c.SkillId))).ToList();
            _enemies = SpawnWave(0);
            StartRound(1);
            Advance();
        }

        private SkillDef FindSkill(string id) => id != null && _db.HasSkill(id) ? _db.GetSkill(id) : null;

        private List<TurnUnit> SpawnWave(int index) =>
            Stage.Waves[index].Enemies.Select((e, i) => new TurnUnit(e.Name, Side.Enemy, i, e.Hp, e.Atk, e.Def, e.Spd, 0, FindSkill(e.SkillId))).ToList();

        /// <summary>이번 라운드에 아직 차례가 남은 유닛 (지금 차례 포함). 화면의 순서 띠용.</summary>
        public IReadOnlyList<TurnUnit> UpcomingOrder()
        {
            var list = new List<TurnUnit>();
            if (CurrentActor != null) list.Add(CurrentActor);
            list.AddRange(_queue.Where(u => u.IsAlive));
            return list;
        }

        // ---------------- 명령 ----------------

        /// <summary>명령이 지금 유효한지. 유효하지 않으면 Submit해도 아무것도 바뀌지 않는다.</summary>
        public TurnCommandError Check(TurnCommand cmd)
        {
            if (IsOver || CurrentActor == null) return TurnCommandError.BattleOver;
            var actor = CurrentActor;
            switch (cmd.Kind)
            {
                case TurnActionKind.Guard:
                    return TurnCommandError.None;
                case TurnActionKind.Attack:
                    return IsAliveAt(_enemies, cmd.Target) ? TurnCommandError.None : TurnCommandError.InvalidTarget;
                default:
                    if (actor.Skill == null) return TurnCommandError.NoSkill;
                    if (actor.CooldownLeft > 0) return TurnCommandError.SkillOnCooldown;
                    if (actor.Skill.Target == SkillTarget.SingleEnemy && !IsAliveAt(_enemies, cmd.Target)) return TurnCommandError.InvalidTarget;
                    if (actor.Skill.Target == SkillTarget.SingleAlly && !IsAliveAt(_allies, cmd.Target)) return TurnCommandError.InvalidTarget;
                    return TurnCommandError.None;
            }
        }

        private static bool IsAliveAt(List<TurnUnit> units, int slot) => slot >= 0 && slot < units.Count && units[slot].IsAlive;

        /// <summary>지금 차례인 아군의 명령을 실행하고, 다음 아군 차례(또는 전투 끝)까지 진행한다.</summary>
        public TurnCommandError Submit(TurnCommand cmd)
        {
            var err = Check(cmd);
            if (err != TurnCommandError.None) return err;
            var actor = CurrentActor;
            CurrentActor = null;
            _commands.Add(cmd);
            Act(actor, cmd);
            EndTurn(actor, cmd.Kind == TurnActionKind.Skill);
            Judge();
            Advance();
            return TurnCommandError.None;
        }

        /// <summary>자동 전투용 아군 명령: 스킬이 준비됐고 쓸 만하면 스킬, 아니면 HP가 가장 낮은 적 공격.</summary>
        public TurnCommand AutoCommand()
        {
            var actor = CurrentActor;
            if (actor == null) return TurnCommand.Guard();
            var skillCmd = SkillChoice(actor, _allies, _enemies);
            if (skillCmd.HasValue) return skillCmd.Value;
            return TurnCommand.Attack(Weakest(_enemies).Slot);
        }

        /// <summary>스킬을 쓸 만하면 그 명령. 회복은 HP 60% 미만인 자기편이 있을 때만.</summary>
        private static TurnCommand? SkillChoice(TurnUnit actor, List<TurnUnit> own, List<TurnUnit> foes)
        {
            if (!actor.SkillReady) return null;
            switch (actor.Skill.Target)
            {
                case SkillTarget.SingleEnemy: return TurnCommand.UseSkill(Weakest(foes).Slot);
                case SkillTarget.AllEnemies: return TurnCommand.UseSkill();
                default:
                    var hurt = own.Where(u => u.IsAlive && u.Hp * 10 < u.MaxHp * 6)
                        .OrderBy(u => (double)u.Hp / u.MaxHp).ThenBy(u => u.Slot).FirstOrDefault();
                    if (hurt == null) return null;
                    return TurnCommand.UseSkill(hurt.Slot);
            }
        }

        private static TurnUnit Weakest(List<TurnUnit> units) =>
            units.Where(u => u.IsAlive).OrderBy(u => u.Hp).ThenBy(u => u.Slot).First();

        /// <summary>명령 목록으로 전투를 다시 돌린다. 명령이 모자라면 autoRest면 자동, 아니면 그 자리에서 멈춘다(IsOver = false).</summary>
        public static TurnBattle Replay(IReadOnlyList<CharacterDef> party, TurnStageDef stage, GameDatabase db, ulong seed,
            IEnumerable<TurnCommand> commands, bool autoRest = false)
        {
            var b = new TurnBattle(party, stage, db, seed);
            foreach (var c in commands ?? Enumerable.Empty<TurnCommand>())
            {
                if (b.IsOver) break;
                var err = b.Submit(c);
                if (err != TurnCommandError.None)
                    throw new InvalidOperationException((b.Commands.Count + 1) + "번째 명령 " + c + "을(를) 쓸 수 없습니다: " + err);
            }
            if (autoRest) b.RunAuto();
            return b;
        }

        /// <summary>남은 아군 차례를 모두 AutoCommand로 진행한다.</summary>
        public void RunAuto()
        {
            while (!IsOver) Submit(AutoCommand());
        }

        // ---------------- 진행 ----------------

        private void StartRound(int round)
        {
            Round = round;
            _queue.Clear();
            foreach (var u in _allies.Concat(_enemies).Where(u => u.IsAlive)
                         .OrderByDescending(u => u.Spd)
                         .ThenBy(u => u.Side == Side.Ally ? 0 : 1)
                         .ThenBy(u => u.Slot))
                _queue.Enqueue(u);
        }

        /// <summary>다음 아군 차례가 오거나 전투가 끝날 때까지 진행한다.</summary>
        private void Advance()
        {
            while (!IsOver)
            {
                if (_queue.Count == 0)
                {
                    EndRound();
                    if (IsOver) return;
                    if (Round >= MaxRounds)
                    {
                        Finish(TurnOutcome.Timeout);
                        return;
                    }
                    StartRound(Round + 1);
                    continue;
                }

                var unit = _queue.Dequeue();
                if (!unit.IsAlive) continue; // 이번 라운드에 먼저 쓰러짐
                unit.Guarding = false;

                if (unit.StunLeft > 0)
                {
                    unit.StunLeft--;
                    Add(TurnLogKind.Stunned, unit.Name, null, 0, false, unit.Name + " 기절해서 움직이지 못함");
                    EndTurn(unit, false);
                    continue;
                }

                if (unit.Side == Side.Ally)
                {
                    CurrentActor = unit;
                    return;
                }

                var cmd = SkillChoice(unit, _enemies, _allies) ?? TurnCommand.Attack(Weakest(_allies).Slot);
                Act(unit, cmd);
                EndTurn(unit, cmd.Kind == TurnActionKind.Skill);
                Judge();
            }
        }

        private void EndTurn(TurnUnit unit, bool usedSkill)
        {
            if (usedSkill) unit.CooldownLeft = unit.Skill.Cooldown;
            else if (unit.CooldownLeft > 0) unit.CooldownLeft--;
        }

        private void EndRound()
        {
            // 독은 라운드 끝에 모두 한꺼번에 받는다. 판정은 전부 받은 뒤에 한 번.
            foreach (var u in _allies.Concat(_enemies).Where(u => u.IsAlive && u.PoisonLeft > 0).ToList())
            {
                int dmg = u.PoisonStacks * u.PoisonPower;
                u.Hp = Math.Max(0, u.Hp - dmg);
                Add(TurnLogKind.PoisonTick, null, u.Name, dmg, false, u.Name + " 독 피해 " + dmg + " (" + u.PoisonStacks + "중첩) [남은 HP " + u.Hp + "]");
                if (--u.PoisonLeft == 0) { u.PoisonStacks = 0; u.PoisonPower = 0; }
                if (!u.IsAlive) Add(TurnLogKind.Defeated, null, u.Name, 0, false, u.Name + " 쓰러짐");
            }
            Judge();
        }

        /// <summary>전멸 판정. 아군 전멸을 먼저 본다 (동시 전멸 = 패배).</summary>
        private void Judge()
        {
            if (IsOver) return;
            if (!_allies.Any(u => u.IsAlive))
            {
                Finish(TurnOutcome.Defeat);
                return;
            }
            if (_enemies.Any(u => u.IsAlive)) return;

            Add(TurnLogKind.WaveCleared, null, null, WaveIndex + 1, false, "웨이브 " + (WaveIndex + 1) + " 정리");
            if (WaveIndex + 1 >= WaveCount)
            {
                Finish(TurnOutcome.Victory);
                return;
            }
            WaveIndex++;
            _enemies = SpawnWave(WaveIndex);
            foreach (var a in _allies) a.ClearStatuses();
            _queue.Clear(); // 이 라운드는 여기서 끝, 다음 라운드부터 새 웨이브
        }

        private void Finish(TurnOutcome outcome)
        {
            Outcome = outcome;
            CurrentActor = null;
            _queue.Clear();
            if (outcome == TurnOutcome.Victory)
            {
                int alive = _allies.Count(a => a.IsAlive);
                Stars = alive == _allies.Count ? 3 : alive == 1 && _allies.Count > 1 ? 1 : 2;
            }
            Add(TurnLogKind.End, null, null, Stars, false,
                outcome == TurnOutcome.Victory ? "승리 (별 " + Stars + ")" : outcome == TurnOutcome.Defeat ? "패배" : "시간 초과");
        }

        // ---------------- 행동 ----------------

        private void Act(TurnUnit actor, TurnCommand cmd)
        {
            var own = actor.Side == Side.Ally ? _allies : _enemies;
            var foes = actor.Side == Side.Ally ? _enemies : _allies;
            switch (cmd.Kind)
            {
                case TurnActionKind.Guard:
                    actor.Guarding = true;
                    Add(TurnLogKind.Guard, actor.Name, null, 0, false, actor.Name + " 방어");
                    break;
                case TurnActionKind.Attack:
                    Hit(actor, foes[cmd.Target], 100, null, TurnLogKind.Attack);
                    break;
                default:
                    var skill = actor.Skill;
                    var targets = skill.Target == SkillTarget.SingleEnemy ? new List<TurnUnit> { foes[cmd.Target] }
                        : skill.Target == SkillTarget.AllEnemies ? foes.Where(u => u.IsAlive).ToList()
                        : skill.Target == SkillTarget.SingleAlly ? new List<TurnUnit> { own[cmd.Target] }
                        : own.Where(u => u.IsAlive).ToList();
                    foreach (var t in targets)
                    {
                        if (skill.TargetsEnemies) Hit(actor, t, skill.Power, skill, TurnLogKind.Skill);
                        else Heal(actor, t, skill);
                    }
                    break;
            }
        }

        private void Hit(TurnUnit actor, TurnUnit target, int power, SkillDef skill, TurnLogKind kind)
        {
            if (!target.IsAlive) return;
            bool crit = actor.CritRate > 0 && _rng.NextInt(100) < actor.CritRate;
            int dmg = BattleSimulator.CalcDamage((int)((long)actor.Atk * power / 100), target.Def, crit);
            if (target.Guarding) dmg = Math.Max(1, dmg / 2);
            target.Hp = Math.Max(0, target.Hp - dmg);
            Add(kind, actor.Name, target.Name, dmg, crit,
                actor.Name + (skill != null ? " [" + skill.Name + "]" : "") + " → " + target.Name + " " + dmg
                + (crit ? "(치명)" : "") + (target.Guarding ? "(방어)" : "") + " [남은 HP " + target.Hp + "]");
            if (!target.IsAlive)
            {
                Add(TurnLogKind.Defeated, actor.Name, target.Name, 0, false, target.Name + " 쓰러짐");
                return;
            }
            if (skill != null && skill.Status != StatusKind.None)
            {
                bool applied = skill.StatusChance >= 100 || (skill.StatusChance > 0 && _rng.NextInt(100) < skill.StatusChance);
                if (applied) ApplyStatus(target, skill);
            }
        }

        private void ApplyStatus(TurnUnit target, SkillDef skill)
        {
            if (skill.Status == StatusKind.Stun)
            {
                target.StunLeft = Math.Max(target.StunLeft, skill.StatusTurns);
                Add(TurnLogKind.StatusApplied, null, target.Name, target.StunLeft, false, target.Name + " 기절 (" + target.StunLeft + "차례)");
            }
            else
            {
                target.PoisonStacks = Math.Min(MaxPoisonStacks, target.PoisonStacks + 1);
                target.PoisonLeft = Math.Max(target.PoisonLeft, skill.StatusTurns);
                target.PoisonPower = Math.Max(target.PoisonPower, skill.StatusPower);
                Add(TurnLogKind.StatusApplied, null, target.Name, target.PoisonStacks, false,
                    target.Name + " 독 " + target.PoisonStacks + "중첩 (" + target.PoisonLeft + "라운드)");
            }
        }

        private void Heal(TurnUnit actor, TurnUnit target, SkillDef skill)
        {
            if (!target.IsAlive) return; // 쓰러진 유닛은 회복하지 않는다
            int amount = (int)((long)actor.Atk * skill.Power / 100);
            int before = target.Hp;
            target.Hp = Math.Min(target.MaxHp, target.Hp + amount);
            Add(TurnLogKind.Heal, actor.Name, target.Name, target.Hp - before, false,
                actor.Name + " [" + skill.Name + "] → " + target.Name + " 회복 " + (target.Hp - before) + " [HP " + target.Hp + "]");
        }

        private void Add(TurnLogKind kind, string actor, string target, int amount, bool crit, string text) =>
            _log.Add(new TurnLogEntry(Round, WaveIndex + 1, kind, actor, target, amount, crit, text));
    }
}
