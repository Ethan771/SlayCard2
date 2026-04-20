using Godot;

namespace SlayCard;

public enum EnemyIntentType
{
    Attack,
    Defend,
    Debuff
}

public struct EnemyIntent
{
    public EnemyIntentType Type;
    public int Damage;
    public int Block;
    public int HitCount;
    public int ApplyVulnerable;
}

// 敌人数据资源：支持纯代码动态构建战斗单位。
public partial class EnemyData : Resource
{
    [Export] public string Id { get; set; } = string.Empty;
    [Export] public string DisplayName { get; set; } = "Enemy";
    [Export] public int MaxHealth { get; set; } = 20;
    [Export] public int CurrentHealth { get; set; } = 20;
    [Export] public int BaseAttack { get; set; } = 6;

    public EnemyData()
    {
    }

    public EnemyData(string id, string displayName, int maxHealth, int baseAttack)
    {
        Id = id;
        DisplayName = displayName;
        MaxHealth = maxHealth;
        CurrentHealth = maxHealth;
        BaseAttack = baseAttack;
    }

    public void ResetHealth()
    {
        CurrentHealth = MaxHealth;
    }

    public bool IsDead()
    {
        return CurrentHealth <= 0;
    }
}
