using Godot;

namespace SlayCard;

// 单张卡牌可视与交互控件：负责拖拽与打出判定。
public partial class CardUI : Control
{
    [Signal] public delegate void CardReleasedEventHandler(CardUI cardUi, bool shouldPlay);

    private static readonly Color AttackColor = new(0.75f, 0.45f, 0.45f);
    private static readonly Color BlockColor = new(0.45f, 0.55f, 0.70f);
    private static readonly Color SkillColor = new(0.50f, 0.65f, 0.50f);

    public CardData CardData { get; private set; } = new();

    private ColorRect _backgroundRect = null!;
    private Label _nameLabel = null!;
    private Label _costLabel = null!;
    private Label _descriptionLabel = null!;

    private bool _isDragging;
    private Vector2 _dragOffset;
    private Vector2 _originalPosition;
    private int _originalZIndex;

    public override void _Ready()
    {
        // 防雷：明确交互节点尺寸，避免 0x0 命中框。
        Size = new Vector2(150, 220);
        CustomMinimumSize = new Vector2(150, 220);
        MouseFilter = MouseFilterEnum.Stop;

        BuildVisualTree();
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
        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                _isDragging = true;
                _dragOffset = GetGlobalMousePosition() - GlobalPosition;
                _originalPosition = Position;
                _originalZIndex = ZIndex;
                ZIndex = 1000;
            }
            else if (_isDragging)
            {
                _isDragging = false;
                ZIndex = _originalZIndex;

                float releaseY = GetGlobalMousePosition().Y;
                bool shouldPlay = releaseY < GetViewportRect().Size.Y * 0.65f;

                EmitSignal(SignalName.CardReleased, this, shouldPlay);

                if (!shouldPlay)
                {
                    Position = _originalPosition;
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
    }

    private void BuildVisualTree()
    {
        _backgroundRect = new ColorRect
        {
            Color = SkillColor,
            MouseFilter = MouseFilterEnum.Ignore,
            Size = new Vector2(150, 220),
            CustomMinimumSize = new Vector2(150, 220)
        };
        AddChild(_backgroundRect);

        _costLabel = new Label
        {
            Position = new Vector2(8, 8),
            Size = new Vector2(30, 30),
            CustomMinimumSize = new Vector2(30, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(0.93f, 0.93f, 0.93f)
        };
        AddChild(_costLabel);

        _nameLabel = new Label
        {
            Position = new Vector2(12, 45),
            Size = new Vector2(126, 24),
            CustomMinimumSize = new Vector2(126, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(0.97f, 0.97f, 0.97f)
        };
        AddChild(_nameLabel);

        _descriptionLabel = new Label
        {
            Position = new Vector2(12, 82),
            Size = new Vector2(126, 120),
            CustomMinimumSize = new Vector2(126, 120),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(0.90f, 0.90f, 0.90f)
        };
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
