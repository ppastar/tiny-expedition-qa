using System;
using System.Collections.Generic;
using System.Linq;
using TinyExpedition.Core.Data;
using TinyExpedition.Core.Random;

namespace TinyExpedition.Core.Battle
{
    public enum Side { Ally, Enemy }

    public sealed class BattleUnit
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
        public bool IsAlive => Hp > 0;

        public BattleUnit(string name, Side side, int slot, int hp, int atk, int def, int spd, int critRate)
        {
            Name = name; Side = side; Slot = slot; MaxHp = hp; Hp = hp; Atk = atk; Def = def; Spd = spd; CritRate = critRate;
        }

        public static BattleUnit FromCharacter(CharacterDef c, int slot) =>
            new BattleUnit(c.Name, Side.Ally, slot, c.Hp, c.Atk, c.Def, c.Spd, c.CritRate);

        public static BattleUnit FromEnemy(EnemyDef e, int slot) =>
            new BattleUnit(e.Name, Side.Enemy, slot, e.Hp, e.Atk, e.Def, e.Spd, 0);
    }

    public enum BattleOutcome { Victory, Defeat, Timeout }

    public sealed class BattleEvent
    {
        public int Round { get; }
        public string Attacker { get; }
        public string Target { get; }
        public int Damage { get; }
        public bool Crit { get; }
        public int TargetHpAfter { get; }

        public BattleEvent(int round, string attacker, string target, int damage, bool crit, int targetHpAfter)
        {
            Round = round; Attacker = attacker; Target = target; Damage = damage; Crit = crit; TargetHpAfter = targetHpAfter;
        }

        public override string ToString() =>
            "R" + Round + " " + Attacker + " → " + Target + " " + Damage + (Crit ? "(치명)" : "") + " [남은 HP " + TargetHpAfter + "]";
    }

    public sealed class BattleResult
    {
        public BattleOutcome Outcome { get; }
        public int Rounds { get; }
        /// <summary>승리 시 1~3. 패배·시간초과는 0.</summary>
        public int Stars { get; }
        public IReadOnlyList<BattleEvent> Log { get; }
        public IReadOnlyList<BattleUnit> Allies { get; }

        public BattleResult(BattleOutcome outcome, int rounds, int stars, IReadOnlyList<BattleEvent> log, IReadOnlyList<BattleUnit> allies)
        {
            Outcome = outcome; Rounds = rounds; Stars = stars; Log = log; Allies = allies;
        }
    }

    /// <summary>
    /// 자동 전투. 같은 파티·스테이지·시드면 항상 같은 결과가 나온다 (버그 재현용).
    /// 규칙:
    /// - 매 라운드 살아 있는 모든 유닛이 속도(Spd) 내림차순으로 행동. 동률이면 아군 먼저, 그다음 슬롯 번호 순.
    /// - 대상은 상대편에서 살아 있는 가장 앞 슬롯.
    /// - 피해 = max(1, floor(Atk * 100 / (100 + Def))), 치명타면 1.5배(내림).
    /// - MaxRounds 안에 끝나지 않으면 Timeout(패배 취급).
    /// - 별: 아군 전원 생존 3, 1명 이상 쓰러짐 2, 1명만 생존 1.
    /// </summary>
    public static class BattleSimulator
    {
        public const int MaxRounds = 30;
        public const int MaxPartySize = 3;

        public static int CalcDamage(int atk, int def, bool crit)
        {
            if (def < 0) def = 0;
            int baseDmg = (int)((long)atk * 100 / (100 + def));
            if (crit) baseDmg = baseDmg * 3 / 2;
            return Math.Max(1, baseDmg);
        }

        public static BattleResult Run(IReadOnlyList<CharacterDef> party, StageDef stage, IRandom rng)
        {
            if (party == null || party.Count == 0) throw new ArgumentException("파티가 비어 있습니다.", nameof(party));
            if (party.Count > MaxPartySize) throw new ArgumentException("파티는 최대 " + MaxPartySize + "명입니다.", nameof(party));
            if (party.Select(p => p.Id).Distinct().Count() != party.Count) throw new ArgumentException("같은 캐릭터를 두 번 넣을 수 없습니다.", nameof(party));

            var allies = party.Select((c, i) => BattleUnit.FromCharacter(c, i)).ToList();
            var enemies = stage.Enemies.Select((e, i) => BattleUnit.FromEnemy(e, i)).ToList();
            var all = allies.Concat(enemies).ToList();
            var log = new List<BattleEvent>();

            for (int round = 1; round <= MaxRounds; round++)
            {
                var order = all.Where(u => u.IsAlive)
                    .OrderByDescending(u => u.Spd)
                    .ThenBy(u => u.Side == Side.Ally ? 0 : 1)
                    .ThenBy(u => u.Slot)
                    .ToList();

                foreach (var actor in order)
                {
                    if (!actor.IsAlive) continue; // 이번 라운드에 먼저 쓰러짐
                    var foes = actor.Side == Side.Ally ? enemies : allies;
                    var target = foes.FirstOrDefault(u => u.IsAlive);
                    if (target == null) break;

                    bool crit = actor.CritRate > 0 && rng.NextInt(100) < actor.CritRate;
                    int dmg = CalcDamage(actor.Atk, target.Def, crit);
                    target.Hp = Math.Max(0, target.Hp - dmg);
                    log.Add(new BattleEvent(round, actor.Name, target.Name, dmg, crit, target.Hp));

                    if (!enemies.Any(u => u.IsAlive))
                        return new BattleResult(BattleOutcome.Victory, round, StarsFor(allies), log, allies);
                    if (!allies.Any(u => u.IsAlive))
                        return new BattleResult(BattleOutcome.Defeat, round, 0, log, allies);
                }
            }
            return new BattleResult(BattleOutcome.Timeout, MaxRounds, 0, log, allies);
        }

        private static int StarsFor(List<BattleUnit> allies)
        {
            int alive = allies.Count(a => a.IsAlive);
            if (alive == allies.Count) return 3;
            if (alive == 1 && allies.Count > 1) return 1;
            return 2;
        }
    }
}
