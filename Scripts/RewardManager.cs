using System;
using System.Collections.Generic;
using Godot;

namespace SlayCard;

// 奖励管理器：战斗胜利后提供三选一卡牌。
public partial class RewardManager : Control
{
    [Signal] public delegate void RewardPickedEventHandler(CardData pickedCard);
    [Signal] public delegate void RewardSkippedEventHandler();

    private readonly Random _rng = new();
    private readonly List<CardData> _rewardPool = new();
    private readonly List<CardUI> _optionUis = new();

    private HBoxContainer _cardsBox = null!;
    private Button _skipButton = null!;

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

        _cardsBox = new HBoxContainer
        {
            Position = Vector2.Zero,
            Size = new Vector2(460, 220),
            CustomMinimumSize = new Vector2(460, 220),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _cardsBox.AddThemeConstantOverride("separation", 40);
        AddChild(_cardsBox);

        _skipButton = new Button
        {
            Text = "Skip Reward",
            Size = new Vector2(180, 38),
            CustomMinimumSize = new Vector2(180, 38),
            MouseFilter = MouseFilterEnum.Stop
        };
        _skipButton.Pressed += OnSkipPressed;
        AddChild(_skipButton);

        CenterCardsBox();
    }

    private void CenterCardsBox()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _cardsBox.Position = new Vector2(viewport.X * 0.5f - _cardsBox.Size.X * 0.5f, viewport.Y * 0.5f - _cardsBox.Size.Y * 0.5f);
        _skipButton.Position = new Vector2(viewport.X * 0.5f - _skipButton.Size.X * 0.5f, _cardsBox.Position.Y + _cardsBox.Size.Y + 24f);
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
                IsRewardCard = true,
                ZIndex = 100
            };

            _cardsBox.AddChild(cardUi);
            cardUi.Setup(data);
            cardUi.CardClicked += _ => OnRewardCardClicked(data);
            _optionUis.Add(cardUi);
        }
    }

    private void OnRewardCardClicked(CardData pickedCard)
    {
        EmitSignal(SignalName.RewardPicked, pickedCard);

        var tween = CreateTween();
        foreach (CardUI cardUi in _optionUis)
        {
            tween.TweenProperty(cardUi, "modulate:a", 0f, 0.16f);
        }

        tween.Finished += QueueFree;
    }

    public async void AutoResolveReward(bool preferSkip)
    {
        if (!Visible)
        {
            return;
        }

        await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
        if (!Visible)
        {
            return;
        }

        if (preferSkip || _optionUis.Count == 0)
        {
            OnSkipPressed();
            return;
        }

        CardData pickedCard = _optionUis[0].CardData;
        OnRewardCardClicked(pickedCard);
    }

    private void OnSkipPressed()
    {
        AudioManager.Instance?.PlaySFX("res://Audio/click.ogg");
        EmitSignal(SignalName.RewardSkipped);
        QueueFree();
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
