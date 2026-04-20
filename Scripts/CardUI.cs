using Godot;
using System;

namespace SlayCard;

// 单张卡牌可视与交互控件：负责拖拽与打出判定。
public partial class CardUI : Control
{
    [Signal] public delegate void CardReleasedEventHandler(CardUI cardUi, bool shouldPlay, int targetIndex);
    [Signal] public delegate void CardClickedEventHandler(CardUI cardUi);
    [Signal] public delegate void ManualInteractionEventHandler(CardUI cardUi);

    private static readonly Color AttackColor = new(0.75f, 0.45f, 0.45f);
    private static readonly Color BlockColor = new(0.45f, 0.55f, 0.70f);
    private static readonly Color SkillColor = new(0.50f, 0.65f, 0.50f);
    private static readonly Vector2 CardSize = new(125, 180);

    public CardData CardData { get; private set; } = new();
    public bool EnableDragging { get; set; } = true;
    public bool IsRewardCard { get; set; }
    public Func<Vector2, int>? TargetResolver { get; set; }

    private ColorRect _backgroundRect = null!;
    private Label _nameLabel = null!;
    private Label _costLabel = null!;
    private Label _descriptionLabel = null!;

    private bool _isDragging;
    private Vector2 _dragOffset;
    private Vector2 _originalPosition;
    private Vector2 _homePosition;
    private float _originalRotation;
    private int _originalZIndex;
    private bool _isHoverRaised;

    public override void _Ready()
    {
        Size = CardSize;
        CustomMinimumSize = CardSize;
        MouseFilter = MouseFilterEnum.Stop;

        BuildVisualTree();
        MouseEntered += OnMouseEntered;
        MouseExited += OnMouseExited;
    }

    public void Setup(CardData cardData)
    {
        CardData = cardData;
        RefreshVisualByType();
        _nameLabel.Text = cardData.DisplayName;
        _costLabel.Text = $"{cardData.Cost}";
        _descriptionLabel.Text = cardData.Description;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if ((IsRewardCard || !EnableDragging) && @event is InputEventMouseButton clickButton && clickButton.ButtonIndex == MouseButton.Left && !clickButton.Pressed)
        {
            AudioManager.Instance?.PlaySFX("res://Audio/click.ogg");
            EmitSignal(SignalName.CardClicked, this);
            return;
        }

        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                EmitSignal(SignalName.ManualInteraction, this);
                AudioManager.Instance?.PlaySFX("res://Audio/click.ogg");
                _isDragging = true;
                _dragOffset = GetGlobalMousePosition() - GlobalPosition;
                _originalPosition = Position;
                _originalRotation = Rotation;
                _originalZIndex = ZIndex;
                ZIndex = 1000;
                Rotation = 0f;
            }
            else if (_isDragging)
            {
                _isDragging = false;
                ZIndex = _originalZIndex;

                float releaseY = GetGlobalMousePosition().Y;
                int targetIndex = TargetResolver?.Invoke(GetGlobalMousePosition()) ?? -1;
                bool needsEnemyTarget = CardData.NeedsEnemyTarget();
                bool shouldPlay;

                if (needsEnemyTarget)
                {
                    shouldPlay = targetIndex >= 0;
                }
                else
                {
                    float playThresholdY = GetViewport().GetVisibleRect().Size.Y * 0.5f;
                    shouldPlay = releaseY < playThresholdY;
                    targetIndex = -1;
                }

                EmitSignal(SignalName.CardReleased, this, shouldPlay, targetIndex);

                if (!shouldPlay)
                {
                    ResetPosition();
                }
            }
        }
        else if (@event is InputEventMouseMotion && _isDragging)
        {
            GlobalPosition = GetGlobalMousePosition() - _dragOffset;
        }
    }

    public void ResetPosition()
    {
        Position = _originalPosition;
        Rotation = _originalRotation;
    }

    public void SetHomeTransform(Vector2 position, float rotation)
    {
        Position = position;
        Rotation = rotation;
        _originalPosition = position;
        _homePosition = position;
        _originalRotation = rotation;
    }

    public async System.Threading.Tasks.Task SimulatePlay(Node target)
    {
        Vector2 targetGlobal = ResolveTargetGlobalPosition(target);
        int targetIndex = TargetResolver?.Invoke(targetGlobal + Size * 0.5f) ?? -1;
        bool shouldPlay = CardData.NeedsEnemyTarget() ? targetIndex >= 0 : true;

        _isDragging = false;
        _isHoverRaised = false;
        _originalZIndex = ZIndex;
        ZIndex = 3000;
        Rotation = 0f;

        var tween = CreateTween();
        tween.TweenProperty(this, "global_position", targetGlobal, 0.4f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);
        await ToSignal(tween, Tween.SignalName.Finished);

        EmitSignal(SignalName.CardReleased, this, shouldPlay, targetIndex);
        QueueFree();
    }

    private Vector2 ResolveTargetGlobalPosition(Node target)
    {
        if (target is Control targetControl)
        {
            Vector2 center = targetControl.GlobalPosition + targetControl.Size * 0.5f;
            return center - Size * 0.5f;
        }

        if (target is Node2D targetNode2D)
        {
            return targetNode2D.GlobalPosition - Size * 0.5f;
        }

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        return new Vector2(viewport.X * 0.5f, viewport.Y * 0.35f);
    }

    private void OnMouseEntered()
    {
        if (_isDragging || IsRewardCard)
        {
            return;
        }

        _isHoverRaised = true;
        _originalZIndex = ZIndex;
        ZIndex = 2000;
        Position = _homePosition + new Vector2(0, -60f);
    }

    private void OnMouseExited()
    {
        if (_isDragging || IsRewardCard || !_isHoverRaised)
        {
            return;
        }

        _isHoverRaised = false;
        ZIndex = _originalZIndex;
        Position = _homePosition;
    }

    private void BuildVisualTree()
    {
        _backgroundRect = new ColorRect
        {
            Color = SkillColor,
            MouseFilter = MouseFilterEnum.Ignore,
            Size = CardSize,
            CustomMinimumSize = CardSize
        };
        AddChild(_backgroundRect);

        _costLabel = new Label
        {
            Position = new Vector2(8, 8),
            Size = new Vector2(28, 28),
            CustomMinimumSize = new Vector2(28, 28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(0.93f, 0.93f, 0.93f)
        };
        _costLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_costLabel);

        _nameLabel = new Label
        {
            Position = new Vector2(10, 38),
            Size = new Vector2(100, 24),
            CustomMinimumSize = new Vector2(100, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(0.97f, 0.97f, 0.97f)
        };
        _nameLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_nameLabel);

        _descriptionLabel = new Label
        {
            Position = new Vector2(10, 68),
            Size = new Vector2(100, 98),
            CustomMinimumSize = new Vector2(100, 98),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(0.90f, 0.90f, 0.90f)
        };
        _descriptionLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_descriptionLabel);
    }

    private void RefreshVisualByType()
    {
        _backgroundRect.Color = CardData.Type switch
        {
            CardType.Attack => AttackColor,
            CardType.Block => BlockColor,
            _ => SkillColor
        };
    }
}
