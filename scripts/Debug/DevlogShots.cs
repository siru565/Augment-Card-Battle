using System.Linq;
using System.Threading.Tasks;
using Godot;
using SpCardgame.Core;
using SpCardgame.Net;
using SpCardgame.UI;

namespace SpCardgame.DevTools;

/// <summary>
/// 개발일지에 넣을 게임 플레이 스크린샷을 자동으로 찍는 개발 도구입니다.
/// 실제로 혼자 하기 한 판을 진행하면서 장면마다 tools/shots/devlog 폴더에 저장합니다.
/// </summary>
public partial class DevlogShots : Node
{
    private const string Folder = "res://tools/shots/devlog";
    private GameController _game = null!;

    /// <summary>tools/shot_lang.txt에 "en"이 있으면 영어로 찍고 파일 이름 앞에 en_을 붙입니다. (설정 파일은 바꾸지 않습니다)</summary>
    private string _prefix = "";

    private HostSession Host => (HostSession)_game.Session!;

    private PlayerView View => _game.Session!.View!;

    public override async void _Ready()
    {
        try
        {
            await Run();
        }
        catch (System.Exception e)
        {
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath($"{Folder}/error.txt"), e.ToString());
        }

        GetTree().Quit();
    }

    private async Task Run()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Folder));
        _game = GD.Load<PackedScene>("res://scenes/Game.tscn").Instantiate<GameController>();
        Collection.TestMode = true;
        _game.DebugForcedTheme = -2;
        AddChild(_game);
        string langFile = ProjectSettings.GlobalizePath("res://tools/shot_lang.txt");
        if (System.IO.File.Exists(langFile) && System.IO.File.ReadAllText(langFile).Trim() == "en")
        {
            Localization.Apply("en");
            _prefix = "en_";
        }

        // tools/shot_bots.txt에 1~3이 있으면 그 수만큼의 봇과 혼자 하기를 찍습니다. (2인·3인 화면 확인용)
        string botsFile = ProjectSettings.GlobalizePath("res://tools/shot_bots.txt");
        if (System.IO.File.Exists(botsFile) && int.TryParse(System.IO.File.ReadAllText(botsFile).Trim(), out int bots) && bots is >= 1 and <= 3)
        {
            GameSettings.SoloBots = bots;
            _prefix += $"b{bots}_";
        }

        // 1. 메인 화면과 설정 창
        await Wait(1.5);
        await Capture("01_main_menu.png");
        _game.DebugToggleSettings();
        await Wait(0.4);
        await Capture("02_settings.png");
        _game.DebugToggleSettings();

        // 2. 게임 시작 — 모두 동시에 첫 특수 증강 고르기
        _game.BotDelay = 0.35f;
        _game.DebugStartSolo(specialAugments: true);
        await Wait(1.3);
        await Capture("03_augment_draft.png");
        _game.Session!.Submit(PlayerAction.ChooseAugment(0));

        // 3. 몇 바퀴 돌린 뒤 내 차례의 보통 게임 화면
        await WaitMyTurn(minTurn: 6);
        _game.BotDelay = 999f;
        await Wait(2.2);
        await Capture("04_table.png");
        var dump = new System.Text.StringBuilder();
        DumpMin(_game, 0, dump);
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath($"{Folder}/{_prefix}minsize.txt"), dump.ToString());

        // 4. +카드 공격이 쌓여서 넘어온 순간과 누적 상태
        Host.DebugPenalty(5);
        _game.DebugFx(new GameEvent(GameEventType.Attack, View.NextSeat, View.PlayerId, Amount: 5));
        await Wait(0.18);
        await Capture("05_attack_fx.png");
        await Wait(1.4);
        await Capture("06_stacked_penalty.png");

        // 5. 받기를 누르면 직접 한 장씩 뽑아야 합니다.
        _game.Session.Submit(PlayerAction.Draw());
        await Wait(0.3);
        _game.Session.Submit(PlayerAction.ForcedDraw());
        await Wait(0.25);
        _game.Session.Submit(PlayerAction.ForcedDraw());
        await Wait(0.7);
        await Capture("07_forced_draw.png");
        for (int i = 0; i < 40 && View.MustDraw; i++)
        {
            _game.Session.Submit(PlayerAction.ForcedDraw());
            await Wait(0.12);
        }

        // 6. 각성 연출과 능력 선택
        _game.BotDelay = 0.35f;
        await WaitMyTurn(minTurn: 0);
        _game.BotDelay = 999f;
        _game.DebugFx(new GameEvent(GameEventType.Awaken, View.PlayerId));
        await Wait(0.2);
        await Capture("08_awaken_fx.png");
        await Wait(1.3);
        Host.DebugForceAbilityChoices();
        await Wait(1.0);
        await Capture("09_ability_choice.png");
        int target = Enumerable.Range(0, View.PlayerCount).First(i => i != View.PlayerId && View.IsActive(i));
        _game.Session.Submit(PlayerAction.ChooseAbility(0, target, CardColor.Red));
        await Wait(0.3);
        for (int i = 0; i < 40 && View.MustDraw; i++)
        {
            _game.Session.Submit(PlayerAction.ForcedDraw());
            await Wait(0.12);
        }

        // 7. 도박사: 2장 중 1장 고르기
        Host.DebugGiveAugment(SpecialAugmentId.Gambler);
        _game.BotDelay = 0.35f;
        for (int i = 0; i < 400 && !(_game.IsHumanTurn && View.CanDraw && View.PendingPenalty == 0); i++)
        {
            if (View.MustDraw)
            {
                _game.Session.Submit(PlayerAction.ForcedDraw());
            }

            await Wait(0.05);
        }

        _game.BotDelay = 999f;
        await Wait(2.2);
        _game.Session.Submit(PlayerAction.Draw());
        await Wait(0.8);
        await Capture("10_gamble.png");
        _game.Session.Submit(PlayerAction.ChooseDraw(0));

        // 7-2. 승리 조건 미니게임들 (잭팟 · 컬링 · 정밀 사수 · 예언자) — 내 차례에 강제로 띄웁니다.
        _game.BotDelay = 0.35f;
        await WaitMyTurn(minTurn: 0);
        _game.BotDelay = 999f;
        int opponent = Enumerable.Range(0, View.PlayerCount).First(i => i != View.PlayerId && View.IsActive(i));
        Host.DebugGiveAugmentTo(opponent, SpecialAugmentId.Jackpot);
        Host.DebugGiveAugmentTo(View.PlayerId, SpecialAugmentId.Curling);
        await Wait(0.3);
        Host.DebugSpinJackpot(opponent);
        await Wait(1.3);
        await Capture("18_jackpot.png");
        await Wait(1.2);

        // 억지 뽑기나 증강 고르기가 남아 있으면 미니게임이 가려지므로 먼저 끝냅니다.
        for (int i = 0; i < 40 && (View.MustDraw || View.AugmentChoices.Count > 0); i++)
        {
            _game.Session!.Submit(View.MustDraw ? PlayerAction.ForcedDraw() : PlayerAction.ChooseAugment(0));
            await Wait(0.05);
        }

        Host.DebugOpenMinigame(MinigameKind.Curling);
        await Wait(0.6);
        await Capture("19_curling.png");
        if (View.Minigame is { } curling)
        {
            var (aim, power) = CurlingSim.BestThrow(curling.A, curling.B);
            _game.Session!.Submit(PlayerAction.Minigame(aim + 0.05f, power + 0.02f));
            await Wait(1.2);
            await Capture("20_curling_throw.png");
            await Wait(3.2);
        }

        Host.DebugOpenMinigame(MinigameKind.Marksman);
        await Wait(0.8);
        await Capture("21_marksman.png");
        if (View.Minigame is { } marks)
        {
            float stop = Enumerable.Range(0, 6000).Select(i => 0.9f + i * 0.001f).First(t => MarksmanRules.IsHit(marks, t));
            _game.Session!.Submit(PlayerAction.Minigame(stop));
            await Wait(0.3);
            await Capture("22_marksman_hit.png");
            await Wait(1.6);
        }

        Host.DebugOpenMinigame(MinigameKind.Oracle);
        await Wait(0.5);
        await Capture("23_oracle.png");
        _game.Session!.Submit(PlayerAction.Minigame(0, 0, CardColor.Blue));
        await Wait(0.5);

        // 8. 끝까지 자동으로 둬서 최종 순위표
        _game.BotDelay = 0.03f;
        _game.DebugAutoPlay = true;
        for (int i = 0; i < 3000 && View.Winner == null; i++)
        {
            await Wait(0.05);
        }

        await Wait(2.0);
        await Capture("11_ranking.png");

        // 8-2. 판 테마: 미니게임 대회 (벽돌깨기 · 두더지 카드 · 미로 탈출)
        _game.DebugAutoPlay = false;
        _game.BotDelay = 999f;
        _game.DebugForcedTheme = (int)ThemeId.Arcade;
        _game.DebugStartSolo(specialAugments: true);
        await Wait(1.0);
        await Capture("24_theme_banner.png");
        _game.Session!.Submit(PlayerAction.ChooseAugment(0));
        await Wait(0.5);
        for (int round = 0; round < 3; round++)
        {
            Host.DebugStartArcade();
            await Wait(3.6);
            await Capture($"{25 + round * 2}_arcade_{round}.png");
            _game.Session.Submit(PlayerAction.ArcadeScore(20 + round));
            Host.DebugFinishArcadeBots();
            await Wait(0.8);
            await Capture($"{26 + round * 2}_arcade_{round}_result.png");
            await Wait(3.2);
        }

        // 8-4. 판 테마: 빙고 · 카드 레이스 · 영토 전쟁 · 비밀 임무 · 보스 레이드 · 시한폭탄
        //      자동으로 몇 바퀴 둔 뒤 내 차례에 멈춰서 테이블 오른쪽 테마 판을 찍습니다.
        var boardThemes = new[] { ThemeId.Bingo, ThemeId.Race, ThemeId.Territory, ThemeId.Mission, ThemeId.Boss, ThemeId.Bomb };
        for (int t = 0; t < boardThemes.Length; t++)
        {
            _game.DebugForcedTheme = (int)boardThemes[t];
            _game.DebugStartSolo(specialAugments: false);
            _game.BotDelay = 0.05f;
            _game.DebugAutoPlay = true;
            int goal = boardThemes[t] == ThemeId.Bingo ? 7 : 12;
            for (int i = 0; i < 400 && (View.TurnCount < goal || View.Theme != boardThemes[t]) && View.Winner == null; i++)
            {
                await Wait(0.05);
            }

            _game.DebugAutoPlay = false;
            _game.BotDelay = 0.3f;
            await WaitMyTurn(minTurn: 0);
            _game.BotDelay = 999f;
            if (boardThemes[t] == ThemeId.Bomb)
            {
                Host.DebugEditState(s => s.BombFuse = 2);
            }

            await Wait(1.2);
            await Capture($"{33 + t}_theme_{boardThemes[t].ToString().ToLowerInvariant()}.png");
            if (boardThemes[t] == ThemeId.Boss)
            {
                // 분노 1·2단계 보스
                for (int rage = 1; rage <= 2; rage++)
                {
                    int level = rage;
                    Host.DebugEditState(s =>
                    {
                        s.BossRage = level;
                        s.BossHp = BossRules.MaxHpFor(s.PlayerCount) * (3 - level) / 3 - 5;
                    });
                    await Wait(0.9);
                    await Capture($"37_theme_boss_rage{rage}.png");
                }
            }
        }

        // 8-5. 교환 카드: 상대 카드 3장 중 1장 고르기 (마지막 테마 판에서 내 차례에 바로 띄웁니다)
        if (_game.IsHumanTurn)
        {
            Host.DebugEditState(s =>
            {
                var opponent = s.Players.First(p => p.Id != View.PlayerId && p.Active && p.Hand.Count > 0);
                s.SwapChoices.Clear();
                s.SwapChoices.AddRange(opponent.Hand.Take(3));
                s.SwapTarget = opponent.Id;
            });
            await Wait(0.6);
            await Capture("39_swap_choice.png");
            _game.Session!.Submit(PlayerAction.ChooseSwap(0));
            await Wait(0.8);
            await Capture("40_swap_done.png");
        }

        _game.DebugForcedTheme = -2;

        // 9. 대기방
        _game.DebugAutoPlay = false;
        _game.DebugHost(24791);
        await Wait(0.8);
        await Capture("12_room.png");

        // 10. 대기방에서 봇을 빼서 2인으로 만든 모습
        if (_game.Session is HostSession room)
        {
            room.RemoveBot();
            room.RemoveBot();
            await Wait(0.4);
            await Capture("13_room_two_players.png");
        }

        // 11. 게임 방법과 도감 (도감은 얻은 것만 빛납니다. 스크린샷용으로 몇 개를 더 넣어 둡니다 — 테스트 모드라 저장되지 않습니다)
        Collection.AddAugment("어벤져스");
        Collection.AddAugment("반사의 거울");
        Collection.AddAbility("운명 교환");
        for (int tab = 0; tab < 3; tab++)
        {
            _game.DebugOpenGuide(tab);
            await Wait(0.5);
            await Capture($"{14 + tab}_guide_{tab}.png");
        }

        for (int page = 1; page <= 2; page++)
        {
            _game.DebugOpenGuide(0, page * 560);
            await Wait(0.3);
            await Capture($"17_guide_howto_{page}.png");
        }
    }

    /// <summary>봇이 두는 동안 기다렸다가 내 차례가 오면 돌아옵니다. (억지 뽑기는 대신 눌러 줍니다)</summary>
    private async Task WaitMyTurn(int minTurn)
    {
        for (int i = 0; i < 600; i++)
        {
            var view = _game.Session?.View;
            if (view != null && view.MustDraw)
            {
                _game.Session!.Submit(PlayerAction.ForcedDraw());
            }
            else if (view != null && view.AugmentChoices.Count > 0)
            {
                _game.Session!.Submit(PlayerAction.ChooseAugment(0));
            }
            else if (_game.IsHumanTurn && view!.TurnCount >= minTurn && view.PendingPenalty == 0 && !view.ChoosingAbility)
            {
                return;
            }
            else if (_game.IsHumanTurn)
            {
                // 조건이 안 맞는 내 차례는 봇 두뇌로 대신 둡니다.
                var action = new AI.RuleBasedBot().Decide(view!, new System.Random(i));
                _game.Session!.Submit(action);
            }

            await Wait(0.1);
        }
    }

    /// <summary>화면 배치가 넘칠 때 원인을 찾으려고, 최소 너비가 큰 컨트롤을 적어 둡니다.</summary>
    private static void DumpMin(Node node, int depth, System.Text.StringBuilder sb)
    {
        if (node is Control { Visible: true } c && c.GetCombinedMinimumSize().X > 200)
        {
            sb.AppendLine($"{new string(' ', depth * 2)}{c.Name} min={c.GetCombinedMinimumSize()} size={c.Size}");
        }

        if (depth > 12)
        {
            return;
        }

        foreach (var child in node.GetChildren())
        {
            DumpMin(child, depth + 1, sb);
        }
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(ProjectSettings.GlobalizePath($"{Folder}/{_prefix}{name}"));
        GD.Print($"[개발일지 스샷] {name}");
    }
}
