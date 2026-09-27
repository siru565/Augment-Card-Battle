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

    // 자식 노드는 scenes/ui/Seat.tscn에 있습니다. (%이름 = 씬 고유 이름)
    private Label _name => GetNode<Label>("%Name");
    private Label _count => GetNode<Label>("%Count");
    private Label _badges => GetNode<Label>("%Badges");
    private HFlowContainer _augments => GetNode<HFlowContainer>("%Augments");
    private FanView _fan => GetNode<FanView>("%Fan");
    private AvatarView _avatar => GetNode<AvatarView>("%Avatar");
    private bool _targetable;
    private string _augmentSignature = "-";

    /// <summary>Seat.tscn으로 자리를 하나 만듭니다.</summary>
    public static SeatView Create(int playerId, string name)
    {
        var seat = Scenes.Create<SeatView>(Scenes.Seat);
        seat.Assign(playerId, name);
        return seat;
    }

    public SeatView()
    {
        MouseFilter = MouseFilterEnum.Stop;
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
        AddThemeStyleboxOverride("panel", UiTheme.Shadowed(UiTheme.Box(bg, border, 1, 8, 10), 10));
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
            _augments.AddChild(AugmentChip.Create(augment, 13));
        }
    }
}
