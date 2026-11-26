namespace TinyExpedition.Core.Data
{
    /// <summary>스킬 대상. 시전자 기준: Enemy = 상대편, Ally = 자기편.</summary>
    public enum SkillTarget { SingleEnemy, AllEnemies, SingleAlly, AllAllies }

    public enum StatusKind { None, Stun, Poison }

    /// <summary>턴제 전투 스킬. 상대편 대상이면 피해(공격력 × Power%), 자기편 대상이면 회복(공격력 × Power%).</summary>
    public sealed class SkillDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public SkillTarget Target { get; set; }
        /// <summary>공격력 대비 %. 100 = 일반 공격과 같은 위력.</summary>
        public int Power { get; set; }
        /// <summary>쓴 뒤 자기 차례 몇 번 동안 다시 못 쓰는지. 0이면 매 차례 사용 가능.</summary>
        public int Cooldown { get; set; }
        public StatusKind Status { get; set; }
        /// <summary>상태이상 지속(자기 차례 수 또는 라운드 수).</summary>
        public int StatusTurns { get; set; }
        /// <summary>독: 중첩 1개당 라운드 끝 피해. 기절은 쓰지 않음.</summary>
        public int StatusPower { get; set; }
        /// <summary>상태이상이 걸릴 확률(%). 100이면 항상.</summary>
        public int StatusChance { get; set; } = 100;

        public bool TargetsEnemies => Target == SkillTarget.SingleEnemy || Target == SkillTarget.AllEnemies;
        public bool IsSingleTarget => Target == SkillTarget.SingleEnemy || Target == SkillTarget.SingleAlly;
    }
}
