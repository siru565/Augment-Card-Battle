using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 원형 아바타입니다. 차례인 플레이어는 금색 테두리가 됩니다.
/// </summary>
public partial class AvatarView : Control
{
    private string _letter;
    private bool _active;
    private float _t;

    public string Letter
    {
        get => _letter;
        set { _letter = value; QueueRedraw(); }
    }

    public bool Active
    {
        get => _active;
        set { _active = value; QueueRedraw(); }
    }

    /// <summary>Godot가 스크립트를 다시 불러올 때 필요한 기본 생성자입니다.</summary>
    public AvatarView() : this("?") { }

    public AvatarView(string letter)
    {
        _letter = letter;
        CustomMinimumSize = new Vector2(56, 56);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (_active)
        {
            _t += (float)delta * 4f;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var center = Size / 2;
        float r = Mathf.Min(Size.X, Size.Y) / 2 - 3;
        DrawCircle(center, r, Color.FromHtml("#34405a"));
        var ring = _active ? UiTheme.Gold with { A = 0.7f + 0.3f * Mathf.Sin(_t) } : new Color(1, 1, 1, 0.25f);
        DrawArc(center, r, 0, Mathf.Tau, 48, ring, _active ? 4f : 2f, true);

        var font = UiTheme.Bold;
        const int size = 24;
        var pos = new Vector2(0, center.Y + font.GetAscent(size) / 2 - 3);
        DrawString(font, pos, _letter, HorizontalAlignment.Center, Size.X, size, Colors.White);
    }
}
