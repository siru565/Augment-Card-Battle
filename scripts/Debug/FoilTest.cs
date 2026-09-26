using Godot;
using SpCardgame.Core;
using SpCardgame.UI;

namespace SpCardgame.Debug;

/// <summary>
/// 개발용: 프리즘·특수 카드를 크게 늘어놓고 빛 효과(card_foil 셰이더)를 시간 간격을 두고 두 번 캡처합니다.
/// 결과는 tools/shots/foil_0.png, foil_1.png입니다.
/// </summary>
public partial class FoilTest : Control
{
    private double _t;
    private int _shots;

    public override void _Ready()
    {
        Theme = UiTheme.LoadTheme();
        AddChild(new ColorRect { Color = new Color("#1b2a24"), AnchorRight = 1, AnchorBottom = 1 });
        var cards = new[]
        {
            new Card(1, CardColor.Wild, CardKind.Wild, -1),
            new Card(2, CardColor.Wild, CardKind.WildDrawFour, -1),
            new Card(3, CardColor.Green, CardKind.Swap, -1),
            new Card(4, CardColor.Red, CardKind.Number, 7),
        };
        for (int i = 0; i < cards.Length; i++)
        {
            AddChild(new CardView { Card = cards[i], Position = new Vector2(40 + i * 230, 40), Size = new Vector2(200, 290), MouseFilter = MouseFilterEnum.Ignore });
        }
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_t > 1.0 + _shots * 0.8 && _shots < 2)
        {
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://tools/shots/foil_{_shots}.png"));
            _shots++;
        }

        if (_shots >= 2)
        {
            GetTree().Quit();
        }
    }
}
