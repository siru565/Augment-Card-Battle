using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 버린 더미 뒤에 그리는 원형 링입니다. 현재 색과 진행 방향(화살표)을 한눈에 보여 줍니다.
/// </summary>
public partial class DirectionRing : Control
{
    private Color _color = Colors.White;
    private int _direction = 1;
    private float _angle;

    public DirectionRing()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Set(Color color, int direction)
    {
        _color = color;
        _direction = direction;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _angle += (float)delta * 0.6f * _direction;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var center = Size / 2;
        float r = Mathf.Min(Size.X, Size.Y) / 2 - 8;

        DrawCircle(center, r + 6, new Color(_color, 0.12f));
        for (int i = 0; i < 3; i++)
        {
            float start = _angle + i * Mathf.Tau / 3;
            float end = start + Mathf.Tau / 3 * 0.72f;
            DrawArc(center, r, start, end, 32, _color, 6f, true);

            // 호 끝에 진행 방향 화살촉을 그립니다.
            float tipAngle = _direction > 0 ? end : start;
            var tip = center + new Vector2(Mathf.Cos(tipAngle), Mathf.Sin(tipAngle)) * r;
            var tangent = new Vector2(-Mathf.Sin(tipAngle), Mathf.Cos(tipAngle)) * _direction;
            var normal = new Vector2(Mathf.Cos(tipAngle), Mathf.Sin(tipAngle));
            DrawColoredPolygon(new[]
            {
                tip + tangent * 14,
                tip - tangent * 2 + normal * 10,
                tip - tangent * 2 - normal * 10,
            }, _color);
        }
    }
}
