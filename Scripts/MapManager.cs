using Godot;

namespace SlayCard;

public enum MapNodeState
{
    Locked,
    Current,
    Completed
}

// 地图管理器：纯代码生成树状路线按钮。
public partial class MapManager : Control
{
    [Signal] public delegate void NodeSelectedEventHandler(int depth, int lane);

    private readonly Vector2 _nodeSize = new(120, 46);
    private readonly int[] _lanesPerDepth = { 1, 2, 3, 2, 1 };
    private readonly System.Collections.Generic.Dictionary<(int depth, int lane), Button> _nodeButtons = new();

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
                    EmitSignal(SignalName.NodeSelected, depth, lane);
                    return true;
                }
            }
        }

        return false;
    }

    public void UpdateNodeStates(int currentFloorIndex)
    {
        for (int depth = 0; depth < _lanesPerDepth.Length; depth++)
        {
            int laneCount = _lanesPerDepth[depth];
            for (int lane = 0; lane < laneCount; lane++)
            {
                MapNodeState state = depth < currentFloorIndex
                    ? MapNodeState.Completed
                    : depth == currentFloorIndex
                        ? MapNodeState.Current
                        : MapNodeState.Locked;
                ApplyNodeVisualState(depth, lane, state);
            }
        }
    }

    private void BuildMapUi()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

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
                var button = new Button
                {
                    Text = depth == _lanesPerDepth.Length - 1 ? "Boss" : $"Fight {depth + 1}-{lane + 1}",
                    Size = _nodeSize,
                    CustomMinimumSize = _nodeSize,
                    MouseFilter = MouseFilterEnum.Stop
                };

                float x = 220 + depth * 190;
                float yBase = viewport.Y * 0.5f;
                float y = yBase + (lane - (laneCount - 1) / 2.0f) * 120;
                button.Position = new Vector2(x, y);
                button.Modulate = new Color(0.9f, 0.9f, 0.9f);

                int depthCapture = depth;
                int laneCapture = lane;
                button.Pressed += () =>
                {
                    if (!button.Disabled)
                    {
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
        string baseText = depth == _lanesPerDepth.Length - 1 ? "Boss" : $"Fight {depth + 1}-{lane + 1}";

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
}
