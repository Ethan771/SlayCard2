using Godot;

namespace SlayCard;

public enum MapNodeState
{
    Locked,
    Current,
    Completed
}

public enum MapNodeKind
{
    Combat,
    Question,
    Chest,
    Shop,
    Campfire,
    Boss
}

// 地图管理器：纯代码生成树状路线按钮。
public partial class MapManager : Control
{
    [Signal] public delegate void NodeSelectedEventHandler(int depth, int lane);

    private readonly Vector2 _nodeSize = new(120, 46);
    private readonly int[] _lanesPerDepth = { 1, 2, 3, 2, 3, 2, 3, 2, 3, 2, 2, 1 };
    private readonly System.Collections.Generic.Dictionary<(int depth, int lane), Button> _nodeButtons = new();
    private readonly System.Collections.Generic.Dictionary<(int depth, int lane), MapNodeKind> _nodeKinds = new();
    private readonly System.Collections.Generic.Dictionary<int, int> _selectedLaneByDepth = new();
    private readonly System.Collections.Generic.Dictionary<int, int> _forcedShopLaneByDepth = new();
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        Size = viewport;
        CustomMinimumSize = viewport;
        MouseFilter = MouseFilterEnum.Pass;
        Visible = false;

        BuildMapUi();
    }

    public void ShowMap()
    {
        Visible = true;
    }

    public void HideMap()
    {
        Visible = false;
    }

    public bool TrySelectFirstAvailableNode()
    {
        if (!Visible)
        {
            return false;
        }

        for (int depth = 0; depth < _lanesPerDepth.Length; depth++)
        {
            int laneCount = _lanesPerDepth[depth];
            for (int lane = 0; lane < laneCount; lane++)
            {
                Button button = _nodeButtons[(depth, lane)];
                if (!button.Disabled)
                {
                    _selectedLaneByDepth[depth] = lane;
                    EmitSignal(SignalName.NodeSelected, depth, lane);
                    return true;
                }
            }
        }

        return false;
    }

    public void UpdateNodeStates(int currentFloorIndex)
    {
        if (currentFloorIndex <= 0)
        {
            _selectedLaneByDepth.Clear();
        }

        for (int depth = 0; depth < _lanesPerDepth.Length; depth++)
        {
            int laneCount = _lanesPerDepth[depth];
            for (int lane = 0; lane < laneCount; lane++)
            {
                MapNodeState state;
                if (depth < currentFloorIndex)
                {
                    state = MapNodeState.Completed;
                }
                else if (depth == currentFloorIndex && IsLaneReachable(depth, lane))
                {
                    state = MapNodeState.Current;
                }
                else
                {
                    state = MapNodeState.Locked;
                }
                ApplyNodeVisualState(depth, lane, state);
            }
        }
    }

    private void BuildMapUi()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        ConfigureForcedShopLanes();

        var bg = new ColorRect
        {
            Color = new Color(0.08f, 0.08f, 0.12f),
            Size = viewport,
            CustomMinimumSize = viewport,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(bg);

        var title = new Label
        {
            Text = "Choose your route",
            Position = new Vector2(30, viewport.Y * 0.10f),
            Size = new Vector2(280, 40),
            CustomMinimumSize = new Vector2(280, 40),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(title);

        for (int depth = 0; depth < _lanesPerDepth.Length; depth++)
        {
            int laneCount = _lanesPerDepth[depth];
            for (int lane = 0; lane < laneCount; lane++)
            {
                bool hasForcedShop = _forcedShopLaneByDepth.TryGetValue(depth, out int forcedShopLane);
                bool isForcedShop = hasForcedShop && lane == forcedShopLane;
                MapNodeKind kind = isForcedShop
                    ? MapNodeKind.Shop
                    : RollNodeKind(depth, allowShop: !hasForcedShop);
                _nodeKinds[(depth, lane)] = kind;
                var button = new Button
                {
                    Text = GetNodeBaseText(depth, lane),
                    Size = _nodeSize,
                    CustomMinimumSize = _nodeSize,
                    MouseFilter = MouseFilterEnum.Stop
                };

                float yBottom = viewport.Y * 0.82f;
                float spacingY = viewport.Y * 0.62f / Mathf.Max(1, _lanesPerDepth.Length - 1);
                float y = yBottom - depth * spacingY;
                float xCenter = viewport.X * 0.5f;
                float xSpread = 170f;
                float x = xCenter + (lane - (laneCount - 1) / 2.0f) * xSpread;
                button.Position = new Vector2(x, y);
                button.Modulate = new Color(0.9f, 0.9f, 0.9f);

                int depthCapture = depth;
                int laneCapture = lane;
                button.Pressed += () =>
                {
                    if (!button.Disabled)
                    {
                        _selectedLaneByDepth[depthCapture] = laneCapture;
                        AudioManager.Instance?.PlaySFX("res://Audio/click.ogg");
                        EmitSignal(SignalName.NodeSelected, depthCapture, laneCapture);
                    }
                };
                AddChild(button);
                _nodeButtons[(depthCapture, laneCapture)] = button;
            }
        }

        UpdateNodeStates(0);
    }

    private void ApplyNodeVisualState(int depth, int lane, MapNodeState state)
    {
        Button button = _nodeButtons[(depth, lane)];
        string baseText = GetNodeBaseText(depth, lane);

        switch (state)
        {
            case MapNodeState.Completed:
                button.Text = $"[Done] {baseText}";
                button.Disabled = true;
                button.Modulate = new Color(0.35f, 0.35f, 0.38f, 1f);
                break;
            case MapNodeState.Current:
                button.Text = $"[HERE] {baseText}";
                button.Disabled = false;
                button.Modulate = new Color(0.74f, 0.70f, 0.58f, 1f);
                break;
            default:
                button.Text = $"[Lock] {baseText}";
                button.Disabled = true;
                button.Modulate = new Color(0.52f, 0.56f, 0.62f, 0.45f);
                break;
        }
    }

    public MapNodeKind GetNodeKind(int depth, int lane)
    {
        return _nodeKinds.TryGetValue((depth, lane), out MapNodeKind kind)
            ? kind
            : MapNodeKind.Combat;
    }

    private MapNodeKind RollNodeKind(int depth, bool allowShop = true)
    {
        if (depth == _lanesPerDepth.Length - 1)
        {
            return MapNodeKind.Boss;
        }

        // 第6层节点（索引5）固定火堆，Boss前一层固定火堆。
        if (depth == 5 || depth == _lanesPerDepth.Length - 2)
        {
            return MapNodeKind.Campfire;
        }

        int roll = _rng.RandiRange(0, 99);
        if (roll < 50)
        {
            return MapNodeKind.Combat;
        }
        if (roll < 70)
        {
            return MapNodeKind.Question;
        }
        if (roll < 85)
        {
            return MapNodeKind.Chest;
        }

        if (depth < 3)
        {
            return MapNodeKind.Combat;
        }

        return allowShop ? MapNodeKind.Shop : MapNodeKind.Combat;
    }

    private void ConfigureForcedShopLanes()
    {
        _forcedShopLaneByDepth.Clear();

        // 1-based floor 4 and 8 => depth index 3 and 7.
        int[] forcedShopDepths = { 3, 7 };
        foreach (int depth in forcedShopDepths)
        {
            if (depth < 0 || depth >= _lanesPerDepth.Length)
            {
                continue;
            }

            if (depth == _lanesPerDepth.Length - 1 || depth == 5 || depth == _lanesPerDepth.Length - 2)
            {
                continue;
            }

            int laneCount = _lanesPerDepth[depth];
            _forcedShopLaneByDepth[depth] = _rng.RandiRange(0, laneCount - 1);
        }
    }

    private bool IsLaneReachable(int depth, int lane)
    {
        if (depth <= 0)
        {
            return true;
        }

        if (!_selectedLaneByDepth.TryGetValue(depth - 1, out int previousLane))
        {
            return true;
        }

        int previousCount = _lanesPerDepth[depth - 1];
        int currentCount = _lanesPerDepth[depth];
        if (currentCount <= 1)
        {
            return lane == 0;
        }

        float normalized = previousCount <= 1
            ? 0.5f
            : previousLane / (float)(previousCount - 1);
        float projected = normalized * (currentCount - 1);
        int left = Mathf.Clamp(Mathf.FloorToInt(projected), 0, currentCount - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(projected), 0, currentCount - 1);
        if (left == right)
        {
            if (right < currentCount - 1)
            {
                right += 1;
            }
            else if (left > 0)
            {
                left -= 1;
            }
        }

        return lane == left || lane == right;
    }

    private string GetNodeBaseText(int depth, int lane)
    {
        MapNodeKind kind = GetNodeKind(depth, lane);
        return kind switch
        {
            MapNodeKind.Boss => "Boss",
            MapNodeKind.Combat => $"Fight {depth + 1}-{lane + 1}",
            MapNodeKind.Question => $"? {depth + 1}-{lane + 1}",
            MapNodeKind.Chest => $"Chest {depth + 1}-{lane + 1}",
            MapNodeKind.Shop => $"Shop {depth + 1}-{lane + 1}",
            MapNodeKind.Campfire => $"Rest {depth + 1}-{lane + 1}",
            _ => $"Node {depth + 1}-{lane + 1}"
        };
    }
}
