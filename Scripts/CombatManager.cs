using System;
using System.Collections.Generic;
using Godot;

namespace SlayCard;

// 战斗管理器：处理抽牌、出牌、能量与回合推进。
public partial class CombatManager : Control
{
    [Signal] public delegate void CombatWonEventHandler();
    [Signal] public delegate void CombatLostEventHandler();
    [Signal] public delegate void CombatStateChangedEventHandler(int energy, int drawPile, int discardPile, int enemyHealth);
    [Signal] public delegate void TurnStateChangedEventHandler(bool isPlayerTurn);
    [Signal] public delegate void EnemyDamagedEventHandler(Vector2 worldPosition, int value);
    [Signal] public delegate void PlayerDamagedEventHandler(Vector2 worldPosition, int value);

    private readonly Random _rng = new();
    private const float HandArcRadius = 1000f;
    private const float HandArcAngleSpread = 30f;
    private static readonly Vector2 HandCenterPosition = new(640, 1400);

    private readonly List<CardData> _drawPile = new();
    private readonly List<CardData> _discardPile = new();
    private readonly List<CardData> _hand = new();
    private readonly List<CardUI> _handUis = new();

    private Control _handRoot = null!;
    private Label _enemyLabel = null!;
    private Label _energyLabel = null!;

    private EnemyData _enemy = null!;
    private GameManager _gameManager = null!;
    private int _energy;
    private int _playerBlock;
    private int _enemyBlock;
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

    public void StartCombat(List<CardData> deck, EnemyData enemy)
    {
        Visible = true;
        _enemy = enemy;
        _enemy.ResetHealth();
        _drawPile.Clear();
        _discardPile.Clear();
        _hand.Clear();
        _enemyBlock = 0;
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

    private void PlayCard(CardData card)
    {
        if (!_isPlayerTurn)
        {
            return;
        }

        if (card.Cost > _energy)
        {
            return;
        }

        _energy -= card.Cost;

        if (card.Damage > 0)
        {
            int realDamage = Mathf.Max(0, card.Damage - _enemyBlock);
            _enemyBlock = Mathf.Max(0, _enemyBlock - card.Damage);
            _enemy.CurrentHealth -= realDamage;
            EmitSignal(SignalName.EnemyDamaged, _enemyLabel.GlobalPosition, realDamage);
        }

        if (card.Block > 0)
        {
            _playerBlock += card.Block;
        }

        _hand.Remove(card);
        _discardPile.Add(card);

        if (_enemy.IsDead())
        {
            UpdateCombatState();
            EmitSignal(SignalName.CombatWon);
            return;
        }

        RebuildHandUi();
        UpdateCombatState();

        if (_energy <= 0)
        {
            EndPlayerTurn();
        }
    }

    private async void ExecuteEnemyTurn()
    {
        await ToSignal(GetTree().CreateTimer(0.45f), SceneTreeTimer.SignalName.Timeout);

        if (_enemyWillAttack)
        {
            int incoming = _enemy.BaseAttack + _rng.Next(0, 3);
            int finalDamage = Mathf.Max(0, incoming - _playerBlock);
            _playerBlock = Mathf.Max(0, _playerBlock - incoming);

            if (finalDamage > 0)
            {
                _gameManager.LoseHealth(finalDamage);
            }

            EmitSignal(SignalName.PlayerDamaged, new Vector2(120, 80), finalDamage);
        }
        else
        {
            _enemyBlock += 6;
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
                MouseFilter = MouseFilterEnum.Stop
            };

            _handRoot.AddChild(cardUi);
            cardUi.Setup(card);
            cardUi.CardReleased += OnCardReleased;
            _handUis.Add(cardUi);
        }

        ApplyHandArcLayout();
    }

    private void OnCardReleased(CardUI cardUi, bool shouldPlay)
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

        PlayCard(cardUi.CardData);
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

        _enemyLabel = new Label
        {
            Position = new Vector2(530, 90),
            Size = new Vector2(260, 40),
            CustomMinimumSize = new Vector2(260, 40),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_enemyLabel);

        _energyLabel = new Label
        {
            Position = new Vector2(30, 30),
            Size = new Vector2(220, 32),
            CustomMinimumSize = new Vector2(220, 32),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_energyLabel);

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
        string intentText = _enemyWillAttack
            ? $"Intent: Attack {_enemy.BaseAttack}-{_enemy.BaseAttack + 2}"
            : "Intent: Defend 6";
        _enemyLabel.Text = $"{_enemy.DisplayName} HP: {Mathf.Max(0, _enemy.CurrentHealth)}  Block: {_enemyBlock}  {intentText}";
        _energyLabel.Text = $"Energy: {_energy}   Draw: {_drawPile.Count}   Discard: {_discardPile.Count}";
        EmitSignal(SignalName.CombatStateChanged, _energy, _drawPile.Count, _discardPile.Count, _enemy.CurrentHealth);
    }

    private void ApplyHandArcLayout()
    {
        int totalCards = _handUis.Count;
        if (totalCards == 0)
        {
            return;
        }

        for (int i = 0; i < totalCards; i++)
        {
            float t = totalCards == 1 ? 0.5f : i / (float)(totalCards - 1);
            float angleDeg = (t - 0.5f) * HandArcAngleSpread;
            float angleRad = Mathf.DegToRad(-90f + angleDeg);

            float x = HandCenterPosition.X + Mathf.Cos(angleRad) * HandArcRadius;
            float y = HandCenterPosition.Y + Mathf.Sin(angleRad) * HandArcRadius;
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
