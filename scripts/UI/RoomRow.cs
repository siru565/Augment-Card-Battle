using System;
using Godot;

namespace SpCardgame.UI;

/// <summary>
/// 대기방 참가자 한 줄입니다. (scenes/ui/RoomRow.tscn) 아바타, 이름, 게임 중·방장 표시, 강퇴 버튼이 있습니다.
/// </summary>
public partial class RoomRow : PanelContainer
{
    /// <summary>강퇴 버튼을 눌렀을 때 호출합니다.</summary>
    public event Action? KickPressed;

    /// <summary>
    /// 한 줄을 만듭니다. name이 비어 있으면 빈자리(봇이 앉음)로 표시합니다.
    /// </summary>
    public static RoomRow Create(string name, ulong steamId, bool busy, bool isHostRow, bool canKick)
    {
        var row = Scenes.Create<RoomRow>(Scenes.RoomRow);
        row.Setup(name, steamId, busy, isHostRow, canKick);
        return row;
    }

    public void Setup(string name, ulong steamId, bool busy, bool isHostRow, bool canKick)
    {
        bool filled = name.Length > 0;
        if (!filled)
        {
            // 빈자리는 더 흐리게 둡니다.
            SelfModulate = new Color(1, 1, 1, 0.4f);
        }

        var avatar = GetNode<AvatarView>("%Avatar");
        avatar.Letter = filled ? SeatView.AvatarLetter(name) : "봇";
        avatar.SetSteamId(filled ? steamId : 0);

        var label = GetNode<Label>("%Name");
        label.Text = filled ? name : "빈자리 (봇)";
        if (!filled)
        {
            label.ThemeTypeVariation = "DimLabel";
            label.AddThemeFontSizeOverride("font_size", 15);
        }

        GetNode<Label>("%Busy").Visible = filled && busy;
        GetNode<Label>("%Host").Visible = isHostRow;
        var kick = GetNode<Button>("%Kick");
        kick.Visible = canKick;
        kick.Pressed += () => KickPressed?.Invoke();
    }
}
