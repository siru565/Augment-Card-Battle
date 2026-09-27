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

    /// <summary>이번 판 테마가 정해졌습니다. Amount = 테마 번호</summary>
    ThemeRevealed,

    /// <summary>미니게임 대회가 시작됐습니다. Amount = 종목</summary>
    ArcadeStarted,

    /// <summary>미니게임 대회 결과입니다. Text = "자리:점수,자리:점수…", Target = 별을 받은 사람(여럿이면 -1), Amount = 최고 점수</summary>
    ArcadeResult,

    /// <summary>야추 족보를 등록했습니다. Text = 족보 이름, Amount = 등록한 족보 수</summary>
    YachtRegistered,

    /// <summary>빙고 칸이 찍혔습니다. Amount = 칸 번호(0~8), Target = 완성한 줄 수</summary>
    BingoMarked,

    /// <summary>레이스 말이 움직였습니다. Amount = 움직인 칸 (뒤로 밀리면 음수)</summary>
    RaceMoved,

    /// <summary>영토 깃발 주인이 바뀌었습니다. Amount = 문양 번호, Target = 이전 주인</summary>
    FlagCaptured,

    /// <summary>보스가 맞았습니다. Amount = 피해, Target = 남은 체력</summary>
    BossHit,

    /// <summary>폭탄이 터졌습니다. Player = 폭탄을 들고 있던 사람, Target = 메달을 받은 사람(여럿이면 -1)</summary>
    BombExploded,
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
