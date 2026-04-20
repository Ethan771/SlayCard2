using System;
using System.Collections.Generic;
using Godot;

namespace SlayCard;

// 战斗管理器：支持多敌人、护盾系统、目标判定、状态与多段伤害。
public partial class CombatManager : Control
{
    [Signal] public delegate void CombatWonEventHandler();
    [Signal] public delegate void CombatLostEventHandler();
    [Signal] public delegate void CombatStateChangedEventHandler(int energy, int drawPile, int discardPile, int playerBlock, int playerWeak, int playerVulnerable);
    [Signal] public delegate void EnemiesStateChangedEventHandler(Godot.Collections.Array<int> enemyHealths, Godot.Collections.Array<int> enemyBlocks, Godot.Collections.Array<int> enemyWeaks, Godot.Collections.Array<int> enemyVulnerables);
    [Signal] public delegate void EnemyIntentChangedEventHandler(Godot.Collections.Array<string> intentTexts);
    [Signal] public delegate void TurnStateChangedEventHandler(bool isPlayerTurn);
    [Signal] public delegate void EnemyDamagedEventHandler(Vector2 worldPosition, int value);
    [Signal] public delegate void PlayerDamagedEventHandler(Vector2 worldPosition, int value);
    [Signal] public delegate void EnemyKilledEventHandler(int enemyIndex);
    [Signal] public delegate void PlayerAttackPerformedEventHandler(int targetIndex);
    [Signal] public delegate void EnemyAttackPerformedEventHandler(int enemyIndex);
    [Signal] public delegate void AutoPlayStateChangedEventHandler(bool isAutoPlaying);

    private readonly Random _rng = new();
    private const float HandArcAngleSpread = 24f;

    private readonly List<CardData> _drawPile = new();
    private readonly List<CardData> _discardPile = new();
    private readonly List<CardData> _hand = new();
    private readonly List<CardUI> _handUis = new();
    private readonly List<Control> _enemyTargetNodes = new();
    private readonly List<int> _enemyBlocks = new();
    private readonly List<int> _enemyWeakStacks = new();
    private readonly List<int> _enemyVulnerableStacks = new();
    private readonly List<EnemyIntent> _enemyIntents = new();

    private Control _handRoot = null!;

    public List<EnemyData> ActiveEnemies { get; } = new();

    private GameManager _gameManager = null!;
    private int _energy;
    private int _playerBlock;
    private int _playerWeakStacks;
    private int _playerVulnerableStacks;
    private bool _isPlayerTurn;
    private bool _isAutoPlaying;
    private bool _isAutoLoopRunning;
    private int _autoPlaySessionId;

    public bool IsAutoPlaying => _isAutoPlaying;
    public bool IsPlayerTurn => _isPlayerTurn;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;
        CustomMinimumSize = Size;
        MouseFilter = MouseFilterEnum.Pass;
        Visible = false;
        BuildCombatUi();
    }

    public void StartCombat(List<CardData> deck, int floorIndex)
    {
        Visible = true;

        ActiveEnemies.Clear();
        _enemyBlocks.Clear();
        _enemyWeakStacks.Clear();
        _enemyVulnerableStacks.Clear();
        _enemyIntents.Clear();
        _enemyTargetNodes.Clear();

        _playerBlock = 0;
        _playerWeakStacks = 0;
        _playerVulnerableStacks = 0;
        ApplyPreCombatRelicEffects();

        int enemyCount = RollEnemyCountByFloor(floorIndex);
        float countScaling = enemyCount switch
        {
            1 => 1.22f,
            2 => 1.00f,
            _ => 0.82f
        };
        for (int i = 0; i < enemyCount; i++)
        {
            int hp = Mathf.RoundToInt((22 + floorIndex * 4.2f) * countScaling) + _rng.Next(0, 2);
            int atk = Mathf.RoundToInt((5 + floorIndex * 1.0f) * (0.92f + countScaling * 0.07f)) + _rng.Next(0, 2);
            hp = Mathf.Max(10, hp);
            atk = Mathf.Max(3, atk);
            ActiveEnemies.Add(new EnemyData($"enemy_{floorIndex}_{i}", "Slime", hp, atk));
            _enemyBlocks.Add(0);
            _enemyWeakStacks.Add(0);
            _enemyVulnerableStacks.Add(0);
            _enemyIntents.Add(default);
        }

        _drawPile.Clear();
        _discardPile.Clear();
        _hand.Clear();

        foreach (CardData card in deck)
        {
            _drawPile.Add(card.Clone());
        }

        Shuffle(_drawPile);
        StartPlayerTurn();
        UpdateCombatState();
    }

    public void BindGameManager(GameManager gameManager)
    {
        _gameManager = gameManager;
    }

    public void SetEnemyTargetNodes(List<Control> enemyNodes)
    {
        _enemyTargetNodes.Clear();
        _enemyTargetNodes.AddRange(enemyNodes);
    }

    public void FreezeCombat()
    {
        _isPlayerTurn = false;
        SetHandInteractable(false);
        EmitSignal(SignalName.TurnStateChanged, false);
    }

    public void HideCombat()
    {
        Visible = false;
    }

    public void SyncUiState()
    {
        UpdateCombatState();
    }

    public void SetAutoPlaying(bool enabled)
    {
        _autoPlaySessionId++;

        if (_isAutoPlaying == enabled)
        {
            if (enabled)
            {
                TryStartAutoPlayLoop();
            }
            return;
        }

        _isAutoPlaying = enabled;
        EmitSignal(SignalName.AutoPlayStateChanged, _isAutoPlaying);

        if (_isAutoPlaying)
        {
            TryStartAutoPlayLoop();
            return;
        }

        RecoverPlayerTurnInputState();
    }

    public void RecoverPlayerTurnInputState()
    {
        if (!_isPlayerTurn || !Visible)
        {
            return;
        }

        SetHandInteractable(true);
        EmitSignal(SignalName.TurnStateChanged, true);
        UpdateCombatState();
    }

    private void StartPlayerTurn()
    {
        _autoPlaySessionId++;
        _isPlayerTurn = true;
        _energy = 3 + GetRelicEnergyBonus();
        _playerBlock = 0;

        for (int i = 0; i < _enemyBlocks.Count; i++)
        {
            _enemyBlocks[i] = 0;
        }

        GenerateEnemyIntents();
        DrawCards(5);
        RebuildHandUi();
        SetHandInteractable(true);
        EmitSignal(SignalName.TurnStateChanged, true);
        UpdateCombatState();
        TryStartAutoPlayLoop();
    }

    public void EndPlayerTurn()
    {
        if (!_isPlayerTurn || !Visible)
        {
            return;
        }

        _isPlayerTurn = false;
        SetHandInteractable(false);
        EmitSignal(SignalName.TurnStateChanged, false);

        foreach (CardData card in _hand)
        {
            _discardPile.Add(card);
        }

        _hand.Clear();
        RebuildHandUi();

        // 玩家回合结束，玩家状态衰减。
        DecayPlayerStatuses();
        ExecuteEnemyTurn();
    }

    public void EndPlayerTurnByManual()
    {
        if (_isAutoPlaying)
        {
            SetAutoPlaying(false);
        }

        EndPlayerTurn();
    }

    private void DrawCards(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (_drawPile.Count == 0)
            {
                if (_discardPile.Count == 0)
                {
                    break;
                }

                _drawPile.AddRange(_discardPile);
                _discardPile.Clear();
                Shuffle(_drawPile);
            }

            CardData card = _drawPile[0];
            _drawPile.RemoveAt(0);
            _hand.Add(card);
            AudioManager.Instance?.PlaySFX("res://Audio/card.ogg");
        }
    }

    private void PlayCard(CardData card, int targetIndex)
    {
        if (!_isPlayerTurn || card.Cost > _energy)
        {
            return;
        }

        bool needsEnemyTarget = card.NeedsEnemyTarget();
        if (needsEnemyTarget && (targetIndex < 0 || targetIndex >= ActiveEnemies.Count))
        {
            return;
        }

        _energy -= card.Cost;

        _hand.Remove(card);
        _discardPile.Add(card);

        if (card.Block > 0)
        {
            _playerBlock += card.Block;
        }

        if (card.EnergyAmount > 0)
        {
            _energy += card.EnergyAmount;
        }

        if (card.Damage > 0 && targetIndex >= 0 && targetIndex < ActiveEnemies.Count)
        {
            int hitCount = Mathf.Max(1, card.HitCount);
            for (int hit = 0; hit < hitCount && targetIndex < ActiveEnemies.Count; hit++)
            {
                ApplyPlayerAttackHit(targetIndex, card.Damage);
                CleanupDeadEnemies();
                if (ActiveEnemies.Count == 0)
                {
                    break;
                }
            }
        }

        if (card.ApplyWeak > 0)
        {
            if (needsEnemyTarget && targetIndex >= 0 && targetIndex < ActiveEnemies.Count)
            {
                _enemyWeakStacks[targetIndex] += card.ApplyWeak;
            }
            else
            {
                _playerWeakStacks += card.ApplyWeak;
            }
        }

        if (card.ApplyVulnerable > 0)
        {
            if (needsEnemyTarget && targetIndex >= 0 && targetIndex < ActiveEnemies.Count)
            {
                _enemyVulnerableStacks[targetIndex] += card.ApplyVulnerable;
            }
            else
            {
                _playerVulnerableStacks += card.ApplyVulnerable;
            }
        }

        if (card.DrawAmount > 0)
        {
            DrawCards(card.DrawAmount);
        }

        CleanupDeadEnemies();

        if (ActiveEnemies.Count == 0)
        {
            RebuildHandUi();
            UpdateCombatState();
            EmitSignal(SignalName.CombatWon);
            return;
        }

        RebuildHandUi();
        UpdateCombatState();
    }

    private void ApplyPlayerAttackHit(int targetIndex, int baseDamage)
    {
        if (targetIndex < 0 || targetIndex >= ActiveEnemies.Count)
        {
            return;
        }

        EmitSignal(SignalName.PlayerAttackPerformed, targetIndex);

        int modifiedDamage = baseDamage;
        if (_playerWeakStacks > 0)
        {
            modifiedDamage -= 3;
        }
        if (_enemyVulnerableStacks[targetIndex] > 0)
        {
            modifiedDamage += 3;
        }

        modifiedDamage = Mathf.Max(0, modifiedDamage);

        int blocked = Mathf.Min(_enemyBlocks[targetIndex], modifiedDamage);
        _enemyBlocks[targetIndex] -= blocked;
        int damageToHealth = modifiedDamage - blocked;

        if (damageToHealth > 0)
        {
            ActiveEnemies[targetIndex].CurrentHealth -= damageToHealth;
        }

        if (damageToHealth > 0 || blocked > 0)
        {
            AudioManager.Instance?.PlaySFX("res://Audio/hit.ogg");
        }

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Vector2 hitPos = _enemyTargetNodes.Count > targetIndex
            ? _enemyTargetNodes[targetIndex].GlobalPosition
            : new Vector2(viewport.X * 0.75f, viewport.Y * 0.55f);
        EmitSignal(SignalName.EnemyDamaged, hitPos, damageToHealth);
    }

    private void ApplyEnemyAttackHit(int enemyIndex, int baseDamage)
    {
        if (enemyIndex < 0 || enemyIndex >= ActiveEnemies.Count)
        {
            return;
        }

        EmitSignal(SignalName.EnemyAttackPerformed, enemyIndex);

        int modifiedDamage = baseDamage;
        if (_enemyWeakStacks[enemyIndex] > 0)
        {
            modifiedDamage -= 3;
        }
        if (_playerVulnerableStacks > 0)
        {
            modifiedDamage += 3;
        }

        modifiedDamage = Mathf.Max(0, modifiedDamage);

        int blocked = Mathf.Min(_playerBlock, modifiedDamage);
        _playerBlock -= blocked;
        int damageToHealth = modifiedDamage - blocked;

        if (damageToHealth > 0)
        {
            _gameManager.LoseHealth(damageToHealth);
        }

        if (damageToHealth > 0 || blocked > 0)
        {
            AudioManager.Instance?.PlaySFX("res://Audio/hit.ogg");
        }

        EmitSignal(SignalName.PlayerDamaged, new Vector2(120, 80), damageToHealth);
    }

    private void CleanupDeadEnemies()
    {
        for (int i = ActiveEnemies.Count - 1; i >= 0; i--)
        {
            if (ActiveEnemies[i].CurrentHealth > 0)
            {
                continue;
            }

            ActiveEnemies.RemoveAt(i);
            _enemyBlocks.RemoveAt(i);
            _enemyWeakStacks.RemoveAt(i);
            _enemyVulnerableStacks.RemoveAt(i);
            _enemyIntents.RemoveAt(i);

            if (_enemyTargetNodes.Count > i)
            {
                _enemyTargetNodes.RemoveAt(i);
            }

            EmitSignal(SignalName.EnemyKilled, i);
        }
    }

    private async void ExecuteEnemyTurn()
    {
        try
        {
            await ToSignal(GetTree().CreateTimer(0.45f), SceneTreeTimer.SignalName.Timeout);

            for (int i = 0; i < ActiveEnemies.Count; i++)
            {
                EnemyIntent intent = _enemyIntents[i];
                if (intent.Type == EnemyIntentType.Defend)
                {
                    _enemyBlocks[i] += intent.Block;
                    continue;
                }

                if (intent.Type == EnemyIntentType.Debuff)
                {
                    EmitSignal(SignalName.EnemyAttackPerformed, i);
                    _playerVulnerableStacks += Mathf.Max(1, intent.ApplyVulnerable);
                    continue;
                }

                int hitCount = Mathf.Max(1, intent.HitCount);
                for (int hit = 0; hit < hitCount; hit++)
                {
                    ApplyEnemyAttackHit(i, intent.Damage);
                    if (_gameManager.PlayerHealth <= 0)
                    {
                        UpdateCombatState();
                        EmitSignal(SignalName.CombatLost);
                        return;
                    }
                }

                if (intent.ApplyVulnerable > 0)
                {
                    _playerVulnerableStacks += intent.ApplyVulnerable;
                }
            }

            DecayEnemyStatuses();
            UpdateCombatState();
            StartPlayerTurn();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Enemy turn failed: {ex}");
            if (_gameManager.PlayerHealth > 0)
            {
                StartPlayerTurn();
            }
        }
    }

    private void DecayPlayerStatuses()
    {
        if (_playerWeakStacks > 0)
        {
            _playerWeakStacks -= 1;
        }

        if (_playerVulnerableStacks > 0)
        {
            _playerVulnerableStacks -= 1;
        }
    }

    private void DecayEnemyStatuses()
    {
        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            if (_enemyWeakStacks[i] > 0)
            {
                _enemyWeakStacks[i] -= 1;
            }

            if (_enemyVulnerableStacks[i] > 0)
            {
                _enemyVulnerableStacks[i] -= 1;
            }
        }
    }

    private void GenerateEnemyIntents()
    {
        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            int roll = _rng.Next(100);
            EnemyData enemy = ActiveEnemies[i];

            if (roll < 40)
            {
                _enemyIntents[i] = new EnemyIntent
                {
                    Type = EnemyIntentType.Attack,
                    Damage = enemy.BaseAttack + _rng.Next(0, 2),
                    HitCount = 1,
                    Block = 0,
                    ApplyVulnerable = _rng.Next(100) < 10 ? 1 : 0
                };
            }
            else if (roll < 65)
            {
                _enemyIntents[i] = new EnemyIntent
                {
                    Type = EnemyIntentType.Attack,
                    Damage = Mathf.Max(1, enemy.BaseAttack - 3 + _rng.Next(0, 2)),
                    HitCount = _rng.Next(2, 4),
                    Block = 0,
                    ApplyVulnerable = 0
                };
            }
            else if (roll < 85)
            {
                _enemyIntents[i] = new EnemyIntent
                {
                    Type = EnemyIntentType.Defend,
                    Damage = 0,
                    HitCount = 1,
                    Block = 5 + _rng.Next(0, 3),
                    ApplyVulnerable = 0
                };
            }
            else
            {
                _enemyIntents[i] = new EnemyIntent
                {
                    Type = EnemyIntentType.Debuff,
                    Damage = 0,
                    HitCount = 1,
                    Block = 0,
                    ApplyVulnerable = 2
                };
            }
        }

        EmitEnemyIntents();
    }

    private void EmitEnemyIntents()
    {
        var intents = new Godot.Collections.Array<string>();
        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            EnemyIntent intent = _enemyIntents[i];
            if (intent.Type == EnemyIntentType.Defend)
            {
                intents.Add($"Intent: Defend {intent.Block}");
            }
            else if (intent.Type == EnemyIntentType.Debuff)
            {
                intents.Add($"Intent: Hex Vuln {intent.ApplyVulnerable}");
            }
            else if (intent.HitCount > 1)
            {
                intents.Add($"Intent: Attack {intent.Damage} x {intent.HitCount}");
            }
            else
            {
                if (intent.ApplyVulnerable > 0)
                {
                    intents.Add($"Intent: Attack {intent.Damage} +V{intent.ApplyVulnerable}");
                }
                else
                {
                    intents.Add($"Intent: Attack {intent.Damage}");
                }
            }
        }

        EmitSignal(SignalName.EnemyIntentChanged, intents);
    }

    private void RebuildHandUi()
    {
        foreach (CardUI cardUi in _handUis)
        {
            cardUi.QueueFree();
        }

        _handUis.Clear();

        for (int i = 0; i < _hand.Count; i++)
        {
            CardData card = _hand[i];
            var cardUi = new CardUI
            {
                MouseFilter = MouseFilterEnum.Stop,
                IsRewardCard = false,
                EnableDragging = true,
                TargetResolver = ResolveTargetIndex
            };

            _handRoot.AddChild(cardUi);
            cardUi.Setup(card);
            cardUi.CardReleased += OnCardReleased;
            cardUi.ManualInteraction += OnManualCardInteraction;
            _handUis.Add(cardUi);
        }

        ApplyHandArcLayout();
    }

    private void OnCardReleased(CardUI cardUi, bool shouldPlay, int targetIndex)
    {
        if (!_isPlayerTurn || !shouldPlay)
        {
            cardUi.ResetPosition();
            return;
        }

        PlayCard(cardUi.CardData, targetIndex);
    }

    private void OnManualCardInteraction(CardUI cardUi)
    {
        if (_isAutoPlaying)
        {
            SetAutoPlaying(false);
        }
    }

    private void TryStartAutoPlayLoop()
    {
        if (!_isAutoPlaying || !_isPlayerTurn || !Visible || _isAutoLoopRunning)
        {
            return;
        }

        _ = RunAutoPlayLoop();
    }

    private async System.Threading.Tasks.Task RunAutoPlayLoop()
    {
        int sessionAtStart = _autoPlaySessionId;
        _isAutoLoopRunning = true;
        try
        {
            while (_isAutoPlaying && _isPlayerTurn && Visible)
            {
                if (sessionAtStart != _autoPlaySessionId)
                {
                    break;
                }

                AutoPlayDecision? decision = EvaluateBestCard();
                if (decision is null)
                {
                    await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
                    if (_isAutoPlaying && _isPlayerTurn && sessionAtStart == _autoPlaySessionId)
                    {
                        EndPlayerTurn();
                    }
                    break;
                }

                if (!GodotObject.IsInstanceValid(decision.CardUi))
                {
                    break;
                }

                Node flightTarget = decision.TargetNode ?? _handRoot;
                await decision.CardUi.SimulatePlay(flightTarget);

                if (!_isAutoPlaying || !_isPlayerTurn || sessionAtStart != _autoPlaySessionId)
                {
                    break;
                }

                await ToSignal(GetTree().CreateTimer(0.8f), SceneTreeTimer.SignalName.Timeout);
            }
        }
        finally
        {
            _isAutoLoopRunning = false;
        }
    }

    public AutoPlayDecision? EvaluateBestCard()
    {
        if (!_isPlayerTurn || _handUis.Count == 0)
        {
            return null;
        }

        int incomingDamage = EstimateIncomingEnemyDamage();
        bool needsDefense = incomingDamage > _playerBlock;

        AutoPlayDecision? bestDecision = null;
        foreach (CardUI cardUi in _handUis)
        {
            CardData card = cardUi.CardData;
            if (card.Cost > _energy)
            {
                continue;
            }

            int targetIndex = -1;
            Control? targetNode = null;
            if (card.NeedsEnemyTarget())
            {
                targetIndex = SelectBestTargetForCard(card);
                if (targetIndex < 0 || targetIndex >= ActiveEnemies.Count)
                {
                    continue;
                }

                if (targetIndex < _enemyTargetNodes.Count)
                {
                    targetNode = _enemyTargetNodes[targetIndex];
                }
            }

            float score = ScoreCard(card, targetIndex, needsDefense);
            if (bestDecision is null || score > bestDecision.Score)
            {
                bestDecision = new AutoPlayDecision(cardUi, targetIndex, targetNode, score);
            }
        }

        if (bestDecision is null || bestDecision.Score < 4f)
        {
            return null;
        }

        return bestDecision;
    }

    private float ScoreCard(CardData card, int targetIndex, bool needsDefense)
    {
        float score = 0f;
        int estimatedDamage = Mathf.Max(0, card.Damage) * Mathf.Max(1, card.HitCount);

        if (needsDefense)
        {
            score += card.Block * 13f;
            if (card.ApplyWeak > 0)
            {
                score += 14f + card.ApplyWeak * 5f;
            }

            score += estimatedDamage * 1.5f;
        }
        else
        {
            score += estimatedDamage * 5f;
            if (card.ApplyVulnerable > 0)
            {
                score += 16f + card.ApplyVulnerable * 6f;
            }
            if (card.ApplyWeak > 0)
            {
                score += 9f + card.ApplyWeak * 3f;
            }
            score += card.Block * 2.5f;
        }

        score += card.DrawAmount * 3f;
        score += card.EnergyAmount * 4f;
        score -= card.Cost * 1.5f;

        if (targetIndex >= 0 && targetIndex < ActiveEnemies.Count)
        {
            int effectiveHp = ActiveEnemies[targetIndex].CurrentHealth + _enemyBlocks[targetIndex];
            score += Mathf.Max(0, 24 - effectiveHp) * 0.6f;
        }

        return score;
    }

    private int EstimateIncomingEnemyDamage()
    {
        int total = 0;
        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            EnemyIntent intent = _enemyIntents[i];
            if (intent.Type != EnemyIntentType.Attack)
            {
                continue;
            }

            int hitDamage = intent.Damage;
            if (_enemyWeakStacks[i] > 0)
            {
                hitDamage -= 3;
            }
            if (_playerVulnerableStacks > 0)
            {
                hitDamage += 3;
            }

            total += Mathf.Max(0, hitDamage) * Mathf.Max(1, intent.HitCount);
        }

        return total;
    }

    private int SelectBestTargetForCard(CardData card)
    {
        int bestIndex = -1;
        int bestScore = int.MaxValue;
        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            int blockPenalty = _enemyBlocks[i] > 0 ? 1000 : 0;
            int score = blockPenalty + ActiveEnemies[i].CurrentHealth;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private int ResolveTargetIndex(Vector2 globalMousePos)
    {
        for (int i = 0; i < _enemyTargetNodes.Count; i++)
        {
            Control targetNode = _enemyTargetNodes[i];
            if (!GodotObject.IsInstanceValid(targetNode))
            {
                continue;
            }

            if (targetNode.GetGlobalRect().HasPoint(globalMousePos) && i < ActiveEnemies.Count)
            {
                return i;
            }
        }

        return -1;
    }

    private void BuildCombatUi()
    {
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        var background = new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.16f, 1f),
            Size = viewportSize,
            CustomMinimumSize = viewportSize,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(background);

        _handRoot = new Control
        {
            Position = Vector2.Zero,
            Size = viewportSize,
            CustomMinimumSize = viewportSize,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_handRoot);
    }

    private void UpdateCombatState()
    {
        var enemyHealths = new Godot.Collections.Array<int>();
        var enemyBlocks = new Godot.Collections.Array<int>();
        var enemyWeaks = new Godot.Collections.Array<int>();
        var enemyVulns = new Godot.Collections.Array<int>();

        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            enemyHealths.Add(Mathf.Max(0, ActiveEnemies[i].CurrentHealth));
            enemyBlocks.Add(_enemyBlocks[i]);
            enemyWeaks.Add(_enemyWeakStacks[i]);
            enemyVulns.Add(_enemyVulnerableStacks[i]);
        }

        EmitEnemyIntents();
        EmitSignal(SignalName.EnemiesStateChanged, enemyHealths, enemyBlocks, enemyWeaks, enemyVulns);
        EmitSignal(SignalName.CombatStateChanged, _energy, _drawPile.Count, _discardPile.Count, _playerBlock, _playerWeakStacks, _playerVulnerableStacks);
    }

    private void ApplyHandArcLayout()
    {
        int totalCards = _handUis.Count;
        if (totalCards == 0)
        {
            return;
        }

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float handArcRadius = viewportSize.Y + 280f;
        Vector2 handCenterPosition = new(viewportSize.X * 0.5f, viewportSize.Y + 800f);

        for (int i = 0; i < totalCards; i++)
        {
            float t = totalCards == 1 ? 0.5f : i / (float)(totalCards - 1);
            float angleDeg = (t - 0.5f) * HandArcAngleSpread;
            float angleRad = Mathf.DegToRad(-90f + angleDeg);

            float x = handCenterPosition.X + Mathf.Cos(angleRad) * handArcRadius;
            float y = handCenterPosition.Y + Mathf.Sin(angleRad) * handArcRadius;
            y = Mathf.Max(y, viewportSize.Y * 0.6f);
            float rotation = Mathf.DegToRad(angleDeg * 0.9f);

            _handUis[i].SetHomeTransform(new Vector2(x, y), rotation);
        }
    }

    private void SetHandInteractable(bool enabled)
    {
        foreach (CardUI cardUi in _handUis)
        {
            cardUi.MouseFilter = enabled ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        }
    }

    public sealed class AutoPlayDecision
    {
        public CardUI CardUi { get; }
        public int TargetIndex { get; }
        public Control? TargetNode { get; }
        public float Score { get; }

        public AutoPlayDecision(CardUI cardUi, int targetIndex, Control? targetNode, float score)
        {
            CardUi = cardUi;
            TargetIndex = targetIndex;
            TargetNode = targetNode;
            Score = score;
        }
    }

    private void Shuffle(List<CardData> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private int RollEnemyCountByFloor(int floorIndex)
    {
        int roll = _rng.Next(100);

        if (floorIndex <= 0)
        {
            return roll < 65 ? 1 : 2;
        }

        if (floorIndex <= 2)
        {
            if (roll < 25)
            {
                return 1;
            }

            return roll < 80 ? 2 : 3;
        }

        if (floorIndex <= 5)
        {
            return roll < 35 ? 2 : 3;
        }

        return roll < 20 ? 2 : 3;
    }

    private void ApplyPreCombatRelicEffects()
    {
        if (_gameManager.Relics.Exists(r => r.Id == "relic_guard"))
        {
            _playerBlock += 8;
        }
    }

    private int GetRelicEnergyBonus()
    {
        return _gameManager.Relics.Exists(r => r.Id == "relic_energy") ? 1 : 0;
    }
}
