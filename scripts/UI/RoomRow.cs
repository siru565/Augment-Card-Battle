using System;
using Godot;

namespace SpCardgame.UI;

/// <summary>
/// 대기방 자리 한 줄입니다. (scenes/ui/RoomRow.tscn)
/// 사람, 봇, 빈자리 세 가지 모양이 있고, 오른쪽 버튼은 방장에게만 보입니다.
/// (사람: 강퇴, 봇: 빼기, 빈자리: 봇 넣기)
/// </summary>
public partial class RoomRow : PanelContainer
{
    /// <summary>오른쪽 버튼(강퇴·빼기·봇 넣기)을 눌렀을 때 호출합니다.</summary>
    public event Action? ButtonPressed;

    /// <summary>사람 자리 한 줄을 만듭니다.</summary>
    public static RoomRow Create(string name, ulong steamId, bool busy, bool isHostRow, bool canKick)
    {
        var row = Scenes.Create<RoomRow>(Scenes.RoomRow);
        row.Setup(name, steamId, busy, isHostRow, canKick);
        return row;
    }

    /// <summary>봇 자리 한 줄을 만듭니다. 방장이면 '빼기' 버튼이 붙습니다.</summary>
    public static RoomRow CreateBot(string name, bool canRemove)
    {
        var row = Scenes.Create<RoomRow>(Scenes.RoomRow);
        row.Setup(name, 0, false, false, false);
        row.GetNode<Label>("%Name").ThemeTypeVariation = "DimLabel";
        row.SetButton(canRemove, "빼기", UiTheme.ButtonKind.Secondary);
        return row;
    }

    /// <summary>빈자리 한 줄을 만듭니다. 방장이면 '봇 넣기' 버튼이 붙습니다.</summary>
    public static RoomRow CreateEmpty(bool canAdd)
    {
        var row = Scenes.Create<RoomRow>(Scenes.RoomRow);
        row.Setup("", 0, false, false, false);
        row.SetButton(canAdd, "+ 봇 넣기", UiTheme.ButtonKind.Secondary);
        return row;
    }

    public void Setup(string name, ulong steamId, bool busy, bool isHostRow, bool canKick)
    {
        bool filled = name.Length > 0;
        var avatar = GetNode<AvatarView>("%Avatar");
        var label = GetNode<Label>("%Name");
        avatar.Letter = filled ? SeatView.AvatarLetter(name) : "";
        avatar.SetSteamId(filled ? steamId : 0);
        label.Text = filled ? name : "빈자리";
        if (!filled)
        {
            // 빈자리는 흐리게 둡니다. (버튼은 그대로 또렷하게)
            avatar.Modulate = new Color(1, 1, 1, 0.35f);
            label.ThemeTypeVariation = "DimLabel";
            label.AddThemeFontSizeOverride("font_size", 15);
            label.Modulate = new Color(1, 1, 1, 0.6f);
        }

        GetNode<Label>("%Busy").Visible = filled && busy;
        GetNode<Label>("%Host").Visible = isHostRow;
        var button = GetNode<Button>("%Kick");
        button.Visible = canKick;
        button.Pressed += () => ButtonPressed?.Invoke();
    }

    private void SetButton(bool visible, string text, UiTheme.ButtonKind kind)
    {
        var button = GetNode<Button>("%Kick");
        button.Visible = visible;
        button.Text = text;
        UiTheme.StyleButton(button, kind, 13);
    }
}
