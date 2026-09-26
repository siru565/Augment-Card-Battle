using Godot;

namespace SpCardgame.UI;

/// <summary>
/// 결과 화면의 순위 한 줄입니다. (scenes/ui/RankRow.tscn) 메달 색 띠, 순위 숫자, 아바타, 이름, 순위가 정해진 이유를 보여 줍니다.
/// </summary>
public partial class RankRow : PanelContainer
{
    public static RankRow Create(int rank, string name, ulong steamId, bool me, string reason, bool eliminated)
    {
        var row = Scenes.Create<RankRow>(Scenes.RankRow);
        row.Setup(rank, name, steamId, me, reason, eliminated);
        return row;
    }

    public void Setup(int rank, string name, ulong steamId, bool me, string reason, bool eliminated)
    {
        var medal = RankEmblem.MedalColor(rank);

        // 순위마다 색이 달라서 띠 스타일은 코드에서 칠합니다. 내 줄은 조금 더 진하게 표시합니다.
        var style = UiTheme.Box(new Color(medal, me ? 0.16f : 0.08f), new Color(medal, me ? 0.6f : 0.18f), 1, 6, 10);
        style.BorderWidthLeft = 4;
        style.BorderColor = new Color(medal, me ? 0.9f : 0.5f);
        AddThemeStyleboxOverride("panel", style);

        var number = GetNode<Label>("%Number");
        number.Text = rank.ToString();
        number.AddThemeColorOverride("font_color", medal.Lightened(0.2f));

        var avatar = GetNode<AvatarView>("%Avatar");
        avatar.Letter = SeatView.AvatarLetter(name);
        avatar.SetSteamId(steamId);
        avatar.Active = rank == 1;

        GetNode<Label>("%Name").Text = me ? $"{name}  (나)" : name;
        var reasonLabel = GetNode<Label>("%Reason");
        reasonLabel.Text = reason;
        reasonLabel.Visible = reason.Length > 0;
        GetNode<Label>("%Eliminated").Visible = eliminated;
    }
}
