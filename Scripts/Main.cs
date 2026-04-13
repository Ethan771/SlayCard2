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

        ConnectSignals();

        _mapManager.ShowMap();
        _combatManager.HideCombat();
        _rewardManager.HideRewards();
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

        var enemy = new EnemyData(
            $"enemy_{depth}_{lane}",
            depth >= 4 ? "Act Boss" : "Slime",
            depth >= 4 ? 70 : 28 + depth * 8,
            depth >= 4 ? 14 : 6 + depth * 2
        );

        _combatManager.StartCombat(_gameManager.Deck, enemy);
    }

    private void OnCombatWon()
    {
        _combatManager.HideCombat();
        _gameManager.AddGold(15);
        _rewardManager.ShowRewards();
    }

    private void OnCombatLost()
    {
        _combatManager.HideCombat();
        _mapManager.ShowMap();
    }

    private void OnPlayerDamaged(Vector2 position, int value)
    {
        if (value <= 0)
        {
            return;
        }

        _gameManager.LoseHealth(value);
        _vfxManager.PlayFloatingText(position, $"-{value}", Colors.Crimson);
        _vfxManager.ShakeScreen();

        if (_gameManager.PlayerHealth <= 0)
        {
            _combatManager.HideCombat();
            _mapManager.ShowMap();
        }
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
    }

    private void RefreshHud()
    {
        _hudLabel.Text = $"HP: {_gameManager.PlayerHealth}/{_gameManager.MaxPlayerHealth}   Gold: {_gameManager.Gold}   Deck: {_gameManager.Deck.Count}";
    }
}
