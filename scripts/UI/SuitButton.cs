using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 문양 고르기 버튼입니다. 문양 아이콘과 이름을 크게 그립니다.
/// </summary>
public partial class SuitButton : Button
{
    private CardColor _suit;

    /// <summary>Godot가 스크립트를 다시 불러올 때 필요한 기본 생성자입니다.</summary>
    public SuitButton() : this(CardColor.Red) { }

    public SuitButton(CardColor suit)
    {
        _suit = suit;
        var color = UiTheme.CardColor(suit);
        UiTheme.StyleButton(this, color.Darkened(0.55f), Colors.White, 18);
        AddThemeStyleboxOverride("normal", UiTheme.Box(color.Darkened(0.6f), color, 2, 14, 8));
        AddThemeStyleboxOverride("hover", UiTheme.Box(color.Darkened(0.4f), Colors.White, 3, 14, 8));
        AddThemeStyleboxOverride("pressed", UiTheme.Box(color.Darkened(0.3f), Colors.White, 3, 14, 8));
    }

    public override void _Draw()
    {
        var color = UiTheme.CardColor(_suit);
        var center = new Vector2(Size.X / 2, Size.Y * 0.42f);
        SuitIcons.Draw(this, _suit, center, Size.X * 0.5f, color, color.Darkened(0.6f));

        const int fontSize = 18;
        var pos = new Vector2(0, Size.Y - 14);
        DrawString(UiTheme.Bold, pos, Card.ColorName(_suit), HorizontalAlignment.Center, Size.X, fontSize, Colors.White);
    }
}
