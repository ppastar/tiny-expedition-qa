namespace TinyExpedition.Core.Data
{
    public sealed class CharacterDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Rarity Rarity { get; set; }
        public int Hp { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Spd { get; set; }
        /// <summary>치명타 확률(%) 0~100.</summary>
        public int CritRate { get; set; }
        /// <summary>턴제 전투 스킬 id (skills.json). 없으면 턴제에서 일반 공격·방어만 한다.</summary>
        public string SkillId { get; set; }
    }
}
