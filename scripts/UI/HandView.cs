using System.Linq;
using Godot;

namespace SpCardgame.UI;

/// <summary>
/// 손패를 가로로 겹쳐서 펼치는 컨트롤입니다. 카드가 많아지면 간격을 자동으로 좁힙니다.
/// 마우스를 올린 카드는 위로 떠오릅니다.
/// </summary>
public partial class HandView : Control
{
    public Vector2 CardSize { get; set; } = new(94, 136);

    /// <summary>카드 사이 최대 간격입니다.</summary>
    public float MaxSpacing { get; set; } = 100f;

    public HandView()
    {
        MouseFilter = MouseFilterEnum.Pass;
        CustomMinimumSize = new Vector2(0, 166);
    }

    public override void _Process(double delta)
    {
        var cards = GetChildren().OfType<CardView>().Where(c => !c.IsQueuedForDeletion()).ToList();
        int n = cards.Count;
        if (n == 0)
        {
            return;
        }

        float width = Size.X;
        float spacing = n == 1 ? 0 : Mathf.Min(MaxSpacing, (width - CardSize.X) / (n - 1));
        float total = CardSize.X + spacing * (n - 1);
        float startX = (width - total) / 2;
        float baseY = Size.Y - CardSize.Y - 4;

        for (int i = 0; i < n; i++)
        {
            var card = cards[i];
            card.Size = CardSize;
            card.Position = new Vector2(startX + spacing * i, baseY - card.Lift);
            card.ZIndex = card.IsHovered ? 10 : 0;
        }
    }
}
