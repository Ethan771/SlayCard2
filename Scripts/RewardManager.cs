using System;
using System.Collections.Generic;
using Godot;

namespace SlayCard;

// 奖励管理器：战斗胜利后提供三选一卡牌。
public partial class RewardManager : Control
{
    [Signal] public delegate void RewardPickedEventHandler(CardData pickedCard);

    private readonly Random _rng = new();
    private readonly List<CardData> _rewardPool = new();
    private readonly List<CardUI> _optionUis = new();
    private Control _cardsRoot = null!;

    public override void _Ready()
    {
        Size = new Vector2(1280, 720);
        CustomMinimumSize = new Vector2(1280, 720);
        MouseFilter = MouseFilterEnum.Pass;
        Visible = false;

        InitializeRewardPool();
        BuildStaticBackground();
    }

    public void ShowRewards()
    {
        Visible = true;
        BuildRewardOptions();
    }

    public void HideRewards()
    {
        ClearOptions();
        Visible = false;
    }

    private void InitializeRewardPool()
    {
        _rewardPool.Add(new CardData("slash_plus", "Slash+", "Deal 9 damage.", 1, damage: 9));
        _rewardPool.Add(new CardData("shield_plus", "Guard+", "Gain 8 block.", 1, block: 8));
        _rewardPool.Add(new CardData("heavy", "Heavy Blow", "Deal 14 damage.", 2, damage: 14));
        _rewardPool.Add(new CardData("focus", "Focus", "Gain 5 block and deal 5.", 1, damage: 5, block: 5));
        _rewardPool.Add(new CardData("burst", "Burst", "Deal 6 damage twice.", 1, damage: 12));
    }

    private void BuildStaticBackground()
    {
        var bg = new ColorRect
        {
            Color = new Color(0.12f, 0.09f, 0.12f),
            Size = new Vector2(1280, 720),
            CustomMinimumSize = new Vector2(1280, 720),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(bg);

        var title = new Label
        {
            Text = "Choose 1 card",
            Position = new Vector2(30, 24),
            Size = new Vector2(240, 34),
            CustomMinimumSize = new Vector2(240, 34),
            MouseFilter = MouseFilterEnum.Ignore
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        AddChild(title);

        _cardsRoot = new Control
        {
            Position = Vector2.Zero,
            Size = new Vector2(1280, 720),
            CustomMinimumSize = new Vector2(1280, 720),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_cardsRoot);
    }

    private void BuildRewardOptions()
    {
        ClearOptions();
        for (int i = 0; i < 3; i++)
        {
            CardData data = _rewardPool[_rng.Next(_rewardPool.Count)].Clone();
            var cardUi = new CardUI
            {
                MouseFilter = MouseFilterEnum.Stop,
                EnableDragging = false,
                ZIndex = 50
            };

            float totalWidth = 3 * 125f + 2 * 42f;
            float startX = (Size.X - totalWidth) * 0.5f;
            cardUi.Position = new Vector2(startX + i * (125f + 42f), Size.Y * 0.5f - 90f);

            _cardsRoot.AddChild(cardUi);
            cardUi.Setup(data);
            cardUi.CardClicked += _ =>
            {
                OnRewardCardClicked(data);
            };

            _optionUis.Add(cardUi);
        }
    }

    private void OnRewardCardClicked(CardData pickedCard)
    {
        EmitSignal(SignalName.RewardPicked, pickedCard);

        var tween = CreateTween();
        foreach (CardUI cardUi in _optionUis)
        {
            tween.TweenProperty(cardUi, "modulate:a", 0f, 0.18f);
        }

        tween.Finished += QueueFree;
    }

    private void ClearOptions()
    {
        foreach (CardUI cardUi in _optionUis)
        {
            cardUi.QueueFree();
        }

        _optionUis.Clear();
    }
}
