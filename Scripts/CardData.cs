using Godot;

namespace SlayCard;

// 卡牌数据资源：用于纯代码构建卡池与奖励。
public partial class CardData : Resource
{
    [Export] public string Id { get; set; } = string.Empty;
    [Export] public string DisplayName { get; set; } = "Card";
    [Export] public string Description { get; set; } = string.Empty;
    [Export] public int Cost { get; set; } = 1;
    [Export] public int Damage { get; set; }
    [Export] public int Block { get; set; }

    public CardData()
    {
    }

    public CardData(string id, string displayName, string description, int cost, int damage = 0, int block = 0)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Cost = cost;
        Damage = damage;
        Block = block;
    }

    public CardData Clone()
    {
        return new CardData(Id, DisplayName, Description, Cost, Damage, Block);
    }
}
