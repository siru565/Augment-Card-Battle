using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 상대 손패 장수를 카드 뒷면 부채꼴로 보여 줍니다.
/// </summary>
public partial class FanView : Control
{
    private int _count;

    public int Count
    {
        get => _count;
        set { _count = value; QueueRedraw(); }
    }

    public FanView()
    {
        CustomMinimumSize = new Vector2(0, 30);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        int shown = Mathf.Min(_count, 14);
        var cardSize = new Vector2(20, 28);
        for (int i = 0; i < shown; i++)
        {
            var rect = new Rect2(new Vector2(i * 11, 1), cardSize);
            DrawStyleBox(UiTheme.Box(Color.FromHtml("#161633"), Color.FromHtml("#c9a24a"), 2, 4, 0), rect);
            SuitIcons.DrawStar(this, rect.GetCenter(), 9, Color.FromHtml("#c9a24a"));
        }

        if (_count > shown)
        {
            DrawString(UiTheme.Bold, new Vector2(shown * 11 + 16, 24), $"+{_count - shown}",
                HorizontalAlignment.Left, -1, 14, UiTheme.TextDim);
        }
    }
}
