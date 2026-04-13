using Godot;

namespace SlayCard;

// 特效管理器：飘字伤害与屏幕震动。
public partial class VfxManager : CanvasLayer
{
    private Node2D _shakeRoot = null!;

    public override void _Ready()
    {
        _shakeRoot = new Node2D();
        AddChild(_shakeRoot);
    }

    public void PlayFloatingText(Vector2 worldPosition, string text, Color color)
    {
        var label = new Label
        {
            Text = text,
            Modulate = color,
            Position = worldPosition,
            Size = new Vector2(120, 30),
            CustomMinimumSize = new Vector2(120, 30),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        AddChild(label);

        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position:y", label.Position.Y - 40, 0.45f);
        tween.TweenProperty(label, "modulate:a", 0f, 0.45f);
        tween.Finished += () => label.QueueFree();
    }

    public void ShakeScreen(float strength = 8f, float duration = 0.15f)
    {
        var tween = CreateTween();
        tween.SetLoops(4);
        tween.TweenProperty(_shakeRoot, "position", new Vector2(strength, 0), duration / 8f);
        tween.TweenProperty(_shakeRoot, "position", new Vector2(-strength, 0), duration / 8f);
        tween.Finished += () => _shakeRoot.Position = Vector2.Zero;
    }
}
