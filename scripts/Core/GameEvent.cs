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

    /// <summary>컬링 스톤을 던졌습니다. Text = "조준;힘;하우스x;회전", Amount = 결과(0 실패, 1 하우스, 2 버튼)</summary>
    CurlingThrown,

    /// <summary>정밀 사수 바늘을 멈췄습니다. Text = 멈춘 시각(초), Amount = 1이면 성공, Target = 연속 성공 수</summary>
    MarksmanStopped,

    /// <summary>잭팟 슬롯이 돌았습니다. Text = "릴1,릴2,릴3" (0~3 문양, 4 ★), Amount = 결과(0 꽝, 1 같은 문양, 2 잭팟)</summary>
    JackpotSpun,

    /// <summary>예언을 확인했습니다. Amount = 1이면 적중, Target = 연속 적중 수</summary>
    OracleChecked,

    /// <summary>도미노가 이어지거나 끊겼습니다. Amount = 지금 연속 수</summary>
    DominoStep,
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
