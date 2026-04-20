using System;
using System.Collections.Generic;
using Godot;

namespace SlayCard;

// 战斗管理器：支持多敌人、目标判定、抽牌与回合推进。
public partial class CombatManager : Control
{
    [Signal] public delegate void CombatWonEventHandler();
    [Signal] public delegate void CombatLostEventHandler();
    [Signal] public delegate void CombatStateChangedEventHandler(int energy, int drawPile, int discardPile);
    [Signal] public delegate void EnemiesStateChangedEventHandler(Godot.Collections.Array<int> enemyHealths);
    [Signal] public delegate void EnemyIntentChangedEventHandler(string intentText);
    [Signal] public delegate void TurnStateChangedEventHandler(bool isPlayerTurn);
    [Signal] public delegate void EnemyDamagedEventHandler(Vector2 worldPosition, int value);
    [Signal] public delegate void PlayerDamagedEventHandler(Vector2 worldPosition, int value);

    private readonly Random _rng = new();
    private const float HandArcAngleSpread = 24f;

    private readonly List<CardData> _drawPile = new();
    private readonly List<CardData> _discardPile = new();
    private readonly List<CardData> _hand = new();
    private readonly List<CardUI> _handUis = new();
    private readonly List<Control> _enemyTargetNodes = new();

    private Control _handRoot = null!;

    public List<EnemyData> ActiveEnemies { get; } = new();

    private GameManager _gameManager = null!;
    private int _energy;
    private int _playerBlock;
    private bool _enemyWillAttack = true;
    private bool _isPlayerTurn;

    public override void _Ready()
    {
        Size = GetViewportRect().Size;
        CustomMinimumSize = Size;
        MouseFilter = MouseFilterEnum.Pass;
        Visible = false;

        BuildCombatUi();
    }

    public void StartCombat(List<CardData> deck, List<EnemyData> enemies)
    {
        Visible = true;
        ActiveEnemies.Clear();
        foreach (EnemyData enemy in enemies)
        {
            enemy.ResetHealth();
            ActiveEnemies.Add(enemy);
        }

        _drawPile.Clear();
        _discardPile.Clear();
        _hand.Clear();
        _enemyWillAttack = true;

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

    public void HideCombat()
    {
        Visible = false;
    }

    private void StartPlayerTurn()
    {
        _isPlayerTurn = true;
        _energy = 3;
        _playerBlock = 0;
        DrawCards(5);
        RebuildHandUi();
        SetHandInteractable(true);
        EmitSignal(SignalName.TurnStateChanged, true);
        UpdateCombatState();
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
        ExecuteEnemyTurn();
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
        }
    }

    private void PlayCard(CardData card, int targetIndex)
    {
        if (!_isPlayerTurn)
        {
            return;
        }

        if (card.Cost > _energy)
        {
            return;
        }

        if (targetIndex < 0 || targetIndex >= ActiveEnemies.Count)
        {
            return;
        }

        _energy -= card.Cost;

        if (card.Damage > 0)
        {
            EnemyData target = ActiveEnemies[targetIndex];
            target.CurrentHealth -= card.Damage;
            Vector2 hitPos = _enemyTargetNodes.Count > targetIndex
                ? _enemyTargetNodes[targetIndex].GlobalPosition
                : new Vector2(GetViewportRect().Size.X * 0.75f, GetViewportRect().Size.Y * 0.45f);
            EmitSignal(SignalName.EnemyDamaged, hitPos, card.Damage);
        }

        if (card.Block > 0)
        {
            _playerBlock += card.Block;
        }

        _hand.Remove(card);
        _discardPile.Add(card);

        ActiveEnemies.RemoveAll(enemy => enemy.IsDead());

        if (ActiveEnemies.Count == 0)
        {
            UpdateCombatState();
            EmitSignal(SignalName.CombatWon);
            return;
        }

        RebuildHandUi();
        UpdateCombatState();
    }

    private async void ExecuteEnemyTurn()
    {
        await ToSignal(GetTree().CreateTimer(0.45f), SceneTreeTimer.SignalName.Timeout);

        int totalIncoming = 0;
        if (_enemyWillAttack)
        {
            foreach (EnemyData enemy in ActiveEnemies)
            {
                totalIncoming += enemy.BaseAttack + _rng.Next(0, 3);
            }

            int finalDamage = Mathf.Max(0, totalIncoming - _playerBlock);
            _playerBlock = Mathf.Max(0, _playerBlock - totalIncoming);

            if (finalDamage > 0)
            {
                _gameManager.LoseHealth(finalDamage);
            }

            EmitSignal(SignalName.PlayerDamaged, new Vector2(120, 80), finalDamage);
        }

        _enemyWillAttack = !_enemyWillAttack;
        UpdateCombatState();

        if (_gameManager.PlayerHealth <= 0)
        {
            EmitSignal(SignalName.CombatLost);
            return;
        }

        StartPlayerTurn();
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
            _handUis.Add(cardUi);
        }

        ApplyHandArcLayout();
    }

    private void OnCardReleased(CardUI cardUi, bool shouldPlay, int targetIndex)
    {
        if (!_isPlayerTurn)
        {
            cardUi.ResetPosition();
            return;
        }

        if (!shouldPlay)
        {
            cardUi.ResetPosition();
            return;
        }

        PlayCard(cardUi.CardData, targetIndex);
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
        var background = new ColorRect
        {
            Color = new Color(0.1f, 0.1f, 0.16f, 1f),
            Size = new Vector2(1280, 720),
            CustomMinimumSize = new Vector2(1280, 720),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(background);

        _handRoot = new Control
        {
            Position = Vector2.Zero,
            Size = new Vector2(1280, 720),
            CustomMinimumSize = new Vector2(1280, 720),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_handRoot);
    }

    private void UpdateCombatState()
    {
        string intentText = _enemyWillAttack ? "Intent: Attack" : "Intent: Defend";
        EmitSignal(SignalName.EnemyIntentChanged, intentText);

        var enemyHealths = new Godot.Collections.Array<int>();
        foreach (EnemyData enemy in ActiveEnemies)
        {
            enemyHealths.Add(Mathf.Max(0, enemy.CurrentHealth));
        }

        EmitSignal(SignalName.EnemiesStateChanged, enemyHealths);
        EmitSignal(SignalName.CombatStateChanged, _energy, _drawPile.Count, _discardPile.Count);
    }

    private void ApplyHandArcLayout()
    {
        int totalCards = _handUis.Count;
        if (totalCards == 0)
        {
            return;
        }

        Vector2 viewportSize = GetViewportRect().Size;
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

    private void Shuffle(List<CardData> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
