using System.Collections.Generic;

namespace TinyExpedition.Core.Data
{
    /// <summary>턴제 전투 스테이지: 적 웨이브를 차례로 상대한다.</summary>
    public sealed class TurnStageDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>먼저 깨야 하는 턴제 스테이지. 첫 스테이지는 null.</summary>
        public string RequiresStageId { get; set; }
        public List<WaveDef> Waves { get; set; } = new List<WaveDef>();
        public int FirstClearGems { get; set; }
    }

    public sealed class WaveDef
    {
        public List<TurnEnemyDef> Enemies { get; set; } = new List<TurnEnemyDef>();
    }

    public sealed class TurnEnemyDef
    {
        public string Name { get; set; } = "";
        public int Hp { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Spd { get; set; }
        /// <summary>없으면 일반 공격만 한다.</summary>
        public string SkillId { get; set; }
    }
}
