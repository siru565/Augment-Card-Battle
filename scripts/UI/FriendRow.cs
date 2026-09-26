using System;
using Godot;
using SpCardgame.Net;

namespace SpCardgame.UI;

/// <summary>
/// Steam 친구 초대 목록의 한 줄입니다. (scenes/ui/FriendRow.tscn) 초대 버튼을 누르면 결과를 알려 줍니다.
/// </summary>
public partial class FriendRow : HBoxContainer
{
    /// <summary>초대를 보낸 뒤 호출합니다. (친구 이름, 성공 여부)</summary>
    public event Action<string, bool>? Invited;

    public static FriendRow Create(SteamRuntime.FriendEntry friend)
    {
        var row = Scenes.Create<FriendRow>(Scenes.FriendRow);
        row.Setup(friend);
        return row;
    }

    public void Setup(SteamRuntime.FriendEntry friend)
    {
        // 이 게임을 켜 둔 친구는 강조색, 온라인은 흰색, 오프라인은 흐리게 표시합니다.
        var color = friend.InThisGame ? UiTheme.Gold : friend.Online ? UiTheme.Text : UiTheme.TextDim;

        var avatar = GetNode<AvatarView>("%Avatar");
        avatar.Letter = SeatView.AvatarLetter(friend.Name);
        avatar.SetSteamId(friend.Id.m_SteamID);

        var name = GetNode<Label>("%Name");
        name.Text = friend.Name;
        name.AddThemeColorOverride("font_color", color);

        var state = GetNode<Label>("%State");
        state.Text = friend.InThisGame ? "게임 중" : friend.Online ? "온라인" : "오프라인";
        state.AddThemeColorOverride("font_color", color);

        var invite = GetNode<Button>("%Invite");
        invite.Disabled = !friend.Online;
        var id = friend.Id;
        invite.Pressed += () =>
        {
            bool ok = SteamRuntime.InviteFriend(id);
            invite.Text = ok ? "보냄" : "실패";
            invite.Disabled = true;
            Invited?.Invoke(friend.Name, ok);
        };
    }
}
