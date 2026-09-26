using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 화면 전체에서 함께 쓰는 색, 폰트, 스타일입니다. 디자인을 바꿀 때는 여기만 고치면 됩니다.
/// </summary>
public static class UiTheme
{
    // 테이블과 패널 색입니다.
    public static readonly Color TableCenter = Color.FromHtml("#2c2660");
    public static readonly Color TableEdge = Color.FromHtml("#0b0a1c");
    public static readonly Color Panel = new(0.06f, 0.09f, 0.12f, 0.82f);
    public static readonly Color PanelBorder = new(1f, 1f, 1f, 0.12f);
    public static readonly Color Gold = Color.FromHtml("#ffd54a");
    public static readonly Color TextDim = Color.FromHtml("#a9b4c2");
    public static readonly Color Danger = Color.FromHtml("#ff6b6b");

    // 등급 색입니다.
    public static readonly Color Silver = Color.FromHtml("#c9d3e3");
    public static readonly Color GoldTier = Color.FromHtml("#ffc940");
    public static readonly Color Prism = Color.FromHtml("#e39bff");

    private static Font? _regular;
    private static Font? _bold;

    /// <summary>한글이 확실히 나오도록 시스템 폰트(맑은 고딕 등)를 씁니다.</summary>
    public static Font Regular => _regular ??= MakeFont(500);

    public static Font Bold => _bold ??= MakeFont(800);

    private static Font MakeFont(int weight) => new SystemFont
    {
        FontNames = new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "sans-serif" },
        FontWeight = weight,
        Antialiasing = TextServer.FontAntialiasing.Gray,
    };

    public static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFont = Regular, DefaultFontSize = 15 };
        theme.SetColor("font_color", "Label", Colors.White);
        return theme;
    }

    public static Color CardColor(CardColor color) => color switch
    {
        Core.CardColor.Red => Color.FromHtml("#ff6a3d"),
        Core.CardColor.Yellow => Color.FromHtml("#a070ff"),
        Core.CardColor.Green => Color.FromHtml("#36c47c"),
        Core.CardColor.Blue => Color.FromHtml("#22b4dc"),
        _ => Color.FromHtml("#e8c35a"),
    };

    public static Color TierColor(AugmentTier tier) => tier switch
    {
        AugmentTier.Silver => Silver,
        AugmentTier.Gold => GoldTier,
        _ => Prism,
    };

    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 1, int radius = 12, int margin = 10)
    {
        var style = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(radius);
        style.SetContentMarginAll(margin);
        return style;
    }

    /// <summary>둥근 버튼 스타일을 한 번에 적용합니다.</summary>
    public static void StyleButton(Button button, Color bg, Color fg, int fontSize = 16)
    {
        button.AddThemeStyleboxOverride("normal", Box(bg, bg.Lightened(0.25f), 1, 10, 8));
        button.AddThemeStyleboxOverride("hover", Box(bg.Lightened(0.15f), Colors.White, 2, 10, 8));
        button.AddThemeStyleboxOverride("pressed", Box(bg.Darkened(0.15f), Colors.White, 2, 10, 8));
        button.AddThemeStyleboxOverride("disabled", Box(bg.Darkened(0.5f) with { A = 0.6f }, PanelBorder, 1, 10, 8));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeColorOverride("font_color", fg);
        button.AddThemeColorOverride("font_hover_color", fg);
        button.AddThemeColorOverride("font_pressed_color", fg);
        button.AddThemeColorOverride("font_disabled_color", new Color(fg, 0.35f));
        button.AddThemeFontOverride("font", Bold);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.FocusMode = Control.FocusModeEnum.None;
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
    }

    public static Label MakeLabel(string text, int size, Color color, bool bold = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        if (bold)
        {
            label.AddThemeFontOverride("font", Bold);
        }

        return label;
    }
}
