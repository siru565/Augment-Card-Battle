using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 게임 방법 · 도감 창입니다. (scenes/screens/Guide.tscn)
/// 탭 세 개: 게임 방법(규칙 설명), 특수 증강 도감, 각성 능력 도감.
/// 도감은 내가 얻어 본 것(Collection)만 빛나고, 아직 못 얻은 것은 흐리게 보여 줍니다.
/// </summary>
public partial class GuideView : ColorRect
{
    private Button[] _tabs = Array.Empty<Button>();
    private Control[] _pages = Array.Empty<Control>();
    private GridContainer _augmentGrid = null!;
    private GridContainer _abilityGrid = null!;
    private Label _augmentProgress = null!;
    private Label _abilityProgress = null!;
    private VBoxContainer _howToBox = null!;
    private readonly List<(StyleBoxFlat Glow, float Phase)> _glows = new();
    private float _time;
    private bool _howToBuilt;

    private static Shader? _foilShader;

    private static Shader FoilShader => _foilShader ??= GD.Load<Shader>("res://assets/shaders/card_foil.gdshader");

    public override void _Ready()
    {
        _tabs = new[]
        {
            GetNode<Button>("%HowToTab"),
            GetNode<Button>("%AugmentTab"),
            GetNode<Button>("%AbilityTab"),
        };
        _pages = new[]
        {
            GetNode<Control>("%HowToPage"),
            GetNode<Control>("%AugmentPage"),
            GetNode<Control>("%AbilityPage"),
        };
        for (int i = 0; i < _tabs.Length; i++)
        {
            int index = i;
            _tabs[i].Pressed += () => SelectTab(index);
        }

        _augmentGrid = GetNode<GridContainer>("%AugmentGrid");
        _abilityGrid = GetNode<GridContainer>("%AbilityGrid");
        _augmentProgress = GetNode<Label>("%AugmentProgress");
        _abilityProgress = GetNode<Label>("%AbilityProgress");
        _howToBox = GetNode<VBoxContainer>("%HowToBox");
        GetNode<Button>("%CloseButton").Pressed += Close;
    }

    /// <summary>창을 엽니다. tab: 0 게임 방법, 1 특수 증강 도감, 2 각성 능력 도감</summary>
    public void Open(int tab = 0)
    {
        if (!_howToBuilt)
        {
            _howToBuilt = true;
            BuildHowTo();
        }

        // 도감은 열 때마다 새로 만듭니다. (게임 중에 새로 얻은 것이 바로 빛나도록)
        BuildCodex();
        Visible = true;
        SelectTab(tab);
    }

    public void Close() => Visible = false;

    /// <summary>게임 방법 페이지를 아래로 내립니다. (스크린샷 확인용)</summary>
    public void DebugScrollHowTo(int pixels) => GetNode<ScrollContainer>("%HowToPage").ScrollVertical = pixels;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event.IsActionPressed("ui_cancel"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible || _glows.Count == 0)
        {
            return;
        }

        // 얻은 카드 뒤의 빛을 천천히 숨 쉬듯 밝아졌다 어두워지게 합니다.
        _time += (float)delta;
        foreach (var (glow, phase) in _glows)
        {
            float t = 0.5f + 0.5f * Mathf.Sin(_time * 2.2f + phase);
            glow.ShadowColor = glow.ShadowColor with { A = 0.5f + 0.45f * t };
            glow.ShadowSize = (int)(14 + 12 * t);
        }
    }

    private void SelectTab(int index)
    {
        for (int i = 0; i < _tabs.Length; i++)
        {
            bool on = i == index;
            _tabs[i].ButtonPressed = on;
            UiTheme.StyleButton(_tabs[i], on ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Secondary);
            _pages[i].Visible = on;
        }
    }

    // ───────────── 도감 ─────────────

    private void BuildCodex()
    {
        _glows.Clear();
        Clear(_augmentGrid);
        Clear(_abilityGrid);

        // 등급(프리즘 → 골드 → 실버) 순서로 늘어놓습니다.
        var augments = SpecialAugments.All
            .Select(SpecialAugments.Info)
            .OrderByDescending(a => a.Tier)
            .ToList();
        foreach (var augment in augments)
        {
            bool owned = Collection.HasAugment(augment.Name);
            _augmentGrid.AddChild(Slot(AbilityCardView.Create(augment), augment.Tier, owned));
        }

        var abilities = AbilityPool.All.OrderByDescending(a => a.Tier).ToList();
        foreach (var ability in abilities)
        {
            bool owned = Collection.HasAbility(ability.Name);
            _abilityGrid.AddChild(Slot(AbilityCardView.Create(ability), ability.Tier, owned));
        }

        int gotAugments = augments.Count(a => Collection.HasAugment(a.Name));
        int gotAbilities = abilities.Count(a => Collection.HasAbility(a.Name));
        _augmentProgress.Text = $"획득 {gotAugments} / {augments.Count}";
        _abilityProgress.Text = $"발동 {gotAbilities} / {abilities.Count}";
    }

    /// <summary>
    /// 도감 칸 하나입니다. 얻은 카드는 뒤에 등급 색 빛(그림자)을 깔고, 못 얻은 카드는 흐리게 만듭니다.
    /// </summary>
    private Control Slot(AbilityCardView card, AugmentTier tier, bool owned)
    {
        var slot = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            slot.AddThemeConstantOverride($"margin_{side}", 12);
        }

        if (owned)
        {
            var color = UiTheme.TierColor(tier);
            var glow = new StyleBoxFlat
            {
                BgColor = Color.FromHtml("#14171d"),
                ShadowColor = color with { A = 0.6f },
                ShadowSize = 14,
            };
            glow.SetCornerRadiusAll(12);
            var back = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            back.AddThemeStyleboxOverride("panel", glow);
            slot.AddChild(back);
            _glows.Add((glow, _glows.Count * 0.7f));
        }

        card.SetCodex(owned);
        slot.AddChild(card);

        // 얻은 카드 위에는 카드 앞면과 같은 빛 셰이더를 씌웁니다. (프리즘은 무지개, 실버·골드는 광택이 지나감)
        if (owned)
        {
            var size = card.CustomMinimumSize;
            var shine = new ShaderMaterial { Shader = FoilShader };
            shine.SetShaderParameter("rect_size", size);
            shine.SetShaderParameter("corner_radius", 10f);
            shine.SetShaderParameter("seed", (float)GD.RandRange(0.0, 10.0));
            shine.SetShaderParameter("gloss_only", tier == AugmentTier.Prism ? 0f : 1f);
            shine.SetShaderParameter("strength", 0.12f);
            slot.AddChild(new ColorRect { Material = shine, MouseFilter = MouseFilterEnum.Ignore });
        }

        return slot;
    }

    private static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    // ───────────── 게임 방법 ─────────────

    private void BuildHowTo()
    {
        Section("목표",
            "손패를 가장 먼저 모두 내면 1등입니다.",
            "1등이 나와도 게임은 끝나지 않고, 남은 사람끼리 계속해서 2등, 3등을 가립니다.");

        Section("내 차례",
            "바닥에 놓인 카드와 문양이 같거나 숫자가 같은 카드를 한 장 냅니다.",
            "낼 카드가 없거나 내고 싶지 않으면 덱을 눌러 1장 뽑습니다. (Space 키) 뽑은 카드를 낼 수 있으면 바로 내도 되고, 아니면 '턴 넘기기'를 누릅니다.",
            "카드에 마우스를 올리면 그 카드의 효과 설명이 나옵니다.");

        Section("문양",
            "문양은 불꽃, 달빛, 숲, 물결 네 가지입니다. 프리즘 카드는 아무 때나 낼 수 있고, 낼 때 다음 문양을 고릅니다.");
        CardRow(
            new Card(-1, CardColor.Red, CardKind.Number, 7),
            new Card(-2, CardColor.Yellow, CardKind.Number, 7),
            new Card(-3, CardColor.Green, CardKind.Number, 3),
            new Card(-4, CardColor.Blue, CardKind.Number, 5),
            new Card(-5, CardColor.Wild, CardKind.Wild));

        Section("공격 카드 (+1 · +2 · +3 · +4)",
            "+카드를 맞으면 그만큼 카드를 받아야 합니다. 하지만 같은 +숫자나 프리즘 +4를 얹으면 공격을 합쳐서 다음 사람에게 넘길 수 있습니다.",
            "넘기지 못하면 자기 차례에 쌓인 장수를 모두 받습니다. 받을 때는 시스템이 대신 뽑지 않고, 덱을 눌러 직접 한 장씩 뽑습니다.");
        CardRow(
            new Card(-6, CardColor.Red, CardKind.DrawOne),
            new Card(-7, CardColor.Yellow, CardKind.DrawTwo),
            new Card(-8, CardColor.Green, CardKind.DrawThree),
            new Card(-9, CardColor.Wild, CardKind.WildDrawFour));

        Section("액션 · 특수 카드");
        foreach (var kind in new[]
                 {
                     CardKind.Skip, CardKind.Reverse, CardKind.Swap, CardKind.Seal,
                     CardKind.Copy, CardKind.Frenzy, CardKind.Awaken,
                 })
        {
            KindRow(new Card(-20 - (int)kind, CardColor.Blue, kind));
        }

        Section("각성",
            "각성 카드를 내면 각성 능력 3가지가 나오고, 그중 하나를 골라 바로 발동합니다.",
            "어떤 능력이 있는지는 '각성 능력 도감'에서 볼 수 있습니다. 한 번 발동해 본 능력은 도감에서 빛납니다.");

        Section("특수 증강",
            "특수 증강을 켜고 시작하면, 시작할 때 모두 동시에 3개 중 1개를 고릅니다. 6번째 차례에 1개를 더 고릅니다. (최대 2개)",
            "특수 증강은 게임 규칙 자체를 바꾸는 영구 효과입니다. 등급은 실버, 골드, 프리즘 세 가지입니다.",
            "상대가 가진 증강은 자리 칸의 증강 표시에 마우스를 올리면 볼 수 있습니다.");

        Section("다른 승리 조건",
            "일부 특수 증강은 손패를 다 내는 것 말고도 이길 방법을 줍니다.",
            "어벤져스: 직업 8가지를 모두 모은 채로 내 차례가 돌아오면 승리",
            $"블랙홀: 손패가 {SpecialAugments.BlackHoleTarget}장이 되는 순간 승리",
            "수집가: 같은 숫자를 네 문양 모두 손에 모으면 승리",
            $"컬링: 카드를 {StreakRules.CurlingCharge}장 낼 때마다 카드 스톤을 튕길 수 있고, 하우스 정중앙(버튼)에 세우면 승리",
            "잭팟: 내 차례마다 슬롯머신이 돌고, ★★★이 나오면 승리",
            $"도미노: 내가 낸 숫자가 1씩 차이 나게 {StreakRules.DominoTarget}번 연속으로 이어지면 승리",
            $"예언자: 다음 내 차례의 바닥 문양을 {StreakRules.OracleTarget}번 연속으로 맞히면 승리",
            $"정밀 사수: 엄청 빠른 바늘을 황금 구간에서 {MarksmanRules.Target}번 연속으로 멈추면 승리",
            "이런 승리 조건의 진행도는 모두에게 공개됩니다. 누가 이기기 직전인지 보고 견제하세요.");

        Section("판 테마",
            "방에서 '판 테마'를 켜 두면 게임을 시작할 때 테마 하나가 무작위로 정해지고, 그 판에만 특별한 승리 조건이 붙습니다.",
            $"미니게임 대회: {Themes.Info(ThemeId.Arcade).Summary}",
            "미니게임은 벽돌깨기 · 두더지 카드 · 미로 탈출이 돌아가며 나오고, 모두 같은 판을 동시에 합니다.",
            $"야추: {Themes.Info(ThemeId.Yacht).Summary}",
            "야추 족보: 트리플 · 투 페어 · 스트레이트 · 플러시 · 풀하우스 · 야추 (내 차례에 족보판 버튼을 누르면 등록, 등록하면 차례가 끝납니다)",
            $"빙고: {Themes.Info(ThemeId.Bingo).Summary}",
            $"카드 레이스: {Themes.Info(ThemeId.Race).Summary}",
            $"영토 전쟁: {Themes.Info(ThemeId.Territory).Summary}",
            $"비밀 임무: {Themes.Info(ThemeId.Mission).Summary}",
            "임무 종류: 정해진 문양 카드 내기 · +카드로 공격하기 · 액션 카드 내기 · 짝수 카드 내기 · 프리즘 카드 내기 · 숫자를 1씩 커지게 이어서 내기",
            $"보스 레이드: {Themes.Info(ThemeId.Boss).Summary}",
            $"시한폭탄: {Themes.Info(ThemeId.Bomb).Summary}",
            "빙고판 · 레이스 트랙 · 깃발 · 임무 · 보스 · 폭탄은 테이블 오른쪽 테마 판에 보입니다. 어느 테마든 손패를 먼저 다 내도 이깁니다. (야추 제외)");

        Section("탈락 규칙",
            $"손패가 {Rules.EliminationLimit}장이 되면 탈락하고 가장 낮은 순위가 됩니다. (블랙홀 증강이 있으면 탈락하지 않습니다)");
    }

    private void Section(string title, params string[] lines)
    {
        var heading = new Label { Text = title, ThemeTypeVariation = "BoldLabel" };
        heading.AddThemeFontSizeOverride("font_size", 20);
        heading.AddThemeColorOverride("font_color", UiTheme.Gold);
        _howToBox.AddChild(heading);

        foreach (string line in lines)
        {
            _howToBox.AddChild(Body(line));
        }
    }

    private static Label Body(string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 15);
        label.AddThemeColorOverride("font_color", new Color(0.82f, 0.84f, 0.88f));
        return label;
    }

    private void CardRow(params Card[] cards)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        foreach (var card in cards)
        {
            row.AddChild(SampleCard(card));
        }

        _howToBox.AddChild(row);
    }

    /// <summary>카드 그림 + 이름 + 효과 설명 한 줄입니다. 설명은 게임 안 툴팁과 같은 문장을 씁니다.</summary>
    private void KindRow(Card card)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(SampleCard(card));

        var texts = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var name = new Label { Text = Card.KindName(card.Kind), ThemeTypeVariation = "BoldLabel" };
        name.AddThemeFontSizeOverride("font_size", 16);
        texts.AddChild(name);
        texts.AddChild(Body(Card.Describe(card.Kind)));
        row.AddChild(texts);
        _howToBox.AddChild(row);
    }

    private static CardView SampleCard(Card card) => new()
    {
        Card = card,
        CustomMinimumSize = new Vector2(64, 92),
        MouseFilter = MouseFilterEnum.Ignore,
    };
}
