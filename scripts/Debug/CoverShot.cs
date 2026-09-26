using System.Threading.Tasks;
using Godot;
using SpCardgame.Core;
using SpCardgame.UI;

namespace SpCardgame.DevTools;

/// <summary>
/// itch.io 커버 이미지를 게임 속 카드 그림 그대로 만들어 저장하는 개발 도구입니다.
/// 1260x1000(고해상도)과 630x500(itch 권장 크기) 두 장을 tools/cover 폴더에 저장합니다.
/// </summary>
public partial class CoverShot : Node
{
    private static readonly Vector2I CanvasSize = new(1260, 1000);

    public override async void _Ready()
    {
        var viewport = new SubViewport
        {
            Size = CanvasSize,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);

        var root = new Control { Size = CanvasSize, Theme = UiTheme.BuildTheme() };
        viewport.AddChild(root);

        root.AddChild(new CoverBackground { Size = CanvasSize });
        AddCards(root);
        AddTitle(root);

        for (int i = 0; i < 4; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        }

        string folder = ProjectSettings.GlobalizePath("res://tools/cover");
        DirAccess.MakeDirRecursiveAbsolute(folder);

        var image = viewport.GetTexture().GetImage();
        image.SavePng($"{folder}/cover_1260x1000.png");
        image.Resize(630, 500, Image.Interpolation.Lanczos);
        image.SavePng($"{folder}/cover_630x500.png");

        GD.Print($"[커버] 저장 완료: {folder}");
        GetTree().Quit();
    }

    /// <summary>카드 다섯 장을 부채꼴로 펼칩니다. 가운데는 각성 카드입니다.</summary>
    private static void AddCards(Control root)
    {
        Card[] cards =
        {
            new(1, CardColor.Red, CardKind.Number, 7),
            new(2, CardColor.Green, CardKind.Skip),
            new(3, CardColor.Yellow, CardKind.Awaken),
            new(4, CardColor.Blue, CardKind.Number, 3),
            new(5, CardColor.Wild, CardKind.WildDrawFour),
        };

        var cardSize = new Vector2(230, 332);
        var pivot = new Vector2(CanvasSize.X / 2f, 1130);

        for (int i = 0; i < cards.Length; i++)
        {
            float angle = (i - 2) * 0.2f;
            var view = new CardView
            {
                Card = cards[i],
                Size = cardSize,
                PivotOffset = new Vector2(cardSize.X / 2, cardSize.Y + 360),
                Rotation = angle,
                Selected = i == 2,
            };
            view.Position = pivot - view.PivotOffset + new Vector2(0, -20);
            if (i == 2)
            {
                view.Position += new Vector2(0, -40);
                view.ZIndex = 1;
            }

            root.AddChild(view);
        }
    }

    private static void AddTitle(Control root)
    {
        var box = new VBoxContainer
        {
            Position = new Vector2(0, 52),
            Size = new Vector2(CanvasSize.X, 260),
            Alignment = BoxContainer.AlignmentMode.Begin,
        };
        box.AddThemeConstantOverride("separation", 4);
        root.AddChild(box);

        var english = UiTheme.MakeLabel("A U G M E N T   C A R D   B A T T L E", 34, new Color(UiTheme.Gold, 0.85f), bold: true);
        english.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(english);

        var title = UiTheme.MakeLabel("증강 카드 배틀", 132, UiTheme.Gold, bold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeConstantOverride("outline_size", 22);
        title.AddThemeColorOverride("font_outline_color", new Color("1a1030"));
        title.AddThemeConstantOverride("shadow_offset_y", 8);
        title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        box.AddChild(title);

        var tagline = UiTheme.MakeLabel("4 문양 · 증강 · 각성   |   최대 4인 온라인 멀티", 34, Colors.White, bold: true);
        tagline.HorizontalAlignment = HorizontalAlignment.Center;
        tagline.AddThemeConstantOverride("outline_size", 10);
        tagline.AddThemeColorOverride("font_outline_color", new Color("1a1030"));
        box.AddChild(tagline);
    }
}
