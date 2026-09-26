namespace SpCardgame.Core;

public enum GameEventType
{
    CardPlayed,
    CardDrawn,
    ForcedDrawStarted,
    Attack,
    Skip,
    Reverse,
    Awaken,
    AbilityUsed,
    AugmentOffered,
    AugmentGained,
    HandsShuffled,
    Immune,
    Win,
    Placed,
}

/// <summary>
/// 화면 연출용 사건입니다. 누가 무엇을 했는지만 담고, 남에게 보이면 안 되는 정보(뽑은 카드 내용)는 넣지 않습니다.
/// 멀티에서는 호스트가 모두에게 그대로 전달합니다.
/// </summary>
public sealed record GameEvent(
    GameEventType Type,
    int Player = -1,
    int Target = -1,
    Card? Card = null,
    int Amount = 0,
    string Text = "");
