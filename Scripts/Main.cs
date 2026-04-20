using System.Collections.Generic;
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
    private VBoxContainer _playerInfoBox = null!;
    private Label _playerIntentLabel = null!;
    private Label _playerHpLabel = null!;

    private readonly List<Control> _enemyRects = new();
    private readonly List<VBoxContainer> _enemyInfoBoxes = new();
    private readonly List<Label> _enemyHpLabels = new();
    private readonly List<Label> _enemyIntentLabels = new();

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
        _combatManager.EnemiesStateChanged += OnEnemiesStateChanged;
        _combatManager.EnemyIntentChanged += OnEnemyIntentChanged;

        _combatManager.EnemyDamaged += (position, value) =>
        {
            _vfxManager.PlayFloatingText(position, $"-{value}", Colors.OrangeRed);
            _vfxManager.ShakeScreen();
        };

        _combatManager.PlayerDamaged += (position, value) =>
        {
            if (value <= 0)
            {
                return;
            }

            _vfxManager.PlayFloatingText(position, $"-{value}", Colors.Crimson);
            _vfxManager.ShakeScreen();
        };

        _rewardManager.RewardPicked += OnRewardPicked;
    }

    private void OnMapNodeSelected(int depth, int lane)
    {
        _mapManager.HideMap();
        _endTurnButton.Visible = true;
        _endTurnButton.Disabled = false;
        ShowEntityVisuals(true);

        List<EnemyData> enemies = BuildEncounter(depth);
        BuildEnemyVisuals(enemies);

        _combatManager.SetEnemyTargetNodes(new List<Control>(_enemyRects));
        _combatManager.StartCombat(_gameManager.Deck, enemies);
    }

    private List<EnemyData> BuildEncounter(int depth)
    {
        int enemyCount = Mathf.Clamp(1 + depth / 2, 1, 3);
        var enemies = new List<EnemyData>();

        for (int i = 0; i < enemyCount; i++)
        {
            enemies.Add(new EnemyData(
                $"enemy_{depth}_{i}",
                depth >= 4 ? "Elite Slime" : "Slime",
                depth >= 4 ? 45 : 24 + depth * 6,
                depth >= 4 ? 12 : 6 + depth
            ));
        }

        return enemies;
    }

    private void OnCombatWon()
    {
        _combatManager.HideCombat();
        _endTurnButton.Visible = false;
        ShowEntityVisuals(false);

        _gameManager.AddGold(15);
        _gameManager.AdvanceFloor();

        EnsureRewardManager();
        _rewardManager.ShowRewards();
    }

    private void OnCombatLost()
    {
        _combatManager.HideCombat();
        _endTurnButton.Visible = false;
        ShowEntityVisuals(false);
        _mapManager.ShowMap();
    }

    private void OnRewardPicked(CardData pickedCard)
    {
        _gameManager.AddCardToDeck(pickedCard);
        _mapManager.ShowMap();
    }

    private void EnsureRewardManager()
    {
        if (GodotObject.IsInstanceValid(_rewardManager))
        {
            return;
        }

        _rewardManager = new RewardManager();
        AddChild(_rewardManager);
        _rewardManager.RewardPicked += OnRewardPicked;
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

        BuildEntityLayer(canvasLayer);
    }

    private void BuildEntityLayer(CanvasLayer canvasLayer)
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
            Size = new Vector2(220, 300),
            CustomMinimumSize = new Vector2(220, 300),
            Color = new Color(0.60f, 0.55f, 0.70f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _entityLayer.AddChild(_playerRect);

        _playerInfoBox = new VBoxContainer
        {
            Size = new Vector2(220, 70),
            CustomMinimumSize = new Vector2(220, 70),
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _entityLayer.AddChild(_playerInfoBox);

        _playerIntentLabel = new Label
        {
            Text = "Intent: Ready",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _playerInfoBox.AddChild(_playerIntentLabel);

        _playerHpLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _playerInfoBox.AddChild(_playerHpLabel);

        UpdatePlayerLayout();
    }

    private void UpdatePlayerLayout()
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 playerCenter = new(viewport.X * 0.25f, viewport.Y * 0.45f);
        Vector2 size = _playerRect.Size;
        _playerRect.Position = playerCenter - size * 0.5f;

        _playerInfoBox.Position = new Vector2(
            playerCenter.X - _playerInfoBox.Size.X * 0.5f,
            _playerRect.Position.Y - _playerInfoBox.Size.Y - 40f
        );
    }

    private void BuildEnemyVisuals(List<EnemyData> enemies)
    {
        foreach (Control rect in _enemyRects)
        {
            rect.QueueFree();
        }
        foreach (VBoxContainer box in _enemyInfoBoxes)
        {
            box.QueueFree();
        }

        _enemyRects.Clear();
        _enemyInfoBoxes.Clear();
        _enemyHpLabels.Clear();
        _enemyIntentLabels.Clear();

        Vector2 viewport = GetViewportRect().Size;
        float areaStart = viewport.X * 0.65f;
        float areaEnd = viewport.X * 0.95f;
        float areaWidth = areaEnd - areaStart;
        float step = enemies.Count == 1 ? 0f : areaWidth / (enemies.Count - 1);

        for (int i = 0; i < enemies.Count; i++)
        {
            float centerX = enemies.Count == 1 ? areaStart + areaWidth * 0.5f : areaStart + i * step;
            float centerY = viewport.Y * 0.45f;

            var rect = new ColorRect
            {
                Size = new Vector2(180, 260),
                CustomMinimumSize = new Vector2(180, 260),
                Color = new Color(0.65f, 0.50f, 0.50f),
                Position = new Vector2(centerX - 90f, centerY - 130f),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _entityLayer.AddChild(rect);

            BuildEnemyAvatar(rect);

            var infoBox = new VBoxContainer
            {
                Position = new Vector2(centerX - 100f, rect.Position.Y - 110f),
                Size = new Vector2(200, 70),
                CustomMinimumSize = new Vector2(200, 70),
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _entityLayer.AddChild(infoBox);

            var intentLabel = new Label
            {
                Text = "Intent: ...",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            infoBox.AddChild(intentLabel);

            var hpLabel = new Label
            {
                Text = $"HP: {enemies[i].CurrentHealth}/{enemies[i].MaxHealth}",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            infoBox.AddChild(hpLabel);

            _enemyRects.Add(rect);
            _enemyInfoBoxes.Add(infoBox);
            _enemyIntentLabels.Add(intentLabel);
            _enemyHpLabels.Add(hpLabel);
        }
    }

    private static void BuildEnemyAvatar(Control enemyRect)
    {
        var avatarFrame = new ColorRect
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(enemyRect.Size.X - 20, enemyRect.Size.Y / 3f),
            CustomMinimumSize = new Vector2(enemyRect.Size.X - 20, enemyRect.Size.Y / 3f),
            Color = new Color(0.24f, 0.24f, 0.30f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        enemyRect.AddChild(avatarFrame);

        var slimeCircle = new Panel
        {
            Position = new Vector2(46, 8),
            Size = new Vector2(58, 58),
            CustomMinimumSize = new Vector2(58, 58),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.45f, 0.60f, 0.45f),
            CornerRadiusTopLeft = 29,
            CornerRadiusTopRight = 29,
            CornerRadiusBottomLeft = 29,
            CornerRadiusBottomRight = 29
        };
        slimeCircle.AddThemeStyleboxOverride("panel", style);
        avatarFrame.AddChild(slimeCircle);

        var eyeLabel = new Label
        {
            Text = "··",
            Position = new Vector2(15, 17),
            Size = new Vector2(28, 24),
            CustomMinimumSize = new Vector2(28, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        slimeCircle.AddChild(eyeLabel);
    }

    private void ShowEntityVisuals(bool show)
    {
        _entityLayer.Visible = show;
    }

    private void OnCombatStateChanged(int energy, int drawPile, int discardPile)
    {
        _currentEnergy = energy;
        RefreshHud();
    }

    private void OnEnemiesStateChanged(Godot.Collections.Array<int> enemyHealths)
    {
        int count = Mathf.Min(enemyHealths.Count, _enemyHpLabels.Count);
        for (int i = 0; i < count; i++)
        {
            _enemyHpLabels[i].Text = $"HP: {enemyHealths[i]}";
        }
    }

    private void OnEnemyIntentChanged(string intentText)
    {
        foreach (Label intentLabel in _enemyIntentLabels)
        {
            intentLabel.Text = intentText;
        }
    }

    private void RefreshHud()
    {
        _hudLabel.Text = $"Energy: {_currentEnergy}   HP: {_gameManager.PlayerHealth}/{_gameManager.MaxPlayerHealth}   Gold: {_gameManager.Gold}";
        _playerHpLabel.Text = $"HP: {_gameManager.PlayerHealth}/{_gameManager.MaxPlayerHealth}";
        UpdatePlayerLayout();
    }
}
