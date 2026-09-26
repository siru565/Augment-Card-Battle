using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 능력 카드 가운데의 문장입니다. 등급 색 육각형 안에 빛나는 별을 그립니다.
/// </summary>
public partial class AbilityEmblem : Control
{
    private AugmentTier _tier;

    /// <summary>문장 색을 정하는 등급입니다.</summary>
    public AugmentTier Tier
    {
        get => _tier;
        set { _tier = value; QueueRedraw(); }
    }
    private float _t;

    /// <summary>Godot가 스크립트를 다시 불러올 때 필요한 기본 생성자입니다.</summary>
    public AbilityEmblem() : this(AugmentTier.Silver) { }

    public AbilityEmblem(AugmentTier tier)
    {
        _tier = tier;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var color = UiTheme.TierColor(_tier);
        var center = Size / 2;
        float r = Mathf.Min(Size.X, Size.Y) * 0.42f;

        var hex = new Vector2[7];
        for (int i = 0; i < 6; i++)
        {
            float a = -Mathf.Pi / 2 + i * Mathf.Tau / 6 + _t * 0.3f;
            hex[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        hex[6] = hex[0];
        DrawColoredPolygon(hex[..6], new Color(color, 0.15f));
        DrawPolyline(hex, color, 2.5f, true);

        float pulse = 1f + 0.08f * Mathf.Sin(_t * 3f);
        SuitIcons.DrawStar(this, center, r * 1.1f * pulse, color);
        DrawCircle(center, r * 0.12f, Colors.White);
    }
}
