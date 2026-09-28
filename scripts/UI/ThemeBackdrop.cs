using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 판 테마에 맞게 테이블 천의 색을 바꾸고, 그 위에 테마 무늬(assets/shaders/theme_table.gdshader)를 깝니다.
/// 게임 내내 깔려 있어서 지금 어떤 테마인지 한눈에 알 수 있습니다.
/// </summary>
public partial class ThemeBackdrop : ColorRect
{
    private sealed record Look(Color Felt, Color Accent, Color Accent2);

    private static readonly Color DefaultFelt = new(0.36f, 0.62f, 0.5f);

    private TextureRect? _felt;
    private ShaderMaterial _material = null!;
    private ThemeId _theme = ThemeId.None;
    private Tween? _tween;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Color = Colors.White;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/theme_table.gdshader") };
        Material = _material;
        _material.SetShaderParameter("mode", 0);
        _material.SetShaderParameter("strength", 0f);
    }

    /// <summary>천(Felt) 노드를 알려 줍니다. 테마에 맞춰 이 천의 색을 바꿉니다.</summary>
    public void Attach(TextureRect felt) => _felt = felt;

    private static Look LookOf(ThemeId theme) => theme switch
    {
        ThemeId.Arcade => new Look(new Color(0.42f, 0.32f, 0.62f), Color.FromHtml("#ff4fd8"), Color.FromHtml("#3ee6ff")),
        ThemeId.Bingo => new Look(new Color(0.72f, 0.38f, 0.32f), Color.FromHtml("#ffd166"), Color.FromHtml("#ff8fab")),
        ThemeId.Race => new Look(new Color(0.42f, 0.44f, 0.48f), Colors.White, Color.FromHtml("#ffd166")),
        ThemeId.Territory => new Look(new Color(0.55f, 0.52f, 0.36f), Color.FromHtml("#f3e1b0"), Color.FromHtml("#ffd166")),
        ThemeId.Mission => new Look(new Color(0.24f, 0.36f, 0.52f), Color.FromHtml("#5dffb0"), Color.FromHtml("#5dffb0")),
        ThemeId.Boss => new Look(new Color(0.5f, 0.22f, 0.24f), Color.FromHtml("#ff5a1f"), Color.FromHtml("#ffc14d")),
        ThemeId.Bomb => new Look(new Color(0.34f, 0.34f, 0.36f), Color.FromHtml("#f5c518"), Color.FromHtml("#ff8a3d")),
        _ => new Look(DefaultFelt, Colors.White, Colors.White),
    };

    private static int ModeOf(ThemeId theme) => theme switch
    {
        ThemeId.Arcade => 1,
        ThemeId.Bingo => 2,
        ThemeId.Race => 3,
        ThemeId.Territory => 4,
        ThemeId.Mission => 5,
        ThemeId.Boss => 6,
        ThemeId.Bomb => 7,
        _ => 0,
    };

    /// <summary>테마를 바꿉니다. 같은 테마면 아무것도 하지 않습니다. danger는 시한폭탄이 곧 터질 때 켭니다.</summary>
    public void SetTheme(ThemeId theme, bool danger = false)
    {
        _material.SetShaderParameter("danger", danger ? 1f : 0f);
        if (theme == _theme)
        {
            return;
        }

        _theme = theme;
        var look = LookOf(theme);
        _material.SetShaderParameter("mode", ModeOf(theme));
        _material.SetShaderParameter("accent", look.Accent);
        _material.SetShaderParameter("accent2", look.Accent2);
        _material.SetShaderParameter("strength", 0f);

        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenMethod(Callable.From<float>(v => _material.SetShaderParameter("strength", v)), 0f, 1f, 1.2f);
        if (_felt != null)
        {
            _tween.TweenProperty(_felt, "modulate", look.Felt, 1.0f);
        }
    }

    public override void _Process(double delta)
    {
        _material.SetShaderParameter("rect_size", Size);
    }
}
