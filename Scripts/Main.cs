using System.Collections.Generic;
using Godot;

namespace SlayCard;

// 唯一总控入口：创建并连接所有系统。
public partial class Main : Node
{
    private static readonly Color PlayerBaseColor = new(0.60f, 0.55f, 0.70f);
    private static readonly Color EnemyBaseColor = new(0.65f, 0.50f, 0.50f);
    private const string DefaultBgmPath = "res://Audio/bgm.ogg";

    private AudioManager _audioManager = null!;
    private GameManager _gameManager = null!;
    private CombatManager _combatManager = null!;
    private MapManager _mapManager = null!;
    private RewardManager _rewardManager = null!;
    private VfxManager _vfxManager = null!;

    private Control _topUiContainer = null!;
    private Label _hudLabel = null!;
    private Button _endTurnButton = null!;
    private Button _autoPlayButton = null!;
    private Button _deckButton = null!;

    private Control _entityLayer = null!;
    private ColorRect _playerRect = null!;
    private VBoxContainer _playerInfoBox = null!;
    private Label _playerIntentLabel = null!;
    private Label _playerHpLabel = null!;
    private Label _playerBlockLabel = null!;
    private Label _playerWeakLabel = null!;
    private Label _playerVulnLabel = null!;

    private readonly List<Control> _enemyRects = new();
    private readonly List<VBoxContainer> _enemyInfoBoxes = new();
    private readonly List<Label> _enemyHpLabels = new();
    private readonly List<Label> _enemyIntentLabels = new();
    private readonly List<Label> _enemyBlockLabels = new();
    private readonly List<Label> _enemyWeakLabels = new();
    private readonly List<Label> _enemyVulnLabels = new();
    private readonly List<int> _lastEnemyWeakStacks = new();
    private readonly List<int> _lastEnemyVulnStacks = new();

    private int _currentEnergy;
    private bool _isAutoUiFlowRunning;
    private int _lastPlayerWeak;
    private int _lastPlayerVuln;

    private Control _deathOverlay = null!;
    private Control _deckOverlay = null!;
    private RichTextLabel _deckListLabel = null!;
    private Control _roomOverlay = null!;
    private Label _roomTitleLabel = null!;
    private VBoxContainer _roomContent = null!;

    private readonly List<RelicData> _relicPool = new();
    private readonly List<PotionData> _potionPool = new();
    private readonly List<CardData> _shopCardPool = new();

    public override void _Ready()
    {
        _audioManager = new AudioManager();
        AddChild(_audioManager);
        _audioManager.PlayBGM(DefaultBgmPath);
        InitializeMetaPools();

        BuildHud();
        BuildDeathOverlay();
        BuildDeckOverlay();
        BuildRoomOverlay();

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
        TryAutoAdvanceOutsideCombat();
    }

    private void ConnectSignals()
    {
        _gameManager.GoldChanged += _ => RefreshHud();
        _gameManager.PlayerHealthChanged += _ => RefreshHud();
        _gameManager.DeckChanged += RefreshHud;
        _gameManager.RelicsChanged += RefreshHud;
        _gameManager.PotionsChanged += RefreshHud;
        _gameManager.FloorIndexChanged += floorIndex => _mapManager.UpdateNodeStates(floorIndex);
        _gameManager.PlayerDied += OnPlayerDied;

        _mapManager.NodeSelected += OnMapNodeSelected;

        _combatManager.CombatWon += OnCombatWon;
        _combatManager.CombatLost += OnCombatLost;
        _combatManager.TurnStateChanged += isPlayerTurn => _endTurnButton.Disabled = !isPlayerTurn;
        _combatManager.CombatStateChanged += OnCombatStateChanged;
        _combatManager.EnemiesStateChanged += OnEnemiesStateChanged;
        _combatManager.EnemyIntentChanged += OnEnemyIntentChanged;
        _combatManager.EnemyKilled += OnEnemyKilled;
        _combatManager.AutoPlayStateChanged += OnAutoPlayStateChanged;
        _combatManager.PlayerAttackPerformed += OnPlayerAttackPerformed;
        _combatManager.EnemyAttackPerformed += OnEnemyAttackPerformed;

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
        _rewardManager.RewardSkipped += OnRewardSkipped;
    }

    private void OnMapNodeSelected(int depth, int lane)
    {
        MapNodeKind kind = _mapManager.GetNodeKind(depth, lane);
        if (kind == MapNodeKind.Question)
        {
            kind = ResolveQuestionRoom();
        }

        _mapManager.HideMap();
        if (kind == MapNodeKind.Combat || kind == MapNodeKind.Boss)
        {
            _endTurnButton.Visible = true;
            _endTurnButton.Disabled = false;
            ShowEntityVisuals(true);
            _lastPlayerWeak = 0;
            _lastPlayerVuln = 0;
            _audioManager.PlayBGM(DefaultBgmPath);

            _combatManager.StartCombat(_gameManager.Deck, depth);
            BuildEnemyVisuals(_combatManager.ActiveEnemies);
            _combatManager.SetEnemyTargetNodes(new List<Control>(_enemyRects));
            _combatManager.SyncUiState();
            return;
        }

        _endTurnButton.Visible = false;
        ShowEntityVisuals(false);
        ShowRoomByKind(kind);
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
        TryAutoAdvanceOutsideCombat();
    }

    private void OnCombatLost()
    {
        _combatManager.HideCombat();
        _endTurnButton.Visible = false;
        ShowEntityVisuals(false);
    }

    private void OnRewardPicked(CardData pickedCard)
    {
        _gameManager.AddCardToDeck(pickedCard);
        _mapManager.ShowMap();
        CallDeferred(nameof(TryAutoAdvanceOutsideCombat));
    }

    private void OnRewardSkipped()
    {
        _mapManager.ShowMap();
        CallDeferred(nameof(TryAutoAdvanceOutsideCombat));
    }

    private void OnPlayerDied()
    {
        _combatManager.FreezeCombat();
        _deathOverlay.Visible = true;
        _deathOverlay.ZIndex = 5000;
    }

    private void RestartRun()
    {
        _deathOverlay.Visible = false;
        _gameManager.ResetRun();
        _combatManager.HideCombat();
        ShowEntityVisuals(false);
        _endTurnButton.Visible = false;
        _mapManager.ShowMap();
        _mapManager.UpdateNodeStates(0);
        TryAutoAdvanceOutsideCombat();
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
        _rewardManager.RewardSkipped += OnRewardSkipped;
    }

    private void BuildHud()
    {
        var canvasLayer = new CanvasLayer();
        AddChild(canvasLayer);

        _topUiContainer = new Control
        {
            Position = new Vector2(30, 30),
            Size = new Vector2(980, 90),
            CustomMinimumSize = new Vector2(980, 90),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        canvasLayer.AddChild(_topUiContainer);

        _hudLabel = new Label
        {
            Position = Vector2.Zero,
            Size = new Vector2(680, 32),
            CustomMinimumSize = new Vector2(680, 32),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _hudLabel.AddThemeFontSizeOverride("font_size", 18);
        _topUiContainer.AddChild(_hudLabel);

        _endTurnButton = new Button
        {
            Text = "End Turn",
            Position = new Vector2(600, 0),
            Size = new Vector2(150, 40),
            CustomMinimumSize = new Vector2(150, 40),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false
        };
        _endTurnButton.Pressed += () => _combatManager.EndPlayerTurnByManual();
        _topUiContainer.AddChild(_endTurnButton);

        _autoPlayButton = new Button
        {
            Text = "Auto-Play: OFF",
            Position = new Vector2(760, 0),
            Size = new Vector2(190, 40),
            CustomMinimumSize = new Vector2(190, 40),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _autoPlayButton.Pressed += OnAutoPlayTogglePressed;
        _topUiContainer.AddChild(_autoPlayButton);
        RefreshAutoPlayButton();

        _deckButton = new Button
        {
            Text = "Deck",
            Position = new Vector2(960, 0),
            Size = new Vector2(90, 40),
            CustomMinimumSize = new Vector2(90, 40),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _deckButton.Pressed += ToggleDeckOverlay;
        _topUiContainer.AddChild(_deckButton);

        BuildEntityLayer(canvasLayer);
    }

    private void BuildEntityLayer(CanvasLayer canvasLayer)
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _entityLayer = new Control
        {
            Position = Vector2.Zero,
            Size = viewport,
            CustomMinimumSize = viewport,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        canvasLayer.AddChild(_entityLayer);

        _playerRect = new ColorRect
        {
            Size = new Vector2(220, 300),
            CustomMinimumSize = new Vector2(220, 300),
            Color = PlayerBaseColor,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _entityLayer.AddChild(_playerRect);

        BuildPlayerAvatar(_playerRect);

        _playerInfoBox = new VBoxContainer
        {
            Size = new Vector2(240, 126),
            CustomMinimumSize = new Vector2(240, 126),
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

        _playerBlockLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.45f, 0.65f, 0.95f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        _playerInfoBox.AddChild(_playerBlockLabel);

        _playerWeakLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        _playerInfoBox.AddChild(_playerWeakLabel);

        _playerVulnLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        _playerInfoBox.AddChild(_playerVulnLabel);

        _playerHpLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _playerInfoBox.AddChild(_playerHpLabel);

        UpdatePlayerLayout();
    }

    private static void BuildPlayerAvatar(Control playerRect)
    {
        var avatarFrame = new ColorRect
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(playerRect.Size.X - 20, playerRect.Size.Y / 3f),
            CustomMinimumSize = new Vector2(playerRect.Size.X - 20, playerRect.Size.Y / 3f),
            Color = new Color(0.24f, 0.24f, 0.30f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        playerRect.AddChild(avatarFrame);

        var armorTop = new ColorRect
        {
            Color = new Color(0.60f, 0.55f, 0.65f),
            Position = new Vector2(52, 10),
            Size = new Vector2(76, 18),
            CustomMinimumSize = new Vector2(76, 18),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        avatarFrame.AddChild(armorTop);

        var armorBottom = new ColorRect
        {
            Color = new Color(0.58f, 0.53f, 0.62f),
            Position = new Vector2(62, 32),
            Size = new Vector2(56, 24),
            CustomMinimumSize = new Vector2(56, 24),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        avatarFrame.AddChild(armorBottom);

        var youLabel = new Label
        {
            Text = "[YOU]",
            Position = new Vector2(56, 58),
            Size = new Vector2(86, 20),
            CustomMinimumSize = new Vector2(86, 20),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        avatarFrame.AddChild(youLabel);
    }

    private void UpdatePlayerLayout()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2 playerCenter = new(viewport.X * 0.25f, viewport.Y * 0.55f);
        Vector2 size = _playerRect.Size;
        _playerRect.Position = playerCenter - size * 0.5f;

        _playerInfoBox.Position = new Vector2(
            playerCenter.X - _playerInfoBox.Size.X * 0.5f,
            _playerRect.Position.Y - _playerInfoBox.Size.Y - 36f
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
        _enemyBlockLabels.Clear();
        _enemyWeakLabels.Clear();
        _enemyVulnLabels.Clear();
        _lastEnemyWeakStacks.Clear();
        _lastEnemyVulnStacks.Clear();

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float areaStart = viewport.X * 0.55f;
        float areaEnd = viewport.X * 0.95f;
        float areaWidth = areaEnd - areaStart;
        float desiredWidth = 180f;
        float minimumGap = enemies.Count >= 3 ? 18f : 28f;
        float enemyWidth = desiredWidth;
        float enemyHeight = 260f;
        if (enemies.Count > 1)
        {
            float maxWidthByArea = (areaWidth - minimumGap * (enemies.Count - 1)) / enemies.Count;
            enemyWidth = Mathf.Clamp(maxWidthByArea, 135f, desiredWidth);
            enemyHeight = enemyWidth / desiredWidth * 260f;
        }

        float spacing = enemies.Count <= 1 ? 0f : enemyWidth + minimumGap;

        for (int i = 0; i < enemies.Count; i++)
        {
            float formationWidth = enemyWidth * enemies.Count + minimumGap * Mathf.Max(0, enemies.Count - 1);
            float firstCenterX = areaStart + (areaWidth - formationWidth) * 0.5f + enemyWidth * 0.5f;
            float centerX = enemies.Count == 1 ? areaStart + areaWidth * 0.5f : firstCenterX + i * spacing;
            float halfWidth = enemyWidth * 0.5f;
            centerX = Mathf.Clamp(centerX, areaStart + halfWidth, areaEnd - halfWidth);
            float centerY = viewport.Y * 0.55f;

            var rect = new ColorRect
            {
                Size = new Vector2(enemyWidth, enemyHeight),
                CustomMinimumSize = new Vector2(enemyWidth, enemyHeight),
                Color = EnemyBaseColor,
                Position = new Vector2(centerX - halfWidth, centerY - enemyHeight * 0.5f),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _entityLayer.AddChild(rect);
            BuildEnemyAvatar(rect);

            var infoBox = new VBoxContainer
            {
                Position = new Vector2(centerX - 150f, rect.Position.Y - 120f),
                Size = new Vector2(300, 110),
                CustomMinimumSize = new Vector2(300, 110),
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _entityLayer.AddChild(infoBox);

            var intentLabel = new Label
            {
                Text = "Intent: ...",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ClipText = false,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(280, 24)
            };
            infoBox.AddChild(intentLabel);

            var blockLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = new Color(0.45f, 0.65f, 0.95f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false
            };
            infoBox.AddChild(blockLabel);

            var weakLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false
            };
            infoBox.AddChild(weakLabel);

            var vulnLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false
            };
            infoBox.AddChild(vulnLabel);

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
            _enemyBlockLabels.Add(blockLabel);
            _enemyWeakLabels.Add(weakLabel);
            _enemyVulnLabels.Add(vulnLabel);
            _enemyHpLabels.Add(hpLabel);
            _lastEnemyWeakStacks.Add(0);
            _lastEnemyVulnStacks.Add(0);
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

    private void BuildDeathOverlay()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _deathOverlay = new Control
        {
            Position = Vector2.Zero,
            Size = viewport,
            CustomMinimumSize = viewport,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        AddChild(_deathOverlay);

        var mask = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.75f),
            Size = viewport,
            CustomMinimumSize = viewport,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _deathOverlay.AddChild(mask);

        var title = new Label
        {
            Text = "YOU DIED",
            Position = new Vector2(viewport.X * 0.5f - 200f, viewport.Y * 0.35f),
            Size = new Vector2(400, 80),
            CustomMinimumSize = new Vector2(400, 80),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = Colors.Red,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        title.AddThemeFontSizeOverride("font_size", 56);
        _deathOverlay.AddChild(title);

        var restart = new Button
        {
            Text = "Restart Run",
            Position = new Vector2(viewport.X * 0.5f - 100f, viewport.Y * 0.5f),
            Size = new Vector2(200, 44),
            CustomMinimumSize = new Vector2(200, 44),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        restart.Pressed += RestartRun;
        _deathOverlay.AddChild(restart);
    }

    private void OnEnemyKilled(int enemyIndex)
    {
        if (enemyIndex < 0 || enemyIndex >= _enemyRects.Count)
        {
            return;
        }

        _enemyRects[enemyIndex].QueueFree();
        _enemyInfoBoxes[enemyIndex].QueueFree();

        _enemyRects.RemoveAt(enemyIndex);
        _enemyInfoBoxes.RemoveAt(enemyIndex);
        _enemyHpLabels.RemoveAt(enemyIndex);
        _enemyIntentLabels.RemoveAt(enemyIndex);
        _enemyBlockLabels.RemoveAt(enemyIndex);
        _enemyWeakLabels.RemoveAt(enemyIndex);
        _enemyVulnLabels.RemoveAt(enemyIndex);
        _lastEnemyWeakStacks.RemoveAt(enemyIndex);
        _lastEnemyVulnStacks.RemoveAt(enemyIndex);
    }

    private void ShowEntityVisuals(bool show)
    {
        _entityLayer.Visible = show;
    }

    private void OnCombatStateChanged(int energy, int drawPile, int discardPile, int playerBlock, int playerWeak, int playerVulnerable)
    {
        _currentEnergy = energy;

        if (playerBlock > 0)
        {
            _playerBlockLabel.Text = $"Block: {playerBlock}";
            _playerBlockLabel.Visible = true;
        }
        else
        {
            _playerBlockLabel.Visible = false;
        }

        _playerWeakLabel.Text = $"[Weak: {playerWeak}]";
        _playerWeakLabel.Visible = playerWeak > 0;
        if (playerWeak > _lastPlayerWeak)
        {
            _vfxManager.PlayFloatingText(_playerRect.GlobalPosition + new Vector2(_playerRect.Size.X * 0.5f, -20f), $"+Weak {playerWeak - _lastPlayerWeak}", new Color(0.98f, 0.92f, 0.55f));
        }
        _lastPlayerWeak = playerWeak;

        _playerVulnLabel.Text = $"[Vuln: {playerVulnerable}]";
        _playerVulnLabel.Visible = playerVulnerable > 0;
        if (playerVulnerable > _lastPlayerVuln)
        {
            _vfxManager.PlayFloatingText(_playerRect.GlobalPosition + new Vector2(_playerRect.Size.X * 0.5f, -48f), $"+Vuln {playerVulnerable - _lastPlayerVuln}", Colors.IndianRed);
        }
        _lastPlayerVuln = playerVulnerable;

        RefreshHud();
    }

    private void OnEnemiesStateChanged(Godot.Collections.Array<int> enemyHealths, Godot.Collections.Array<int> enemyBlocks, Godot.Collections.Array<int> enemyWeaks, Godot.Collections.Array<int> enemyVulns)
    {
        int count = Mathf.Min(enemyHealths.Count, _enemyHpLabels.Count);
        for (int i = 0; i < count; i++)
        {
            _enemyHpLabels[i].Text = $"HP: {enemyHealths[i]}";

            int blockValue = i < enemyBlocks.Count ? enemyBlocks[i] : 0;
            _enemyBlockLabels[i].Text = $"Block: {blockValue}";
            _enemyBlockLabels[i].Visible = blockValue > 0;

            int weakValue = i < enemyWeaks.Count ? enemyWeaks[i] : 0;
            _enemyWeakLabels[i].Text = $"[Weak: {weakValue}]";
            _enemyWeakLabels[i].Visible = weakValue > 0;
            if (i < _lastEnemyWeakStacks.Count && weakValue > _lastEnemyWeakStacks[i])
            {
                _vfxManager.PlayFloatingText(_enemyRects[i].GlobalPosition + new Vector2(_enemyRects[i].Size.X * 0.5f, -18f), $"+Weak {weakValue - _lastEnemyWeakStacks[i]}", new Color(0.98f, 0.92f, 0.55f));
            }
            if (i < _lastEnemyWeakStacks.Count)
            {
                _lastEnemyWeakStacks[i] = weakValue;
            }

            int vulnValue = i < enemyVulns.Count ? enemyVulns[i] : 0;
            _enemyVulnLabels[i].Text = $"[Vuln: {vulnValue}]";
            _enemyVulnLabels[i].Visible = vulnValue > 0;
            if (i < _lastEnemyVulnStacks.Count && vulnValue > _lastEnemyVulnStacks[i])
            {
                _vfxManager.PlayFloatingText(_enemyRects[i].GlobalPosition + new Vector2(_enemyRects[i].Size.X * 0.5f, -42f), $"+Vuln {vulnValue - _lastEnemyVulnStacks[i]}", Colors.IndianRed);
            }
            if (i < _lastEnemyVulnStacks.Count)
            {
                _lastEnemyVulnStacks[i] = vulnValue;
            }
        }
    }

    private void OnEnemyIntentChanged(Godot.Collections.Array<string> intentTexts)
    {
        int count = Mathf.Min(intentTexts.Count, _enemyIntentLabels.Count);
        for (int i = 0; i < count; i++)
        {
            _enemyIntentLabels[i].Text = intentTexts[i];
        }
    }

    private void RefreshHud()
    {
        int potionCount = _gameManager.Potions.FindAll(p => p is not null).Count;
        _hudLabel.Text = $"Energy: {_currentEnergy}   HP: {_gameManager.PlayerHealth}/{_gameManager.MaxPlayerHealth}   Gold: {_gameManager.Gold}   Relics: {_gameManager.Relics.Count}   Potions: {potionCount}/3";
        _playerHpLabel.Text = $"HP: {_gameManager.PlayerHealth}/{_gameManager.MaxPlayerHealth}";
        UpdatePlayerLayout();
        if (_deckOverlay.Visible)
        {
            RefreshDeckOverlay();
        }
    }

    private void OnAutoPlayTogglePressed()
    {
        _combatManager.SetAutoPlaying(!_combatManager.IsAutoPlaying);
        RefreshAutoPlayButton();
        TryAutoAdvanceOutsideCombat();
    }

    private void OnAutoPlayStateChanged(bool isAutoPlaying)
    {
        if (!isAutoPlaying)
        {
            _combatManager.RecoverPlayerTurnInputState();
        }

        RefreshAutoPlayButton();
    }

    private void RefreshAutoPlayButton()
    {
        bool enabled = _combatManager != null && _combatManager.IsAutoPlaying;
        _autoPlayButton.Text = enabled ? "Auto-Play: ON" : "Auto-Play: OFF";
        _autoPlayButton.Modulate = enabled
            ? new Color(0.55f, 1.0f, 0.55f, 1f)
            : new Color(1f, 1f, 1f, 1f);
    }

    private async void TryAutoAdvanceOutsideCombat()
    {
        if (!_combatManager.IsAutoPlaying || _combatManager.Visible || _isAutoUiFlowRunning)
        {
            return;
        }

        _isAutoUiFlowRunning = true;
        try
        {
            await ToSignal(GetTree().CreateTimer(0.4f), SceneTreeTimer.SignalName.Timeout);

            if (!_combatManager.IsAutoPlaying || _combatManager.Visible)
            {
                return;
            }

            if (GodotObject.IsInstanceValid(_rewardManager) && _rewardManager.Visible)
            {
                bool preferSkip = _gameManager.Deck.Count >= 14;
                _rewardManager.AutoResolveReward(preferSkip);
                return;
            }

            if (_roomOverlay.Visible)
            {
                foreach (Node child in _roomContent.GetChildren())
                {
                    if (child is Button button)
                    {
                        button.EmitSignal(Button.SignalName.Pressed);
                        return;
                    }
                }
            }

            if (_mapManager.Visible)
            {
                _mapManager.TrySelectFirstAvailableNode();
            }
        }
        finally
        {
            _isAutoUiFlowRunning = false;
        }
    }

    private void InitializeMetaPools()
    {
        _relicPool.Clear();
        _relicPool.Add(new RelicData("relic_guard", "Stone Guard", "Start each combat with 8 Block."));
        _relicPool.Add(new RelicData("relic_energy", "Amber Battery", "Gain +1 energy each turn."));
        _relicPool.Add(new RelicData("relic_coin", "Golden Scarab", "Gain 20 gold immediately."));

        _potionPool.Clear();
        _potionPool.Add(new PotionData("heal_potion", "Heal Potion", "Recover 12 HP."));
        _potionPool.Add(new PotionData("energy_potion", "Energy Potion", "Gain +1 energy next combat."));
        _potionPool.Add(new PotionData("power_potion", "Power Potion", "Mysterious combat boost."));

        _shopCardPool.Clear();
        _shopCardPool.Add(new CardData("slice", "Slice", "Deal 7 damage.", 1, damage: 7));
        _shopCardPool.Add(new CardData("fortify", "Fortify", "Gain 8 block.", 1, block: 8));
        _shopCardPool.Add(new CardData("pierce", "Pierce", "Deal 5 damage twice.", 1, damage: 5, hitCount: 2));
    }

    private void BuildDeckOverlay()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _deckOverlay = new Control
        {
            Size = viewport,
            CustomMinimumSize = viewport,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 9000,
            ZAsRelative = false
        };
        AddChild(_deckOverlay);

        _deckOverlay.AddChild(new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.75f),
            Size = viewport,
            CustomMinimumSize = viewport,
            MouseFilter = Control.MouseFilterEnum.Stop
        });

        var panel = new Panel
        {
            Position = new Vector2(viewport.X * 0.5f - 260f, 80f),
            Size = new Vector2(520f, viewport.Y - 160f),
            CustomMinimumSize = new Vector2(520f, viewport.Y - 160f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _deckOverlay.AddChild(panel);

        var closeBtn = new Button
        {
            Text = "Close",
            Position = new Vector2(410, 12),
            Size = new Vector2(90, 32)
        };
        closeBtn.Pressed += ToggleDeckOverlay;
        panel.AddChild(closeBtn);

        _deckListLabel = new RichTextLabel
        {
            Position = new Vector2(18, 52),
            Size = new Vector2(484, panel.Size.Y - 70f),
            ScrollActive = true,
            BbcodeEnabled = false
        };
        panel.AddChild(_deckListLabel);
    }

    private void ToggleDeckOverlay()
    {
        _deckOverlay.Visible = !_deckOverlay.Visible;
        if (_deckOverlay.Visible)
        {
            _deckOverlay.MoveToFront();
            RefreshDeckOverlay();
        }
    }

    private void RefreshDeckOverlay()
    {
        var lines = new List<string>
        {
            $"Deck: {_gameManager.Deck.Count} cards",
            $"Gold: {_gameManager.Gold}",
            $"Relics: {_gameManager.Relics.Count}",
            $"Potions: {_gameManager.Potions.FindAll(p => p is not null).Count}/3",
            ""
        };

        if (_gameManager.Relics.Count > 0)
        {
            lines.Add("== Relics ==");
            foreach (RelicData relic in _gameManager.Relics)
            {
                lines.Add($"- {relic.Name}: {relic.Description}");
            }
            lines.Add("");
        }

        lines.Add("== Potions ==");
        for (int i = 0; i < _gameManager.Potions.Count; i++)
        {
            PotionData? potion = _gameManager.Potions[i];
            lines.Add(potion is null
                ? $"Slot {i + 1}: [Empty]"
                : $"Slot {i + 1}: {potion.Name} - {potion.Description}");
        }

        lines.Add("");
        lines.Add("== Deck ==");

        for (int i = 0; i < _gameManager.Deck.Count; i++)
        {
            CardData card = _gameManager.Deck[i];
            lines.Add($"{i + 1}. {card.DisplayName} (Cost {card.Cost})");
        }

        _deckListLabel.Text = string.Join("\n", lines);
    }

    private void BuildRoomOverlay()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _roomOverlay = new Control
        {
            Size = viewport,
            CustomMinimumSize = viewport,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        AddChild(_roomOverlay);

        _roomOverlay.AddChild(new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.72f),
            Size = viewport,
            CustomMinimumSize = viewport
        });

        var panel = new Panel
        {
            Position = new Vector2(viewport.X * 0.5f - 300f, viewport.Y * 0.5f - 190f),
            Size = new Vector2(600, 380),
            CustomMinimumSize = new Vector2(600, 380)
        };
        _roomOverlay.AddChild(panel);

        _roomTitleLabel = new Label
        {
            Position = new Vector2(20, 16),
            Size = new Vector2(560, 34),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _roomTitleLabel.AddThemeFontSizeOverride("font_size", 24);
        panel.AddChild(_roomTitleLabel);

        _roomContent = new VBoxContainer
        {
            Position = new Vector2(40, 68),
            Size = new Vector2(520, 280),
            CustomMinimumSize = new Vector2(520, 280)
        };
        _roomContent.AddThemeConstantOverride("separation", 14);
        panel.AddChild(_roomContent);
    }

    private MapNodeKind ResolveQuestionRoom()
    {
        int roll = (int)GD.RandRange(0, 99);
        if (roll < 40) return MapNodeKind.Combat;
        if (roll < 60) return MapNodeKind.Chest;
        if (roll < 80) return MapNodeKind.Shop;
        return MapNodeKind.Question;
    }

    private void ShowRoomByKind(MapNodeKind kind)
    {
        _roomOverlay.Visible = true;
        foreach (Node child in _roomContent.GetChildren())
        {
            child.QueueFree();
        }

        switch (kind)
        {
            case MapNodeKind.Chest:
                ShowChestRoom();
                break;
            case MapNodeKind.Shop:
                ShowShopRoom();
                break;
            default:
                ShowSpecialEventRoom();
                break;
        }
    }

    private void ShowChestRoom()
    {
        _roomTitleLabel.Text = "Treasure Chest";
        AddRoomText("Open the chest to gain a relic reward.");
        AddRoomButton("Open Chest", () =>
        {
            RelicData relic = _relicPool[(int)GD.RandRange(0, _relicPool.Count - 1)];
            bool added = _gameManager.AddRelic(relic);
            if (relic.Id == "relic_coin")
            {
                _gameManager.AddGold(20);
            }
            AddRoomText(added ? $"Gained relic: {relic.Name}" : $"Already owned relic: {relic.Name}");
            CompleteNonCombatRoom();
        });
    }

    private void ShowShopRoom()
    {
        _roomTitleLabel.Text = "Merchant";
        AddRoomText("Spend gold to buy cards/potions/relics or remove a card.");

        AddRoomButton("Buy Card (50g)", () =>
        {
            if (_gameManager.Gold < 50)
            {
                AddRoomText("Not enough gold.");
                return;
            }

            _gameManager.AddGold(-50);
            CardData card = _shopCardPool[(int)GD.RandRange(0, _shopCardPool.Count - 1)].Clone();
            _gameManager.AddCardToDeck(card);
            AddRoomText($"Bought card: {card.DisplayName}");
        });

        AddRoomButton("Buy Potion (35g)", () =>
        {
            if (_gameManager.Gold < 35)
            {
                AddRoomText("Not enough gold.");
                return;
            }

            if (!_gameManager.AddPotion(_potionPool[(int)GD.RandRange(0, _potionPool.Count - 1)]))
            {
                AddRoomText("Potion slots are full.");
                return;
            }

            _gameManager.AddGold(-35);
            AddRoomText("Bought a potion.");
        });

        AddRoomButton("Buy Relic (120g)", () =>
        {
            if (_gameManager.Gold < 120)
            {
                AddRoomText("Not enough gold.");
                return;
            }

            RelicData relic = _relicPool[(int)GD.RandRange(0, _relicPool.Count - 1)];
            if (!_gameManager.AddRelic(relic))
            {
                AddRoomText("Merchant has no new relic for you.");
                return;
            }

            _gameManager.AddGold(-120);
            AddRoomText($"Bought relic: {relic.Name}");
        });

        AddRoomButton("Remove Random Card (75g)", () =>
        {
            if (_gameManager.Gold < 75)
            {
                AddRoomText("Not enough gold.");
                return;
            }

            if (!_gameManager.RemoveRandomCardFromDeck())
            {
                AddRoomText("No removable card.");
                return;
            }

            _gameManager.AddGold(-75);
            AddRoomText("A card was removed from your deck.");
        });

        AddRoomButton("Leave Shop", CompleteNonCombatRoom);
    }

    private void ShowSpecialEventRoom()
    {
        _roomTitleLabel.Text = "Special Event";
        int roll = (int)GD.RandRange(0, 2);
        switch (roll)
        {
            case 0:
                AddRoomText("You found hidden coins (+25 gold).");
                _gameManager.AddGold(25);
                break;
            case 1:
                AddRoomText("You drink from a fountain (+10 HP).");
                _gameManager.Heal(10);
                break;
            default:
                AddRoomText("A cursed mirror scratches you (-6 HP), but grants a relic.");
                _gameManager.LoseHealth(6);
                _gameManager.AddRelic(_relicPool[(int)GD.RandRange(0, _relicPool.Count - 1)]);
                break;
        }

        AddRoomButton("Continue", CompleteNonCombatRoom);
    }

    private void CompleteNonCombatRoom()
    {
        _roomOverlay.Visible = false;
        _gameManager.AdvanceFloor();
        _mapManager.ShowMap();
        TryAutoAdvanceOutsideCombat();
    }

    private void AddRoomText(string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _roomContent.AddChild(label);
    }

    private void AddRoomButton(string text, System.Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            Size = new Vector2(300, 34),
            CustomMinimumSize = new Vector2(300, 34)
        };
        button.Pressed += () =>
        {
            AudioManager.Instance?.PlaySFX("res://Audio/click.ogg");
            onPressed();
            RefreshHud();
            if (_deckOverlay.Visible)
            {
                RefreshDeckOverlay();
            }
        };
        _roomContent.AddChild(button);
    }

    private void OnPlayerAttackPerformed(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= _enemyRects.Count)
        {
            return;
        }

        Control enemyRect = _enemyRects[targetIndex];
        if (!GodotObject.IsInstanceValid(enemyRect))
        {
            return;
        }

        AnimateAttackerTowards(_playerRect, enemyRect, 28f);
        if (enemyRect is ColorRect enemyColorRect)
        {
            FlashTarget(enemyColorRect, EnemyBaseColor, new Color(0.92f, 0.72f, 0.72f));
        }
    }

    private void OnEnemyAttackPerformed(int enemyIndex)
    {
        if (enemyIndex < 0 || enemyIndex >= _enemyRects.Count)
        {
            return;
        }

        Control enemyRect = _enemyRects[enemyIndex];
        if (!GodotObject.IsInstanceValid(enemyRect))
        {
            return;
        }

        AnimateAttackerTowards(enemyRect, _playerRect, 22f);
        FlashTarget(_playerRect, PlayerBaseColor, new Color(0.82f, 0.72f, 0.88f));
    }

    private static void AnimateAttackerTowards(Control attacker, Control target, float dashDistance)
    {
        Vector2 attackerCenter = attacker.GlobalPosition + attacker.Size * 0.5f;
        Vector2 targetCenter = target.GlobalPosition + target.Size * 0.5f;
        Vector2 direction = (targetCenter - attackerCenter).Normalized();
        Vector2 start = attacker.Position;
        Vector2 dashPosition = start + direction * dashDistance;

        var tween = attacker.CreateTween();
        tween.TweenProperty(attacker, "position", dashPosition, 0.09f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(attacker, "position", start, 0.12f)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Quad);
    }

    private static void FlashTarget(ColorRect targetRect, Color baseColor, Color flashColor)
    {
        var tween = targetRect.CreateTween();
        tween.TweenProperty(targetRect, "color", flashColor, 0.05f);
        tween.TweenProperty(targetRect, "color", baseColor, 0.12f);
    }
}
