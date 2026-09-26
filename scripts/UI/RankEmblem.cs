using Godot;

namespace SpCardgame.UI;

/// <summary>
/// 결과 화면 위쪽의 메달 문장입니다. 순위 색 원판 + 별 + 순위 숫자를 그리고,
/// 1등이면 뒤에서 빛줄기가 천천히 돌고 테두리가 반짝입니다.
/// </summary>
public partial class RankEmblem : Control
{
    private int _rank;
    private float _t;

    public int Rank
    {
        get => _rank;
        set { _rank = value; QueueRedraw(); }
    }

    public RankEmblem()
    {
        CustomMinimumSize = new Vector2(150, 150);
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
    }

    /// <summary>순위별 메달 색입니다. (1 금, 2 은, 3 동, 그 밖은 회색)</summary>
    public static Color MedalColor(int rank) => rank switch
    {
        1 => Color.FromHtml("#f5c451"),
        2 => Color.FromHtml("#cfd6e0"),
        3 => Color.FromHtml("#d38b52"),
        _ => Color.FromHtml("#6f7784"),
    };

    public override void _Process(double delta)
    {
        _t += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var center = Size / 2;
        float r = Mathf.Min(Size.X, Size.Y) * 0.34f;
        var medal = MedalColor(_rank);

        // 1등은 뒤에서 빛줄기가 돕니다.
        if (_rank == 1)
        {
            const int rays = 14;
            for (int i = 0; i < rays; i++)
            {
                float a = _t * 0.25f + i * Mathf.Tau / rays;
                var points = new[]
                {
                    center,
                    center + new Vector2(Mathf.Cos(a - 0.09f), Mathf.Sin(a - 0.09f)) * r * 2.2f,
                    center + new Vector2(Mathf.Cos(a + 0.09f), Mathf.Sin(a + 0.09f)) * r * 2.2f,
                };
                DrawPolygon(points, new[] { new Color(medal, 0.35f), new Color(medal, 0f), new Color(medal, 0f) });
            }
        }

        // 은은한 후광
        for (int i = 6; i >= 1; i--)
        {
            DrawCircle(center, r + i * 4, new Color(medal, 0.035f));
        }

        // 메달 원판: 바깥 링 → 안쪽 판 → 안쪽 링
        DrawCircle(center, r, medal.Darkened(0.35f));
        DrawCircle(center, r * 0.84f, medal.Darkened(0.62f));
        DrawArc(center, r, 0, Mathf.Tau, 64, medal.Lightened(0.2f), 3f, true);
        DrawArc(center, r * 0.84f, 0, Mathf.Tau, 64, new Color(medal, 0.8f), 1.5f, true);

        // 테두리를 따라 도는 반짝임
        float spark = _t * 1.4f;
        DrawArc(center, r, spark, spark + 0.7f, 16, new Color(1, 1, 1, 0.75f), 3f, true);

        // 가운데 별과 순위 숫자
        SuitIcons.DrawStar(this, center + new Vector2(0, -r * 0.3f), r * 0.55f, new Color(medal, 0.9f));
        var font = UiTheme.Title;
        int size = (int)(r * 0.62f);
        string text = _rank > 0 ? _rank.ToString() : "-";
        var pos = new Vector2(0, center.Y + r * 0.52f);
        DrawStringOutline(font, pos, text, HorizontalAlignment.Center, Size.X, size, 6, new Color(0, 0, 0, 0.6f));
        DrawString(font, pos, text, HorizontalAlignment.Center, Size.X, size, medal.Lightened(0.35f));
    }
}
