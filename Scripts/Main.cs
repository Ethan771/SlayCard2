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

    private Label _hudLabel = null!;
    private Button _endTurnButton = null!;
    private Control _entityLayer = null!;
    private ColorRect _playerRect = null!;
    private ColorRect _enemyRect = null!;
    private Label _playerNameLabel = null!;
    private Label _playerHpLabel = null!;
    private Label _enemyNameLabel = null!;
    private Label _enemyHpLabel = null!;
    private int _currentEnemyMaxHealth;

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

        _mapManager.NodeSelected += OnMapNodeSelected;
        _combatManager.CombatWon += OnCombatWon;
        _combatManager.CombatLost += OnCombatLost;
        _combatManager.TurnStateChanged += isPlayerTurn => _endTurnButton.Disabled = !isPlayerTurn;
        _combatManager.CombatStateChanged += OnCombatStateChanged;
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
        _enemyNameLabel.Text = enemy.DisplayName;
        _enemyHpLabel.Text = $"HP: {enemy.CurrentHealth}/{_currentEnemyMaxHealth}";
        _combatManager.StartCombat(_gameManager.Deck, enemy);
    }

    private void OnCombatWon()
    {
        _combatManager.HideCombat();
        _endTurnButton.Visible = false;
        ShowEntityVisuals(false);
        _gameManager.AddGold(15);
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

        _hudLabel = new Label
        {
            Position = new Vector2(16, 10),
            Size = new Vector2(600, 32),
            CustomMinimumSize = new Vector2(600, 32),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        canvasLayer.AddChild(_hudLabel);

        _endTurnButton = new Button
        {
            Text = "End Turn",
            Position = new Vector2(1120, 14),
            Size = new Vector2(140, 36),
            CustomMinimumSize = new Vector2(140, 36),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false
        };
        _endTurnButton.Pressed += () => _combatManager.EndPlayerTurn();
        canvasLayer.AddChild(_endTurnButton);

        BuildEntityVisuals(canvasLayer);
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
            Position = new Vector2(70, 110),
            Size = new Vector2(200, 300),
            CustomMinimumSize = new Vector2(200, 300),
            Color = new Color(0.60f, 0.55f, 0.70f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _entityLayer.AddChild(_playerRect);

        _enemyRect = new ColorRect
        {
            Position = new Vector2(1010, 110),
            Size = new Vector2(200, 300),
            CustomMinimumSize = new Vector2(200, 300),
            Color = new Color(0.65f, 0.50f, 0.50f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _entityLayer.AddChild(_enemyRect);

        _playerNameLabel = new Label
        {
            Text = "Player",
            Position = _playerRect.Position + new Vector2(16, 16),
            Size = new Vector2(168, 30),
            CustomMinimumSize = new Vector2(168, 30),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(0.95f, 0.95f, 0.95f)
        };
        _entityLayer.AddChild(_playerNameLabel);

        _playerHpLabel = new Label
        {
            Position = _playerRect.Position + new Vector2(16, 52),
            Size = new Vector2(168, 30),
            CustomMinimumSize = new Vector2(168, 30),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(0.90f, 0.90f, 0.90f)
        };
        _entityLayer.AddChild(_playerHpLabel);

        _enemyNameLabel = new Label
        {
            Text = "Enemy",
            Position = _enemyRect.Position + new Vector2(16, 16),
            Size = new Vector2(168, 30),
            CustomMinimumSize = new Vector2(168, 30),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(0.95f, 0.95f, 0.95f)
        };
        _entityLayer.AddChild(_enemyNameLabel);

        _enemyHpLabel = new Label
        {
            Position = _enemyRect.Position + new Vector2(16, 52),
            Size = new Vector2(168, 30),
            CustomMinimumSize = new Vector2(168, 30),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(0.90f, 0.90f, 0.90f)
        };
        _entityLayer.AddChild(_enemyHpLabel);
    }

    private void ShowEntityVisuals(bool show)
    {
        _entityLayer.Visible = show;
    }

    private void OnCombatStateChanged(int energy, int drawPile, int discardPile, int enemyHealth)
    {
        _enemyHpLabel.Text = $"HP: {Mathf.Max(0, enemyHealth)}/{_currentEnemyMaxHealth}";
    }

    private void RefreshHud()
    {
        _hudLabel.Text = $"HP: {_gameManager.PlayerHealth}/{_gameManager.MaxPlayerHealth}   Gold: {_gameManager.Gold}   Deck: {_gameManager.Deck.Count}";
        _playerHpLabel.Text = $"HP: {_gameManager.PlayerHealth}/{_gameManager.MaxPlayerHealth}";
    }
}
