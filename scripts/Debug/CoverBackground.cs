using Godot;
using SpCardgame.Core;
using SpCardgame.UI;

namespace SpCardgame.DevTools;

/// <summary>
/// itch.io 커버 이미지의 배경입니다. 남보라 원형 그라데이션 위에 마름모 격자, 빛 번짐, 문양 네 개를 그립니다.
/// </summary>
public partial class CoverBackground : Control
{
    public override void _Draw()
    {
        var size = Size;
        var center = new Vector2(size.X / 2, size.Y * 0.56f);

        // 가운데가 밝은 원형 그라데이션을 동심원으로 겹쳐 그립니다.
        DrawRect(new Rect2(Vector2.Zero, size), UiTheme.TableEdge);
        float maxR = size.Length() * 0.62f;
        for (int i = 60; i >= 0; i--)
        {
            float t = i / 60f;
            DrawCircle(center, maxR * t, UiTheme.TableEdge.Lerp(UiTheme.TableCenter, 1f - t));
        }

        // 옅은 마름모 격자입니다.
        var line = new Color(UiTheme.Gold, 0.06f);
        float step = size.X / 14f;
        for (float x = -size.Y; x < size.X + size.Y; x += step)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x + size.Y, size.Y), line, 2f, true);
            DrawLine(new Vector2(x + size.Y, 0), new Vector2(x, size.Y), line, 2f, true);
        }

        // 카드 뒤의 빛 번짐입니다.
        for (int i = 20; i >= 1; i--)
        {
            DrawCircle(center, size.X * 0.03f * i, new Color(UiTheme.Gold, 0.012f));
        }

        // 네 귀퉁이 근처에 문양을 크게, 옅게 둡니다.
        var suits = new[] { CardColor.Red, CardColor.Yellow, CardColor.Green, CardColor.Blue };
        var spots = new[]
        {
            new Vector2(0.1f, 0.3f), new Vector2(0.9f, 0.3f), new Vector2(0.12f, 0.86f), new Vector2(0.88f, 0.86f),
        };
        for (int i = 0; i < 4; i++)
        {
            var color = UiTheme.CardColor(suits[i]);
            SuitIcons.Draw(this, suits[i], spots[i] * size, size.X * 0.13f, new Color(color, 0.35f), UiTheme.TableEdge);
        }
    }
}
