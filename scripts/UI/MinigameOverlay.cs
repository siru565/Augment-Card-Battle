using System;
using System.Globalization;
using Godot;
using SpCardgame.Core;

namespace SpCardgame.UI;

/// <summary>
/// 승리 조건 미니게임 창입니다. (scenes/screens/Minigame.tscn)
/// 내 미니게임이면 직접 조작하고, 다른 사람 미니게임이면 똑같은 화면을 구경합니다.
/// 결과는 방장이 판정해서 모두에게 보낸 사건(GameEvent)으로 보여 줍니다.
/// </summary>
public partial class MinigameOverlay : ColorRect
{
    /// <summary>내가 조작을 끝냈을 때 보낼 행동입니다.</summary>
    public event Action<PlayerAction>? Submitted;

    private Label _title = null!;
    private Label _subtitle = null!;
    private Label _result = null!;
    private Label _hint = null!;
    private MinigameStage _stage = null!;
    private HBoxContainer _suitRow = null!;
    private double _closeAt = -1;
    private bool _waitingResult;

    /// <summary>지금 보여 주는 미니게임 번호입니다. (같은 미니게임을 두 번 열지 않게)</summary>
    public int ShownId { get; private set; } = -1;

    /// <summary>
    /// 새 게임을 시작할 때 부릅니다. 게임마다 번호가 1부터 다시 매겨지므로,
    /// 이전 게임의 번호를 기억하고 있으면 새 게임의 같은 번호를 "이미 연 것"으로 착각해서 창이 안 뜹니다.
    /// </summary>
    public void Forget()
    {
        ShownId = -1;
        Visible = false;
    }

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _subtitle = GetNode<Label>("%Subtitle");
        _result = GetNode<Label>("%Result");
        _hint = GetNode<Label>("%Hint");
        _stage = GetNode<MinigameStage>("%Stage");
        _suitRow = GetNode<HBoxContainer>("%SuitRow");
        _stage.Committed += OnCommitted;

        foreach (var suit in new[] { CardColor.Red, CardColor.Yellow, CardColor.Green, CardColor.Blue })
        {
            var button = new SuitButton(suit) { CustomMinimumSize = new Vector2(104, 120), FocusMode = FocusModeEnum.None };
            button.Pressed += () => Submit(PlayerAction.Minigame(0, 0, suit));
            _suitRow.AddChild(button);
        }
    }

    /// <summary>미니게임을 엽니다. mine이면 조작할 수 있습니다.</summary>
    public void Open(MinigameInfo game, bool mine, string who)
    {
        ShownId = game.Id;
        _waitingResult = false;
        _closeAt = -1;
        _result.Text = "";
        _result.RemoveThemeColorOverride("font_color");
        Visible = true;

        bool oracle = game.Kind == MinigameKind.Oracle;
        _stage.Visible = !oracle;
        _suitRow.Visible = oracle && mine;
        // 구경하는 사람은 창 뒤의 게임 화면도 누를 수 있게 합니다.
        MouseFilter = mine ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;

        switch (game.Kind)
        {
            case MinigameKind.Curling:
                _title.Text = "컬링";
                _subtitle.Text = mine
                    ? "얼음판을 누른 채 아래로 당겼다 놓으면 카드 스톤이 날아갑니다. 버튼(금색 정중앙)에 세우면 승리!"
                    : $"{who}의 컬링 투구";
                _hint.Text = mine ? "당긴 길이 = 힘, 당긴 방향의 반대 = 조준 · 얼음은 화살표 쪽으로 휘어요" : "";
                break;

            case MinigameKind.Marksman:
                _title.Text = "정밀 사수";
                _subtitle.Text = mine
                    ? "바늘이 황금 구간에 들어온 순간 클릭하거나 Space를 누르세요."
                    : $"{who}의 정밀 사수";
                _hint.Text = mine ? $"{MarksmanRules.Target}번 연속 명중하면 승리 · 놓치면 처음부터" : "";
                break;

            default:
                _title.Text = "예언자";
                _subtitle.Text = mine
                    ? "다음 내 차례가 올 때 바닥 카드의 문양은?"
                    : $"{who}가 예언하는 중";
                _hint.Text = $"{StreakRules.OracleTarget}번 연속으로 맞히면 승리";
                break;
        }

        _stage.Begin(game, mine);
    }

    public void Close()
    {
        Visible = false;
        _closeAt = -1;
        _waitingResult = false;
    }

    /// <summary>진행 중인 연출이 있는지 알려 줍니다. (연출 중에는 게임 상태가 바뀌어도 바로 닫지 않습니다)</summary>
    public bool Busy => Visible && (_waitingResult || _closeAt > 0);

    private void OnCommitted(float a, float b) => Submit(PlayerAction.Minigame(a, b));

    private void Submit(PlayerAction action)
    {
        if (_stage.Game?.Kind == MinigameKind.Oracle)
        {
            Close();
        }
        else
        {
            _waitingResult = true;
            _hint.Text = "";
        }

        Submitted?.Invoke(action);
    }

    /// <summary>방장이 보낸 결과 사건을 보여 줍니다.</summary>
    public void ShowResult(GameEvent e)
    {
        if (!Visible)
        {
            return;
        }

        _waitingResult = false;
        if (e.Type == GameEventType.CurlingThrown)
        {
            var parts = e.Text.Split(';');
            if (parts.Length >= 2)
            {
                float aim = float.Parse(parts[0], CultureInfo.InvariantCulture);
                float power = float.Parse(parts[1], CultureInfo.InvariantCulture);
                _stage.ShowThrow(aim, power, (CurlingSim.Outcome)e.Amount);
            }

            _pendingText = e.Amount switch
            {
                2 => ("버튼! 승리!", UiTheme.Gold),
                1 => ("하우스 안! 손패 1장 버림", Color.FromHtml("#9fe3ff")),
                _ => ("아깝다…", UiTheme.TextDim),
            };
            _closeAt = Time.GetTicksMsec() / 1000.0 + 60; // 스톤이 멈춘 뒤에 닫는 시간을 정합니다.
        }
        else if (e.Type == GameEventType.MarksmanStopped)
        {
            float time = float.Parse(e.Text, CultureInfo.InvariantCulture);
            bool hit = e.Amount == 1;
            _stage.ShowStop(time, hit);
            SetResult(hit
                ? e.Target >= MarksmanRules.Target ? "명중! 승리!" : $"명중! ({e.Target}/{MarksmanRules.Target})"
                : "빗나감…", hit ? UiTheme.Gold : Color.FromHtml("#ff7a6b"));
            _closeAt = Time.GetTicksMsec() / 1000.0 + 1.4;
        }
    }

    private (string Text, Color Color)? _pendingText;

    private void SetResult(string text, Color color)
    {
        _result.Text = text;
        _result.AddThemeColorOverride("font_color", color);
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        double now = Time.GetTicksMsec() / 1000.0;

        // 컬링: 스톤이 멈추면 결과 글자를 띄우고 잠시 뒤 닫습니다.
        if (_pendingText.HasValue && _stage.AnimationDone)
        {
            SetResult(_pendingText.Value.Text, _pendingText.Value.Color);
            _pendingText = null;
            _closeAt = now + 1.3;
        }

        if (_closeAt > 0 && now >= _closeAt)
        {
            Close();
        }
    }
}
