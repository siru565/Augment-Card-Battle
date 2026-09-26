using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 상대 플레이어 자리입니다. 이름, 손패 장수(뒷면 부채꼴), 증강, 상태를 보여 줍니다.
/// 대상 선택 중에는 클릭할 수 있습니다.
/// </summary>
public partial class SeatView : PanelContainer
{
    public int PlayerId { get; set; }

    public event Action<int>? Clicked;

    private readonly Label _name;
    private readonly Label _count;
    private readonly Label _badges;
    private readonly HBoxContainer _augments;
    private readonly FanView _fan;
    private readonly AvatarView _avatar;
    private bool _targetable;
    private string _augmentSignature = "-";

    /// <summary>Godot가 스크립트를 다시 불러올 때 필요한 기본 생성자입니다.</summary>
    public SeatView() : this(0, "?") { }

    public SeatView(int playerId, string name)
    {
        PlayerId = playerId;
        MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(0, 100);

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        AddChild(row);

        _avatar = new AvatarView(AvatarLetter(name));
        row.AddChild(_avatar);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 4);
        row.AddChild(col);

        var header = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        header.AddThemeConstantOverride("separation", 8);
        col.AddChild(header);

        _name = UiTheme.MakeLabel(name, 17, UiTheme.Text, bold: true);
        _count = UiTheme.MakeLabel("", 14, UiTheme.TextDim);
        _badges = UiTheme.MakeLabel("", 13, UiTheme.Danger, bold: true);
        header.AddChild(_name);
        header.AddChild(_count);
        header.AddChild(_badges);

        _fan = new FanView();
        col.AddChild(_fan);

        _augments = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        _augments.AddThemeConstantOverride("separation", 4);
        col.AddChild(_augments);
    }

    /// <summary>게임이 시작될 때 이 자리에 앉을 플레이어를 정합니다.</summary>
    public void Assign(int playerId, string name, ulong steamId = 0)
    {
        PlayerId = playerId;
        _name.Text = name;
        _avatar.Letter = AvatarLetter(name);
        _avatar.SetSteamId(steamId);
        _augmentSignature = "-";
    }

    /// <summary>봇은 마지막 글자(A, B, C), 사람은 첫 글자를 아바타에 씁니다.</summary>
    public static string AvatarLetter(string name) =>
        string.IsNullOrEmpty(name) ? "?" : name.StartsWith("봇 ") ? name[^1..] : name[..1];

    public override void _GuiInput(InputEvent @event)
    {
        if (_targetable && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Clicked?.Invoke(PlayerId);
            AcceptEvent();
        }
    }

    public void UpdateView(int handCount, IReadOnlyList<AugmentInfo> augments, bool sealedNow, bool isTurn,
        bool isNext, bool targetable, string extra = "")
    {
        _targetable = targetable;
        MouseDefaultCursorShape = targetable ? CursorShape.PointingHand : CursorShape.Arrow;

        _count.Text = $"{handCount}장";
        _fan.Count = handCount;
        _avatar.Active = isTurn;

        var badges = new List<string>();
        if (handCount == 1)
        {
            badges.Add("1장 남음");
        }

        if (sealedNow)
        {
            badges.Add("봉인");
        }

        if (isNext)
        {
            badges.Add("다음 차례");
        }

        if (!string.IsNullOrEmpty(extra))
        {
            badges.Insert(0, extra);
        }

        _badges.Text = string.Join(" · ", badges);
        var badgeColor = handCount == 1 || sealedNow ? UiTheme.Danger
            : !string.IsNullOrEmpty(extra) ? UiTheme.Gold
            : UiTheme.TextDim;
        _badges.AddThemeColorOverride("font_color", badgeColor);

        string signature = string.Join(",", augments.Select(a => $"{a.Name}{a.Cooldown}"));
        if (signature != _augmentSignature)
        {
            _augmentSignature = signature;
            RebuildAugments(augments);
        }

        // 차례인 자리는 강조색 테두리, 대상으로 고를 수 있는 자리는 강조색 배경을 옅게 깝니다.
        var border = targetable || isTurn ? UiTheme.Gold : UiTheme.PanelBorder;
        var bg = targetable ? new Color(UiTheme.Gold, 0.12f) : UiTheme.Panel;
        AddThemeStyleboxOverride("panel", UiTheme.Box(bg, border, 1, 8, 10));
    }

    private void RebuildAugments(IReadOnlyList<AugmentInfo> augments)
    {
        foreach (var child in _augments.GetChildren())
        {
            child.QueueFree();
        }

        if (augments.Count == 0)
        {
            _augments.AddChild(UiTheme.MakeLabel("증강 없음", 12, new Color(UiTheme.TextDim, 0.5f)));
        }

        foreach (var augment in augments)
        {
            _augments.AddChild(new AugmentChip(augment, 13));
        }
    }
}
