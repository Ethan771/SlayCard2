using Godot;

namespace SlayCard;

// 唯一总控入口：创建并连接所有系统。
public partial class Main : Node
{
    private GameManager _gameManager = null!;
    private CombatManager _combatManager = null!;
    private MapManager _mapManager = null!;
    private RewardManager _rewardManager = null!;
    private VfxManager _vfxManager = null!;

    private Control _topUiContainer = null!;
    private Label _hudLabel = null!;
    private Button _endTurnButton = null!;
    private Control _entityLayer = null!;
    private ColorRect _playerRect = null!;
    private ColorRect _enemyRect = null!;
    private VBoxContainer _playerInfoBox = null!;
    private VBoxContainer _enemyInfoBox = null!;
    private Label _playerIntentLabel = null!;
    private Label _playerHpLabel = null!;
    private Label _enemyIntentLabel = null!;
    private Label _enemyHpLabel = null!;
    private Label _playerBodyHpLabel = null!;
    private Label _enemyBodyHpLabel = null!;
    private ColorRect _playerAvatarFrame = null!;
    private ColorRect _enemyAvatarFrame = null!;
    private int _currentEnemyMaxHealth;
    private int _currentEnergy;

    public override void _Ready()
    {
        BuildHud();

        _gameManager = new GameManager();
        _combatManager = new CombatManager();
        _mapManager = new MapManager();
        _rewardManager = new RewardManager();
        _vfxManager = new VfxManager();

        AddChild(_gameManager);
        AddChild(_mapManager);
        AddChild(_combatManager);
        AddChild(_rewardManager);
        AddChild(_vfxManager);

        _combatManager.BindGameManager(_gameManager);
        ConnectSignals();
        GetViewport().SizeChanged += OnViewportSizeChanged;

        _mapManager.ShowMap();
        _mapManager.UpdateNodeStates(_gameManager.CurrentFloorIndex);
        _combatManager.HideCombat();
        _rewardManager.HideRewards();
        ShowEntityVisuals(false);
        RefreshHud();
    }

    private void ConnectSignals()
    {
        _gameManager.GoldChanged += _ => RefreshHud();
        _gameManager.PlayerHealthChanged += _ => RefreshHud();
        _gameManager.DeckChanged += RefreshHud;
        _gameManager.FloorIndexChanged += floorIndex => _mapManager.UpdateNodeStates(floorIndex);

        _mapManager.NodeSelected += OnMapNodeSelected;
        _combatManager.CombatWon += OnCombatWon;
        _combatManager.CombatLost += OnCombatLost;
        _combatManager.TurnStateChanged += isPlayerTurn => _endTurnButton.Disabled = !isPlayerTurn;
        _combatManager.CombatStateChanged += OnCombatStateChanged;
        _combatManager.EnemyIntentChanged += intent => _enemyIntentLabel.Text = intent;
        _combatManager.EnemyDamaged += (position, value) =>
        {
            _vfxManager.PlayFloatingText(position, $"-{value}", Colors.OrangeRed);
            _vfxManager.ShakeScreen();
        };
        _combatManager.PlayerDamaged += OnPlayerDamaged;

        _rewardManager.RewardPicked += OnRewardPicked;
    }

    private void OnMapNodeSelected(int depth, int lane)
    {
        _mapManager.HideMap();
        _endTurnButton.Visible = true;
        _endTurnButton.Disabled = false;
        ShowEntityVisuals(true);

        var enemy = new EnemyData(
            $"enemy_{depth}_{lane}",
            depth >= 4 ? "Act Boss" : "Slime",
            depth >= 4 ? 70 : 28 + depth * 8,
            depth >= 4 ? 14 : 6 + depth * 2
        );

        _currentEnemyMaxHealth = enemy.MaxHealth;
        _enemyIntentLabel.Text = "Intent: ...";
        _enemyHpLabel.Text = $"HP: {enemy.CurrentHealth}/{_currentEnemyMaxHealth}";
        _combatManager.StartCombat(_gameManager.Deck, enemy);
    }

    private void OnCombatWon()
    {
        _combatManager.HideCombat();
        _endTurnButton.Visible = false;
        ShowEntityVisuals(false);
        _gameManager.AddGold(15);
        _gameManager.AdvanceFloor();
        _rewardManager.ShowRewards();
    }

    private void OnCombatLost()
    {
        _combatManager.HideCombat();
        _endTurnButton.Visible = false;
        ShowEntityVisuals(false);
        _mapManager.ShowMap();
    }

    private void OnPlayerDamaged(Vector2 position, int value)
    {
        if (value <= 0)
        {
            return;
        }

        _vfxManager.PlayFloatingText(position, $"-{value}", Colors.Crimson);
        _vfxManager.ShakeScreen();
    }

    private void OnRewardPicked(CardData pickedCard)
    {
        _gameManager.AddCardToDeck(pickedCard);
        _rewardManager.HideRewards();
        _mapManager.ShowMap();
    }

    private void BuildHud()
    {
        var canvasLayer = new CanvasLayer();
        AddChild(canvasLayer);

        _topUiContainer = new Control
        {
            Position = new Vector2(30, 30),
            Size = new Vector2(680, 90),
            CustomMinimumSize = new Vector2(680, 90),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        canvasLayer.AddChild(_topUiContainer);

        _hudLabel = new Label
        {
            Position = Vector2.Zero,
            Size = new Vector2(600, 32),
            CustomMinimumSize = new Vector2(600, 32),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _hudLabel.AddThemeFontSizeOverride("font_size", 18);
        _topUiContainer.AddChild(_hudLabel);

        _endTurnButton = new Button
        {
            Text = "End Turn",
            Position = new Vector2(530, 0),
            Size = new Vector2(150, 40),
            CustomMinimumSize = new Vector2(150, 40),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false
        };
        _endTurnButton.AddThemeFontSizeOverride("font_size", 18);
        _endTurnButton.Pressed += () => _combatManager.EndPlayerTurn();
        _topUiContainer.AddChild(_endTurnButton);

        BuildEntityVisuals(canvasLayer);
        UpdateResponsiveLayout();
    }

    private void BuildEntityVisuals(CanvasLayer canvasLayer)
    {
        _entityLayer = new Control
        {
            Position = Vector2.Zero,
            Size = new Vector2(1280, 720),
            CustomMinimumSize = new Vector2(1280, 720),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        canvasLayer.AddChild(_entityLayer);

        _playerRect = new ColorRect
        {
            Size = new Vector2(200, 300),
            CustomMinimumSize = new Vector2(200, 300),
            Color = new Color(0.60f, 0.55f, 0.70f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _entityLayer.AddChild(_playerRect);
        BuildPlayerAvatarAndBody();

        _enemyRect = new ColorRect
        {
            Size = new Vector2(200, 300),
            CustomMinimumSize = new Vector2(200, 300),
            Color = new Color(0.65f, 0.50f, 0.50f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _entityLayer.AddChild(_enemyRect);
        BuildEnemyAvatarAndBody();

        _playerInfoBox = new VBoxContainer
        {
            Position = Vector2.Zero,
            Size = new Vector2(220, 70),
            CustomMinimumSize = new Vector2(220, 70),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _entityLayer.AddChild(_playerInfoBox);

        _playerIntentLabel = new Label
        {
            Text = "Intent: Ready",
            Size = new Vector2(220, 30),
            CustomMinimumSize = new Vector2(220, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(0.95f, 0.95f, 0.95f)
        };
        _playerIntentLabel.AddThemeFontSizeOverride("font_size", 18);
        _playerInfoBox.AddChild(_playerIntentLabel);

        _playerHpLabel = new Label
        {
            Position = Vector2.Zero,
            Size = new Vector2(220, 28),
            CustomMinimumSize = new Vector2(220, 28),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(0.90f, 0.90f, 0.90f)
        };
        _playerHpLabel.AddThemeFontSizeOverride("font_size", 18);
        _playerInfoBox.AddChild(_playerHpLabel);

        _enemyInfoBox = new VBoxContainer
        {
            Position = Vector2.Zero,
            Size = new Vector2(220, 70),
            CustomMinimumSize = new Vector2(220, 70),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _entityLayer.AddChild(_enemyInfoBox);

        _enemyIntentLabel = new Label
        {
            Text = "Intent: ...",
            Size = new Vector2(220, 30),
            CustomMinimumSize = new Vector2(220, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(0.95f, 0.95f, 0.95f)
        };
        _enemyIntentLabel.AddThemeFontSizeOverride("font_size", 18);
        _enemyInfoBox.AddChild(_enemyIntentLabel);

        _enemyHpLabel = new Label
        {
            Position = Vector2.Zero,
            Size = new Vector2(220, 28),
            CustomMinimumSize = new Vector2(220, 28),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(0.90f, 0.90f, 0.90f)
        };
        _enemyHpLabel.AddThemeFontSizeOverride("font_size", 18);
        _enemyInfoBox.AddChild(_enemyHpLabel);
    }

    private void ShowEntityVisuals(bool show)
    {
        _entityLayer.Visible = show;
    }

    private void OnCombatStateChanged(int energy, int drawPile, int discardPile, int enemyHealth)
    {
        _currentEnergy = energy;
        _enemyHpLabel.Text = $"HP: {Mathf.Max(0, enemyHealth)}/{_currentEnemyMaxHealth}";
        _enemyBodyHpLabel.Text = _enemyHpLabel.Text;
        RefreshHud();
    }

    private void RefreshHud()
    {
        _hudLabel.Text = $"Energy: {_currentEnergy}   HP: {_gameManager.PlayerHealth}/{_gameManager.MaxPlayerHealth}   Gold: {_gameManager.Gold}";
        _playerHpLabel.Text = $"HP: {_gameManager.PlayerHealth}/{_gameManager.MaxPlayerHealth}";
        _playerBodyHpLabel.Text = _playerHpLabel.Text;
    }

    private void OnViewportSizeChanged()
    {
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        Vector2 entitySize = _playerRect.Size;

        ApplyAnchoredRect(_playerRect, new Vector2(0.25f, 0.45f), entitySize);
        ApplyAnchoredRect(_enemyRect, new Vector2(0.75f, 0.45f), entitySize);

        ApplyAnchoredInfoBoxAboveEntity(_playerInfoBox, new Vector2(0.25f, 0.45f), entitySize, 40f);
        ApplyAnchoredInfoBoxAboveEntity(_enemyInfoBox, new Vector2(0.75f, 0.45f), entitySize, 40f);

        LayoutAvatarFrame(_playerAvatarFrame, entitySize);
        LayoutAvatarFrame(_enemyAvatarFrame, entitySize);
    }

    private static void ApplyAnchoredRect(Control rect, Vector2 centerPercent, Vector2 size)
    {
        rect.AnchorLeft = centerPercent.X;
        rect.AnchorRight = centerPercent.X;
        rect.AnchorTop = centerPercent.Y;
        rect.AnchorBottom = centerPercent.Y;
        rect.OffsetLeft = -size.X * 0.5f;
        rect.OffsetTop = -size.Y * 0.5f;
        rect.OffsetRight = size.X * 0.5f;
        rect.OffsetBottom = size.Y * 0.5f;
        rect.PivotOffset = size * 0.5f;
    }

    private static void ApplyAnchoredInfoBoxAboveEntity(Control box, Vector2 centerPercent, Vector2 entitySize, float verticalGap)
    {
        Vector2 boxSize = box.Size;
        box.AnchorLeft = centerPercent.X;
        box.AnchorRight = centerPercent.X;
        box.AnchorTop = centerPercent.Y;
        box.AnchorBottom = centerPercent.Y;
        box.OffsetLeft = -boxSize.X * 0.5f;
        box.OffsetTop = -(entitySize.Y * 0.5f + verticalGap + boxSize.Y);
        box.OffsetRight = boxSize.X * 0.5f;
        box.OffsetBottom = -(entitySize.Y * 0.5f + verticalGap);
    }

    private void BuildPlayerAvatarAndBody()
    {
        _playerAvatarFrame = new ColorRect
        {
            Color = new Color(0.24f, 0.24f, 0.30f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _playerRect.AddChild(_playerAvatarFrame);

        var armorTop = new ColorRect
        {
            Color = new Color(0.60f, 0.55f, 0.65f),
            Position = new Vector2(52, 10),
            Size = new Vector2(76, 18),
            CustomMinimumSize = new Vector2(76, 18),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _playerAvatarFrame.AddChild(armorTop);

        var armorBottom = new ColorRect
        {
            Color = new Color(0.58f, 0.53f, 0.62f),
            Position = new Vector2(62, 32),
            Size = new Vector2(56, 24),
            CustomMinimumSize = new Vector2(56, 24),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _playerAvatarFrame.AddChild(armorBottom);

        var youLabel = new Label
        {
            Text = "[YOU]",
            Position = new Vector2(56, 58),
            Size = new Vector2(86, 20),
            CustomMinimumSize = new Vector2(86, 20),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        youLabel.AddThemeFontSizeOverride("font_size", 16);
        _playerAvatarFrame.AddChild(youLabel);

        var lowerInfo = new VBoxContainer
        {
            Position = new Vector2(10, 205),
            Size = new Vector2(180, 84),
            CustomMinimumSize = new Vector2(180, 84),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _playerRect.AddChild(lowerInfo);

        var nameLabel = new Label
        {
            Text = "Player",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 18);
        lowerInfo.AddChild(nameLabel);

        _playerBodyHpLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _playerBodyHpLabel.AddThemeFontSizeOverride("font_size", 18);
        lowerInfo.AddChild(_playerBodyHpLabel);
    }

    private void BuildEnemyAvatarAndBody()
    {
        _enemyAvatarFrame = new ColorRect
        {
            Color = new Color(0.24f, 0.24f, 0.30f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _enemyRect.AddChild(_enemyAvatarFrame);

        var slimeCircle = new Panel
        {
            Position = new Vector2(58, 8),
            Size = new Vector2(64, 64),
            CustomMinimumSize = new Vector2(64, 64),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.45f, 0.60f, 0.45f),
            CornerRadiusTopLeft = 32,
            CornerRadiusTopRight = 32,
            CornerRadiusBottomLeft = 32,
            CornerRadiusBottomRight = 32
        };
        slimeCircle.AddThemeStyleboxOverride("panel", style);
        _enemyAvatarFrame.AddChild(slimeCircle);

        var eyeLabel = new Label
        {
            Text = "··",
            Position = new Vector2(17, 18),
            Size = new Vector2(30, 24),
            CustomMinimumSize = new Vector2(30, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        eyeLabel.AddThemeFontSizeOverride("font_size", 22);
        slimeCircle.AddChild(eyeLabel);

        var slimeLabel = new Label
        {
            Text = "[Slime]",
            Position = new Vector2(52, 72),
            Size = new Vector2(86, 20),
            CustomMinimumSize = new Vector2(86, 20),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        slimeLabel.AddThemeFontSizeOverride("font_size", 16);
        _enemyAvatarFrame.AddChild(slimeLabel);

        var lowerInfo = new VBoxContainer
        {
            Position = new Vector2(10, 205),
            Size = new Vector2(180, 84),
            CustomMinimumSize = new Vector2(180, 84),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _enemyRect.AddChild(lowerInfo);

        var nameLabel = new Label
        {
            Text = "Slime",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 18);
        lowerInfo.AddChild(nameLabel);

        _enemyBodyHpLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _enemyBodyHpLabel.AddThemeFontSizeOverride("font_size", 18);
        lowerInfo.AddChild(_enemyBodyHpLabel);
    }

    private static void LayoutAvatarFrame(Control frame, Vector2 entitySize)
    {
        frame.Position = new Vector2(10, 10);
        frame.Size = new Vector2(entitySize.X - 20, entitySize.Y / 3f - 10f);
        frame.CustomMinimumSize = frame.Size;
    }
}
