using System.Collections.Generic;
using Godot;

namespace SlayCard;

// 全局状态管理器：跨战斗保留牌组、金币、生命值。
public partial class GameManager : Node
{
    [Signal] public delegate void GoldChangedEventHandler(int newGold);
    [Signal] public delegate void PlayerHealthChangedEventHandler(int currentHealth);
    [Signal] public delegate void DeckChangedEventHandler();
    [Signal] public delegate void FloorIndexChangedEventHandler(int floorIndex);
    [Signal] public delegate void PlayerDiedEventHandler();
    [Signal] public delegate void RelicsChangedEventHandler();
    [Signal] public delegate void PotionsChangedEventHandler();

    public List<CardData> Deck { get; } = new();
    public List<RelicData> Relics { get; } = new();
    public List<PotionData?> Potions { get; } = new() { null, null, null };
    public int Gold { get; private set; }
    public int PlayerHealth { get; private set; }
    public int MaxPlayerHealth { get; private set; } = 80;
    public int CurrentFloorIndex { get; private set; }

    public override void _Ready()
    {
        InitializeNewRun();
    }

    public void InitializeNewRun()
    {
        Deck.Clear();
        Gold = 0;
        PlayerHealth = MaxPlayerHealth;
        CurrentFloorIndex = 0;
        Relics.Clear();
        for (int i = 0; i < Potions.Count; i++)
        {
            Potions[i] = null;
        }

        for (int i = 0; i < 4; i++)
        {
            Deck.Add(new CardData("strike", "Strike", "Deal 6 damage.", 1, damage: 6, cardType: CardType.Attack));
        }

        for (int i = 0; i < 4; i++)
        {
            Deck.Add(new CardData("defend", "Defend", "Gain 5 block.", 1, block: 5, cardType: CardType.Block));
        }

        Deck.Add(new CardData("pummel", "Pummel", "Deal 2 damage 4 times.", 1, damage: 2, cardType: CardType.Attack, hitCount: 4));
        Deck.Add(new CardData("bash", "Bash", "Deal 8 damage. Apply 2 Vulnerable.", 2, damage: 8, cardType: CardType.Attack, applyVulnerable: 2));
        Deck.Add(new CardData("adrenaline", "Adrenaline", "Gain 1 energy. Draw 2 cards.", 0, cardType: CardType.Skill, drawAmount: 2, energyAmount: 1));
        Deck.Add(new CardData("blind", "Blind", "Apply 2 Weak to a target.", 1, cardType: CardType.Skill, applyWeak: 2, requiresEnemyTarget: true));

        EmitSignal(SignalName.DeckChanged);
        EmitSignal(SignalName.GoldChanged, Gold);
        EmitSignal(SignalName.PlayerHealthChanged, PlayerHealth);
        EmitSignal(SignalName.FloorIndexChanged, CurrentFloorIndex);
        EmitSignal(SignalName.RelicsChanged);
        EmitSignal(SignalName.PotionsChanged);
    }

    public void ResetRun()
    {
        InitializeNewRun();
    }

    public void AddCardToDeck(CardData card)
    {
        Deck.Add(card.Clone());
        EmitSignal(SignalName.DeckChanged);
    }

    public void AddGold(int amount)
    {
        Gold += amount;
        EmitSignal(SignalName.GoldChanged, Gold);
    }

    public void LoseHealth(int amount)
    {
        PlayerHealth = Mathf.Max(0, PlayerHealth - amount);
        EmitSignal(SignalName.PlayerHealthChanged, PlayerHealth);
        if (PlayerHealth <= 0)
        {
            EmitSignal(SignalName.PlayerDied);
        }
    }

    public void Heal(int amount)
    {
        PlayerHealth = Mathf.Min(MaxPlayerHealth, PlayerHealth + amount);
        EmitSignal(SignalName.PlayerHealthChanged, PlayerHealth);
    }

    public void AdvanceFloor()
    {
        CurrentFloorIndex += 1;
        EmitSignal(SignalName.FloorIndexChanged, CurrentFloorIndex);
    }

    public bool AddRelic(RelicData relic)
    {
        if (Relics.Exists(r => r.Id == relic.Id))
        {
            return false;
        }

        Relics.Add(relic);
        EmitSignal(SignalName.RelicsChanged);
        return true;
    }

    public bool AddPotion(PotionData potion)
    {
        for (int i = 0; i < Potions.Count; i++)
        {
            if (Potions[i] is null)
            {
                Potions[i] = potion;
                EmitSignal(SignalName.PotionsChanged);
                return true;
            }
        }

        return false;
    }

    public bool RemoveRandomCardFromDeck()
    {
        if (Deck.Count <= 1)
        {
            return false;
        }

        Deck.RemoveAt((int)GD.RandRange(0, Deck.Count - 1));
        EmitSignal(SignalName.DeckChanged);
        return true;
    }
}
