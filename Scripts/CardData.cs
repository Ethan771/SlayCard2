using Godot;

namespace SlayCard;

public enum CardType
{
    Attack,
    Block,
    Skill
}

// 卡牌数据资源：用于纯代码构建卡池与奖励。
public partial class CardData : Resource
{
    [Export] public string Id { get; set; } = string.Empty;
    [Export] public string DisplayName { get; set; } = "Card";
    [Export] public string Description { get; set; } = string.Empty;
    [Export] public int Cost { get; set; } = 1;
    [Export] public int Damage { get; set; }
    [Export] public int Block { get; set; }
    [Export] public int HitCount { get; set; } = 1;
    [Export] public int DrawAmount { get; set; }
    [Export] public int EnergyAmount { get; set; }
    [Export] public int ApplyWeak { get; set; }
    [Export] public int ApplyVulnerable { get; set; }
    [Export] public bool RequiresEnemyTarget { get; set; }
    [Export] public CardType Type { get; set; } = CardType.Skill;

    public CardData()
    {
    }

    public CardData(
        string id,
        string displayName,
        string description,
        int cost,
        int damage = 0,
        int block = 0,
        CardType? cardType = null,
        int hitCount = 1,
        int drawAmount = 0,
        int energyAmount = 0,
        int applyWeak = 0,
        int applyVulnerable = 0,
        bool requiresEnemyTarget = false)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Cost = cost;
        Damage = damage;
        Block = block;
        Type = cardType ?? InferType(damage, block);
        HitCount = Mathf.Max(1, hitCount);
        DrawAmount = drawAmount;
        EnergyAmount = energyAmount;
        ApplyWeak = applyWeak;
        ApplyVulnerable = applyVulnerable;
        RequiresEnemyTarget = requiresEnemyTarget;
    }

    public CardData Clone()
    {
        return new CardData(
            Id,
            DisplayName,
            Description,
            Cost,
            Damage,
            Block,
            Type,
            HitCount,
            DrawAmount,
            EnergyAmount,
            ApplyWeak,
            ApplyVulnerable,
            RequiresEnemyTarget);
    }

    public bool NeedsEnemyTarget()
    {
        return RequiresEnemyTarget || Damage > 0 || Type == CardType.Attack;
    }

    private static CardType InferType(int damage, int block)
    {
        if (damage > 0 && block <= 0)
        {
            return CardType.Attack;
        }

        if (block > 0 && damage <= 0)
        {
            return CardType.Block;
        }

        return CardType.Skill;
    }
}
