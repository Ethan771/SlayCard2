using Godot;

namespace SlayCard;

// 地图管理器：纯代码生成树状路线按钮。
public partial class MapManager : Control
{
    [Signal] public delegate void NodeSelectedEventHandler(int depth, int lane);

    private readonly Vector2 _nodeSize = new(120, 46);

    public override void _Ready()
    {
        Size = new Vector2(1280, 720);
        CustomMinimumSize = new Vector2(1280, 720);
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

    private void BuildMapUi()
    {
        var bg = new ColorRect
        {
            Color = new Color(0.08f, 0.08f, 0.12f),
            Size = new Vector2(1280, 720),
            CustomMinimumSize = new Vector2(1280, 720),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(bg);

        var title = new Label
        {
            Text = "Choose your route",
            Position = new Vector2(30, 20),
            Size = new Vector2(280, 40),
            CustomMinimumSize = new Vector2(280, 40),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(title);

        int[] lanesPerDepth = { 1, 2, 3, 2, 1 };
        for (int depth = 0; depth < lanesPerDepth.Length; depth++)
        {
            int laneCount = lanesPerDepth[depth];
            for (int lane = 0; lane < laneCount; lane++)
            {
                var button = new Button
                {
                    Text = depth == lanesPerDepth.Length - 1 ? "Boss" : $"Fight {depth + 1}-{lane + 1}",
                    Size = _nodeSize,
                    CustomMinimumSize = _nodeSize,
                    MouseFilter = MouseFilterEnum.Stop
                };

                float x = 220 + depth * 190;
                float yBase = 360;
                float y = yBase + (lane - (laneCount - 1) / 2.0f) * 120;
                button.Position = new Vector2(x, y);

                int depthCapture = depth;
                int laneCapture = lane;
                button.Pressed += () => EmitSignal(SignalName.NodeSelected, depthCapture, laneCapture);
                AddChild(button);
            }
        }
    }
}
