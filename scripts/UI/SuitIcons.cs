using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 네 가지 문양(불꽃, 달빛, 숲, 물결) 아이콘을 벡터로 그립니다.
/// 이미지 파일 없이 어떤 크기에서도 선명하게 그릴 수 있습니다.
/// 반드시 대상 CanvasItem의 _Draw 안에서 호출해야 합니다.
/// </summary>
public static class SuitIcons
{
    public static void Draw(CanvasItem ci, CardColor suit, Vector2 center, float size, Color color, Color background)
    {
        switch (suit)
        {
            case CardColor.Red:
                DrawFlame(ci, center, size, color);
                break;
            case CardColor.Yellow:
                DrawMoon(ci, center, size, color, background);
                break;
            case CardColor.Green:
                DrawLeaf(ci, center, size, color, background);
                break;
            case CardColor.Blue:
                DrawDrop(ci, center, size, color, background);
                break;
            default:
                DrawStar(ci, center, size, color);
                break;
        }
    }

    /// <summary>아래는 둥글고 위는 뾰족한 불꽃입니다. 안쪽에 밝은 불꽃을 한 겹 더 그립니다.</summary>
    private static void DrawFlame(CanvasItem ci, Vector2 c, float s, Color color)
    {
        ci.DrawColoredPolygon(FlameShape(c, s * 0.5f), color);
        ci.DrawColoredPolygon(FlameShape(c + new Vector2(0, s * 0.12f), s * 0.26f), color.Lightened(0.45f));
    }

    private static Vector2[] FlameShape(Vector2 c, float r)
    {
        var points = new System.Collections.Generic.List<Vector2>();
        var baseCenter = c + new Vector2(0, r * 0.35f);

        // 아래쪽 반원입니다.
        for (int i = 0; i <= 12; i++)
        {
            float a = Mathf.Pi * i / 12f;
            points.Add(baseCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.75f);
        }

        // 왼쪽 옆면을 곡선으로 올려서 꼭대기로 갑니다.
        var left = baseCenter + new Vector2(-r * 0.75f, 0);
        var tip = c + new Vector2(r * 0.1f, -r * 1.25f);
        for (int i = 1; i <= 10; i++)
        {
            float t = i / 10f;
            points.Add(Bezier(left, c + new Vector2(-r * 0.9f, -r * 0.7f), tip, t));
        }

        // 오른쪽 옆면을 따라 내려옵니다.
        var right = baseCenter + new Vector2(r * 0.75f, 0);
        for (int i = 1; i < 10; i++)
        {
            float t = i / 10f;
            points.Add(Bezier(tip, c + new Vector2(r * 0.55f, -r * 0.35f), right, t));
        }

        return points.ToArray();
    }

    /// <summary>
    /// 초승달입니다. 큰 원에서 작은 원을 뺀 모양을 다각형으로 직접 만들어서, 어떤 배경 위에서도 깔끔하게 보입니다.
    /// </summary>
    private static void DrawMoon(CanvasItem ci, Vector2 c, float s, Color color, Color background)
    {
        float r = s * 0.46f;
        var offset = new Vector2(r * 0.42f, -r * 0.28f);
        float innerR = r * 0.82f;
        var innerCenter = c + offset;
        float baseAngle = offset.Angle();
        const int steps = 48;

        var points = new System.Collections.Generic.List<Vector2>();

        // 바깥 원에서 안쪽 원에 가려지지 않는 부분입니다.
        for (int i = 0; i <= steps; i++)
        {
            float a = baseAngle + Mathf.Tau * i / steps;
            var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            if (p.DistanceTo(innerCenter) >= innerR)
            {
                points.Add(p);
            }
        }

        // 안쪽 원 중에서 바깥 원 안에 들어오는 부분을 거꾸로 이어서 닫습니다.
        var inner = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i <= steps; i++)
        {
            float a = baseAngle + Mathf.Tau * i / steps;
            var p = innerCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * innerR;
            if (p.DistanceTo(c) <= r)
            {
                inner.Add(p);
            }
        }

        inner.Reverse();
        points.AddRange(inner);

        if (points.Count >= 3)
        {
            ci.DrawColoredPolygon(points.ToArray(), color);
        }

        ci.DrawCircle(c + new Vector2(r * 0.55f, r * 0.45f), r * 0.12f, color);
    }

    /// <summary>양쪽이 뾰족한 잎사귀에 잎맥 한 줄을 그립니다.</summary>
    private static void DrawLeaf(CanvasItem ci, Vector2 c, float s, Color color, Color background)
    {
        float h = s * 0.95f;
        float w = s * 0.34f;
        var rotation = Transform2D.Identity.Rotated(-0.6f);
        var points = new System.Collections.Generic.List<Vector2>();

        for (int i = 0; i <= 14; i++)
        {
            float t = i / 14f;
            points.Add(c + rotation * new Vector2(-w * Mathf.Sin(Mathf.Pi * t), -h / 2 + h * t));
        }

        for (int i = 13; i >= 1; i--)
        {
            float t = i / 14f;
            points.Add(c + rotation * new Vector2(w * Mathf.Sin(Mathf.Pi * t), -h / 2 + h * t));
        }

        ci.DrawColoredPolygon(points.ToArray(), color);
        ci.DrawLine(c + rotation * new Vector2(0, -h * 0.38f), c + rotation * new Vector2(0, h * 0.55f),
            background, Mathf.Max(1.5f, s * 0.05f), true);
    }

    /// <summary>위가 뾰족한 물방울입니다. 반사광을 작게 넣습니다.</summary>
    private static void DrawDrop(CanvasItem ci, Vector2 c, float s, Color color, Color background)
    {
        float r = s * 0.36f;
        var body = c + new Vector2(0, r * 0.45f);
        var tip = c + new Vector2(0, -r * 1.55f);
        var points = new System.Collections.Generic.List<Vector2>();

        for (int i = 0; i <= 16; i++)
        {
            float a = -0.25f + (Mathf.Pi + 0.5f) * i / 16f;
            points.Add(body + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }

        var leftStart = points[^1];
        for (int i = 1; i <= 8; i++)
        {
            points.Add(Bezier(leftStart, c + new Vector2(-r * 0.55f, -r * 0.7f), tip, i / 8f));
        }

        var rightEnd = points[0];
        for (int i = 1; i < 8; i++)
        {
            points.Add(Bezier(tip, c + new Vector2(r * 0.55f, -r * 0.7f), rightEnd, i / 8f));
        }

        ci.DrawColoredPolygon(points.ToArray(), color);
        ci.DrawArc(body, r * 0.6f, Mathf.Pi * 1.05f, Mathf.Pi * 1.45f, 8, background with { A = 0.7f },
            Mathf.Max(1.5f, s * 0.05f), true);
    }

    /// <summary>프리즘 카드에 쓰는 네 갈래 별입니다.</summary>
    public static void DrawStar(CanvasItem ci, Vector2 c, float s, Color color)
    {
        float outer = s * 0.5f;
        float inner = s * 0.16f;
        var points = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            float a = -Mathf.Pi / 2 + i * Mathf.Pi / 4;
            float r = i % 2 == 0 ? outer : inner;
            points[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        ci.DrawColoredPolygon(points, color);
    }

    private static Vector2 Bezier(Vector2 a, Vector2 control, Vector2 b, float t)
    {
        var p0 = a.Lerp(control, t);
        var p1 = control.Lerp(b, t);
        return p0.Lerp(p1, t);
    }
}
