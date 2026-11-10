using System.Collections.Generic;

namespace TinyExpedition.Core.Data
{
    public sealed class StageDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>이 스테이지를 열려면 먼저 깨야 하는 스테이지. 첫 스테이지는 null.</summary>
        public string RequiresStageId { get; set; }
        public List<EnemyDef> Enemies { get; set; } = new List<EnemyDef>();
        /// <summary>첫 클리어 보상(보석).</summary>
        public int FirstClearGems { get; set; }
    }

    public sealed class EnemyDef
    {
        public string Name { get; set; } = "";
        public int Hp { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Spd { get; set; }
    }
}
