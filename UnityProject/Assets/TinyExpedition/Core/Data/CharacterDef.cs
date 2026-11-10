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
    }
}
