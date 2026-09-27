using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpCardgame.AI;
using SpCardgame.Core;
using SpCardgame.Net;

namespace SpCardgame.UI;

/// <summary>
/// 게임 화면입니다. 처음에는 로비(혼자 하기 / 방 만들기 / 참가하기)를 보여 주고,
/// 게임이 시작되면 GameSession이 주는 PlayerView만 그립니다. 규칙 판정은 세션(호스트)이 합니다.
/// </summary>
public partial class GameController : Control
{
    private const int SeatCount = HostSession.SeatCount;

    /// <summary>봇이 한 번 행동하기까지 기다리는 시간(초)입니다.</summary>
    [Export]
    public float BotDelay
    {
        get => _botDelay;
        set
        {
            _botDelay = value;
            if (_session is HostSession host)
            {
                host.BotDelay = value;
            }
        }
    }

    /// <summary>켜 두면 로비 없이 바로 혼자 하기로 시작합니다. (개발용 스크린샷 도구에서 사용합니다.)</summary>
    [Export] public bool AutoStartSolo { get; set; }

    private float _botDelay = 0.9f;
    private GameSession? _session;
    private IDisposable? _transport;
    private NetBridge _net = null!;
    private int _lastTopCardId = -1;
    private string _handSignature = "";
    private string _myAugmentSignature = "";

    // 사람이 카드를 고르는 중인 상태입니다.
    private Card? _pendingCard;
    private CardColor _pendingColor = CardColor.Wild;
    private bool _awaitingColor;
    private bool _awaitingTarget;

    // 각성 능력을 고르는 중인 상태입니다. (선택지 번호, 고른 대상)
    private int? _pendingAbility;
    private int _pendingAbilityTarget = -1;
    private Control _abilityOverlay = null!;
    private HBoxContainer _abilityRow = null!;
    private string _abilitySignature = "";

    // 특수 증강을 고르는 중인 상태입니다.
    private Control _augmentOverlay = null!;
    private HBoxContainer _augmentRow = null!;
    private string _augmentSignature = "";

    /// <summary>연출(각성 번쩍임 등)이 끝날 때까지 선택 창을 잠깐 미뤄 둘 시각(ms)입니다.</summary>
    private ulong _overlayHoldUntil;

    // 연출 레이어와 흔들 대상입니다.
    private FxLayer _fx = null!;
    private Control _shakeRoot = null!;

    /// <summary>승리 사유입니다. (예: 어벤져스 — 8가지 직업을 모두 모았습니다)</summary>
    private string _winReason = "";

    /// <summary>사람마다 순위가 정해진 이유입니다. (게임 종료 화면의 순위표에 씁니다)</summary>
    private readonly Dictionary<int, string> _placeReasons = new();

    /// <summary>직전 화면 갱신 때 내 차례였는지 기억합니다. (내 차례 알림음용)</summary>
    private bool _wasMyTurn;

    /// <summary>증강 획득이나 오류처럼 잠깐 보여 줄 알림입니다. 다음 행동 때 지워집니다.</summary>
    private string _notice = "";

    /// <summary>마지막으로 누가 무엇을 했는지 보여 주는 한 줄입니다.</summary>
    private string _lastAction = "";

    // 게임 화면 노드입니다.
    private readonly SeatView[] _seats = new SeatView[SeatCount];
    private Control _table = null!;
    private DirectionRing _ring = null!;
    private CardView _discard = null!;

    /// <summary>버린 더미 아래에 흐트러져 깔린 지난 카드들입니다. (진짜 테이블처럼 보이게 합니다)</summary>
    private readonly CardView[] _discardPile = new CardView[6];

    /// <summary>깔린 카드마다의 어긋난 위치와 각도입니다.</summary>
    private readonly (Vector2 Offset, float Rotation)[] _pileJitter = new (Vector2, float)[6];

    private Card? _previousTop;
    private readonly CardView[] _drawStack = new CardView[3];
    private Label _drawLabel = null!;
    private PanelContainer _colorPill = null!;
    private PanelContainer _penaltyBadge = null!;
    private Label _penaltyLabel = null!;
    private Label _colorPillLabel = null!;
    private Label _infoLabel = null!;
    private Label _promptLabel = null!;
    private PanelContainer _promptPanel = null!;
    private Label _myNameLabel = null!;
    private Label _myCountLabel = null!;
    private AvatarView _myAvatar = null!;
    private HBoxContainer _myAugments = null!;
    private Button _drawButton = null!;
    private Button _passButton = null!;
    private Button _cancelButton = null!;
    private Button _newGameButton = null!;
    private HandView _hand = null!;
    private PanelContainer _logPanel = null!;
    private RichTextLabel _logLabel = null!;

    /// <summary>기록 창에 쓴 원문 줄들입니다. 언어를 바꾸면 이것으로 다시 씁니다.</summary>
    private readonly List<string> _logHistory = new();
    private Control _colorOverlay = null!;
    private Control _gameOverOverlay = null!;
    private Label _gameOverLabel = null!;
    private VBoxContainer _rankList = null!;
    private RankEmblem _rankEmblem = null!;
    private PanelContainer _gameOverPanel = null!;
    private Button _againButton = null!;
    private Button _roomButton = null!;
    private Button _mainMenuButton = null!;
    private Button _endGameButton = null!;
    private Label _myProgress = null!;
    private PanelContainer _toast = null!;
    /// <summary>알림(토스트) 연출입니다. 모양과 시간은 Game.tscn의 ToastAnim에서 고칩니다.</summary>
    private AnimationPlayer _toastAnim = null!;
    private Label _toastLabel = null!;

    // 로비 노드입니다.
    private Control _lobbyOverlay = null!;
    private LineEdit _nameEdit = null!;
    private LineEdit _addressEdit = null!;
    private LineEdit _portEdit = null!;
    private Label _lobbyStatus = null!;
    private Button _startButton = null!;
    private OptionButton _botLevelOption = null!;
    private GuideView _guide = null!;
    private Control _lobbyMenu = null!;
    private Control _lobbyRoom = null!;
    private LineEdit _codeEdit = null!;
    private Button _steamHostButton = null!;
    private Button _steamJoinButton = null!;
    private Label _steamStatus = null!;
    private Control _steamRoomBox = null!;
    private Label _roomCodeLabel = null!;
    private string _roomCode = "";
    private VBoxContainer _roomPlayers = null!;
    private CheckButton _augmentToggle = null!;
    private CheckButton _soloAugmentToggle = null!;
    private Control _friendBox = null!;
    private VBoxContainer _friendList = null!;
    private bool _updatingToggle;

    // 게임 안 초대 알림입니다.
    private PanelContainer _inviteBanner = null!;
    private Label _inviteLabel = null!;
    private Steamworks.CSteamID _inviteLobby;

    /// <summary>지금 내 차례인지 알려 줍니다. (개발용 스크린샷 도구에서 사용합니다.)</summary>
    public bool IsHumanTurn => _session?.View is { IsMyTurn: true, Winner: null };

    // ───────────── 개발용 (자동 테스트) ─────────────

    /// <summary>켜 두면 내 차례에 봇 두뇌로 자동으로 둡니다. 멀티 자동 테스트에서 사용합니다.</summary>
    public bool DebugAutoPlay { get; set; }

    public GameSession? Session => _session;

    private readonly AI.RuleBasedBot _autoBrain = new();
    private readonly Random _autoRng = new();
    private float _autoTimer;

    public void DebugStartSolo(bool specialAugments = false)
    {
        _soloAugmentToggle.ButtonPressed = specialAugments;
        StartSolo();
    }

    /// <summary>연출만 강제로 띄웁니다. (스크린샷 확인용)</summary>
    public void DebugFx(GameEvent gameEvent) => OnFx(gameEvent);

    public void DebugToggleSettings() => ToggleSettings();

    /// <summary>게임 방법 · 도감 창을 엽니다. (스크린샷 확인용)</summary>
    public void DebugOpenGuide(int tab, int scroll = 0)
    {
        _guide.Open(tab);
        _guide.DebugScrollHowTo(scroll);
    }

    public void DebugHost(int port)
    {
        _portEdit.Text = port.ToString();
        StartHosting();
    }

    /// <summary>대기방의 '방 나가기'를 누른 것과 같습니다. (멀티 테스트용)</summary>
    public void DebugLeaveRoom() => BackToLobby("");

    public void DebugJoin(string address, int port)
    {
        _addressEdit.Text = address;
        _portEdit.Text = port.ToString();
        StartJoining();
    }

    public void DebugStartGame() => (_session as HostSession)?.StartGame();

    private void RunAutoPlay(double delta)
    {
        var view = _session?.View;
        if (!DebugAutoPlay || view == null || !(view.IsMyTurn || view.MustDraw || view.AugmentChoices.Count > 0) || view.Winner.HasValue)
        {
            return;
        }

        _autoTimer += (float)delta;
        if (_autoTimer < 0.15f)
        {
            return;
        }

        _autoTimer = 0f;
        Submit(_autoBrain.Decide(view, _autoRng));
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = UiTheme.LoadTheme();

        _net = new NetBridge { Name = "Net" };
        AddChild(_net);

        // 효과음과 저장된 설정입니다. 창 크기는 실제 게임 창일 때만 바꿉니다. (테스트용 분할 화면에서는 건드리지 않습니다)
        AddChild(new Audio.Sfx());

        // 배경음악은 실제 게임 창에서 하나만 틉니다. (테스트용 분할 화면에서 두 번 겹치지 않게)
        if (GetViewport() == GetTree().Root)
        {
            AddChild(new Audio.Music());
        }

        GameSettings.Load();
        GameSettings.ApplyAudio();
        Localization.Apply(GameSettings.Language);
        Localization.Changed += OnLanguageChanged;
        if (GetViewport() == GetTree().Root && !_displayApplied)
        {
            _displayApplied = true;
            GameSettings.ApplyDisplay();
        }

        // 버튼을 누르면 딸깍 소리가 나게 합니다. (나중에 생기는 버튼도 포함)
        GetTree().NodeAdded += OnNodeAdded;

        SteamRuntime.EnsureInitialized(this);
        SteamRuntime.JoinRequested += lobby => Callable.From(() => JoinSteamLobby(lobby)).CallDeferred();
        SteamRuntime.InviteReceived += (from, lobby) => Callable.From(() => ShowInvite(from, lobby)).CallDeferred();
        SteamRuntime.AvatarLoaded += OnAvatarLoaded;

        BindUi();
        ShowLobbyMenu("");

        // 배포한 exe가 제대로 도는지 확인하는 자가 진단입니다. (실행: 게임.exe -- --selftest)
        if (OS.GetCmdlineUserArgs().Contains("--selftest"))
        {
            string folder = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "selftest.txt"),
                $"steam_ready={SteamRuntime.IsReady}\nname={SteamRuntime.PersonaName}\nerror={SteamRuntime.InitError}\n");
            GetTree().Quit();
            return;
        }

        if (AutoStartSolo)
        {
            StartSolo();
            return;
        }

        // Steam 친구 목록에서 "게임 참가"로 게임이 켜진 경우 바로 그 방으로 들어갑니다.
        if (SteamRuntime.TryParseConnect(string.Join(" ", OS.GetCmdlineArgs()), out var launchLobby))
        {
            Callable.From(() => JoinSteamLobby(launchLobby)).CallDeferred();
        }
    }

    private static bool _displayApplied;

    private void OnNodeAdded(Node node)
    {
        if (node is BaseButton button && IsAncestorOf(node) && node is not CardView)
        {
            button.Pressed += () => Audio.Sfx.Play("click", -6f);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Esc로 설정 창을 열고 닫습니다.
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            ToggleSettings();
            GetViewport().SetInputAsHandled();
            return;
        }

        // 스페이스바로도 카드를 뽑을 수 있습니다. (억지 뽑기를 연타하기 편하게)
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space } && _session?.View != null)
        {
            DrawPressed();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        LayoutTable();

        // 연출 대기가 끝난 순간 화면을 갱신해서 선택 창(증강·각성)이 바로 뜨게 합니다.
        if (_holdPending && !OverlaysHeld)
        {
            _holdPending = false;
            Refresh();
        }

        (_transport as IPollingTransport)?.Poll();
        _session?.Tick(delta);
        RunAutoPlay(delta);
    }

    public override void _ExitTree()
    {
        SteamRuntime.AvatarLoaded -= OnAvatarLoaded;
        Localization.Changed -= OnLanguageChanged;
        _net.Close();
    }

    /// <summary>
    /// 언어가 바뀌면 라벨은 Godot가 알아서 다시 번역하고, 직접 그리는 글자(카드, 아바타)와 기록만 여기서 다시 만듭니다.
    /// </summary>
    private void OnLanguageChanged()
    {
        RedrawAll(this);
        _logLabel.Clear();
        foreach (string line in _logHistory)
        {
            RenderLog(line);
        }

        Refresh();
    }

    private static void RedrawAll(Node node)
    {
        if (node is CanvasItem item)
        {
            item.QueueRedraw();
        }

        foreach (var child in node.GetChildren())
        {
            RedrawAll(child);
        }
    }

    /// <summary>늦게 도착한 Steam 프로필 사진을 해당 아바타에 다시 그립니다.</summary>
    private void OnAvatarLoaded(ulong steamId) => Callable.From(() =>
    {
        foreach (var avatar in FindAvatars(this).Where(a => a.SteamId == steamId))
        {
            avatar.RefreshPicture();
        }
    }).CallDeferred();

    private static IEnumerable<AvatarView> FindAvatars(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is AvatarView avatar)
            {
                yield return avatar;
            }

            foreach (var inner in FindAvatars(child))
            {
                yield return inner;
            }
        }
    }

    // ───────────── 세션 시작과 종료 ─────────────

    private string PlayerName
    {
        get
        {
            string name = _nameEdit.Text.Trim();
            return string.IsNullOrEmpty(name) ? "나" : name;
        }
    }

    private void StartSolo()
    {
        LeaveSession();
        var options = new GameOptions(SpecialAugments: _soloAugmentToggle.ButtonPressed);
        var host = new HostSession(PlayerName, null, GameVersion.Current, options) { BotDelay = _botDelay, HostSteamId = SteamRuntime.MySteamId };
        host.SetBotCount(GameSettings.SoloBots);
        host.SetBotLevel((BotLevel)GameSettings.BotLevel);
        AttachSession(host);
        host.StartGame();
    }

    private void StartHosting()
    {
        LeaveSession();
        int port = ParsePort();
        var error = _net.Host(port);
        if (error != Error.Ok)
        {
            ShowLobbyMenu($"방을 만들지 못했어요. ({error}) 포트 {port}가 이미 쓰이고 있는지 확인해 주세요.");
            return;
        }

        var transport = new ServerTransport(_net);
        _transport = transport;
        AttachHost(transport);
        ShowLobbyRoom($"방을 만들었어요. 포트 {port}\n같은 PC라면 127.0.0.1, 다른 PC라면 이 PC의 IP로 접속하면 돼요.");
    }

    private void StartJoining()
    {
        LeaveSession();
        int port = ParsePort();
        string address = string.IsNullOrWhiteSpace(_addressEdit.Text) ? "127.0.0.1" : _addressEdit.Text.Trim();
        var error = _net.Join(address, port);
        if (error != Error.Ok)
        {
            ShowLobbyMenu($"접속을 시작하지 못했어요. ({error})");
            return;
        }

        var transport = new ClientTransport(_net);
        _transport = transport;
        AttachSession(new ClientSession(PlayerName, transport, GameVersion.Current));
        ShowLobbyRoom($"{address}:{port} 에 접속하는 중...");
    }

    private int ParsePort() => int.TryParse(_portEdit.Text, out int port) && port is > 1024 and < 65536
        ? port
        : NetBridge.DefaultPort;

    /// <summary>멀티 방을 만들 때 방장 세션을 붙입니다. 봇 난이도는 마지막으로 고른 값으로 시작합니다.</summary>
    private void AttachHost(IServerTransport transport)
    {
        var host = new HostSession(PlayerName, transport, GameVersion.Current) { BotDelay = _botDelay, HostSteamId = SteamRuntime.MySteamId };
        host.SetBotLevel((BotLevel)GameSettings.BotLevel);
        AttachSession(host);
    }

    private void AttachSession(GameSession session)
    {
        _session = session;
        session.Changed += Refresh;
        session.LobbyChanged += RefreshLobby;
        session.GameStarted += OnGameStarted;
        session.LogLine += AppendLog;
        session.Fx += OnFx;
        session.ReturnedToRoom += OnReturnedToRoom;
        session.ErrorMessage += OnSessionError;
        session.Disconnected += reason => Callable.From(() => BackToLobby(reason)).CallDeferred();
    }

    private void LeaveSession()
    {
        (_session as ClientSession)?.SayGoodbye();
        _transport?.Dispose();
        _transport = null;
        _session = null;
        _roomCode = "";
        _net.Close();
        SteamRuntime.LeaveLobby();
    }

    // ───────────── Steam 멀티 ─────────────

    /// <summary>Steam 로비를 만들고 호스트가 됩니다. Steam 중계망을 쓰므로 포트포워딩이 필요 없습니다.</summary>
    private void StartSteamHosting()
    {
        LeaveSession();
        ShowLobbyRoom("Steam 방을 만드는 중...");
        SteamRuntime.CreateLobby(HostSession.SeatCount, lobby =>
        {
            var transport = new SteamServerTransport();
            _transport = transport;
            _roomCode = SteamRuntime.ToRoomCode(lobby);
            AttachHost(transport);
            ShowLobbyRoom("Steam 방을 만들었어요! 친구를 초대하거나 방 코드를 알려 주세요.");
        }, error => ShowLobbyMenu(error));
    }

    private void JoinSteamByCode()
    {
        if (!SteamRuntime.TryParseRoomCode(_codeEdit.Text, out var lobby))
        {
            ShowLobbyMenu("방 코드가 올바르지 않아요.");
            return;
        }

        JoinSteamLobby(lobby);
    }

    /// <summary>Steam 로비에 들어간 뒤, 방장에게 P2P로 연결합니다. (친구 초대 수락도 여기로 옵니다.)</summary>
    private void JoinSteamLobby(Steamworks.CSteamID lobby)
    {
        LeaveSession();
        ShowLobbyRoom("Steam 방에 들어가는 중...");
        SteamRuntime.JoinLobby(lobby, (enteredLobby, owner) =>
        {
            var transport = new SteamClientTransport(owner);
            _transport = transport;
            _roomCode = SteamRuntime.ToRoomCode(enteredLobby);
            AttachSession(new ClientSession(PlayerName, transport, GameVersion.Current));
            ShowLobbyRoom("방장에게 연결하는 중...");
        }, error => ShowLobbyMenu(error));
    }

    /// <summary>게임이나 방에서 나와 로비 첫 화면으로 돌아갑니다.</summary>
    private void BackToLobby(string reason)
    {
        LeaveSession();
        ShowLobbyMenu(reason);
    }

    private void OnGameStarted()
    {
        if (_session == null)
        {
            return;
        }

        Audio.Sfx.Play("deal", -4f);
        Audio.Music.SetInGame(true);
        _wasMyTurn = false;
        _lobbyOverlay.Visible = false;
        _gameOverOverlay.Visible = false;
        _augmentOverlay.Visible = false;
        _abilityOverlay.Visible = false;
        _winReason = "";
        _placeReasons.Clear();
        _overlayHoldUntil = 0;
        _abilitySignature = "";
        _augmentSignature = "";
        _logLabel.Clear();
        _logHistory.Clear();
        ClearPending();
        _notice = "";
        _lastAction = "";
        _lastTopCardId = -1;
        _previousTop = null;
        PushDiscardPile(null);
        _handSignature = "";
        _myAugmentSignature = "";

        // 내 자리를 기준으로 다음 사람부터 위쪽에 차례대로 앉힙니다.
        // 인원이 적으면 칸을 비워 둡니다. (2인: 가운데 한 칸, 3인: 양쪽 두 칸)
        int players = Math.Max(2, _session.Names.Length);
        var slots = OpponentSlots(players);
        for (int k = 1; k < SeatCount; k++)
        {
            _seats[k].SetEmpty(true);
        }

        for (int k = 1; k < players; k++)
        {
            int seat = (_session.MySeat + k) % players;
            var slot = _seats[slots[k - 1]];
            slot.SetEmpty(false);
            slot.Assign(seat, _session.NameOf(seat), _session.SteamIdOf(seat));
        }

        _myNameLabel.Text = _session.NameOf(_session.MySeat);
        _myAvatar.Letter = SeatView.AvatarLetter(_session.NameOf(_session.MySeat));
        _myAvatar.SetSteamId(_session.SteamIdOf(_session.MySeat));
        _newGameButton.Visible = _session.IsHost;
        _newGameButton.Text = _session.IsSolo ? "새 게임" : "모두 대기방으로";
    }

    /// <summary>
    /// 게임 화면의 나가기입니다. 혼자 하기는 메인 화면으로, 멀티는 나만 대기방으로 갑니다.
    /// (내 자리는 봇이 이어받고, 다른 사람들은 계속 플레이합니다)
    /// </summary>
    private void LeavePressed()
    {
        if (_session == null || _session.IsSolo)
        {
            BackToLobby("");
            return;
        }

        _session.LeaveGame();
    }

    /// <summary>방장이 판을 끝내고 대기방으로 돌아왔을 때 호출합니다.</summary>
    private void OnReturnedToRoom()
    {
        _gameOverOverlay.Visible = false;
        _augmentOverlay.Visible = false;
        _abilityOverlay.Visible = false;
        _colorOverlay.Visible = false;
        ClearPending();
        ShowLobbyRoom(RoomStatusText());
    }

    private void OnSessionError(string message)
    {
        if (_session?.View == null)
        {
            _lobbyStatus.Text = message;
            return;
        }

        _notice = $"낼 수 없어요: {message}";
        Audio.Sfx.Play("error", -4f);
        Refresh();
    }


    // ───────────── 입력 처리 ─────────────

    private void Submit(PlayerAction action)
    {
        _notice = "";
        ClearPending();
        _session?.Submit(action);
        Refresh();
    }

    private void OnHandCardClicked(CardView cardView)
    {
        var card = cardView.Card;
        var view = _session?.View;
        if (card == null || view == null)
        {
            return;
        }

        if (_awaitingTarget || _awaitingColor)
        {
            ClearPending();
        }

        if (!view.PlayableCardIds.Contains(card.Id))
        {
            Refresh();
            return;
        }

        _pendingCard = card;

        if (view.ColorRequiredIds.Contains(card.Id))
        {
            _awaitingColor = true;
        }
        else if (view.TargetRequiredIds.Contains(card.Id))
        {
            _awaitingTarget = true;
        }
        else
        {
            Submit(PlayerAction.Play(card.Id));
            return;
        }

        Refresh();
    }

    private void OnColorPicked(CardColor color)
    {
        if (_pendingAbility.HasValue)
        {
            Submit(PlayerAction.ChooseAbility(_pendingAbility.Value, _pendingAbilityTarget, color));
            return;
        }

        var view = _session?.View;
        if (_pendingCard == null || view == null)
        {
            return;
        }

        _pendingColor = color;
        _awaitingColor = false;

        if (view.TargetRequiredIds.Contains(_pendingCard.Id))
        {
            _awaitingTarget = true;
            Refresh();
            return;
        }

        Submit(PlayerAction.Play(_pendingCard.Id, _pendingColor));
    }

    private void OnSeatClicked(int playerId)
    {
        if (_awaitingTarget && _pendingAbility.HasValue && _session?.View is { } abilityView)
        {
            var ability = abilityView.AbilityChoices[_pendingAbility.Value];
            if (ability.NeedsSuit)
            {
                _pendingAbilityTarget = playerId;
                _awaitingTarget = false;
                _awaitingColor = true;
                Refresh();
                return;
            }

            Submit(PlayerAction.ChooseAbility(_pendingAbility.Value, playerId));
            return;
        }

        if (_awaitingTarget && _pendingCard != null)
        {
            Submit(PlayerAction.Play(_pendingCard.Id, _pendingColor, playerId));
        }
    }

    /// <summary>각성 능력 선택지를 골랐을 때 호출합니다. 대상이나 문양이 필요하면 이어서 고르게 합니다.</summary>
    private void OnAbilityPicked(int index)
    {
        var view = _session?.View;
        if (view == null || index >= view.AbilityChoices.Count)
        {
            return;
        }

        var ability = view.AbilityChoices[index];
        ClearPending();
        _pendingAbility = index;

        if (ability.NeedsTarget)
        {
            _awaitingTarget = true;
        }
        else if (ability.NeedsSuit)
        {
            _awaitingColor = true;
        }
        else
        {
            Submit(PlayerAction.ChooseAbility(index));
            return;
        }

        Refresh();
    }

    private void RefreshAbilityOverlay(PlayerView view)
    {
        bool show = view.AbilityChoices.Count > 0 && !_awaitingTarget && !_awaitingColor && !view.Winner.HasValue && !OverlaysHeld;
        string signature = string.Join(",", view.AbilityChoices.Select(a => a.Id)) + view.TurnCount;

        if (show && signature != _abilitySignature)
        {
            _abilitySignature = signature;
            foreach (var child in _abilityRow.GetChildren())
            {
                child.QueueFree();
            }

            for (int i = 0; i < view.AbilityChoices.Count; i++)
            {
                int index = i;
                var card = AbilityCardView.Create(view.AbilityChoices[i]);
                card.Modulate = new Color(1, 1, 1, 0);
                card.Picked += () => OnAbilityPicked(index);
                _abilityRow.AddChild(card);

                // 카드가 차례대로 나타나는 연출입니다.
                var tween = CreateTween();
                tween.TweenInterval(0.08 * i);
                tween.TweenProperty(card, "modulate:a", 1f, 0.25);
            }
        }

        if (!show && view.AbilityChoices.Count == 0)
        {
            _abilitySignature = "";
        }

        _abilityOverlay.Visible = show;
    }

    private void OnDrawPileClicked(CardView _) => DrawPressed();

    /// <summary>덱 클릭, 뽑기 버튼, 스페이스바가 모두 여기로 옵니다. 억지로 뽑아야 하면 그것부터 뽑습니다.</summary>
    private void DrawPressed()
    {
        var view = _session?.View;
        if (view == null || view.Winner.HasValue)
        {
            return;
        }

        if (view.MustDraw)
        {
            Submit(PlayerAction.ForcedDraw());
            return;
        }

        if (view.CanDraw && !_awaitingColor && !_awaitingTarget)
        {
            Submit(PlayerAction.Draw());
        }
    }

    /// <summary>특수 증강을 골랐을 때 호출합니다.</summary>
    private void OnAugmentPicked(int index)
    {
        var view = _session?.View;
        if (view == null || index >= view.AugmentChoices.Count)
        {
            return;
        }

        Submit(PlayerAction.ChooseAugment(index));
    }

    /// <summary>연출이 끝날 때까지 선택 창을 잠깐 미룹니다.</summary>
    private void HoldOverlays(float seconds)
    {
        _overlayHoldUntil = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        _holdPending = true;
    }

    private bool OverlaysHeld => Time.GetTicksMsec() < _overlayHoldUntil;

    /// <summary>연출 대기가 끝나면 선택 창을 띄우도록 화면을 한 번 더 갱신해야 하는지 표시합니다.</summary>
    private bool _holdPending;

    private void RefreshAugmentOverlay(PlayerView view)
    {
        bool show = view.AugmentChoices.Count > 0 && !view.Winner.HasValue && !OverlaysHeld;
        string signature = string.Join(",", view.AugmentChoices.Select(a => a.Name)) + view.TurnCount;

        if (show && signature != _augmentSignature)
        {
            _augmentSignature = signature;
            foreach (var child in _augmentRow.GetChildren())
            {
                child.QueueFree();
            }

            for (int i = 0; i < view.AugmentChoices.Count; i++)
            {
                int index = i;
                var card = AbilityCardView.Create(view.AugmentChoices[i]);
                card.Modulate = new Color(1, 1, 1, 0);
                card.Scale = new Vector2(0.7f, 0.7f);
                card.Picked += () => OnAugmentPicked(index);
                _augmentRow.AddChild(card);

                // 카드가 한 장씩 뒤집히듯 나타나는 연출입니다.
                var tween = CreateTween();
                tween.TweenInterval(0.12 * i);
                tween.TweenProperty(card, "modulate:a", 1f, 0.2);
                tween.Parallel().TweenProperty(card, "scale", Vector2.One, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            }
        }

        if (view.AugmentChoices.Count == 0)
        {
            _augmentSignature = "";
        }

        _augmentOverlay.Visible = show;
    }

    private void ClearPending()
    {
        _pendingAbility = null;
        _pendingAbilityTarget = -1;
        _pendingCard = null;
        _pendingColor = CardColor.Wild;
        _awaitingColor = false;
        _awaitingTarget = false;
    }

    // ───────────── 화면 갱신 ─────────────

    private void Refresh()
    {
        var view = _session?.View;
        if (_session == null || view == null || !_session.Playing)
        {
            return;
        }

        bool finished = view.Winner.HasValue;

        // 빼앗기 등으로 얻은 증강도 도감에 넣습니다.
        foreach (var augment in view.Augments[view.PlayerId])
        {
            Collection.AddAugment(augment.Name);
        }

        // 내 차례가 새로 시작되면 딩동 소리를 냅니다.
        bool myTurnNow = view.IsMyTurn && !finished;
        if (myTurnNow && !_wasMyTurn)
        {
            Audio.Sfx.Play("my_turn", -4f, 1f, 0f);
        }

        _wasMyTurn = myTurnNow;

        for (int k = 1; k < SeatCount; k++)
        {
            int seat = _seats[k].PlayerId;
            if (seat < 0 || seat >= view.PlayerCount)
            {
                continue;
            }

            _seats[k].UpdateView(view.HandCounts[seat], view.Augments[seat], view.Sealed[seat],
                isTurn: (view.PayingDebt ? view.DebtPlayer : view.CurrentPlayer) == seat && !finished,
                isNext: view.NextSeat == seat,
                targetable: _awaitingTarget,
                extra: SeatStatus(view, seat));
        }

        RefreshTable(view);
        RefreshMyArea(view);
        RefreshHand(view);
        RefreshPrompt(view);

        _colorOverlay.Visible = _awaitingColor;
        RefreshAbilityOverlay(view);
        RefreshAugmentOverlay(view);
        RefreshGambleOverlay(view);

        if (finished)
        {
            bool wasVisible = _gameOverOverlay.Visible;
            _gameOverOverlay.Visible = true;
            int myRank = view.Ranks[view.PlayerId];
            _gameOverLabel.Text = myRank == 1 ? "우승" : myRank > 0 ? $"{myRank}등" : "게임 종료";
            _gameOverLabel.AddThemeColorOverride("font_color", myRank is > 0 and <= 3 ? RankEmblem.MedalColor(myRank).Lightened(0.15f) : UiTheme.Text);
            _rankEmblem.Rank = myRank;
            _gameOverPanel.AddThemeStyleboxOverride("panel", UiTheme.Ornate(Color.FromHtml("#171b23f5"), RankEmblem.MedalColor(myRank), 32));
            RebuildRankList(view);

            // 혼자 하기: 다시 하기 / 메인으로
            // 방장: 모두 대기방으로 / 바로 한 판 더 (대기방에 있는 사람까지 모두 모읍니다)
            // 참가자: 대기방으로 (방장이 새 판을 시작하면 자동으로 불려 갑니다)
            bool host = _session.IsHost;
            bool solo = _session.IsSolo;
            _roomButton.Visible = !solo;
            _roomButton.Text = host ? "모두 대기방으로" : "대기방으로";
            _againButton.Disabled = !host;
            _againButton.Text = !host ? "방장 대기 중"
                : solo ? "다시 하기"
                : "한 판 더";
            _mainMenuButton.Visible = solo;

            if (!wasVisible)
            {
                // 순위표를 가리지 않도록 떠 있던 알림과 글자 연출을 치웁니다.
                _toastAnim.Stop();
                _toast.Visible = false;
                _fx.ClearTransient();

                // 결과 창이 튀어나오는 연출은 GameOver.tscn의 AppearAnim("appear")입니다.
                _gameOverPanel.PivotOffset = _gameOverPanel.Size / 2;
                _gameOverOverlay.GetNode<AnimationPlayer>("%AppearAnim").Play("appear");
            }
        }
        else
        {
            _gameOverOverlay.Visible = false;
        }
    }

    /// <summary>지금 쌓인 공격 위에 얹을 수 있는 카드 안내입니다.</summary>
    private static string StackHint(PlayerView view)
    {
        var top = view.TopCard.Kind == CardKind.Copy ? view.LastEffect : view.TopCard.Kind;
        return top == CardKind.WildDrawFour ? "아무 +카드나" : $"+{Card.DrawAmount(top)}(문양 상관없음)이나 프리즘 +4";
    }

    private static bool HasBlackHole(PlayerView view, int seat) =>
        view.Augments[seat].Any(a => a.Name == SpecialAugments.NameOf(SpecialAugmentId.BlackHole));

    /// <summary>결과 화면의 순위 줄들을 만듭니다. 메달 색 띠, 아바타, 이름, 순위가 정해진 이유를 보여 줍니다.</summary>
    private void RebuildRankList(PlayerView view)
    {
        foreach (var child in _rankList.GetChildren())
        {
            child.QueueFree();
        }

        var seats = Enumerable.Range(0, view.PlayerCount).Where(i => view.Ranks[i] > 0).OrderBy(i => view.Ranks[i]).ToList();
        for (int n = 0; n < seats.Count; n++)
        {
            int seat = seats[n];
            int rank = view.Ranks[seat];
            bool me = seat == view.PlayerId;
            string name = _session!.NameOf(seat);
            string reason = _placeReasons.TryGetValue(seat, out var r) ? r : "";
            var row = RankRow.Create(rank, name, _session.SteamIdOf(seat), me, reason, view.Eliminated[seat]);
            _rankList.AddChild(row);

            // 위에서부터 차례로 미끄러져 들어옵니다.
            row.Modulate = new Color(1, 1, 1, 0);
            var tween = row.CreateTween();
            tween.TweenInterval(0.25 + n * 0.12);
            tween.TweenProperty(row, "modulate:a", 1f, 0.25);
        }
    }

    /// <summary>상대 자리에 붙일 상태 문구입니다. (억지 뽑기 중, 다른 승리 조건 진행도)</summary>
    private static string SeatStatus(PlayerView view, int seat)
    {
        if (view.Ranks[seat] > 0)
        {
            return view.Eliminated[seat] ? $"탈락 · {view.Ranks[seat]}등" : view.Ranks[seat] == 1 ? "1등" : $"{view.Ranks[seat]}등";
        }

        if (view.HandCounts[seat] >= Rules.EliminationLimit - 4 && !HasBlackHole(view, seat))
        {
            return $"탈락 위험 {view.HandCounts[seat]}/{Rules.EliminationLimit}";
        }

        if (view.SkipNext[seat])
        {
            return "저격당함 · 다음 차례 건너뜀";
        }

        if (view.DraftPending[seat])
        {
            return "증강 고르는 중";
        }

        if (view.PendingPenalty > 0 && view.CurrentPlayer == seat && !view.PayingDebt)
        {
            return $"누적 +{view.PendingPenalty} 받는 중";
        }

        if (view.DebtPlayer == seat)
        {
            return view.DebtRemaining < 0 ? $"{Card.ColorName(view.DebtSuit)} 찾는 중 ({view.DebtDrawn}장)" : $"뽑는 중 {view.DebtDrawn}/{view.DebtDrawn + view.DebtRemaining}";
        }

        if (view.JobProgress[seat] >= 0)
        {
            int total = SpecialAugments.AllJobs.Count;
            return view.JobProgress[seat] >= total
                ? $"어벤져스 집결! 다음 차례에 승리 ({total}/{total})"
                : $"직업 {view.JobProgress[seat]}/{total}";
        }

        if (view.Augments[seat].Any(a => a.Name == SpecialAugments.NameOf(SpecialAugmentId.BlackHole)))
        {
            return $"블랙홀 {view.HandCounts[seat]}/{SpecialAugments.BlackHoleTarget}";
        }

        return "";
    }

    private void RefreshTable(PlayerView view)
    {
        var colorValue = UiTheme.CardColor(view.CurrentColor);
        _ring.Set(colorValue, view.Direction);

        _discard.Card = view.TopCard;
        _discard.HoverExtra = $"현재 문양: {Card.ColorName(view.CurrentColor)}";
        if (view.TopCard.Id != _lastTopCardId)
        {
            _lastTopCardId = view.TopCard.Id;
            PushDiscardPile(_previousTop);
            _previousTop = view.TopCard;
            PlayDiscardPop();
        }

        bool canDraw = view.CanDraw && !_awaitingColor && !_awaitingTarget;
        _drawStack[^1].Playable = canDraw;
        bool penaltyOnMe = view.PendingPenalty > 0 && view.IsMyTurn && !view.PayingDebt;
        _drawStack[^1].Alert = view.MustDraw || penaltyOnMe;
        _drawLabel.Text = view.MustDraw ? "덱을 눌러 뽑기  Space"
            : penaltyOnMe ? $"덱을 눌러 +{view.PendingPenalty} 받기"
            : canDraw ? $"뽑기  {view.DrawPileCount}"
            : $"덱 {view.DrawPileCount}장";
        _drawLabel.AddThemeColorOverride("font_color", view.MustDraw || penaltyOnMe ? UiTheme.Danger : canDraw ? UiTheme.Gold : UiTheme.TextDim);

        // 쌓인 공격 장수를 버린 더미 위에 크게 보여 줍니다.
        _penaltyBadge.Visible = view.PendingPenalty > 0 && !view.Winner.HasValue;
        _penaltyLabel.Text = $"+{view.PendingPenalty}";

        _colorPillLabel.Text = $"{Card.ColorName(view.CurrentColor)}\n" +
                               $"{(view.Direction == 1 ? "시계 방향" : "반시계 방향")}";
        _colorPill.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(colorValue, 0.14f), new Color(colorValue, 0.6f), 1, 6, 10));

        // 혼자 하기는 전송 계층이 없는 방장뿐입니다. Steam 방장도 멀티로 표시합니다.
        string mode = _session!.IsSolo ? "혼자 하기" : _session is ClientSession ? "멀티 · 참가자" : "멀티 · 방장";
        _infoLabel.Text = $"{mode}     {view.Round}라운드  ·  턴 {view.TurnCount + 1}";
    }

    private void RefreshMyArea(PlayerView view)
    {
        _myAvatar.Active = view.IsMyTurn || view.MustDraw;
        string sealedText = view.Sealed[view.PlayerId] ? "  · 봉인" : "";
        _myCountLabel.Text = $"{view.Hand.Count}장{sealedText}";

        var mine = view.Augments[view.PlayerId];
        string signature = string.Join(",", mine.Select(a => a.Name)) + view.Options.SpecialAugments;
        if (signature != _myAugmentSignature)
        {
            _myAugmentSignature = signature;
            RebuildMyAugments(view, mine);
        }

        _myProgress.Text = MyProgressText(view);
        _myProgress.Visible = _myProgress.Text.Length > 0;

        bool choosing = _awaitingColor || _awaitingTarget;
        _drawButton.Disabled = !(view.CanDraw || view.MustDraw) || choosing;
        _drawButton.Text = view.MustDraw
            ? view.DebtRemaining < 0 ? "뽑기 (찾을 때까지)" : $"뽑기 ({view.DebtRemaining}장 남음)"
            : view.PendingPenalty > 0 && view.IsMyTurn ? $"+{view.PendingPenalty} 받기"
            : "카드 뽑기";
        _passButton.Disabled = !view.CanPass || choosing;
        _passButton.Text = view.FrenzyActive ? "연속 내기 끝" : "턴 넘기기";
        _cancelButton.Visible = _awaitingTarget;
    }

    private void RebuildMyAugments(PlayerView view, IReadOnlyList<AugmentInfo> mine)
    {
        foreach (var child in _myAugments.GetChildren())
        {
            child.QueueFree();
        }

        if (!view.Options.SpecialAugments)
        {
            return;
        }

        _myAugments.AddChild(UiTheme.MakeLabel($"특수 증강 {mine.Count}/{SpecialAugments.MaxPerPlayer}", 14, UiTheme.TextDim));
        foreach (var augment in mine)
        {
            _myAugments.AddChild(AugmentChip.Create(augment, 15));
        }
    }

    /// <summary>내 증강 진행 상황입니다. (다음 증강까지 남은 차례, 다른 승리 조건 진행도)</summary>
    private static string MyProgressText(PlayerView view)
    {
        var parts = new List<string>();
        if (view.IsActive(view.PlayerId) && !HasBlackHole(view, view.PlayerId) && view.Hand.Count >= Rules.EliminationLimit - 6)
        {
            parts.Add($"⚠ 탈락 위험! 손패 {view.Hand.Count}/{Rules.EliminationLimit}장");
        }

        var mine = view.Augments[view.PlayerId];

        if (view.Jobs.Count > 0)
        {
            var have = view.Jobs.Select(j => j.Job).Distinct().ToList();
            var missing = SpecialAugments.AllJobs.Where(j => !have.Contains(j)).Select(SpecialAugments.JobName);
            parts.Add(have.Count >= SpecialAugments.AllJobs.Count
                ? "어벤져스 집결! 이대로 내 차례가 다시 오면 승리 — 직업 카드를 지키세요"
                : $"직업 {have.Count}/{SpecialAugments.AllJobs.Count} (없음: {string.Join(", ", missing)})");
        }

        if (mine.Any(a => a.Name == SpecialAugments.NameOf(SpecialAugmentId.BlackHole)))
        {
            parts.Add($"블랙홀 {view.Hand.Count}/{SpecialAugments.BlackHoleTarget}장");
        }

        if (mine.Any(a => a.Name == SpecialAugments.NameOf(SpecialAugmentId.Collector)))
        {
            parts.Add($"수집 {GameEngine.CollectorProgress(view.Hand)}/4 문양");
        }

        if (mine.Any(a => a.Name == SpecialAugments.NameOf(SpecialAugmentId.SuitLord)) && view.LordSuit != CardColor.Wild)
        {
            parts.Add($"내 문양: {Card.ColorName(view.LordSuit)}");
        }

        if (view.AugmentCountdown > 0)
        {
            parts.Add($"다음 증강까지 내 차례 {view.AugmentCountdown}번");
        }

        return string.Join("   ·   ", parts);
    }

    private void RefreshHand(PlayerView view)
    {
        // 내용이 그대로면 다시 만들지 않습니다. (마우스를 올린 카드의 팝업이 깜빡이지 않게 합니다.)
        string signature = string.Join(",", view.Hand.Select(c => c.Id)) + "|" +
                           string.Join(",", view.PlayableCardIds) + "|" +
                           $"{view.IsMyTurn}{_pendingCard?.Id}{_awaitingColor}{_awaitingTarget}{view.DrawnCard?.Id}{view.LastEffect}{view.Jobs.Count}";
        if (signature == _handSignature)
        {
            return;
        }

        _handSignature = signature;

        foreach (var child in _hand.GetChildren())
        {
            _hand.RemoveChild(child);
            child.QueueFree();
        }

        var sorted = view.Hand
            .OrderBy(c => c.Color)
            .ThenBy(c => c.Kind)
            .ThenBy(c => c.Number);

        foreach (var card in sorted)
        {
            bool playable = view.PlayableCardIds.Contains(card.Id);
            var cardView = new CardView
            {
                Card = card,
                LiftOnHover = true,
                Playable = playable && !_awaitingTarget && !_awaitingColor,
                Dimmed = view.IsMyTurn && !playable,
                Selected = _pendingCard?.Id == card.Id,
                JustDrawn = view.DrawnCard?.Id == card.Id,
                HoverExtra = HandCardExtra(card, view, playable),
            };

            var job = view.Jobs.FirstOrDefault(j => j.CardId == card.Id);
            if (job != null)
            {
                cardView.Badge = SpecialAugments.JobName(job.Job);
                cardView.BadgeColor = Color.FromHtml(SpecialAugments.JobColorHex(job.Job));
            }

            cardView.Clicked += OnHandCardClicked;
            _hand.AddChild(cardView);
        }
    }

    private static string HandCardExtra(Card card, PlayerView view, bool playable)
    {
        var lines = new List<string>();

        if (card.Kind == CardKind.Copy)
        {
            lines.Add(view.LastEffect is CardKind.Number or CardKind.Wild
                ? "지금 내면: 복사할 효과가 없어요."
                : $"지금 내면: [{Card.KindName(view.LastEffect)}] 효과가 발동해요.");
        }

        var job = view.Jobs.FirstOrDefault(j => j.CardId == card.Id);
        if (job != null)
        {
            int same = view.Jobs.Count(j => j.Job == job.Job);
            lines.Add($"[어벤져스] 직업: {SpecialAugments.JobName(job.Job)}" + (same > 1 ? $" (같은 직업 {same}장 — 내도 괜찮아요)" : " (하나뿐인 직업!)"));
        }

        if (view.TargetRequiredIds.Contains(card.Id))
        {
            lines.Add("낸 뒤 위쪽 상대를 클릭해서 대상을 골라요.");
        }

        if (view.IsMyTurn)
        {
            lines.Add(playable ? "클릭해서 내기" : "지금은 낼 수 없어요.");
        }

        return string.Join("\n", lines);
    }

    private void RefreshPrompt(PlayerView view)
    {
        string turnName = _session!.NameOf(view.CurrentPlayer);
        string prompt;

        if (view.Winner.HasValue)
        {
            prompt = "게임 종료";
        }
        else if (!view.IsActive(view.PlayerId))
        {
            int rank = view.Ranks[view.PlayerId];
            int left = Enumerable.Range(0, view.PlayerCount).Count(view.IsActive);
            prompt = (view.Eliminated[view.PlayerId] ? $"탈락 ({rank}등)  ·  " : $"{rank}등 확정  ·  ")
                     + $"남은 {left}명이 순위를 가리는 중 (관전)";
        }
        else if (view.DrawChoices.Count > 0)
        {
            prompt = "[도박사] 뽑은 2장 중 가질 카드를 고르세요.";
        }
        else if (view.MustDraw)
        {
            string goal = view.DebtRemaining < 0
                ? $"{Card.ColorName(view.DebtSuit)} 카드가 나올 때까지 (지금 {view.DebtDrawn}장)"
                : $"{view.DebtDrawn}/{view.DebtDrawn + view.DebtRemaining}장";
            prompt = $"[{view.DebtReason}] 덱을 눌러 뽑으세요  {goal}";
        }
        else if (view.PayingDebt)
        {
            string who = _session.NameOf(view.DebtPlayer);
            prompt = view.DebtRemaining < 0
                ? $"{who}가 {Card.ColorName(view.DebtSuit)} 카드를 찾아 뽑는 중... ({view.DebtDrawn}장째)"
                : $"{who}가 카드를 뽑는 중... ({view.DebtDrawn}/{view.DebtDrawn + view.DebtRemaining})";
        }
        else if (view.PendingPenalty > 0 && view.IsMyTurn && view.AugmentChoices.Count == 0)
        {
            prompt = view.PlayableCardIds.Count > 0
                ? $"+{view.PendingPenalty} 공격  ·  {StackHint(view)}로 넘기거나 {view.PendingPenalty}장 받기"
                : $"+{view.PendingPenalty} 공격  ·  덱을 눌러 {view.PendingPenalty}장 받기";
        }
        else if (view.PendingPenalty > 0 && !view.IsMyTurn)
        {
            prompt = $"+{view.PendingPenalty} 공격이 {turnName}에게 넘어갔습니다";
        }
        else if (view.AugmentChoices.Count > 0)
        {
            prompt = "특수 증강을 하나 고르세요";
        }
        else if (view.DraftPending.Any(d => d))
        {
            int left = view.DraftPending.Count(d => d);
            prompt = $"다른 사람들이 첫 특수 증강을 고르는 중... ({left}명 남음)";
        }
        else if (view.ChoosingAugment && !view.IsMyTurn)
        {
            prompt = $"{turnName}가 특수 증강을 고르는 중...";
        }
        else if (_pendingAbility.HasValue && view.AbilityChoices.Count > _pendingAbility.Value)
        {
            string abilityName = view.AbilityChoices[_pendingAbility.Value].Name;
            prompt = _awaitingTarget
                ? $"[{abilityName}] 대상을 고르세요. 위쪽의 상대를 클릭하세요. (취소하면 다시 고를 수 있어요)"
                : $"[{abilityName}] 문양을 고르세요.";
        }
        else if (_awaitingColor)
        {
            prompt = $"[{_pendingCard}] 바꿀 문양을 고르세요.";
        }
        else if (_awaitingTarget)
        {
            prompt = $"[{_pendingCard}] 대상을 고르세요. 위쪽의 상대를 클릭하세요.";
        }
        else if (view.ChoosingAbility && !view.IsMyTurn)
        {
            prompt = $"{turnName}가 각성 능력을 고르는 중...";
        }
        else if (view.AbilityChoices.Count > 0)
        {
            prompt = "각성 능력을 하나 고르세요";
        }
        else if (!view.IsMyTurn)
        {
            prompt = string.IsNullOrEmpty(_lastAction) ? $"{turnName}의 차례..." : $"{_lastAction}\n{turnName}의 차례...";
        }
        else if (view.FrenzyActive)
        {
            prompt = view.FrenzyNumber >= 0
                ? $"[쌍둥이] 숫자 {view.FrenzyNumber} 카드를 문양 상관없이 이어서 내거나 '연속 내기 끝'을 누르세요."
                : $"연속 내기  ·  {Card.ColorName(view.FrenzyColor)} 숫자 카드를 계속 낼 수 있습니다";
        }
        else if (view.HasDrawnThisTurn)
        {
            prompt = "뽑은 카드를 내거나 턴을 넘기세요";
        }
        else if (view.PlayableCardIds.Count == 0)
        {
            prompt = "낼 카드가 없습니다. 덱에서 뽑으세요";
        }
        else
        {
            prompt = view.Sealed[view.PlayerId]
                ? "봉인  ·  숫자 카드만 낼 수 있습니다"
                : "내 차례";
        }

        _promptLabel.Text = string.IsNullOrEmpty(_notice) ? prompt : $"{_notice}\n{prompt}";

        bool urgent = view.MustDraw;
        bool active = view.IsMyTurn || urgent;
        var accent = urgent ? UiTheme.Danger : active || _awaitingTarget ? UiTheme.Gold : UiTheme.PanelBorder;
        _promptPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(urgent ? new Color(0.22f, 0.05f, 0.06f, 0.92f) : UiTheme.Panel, accent, 1, 6, 8));
        _promptLabel.AddThemeColorOverride("font_color", urgent ? Color.FromHtml("#ffb3a8") : active ? UiTheme.Gold : UiTheme.Text);
    }

    private void RefreshLobby()
    {
        if (_session == null)
        {
            return;
        }

        var names = _session.LobbyNames;
        bool isHost = _session.IsHost;

        foreach (var child in _roomPlayers.GetChildren())
        {
            child.QueueFree();
        }

        // 자리 순서: 사람(방장 먼저) → 봇 → 빈자리. 한 줄의 모양은 scenes/ui/RoomRow.tscn에 있습니다.
        var host = _session as HostSession;
        bool canEdit = isHost && !_session.GameRunning;
        for (int i = 0; i < names.Length; i++)
        {
            ulong steamId = i < _session.LobbySteamIds.Length ? _session.LobbySteamIds[i] : 0;
            bool busy = i < _session.LobbyBusy.Length && _session.LobbyBusy[i];
            bool canKick = i > 0 && isHost;
            var row = RoomRow.Create(names[i], steamId, busy, i == 0, canKick);
            if (canKick && host != null)
            {
                int index = i;
                row.ButtonPressed += () => host.Kick(index);
            }

            _roomPlayers.AddChild(row);
        }

        int bots = Math.Min(_session.LobbyBots, HostSession.SeatCount - names.Length);
        for (int b = 0; b < bots; b++)
        {
            // 게임에서 쓰는 이름과 같게 "봇 A, 봇 B…"로 보여 줍니다. 빼기는 맨 뒤 봇부터 뺍니다.
            var row = RoomRow.CreateBot($"봇 {(char)('A' + b)}", canEdit && b == bots - 1);
            if (host != null)
            {
                row.ButtonPressed += host.RemoveBot;
            }

            _roomPlayers.AddChild(row);
        }

        for (int e = names.Length + bots; e < HostSession.SeatCount; e++)
        {
            var row = RoomRow.CreateEmpty(canEdit && e == names.Length + bots);
            if (host != null)
            {
                row.ButtonPressed += host.AddBot;
            }

            _roomPlayers.AddChild(row);
        }

        _updatingToggle = true;
        _augmentToggle.ButtonPressed = _session.RoomOptions.SpecialAugments;
        _botLevelOption.Selected = (int)_session.LobbyBotLevel;
        _updatingToggle = false;

        bool running = _session.GameRunning;
        _augmentToggle.Disabled = !isHost || running;
        _botLevelOption.Disabled = !isHost || running;
        bool enoughPlayers = names.Length + _session.LobbyBots >= HostSession.MinPlayers;
        _startButton.Visible = isHost;
        _startButton.Disabled = running || !enoughPlayers;
        _startButton.Text = running ? "게임 진행 중" : "게임 시작";
        _endGameButton.Visible = isHost && running;

        if (!_session.Playing && names.Length > 0)
        {
            _lobbyStatus.Text = RoomStatusText();
        }
    }

    /// <summary>대기방 아래쪽 안내 문구입니다.</summary>
    private string RoomStatusText()
    {
        if (_session == null)
        {
            return "";
        }

        if (_session.GameRunning)
        {
            int playing = _session.LobbyBusy.Count(b => b);
            return _session.IsHost
                ? $"{playing}명이 게임 중입니다. '게임 끝내기'로 모두 대기방으로 부를 수 있습니다."
                : $"{playing}명이 게임 중입니다. 판이 끝나면 함께할 수 있습니다.";
        }

        if (_session.IsHost && _session.LobbyNames.Length + _session.LobbyBots < HostSession.MinPlayers)
        {
            return "혼자서는 시작할 수 없어요. 봇을 넣거나 친구를 초대하세요.";
        }

        return _session.IsHost
            ? "준비되면 게임을 시작하세요."
            : "방장이 게임을 시작하기를 기다리는 중";
    }

    /// <summary>Steam 친구 목록을 다시 불러와서 초대 버튼을 만듭니다.</summary>
    private void RefreshFriends()
    {
        foreach (var child in _friendList.GetChildren())
        {
            child.QueueFree();
        }

        var friends = SteamRuntime.GetFriends();
        if (friends.Count == 0)
        {
            _friendList.AddChild(UiTheme.MakeLabel("친구 목록을 불러오지 못했어요.", 13, UiTheme.TextDim));
            return;
        }

        foreach (var friend in friends.Take(60))
        {
            var row = FriendRow.Create(friend);
            row.Invited += (name, ok) =>
                _lobbyStatus.Text = ok ? $"{name}님에게 초대를 보냈습니다." : "초대를 보내지 못했습니다. Steam 연결을 확인해 주세요.";
            _friendList.AddChild(row);
        }
    }

    // ───────────── 연출 ─────────────

    /// <summary>
    /// 상대 인원에 따라 위쪽 세 칸(1: 왼쪽, 2: 가운데, 3: 오른쪽) 중 어디에 앉힐지 정합니다.
    /// 차례 순서대로 왼쪽 → 오른쪽입니다.
    /// </summary>
    private static int[] OpponentSlots(int players) => players switch
    {
        2 => new[] { 2 },
        3 => new[] { 1, 3 },
        _ => new[] { 1, 2, 3 },
    };

    /// <summary>플레이어 자리의 화면 위치입니다. 내 자리는 손패 가운데입니다.</summary>
    private Vector2 SeatAnchor(int playerId)
    {
        if (_session == null || playerId == _session.MySeat || playerId < 0)
        {
            return _hand.GetGlobalRect().GetCenter() + new Vector2(0, -70);
        }

        foreach (var seat in _seats)
        {
            if (seat != null && seat.PlayerId == playerId)
            {
                return seat.GetGlobalRect().GetCenter();
            }
        }

        return _table.GetGlobalRect().GetCenter();
    }

    private Vector2 DiscardCenter => _discard.GetGlobalRect().GetCenter();

    private Vector2 DrawPileCenter => _drawStack[^1].GetGlobalRect().GetCenter();

    /// <summary>
    /// 엔진에서 온 사건을 화면 연출로 바꿉니다. 규칙에는 영향을 주지 않습니다.
    /// </summary>
    /// <summary>도감: 내가 얻은 특수 증강과 내가 발동한 각성 능력을 기록합니다.</summary>
    private static void RecordCollection(GameEvent e)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (e.Type == GameEventType.AugmentGained)
        {
            Collection.AddAugment(e.Text);
        }
        else if (e.Type == GameEventType.AbilityUsed)
        {
            Collection.AddAbility(e.Text);
        }
    }

    private void OnFx(GameEvent e)
    {
        if (_session == null || !IsInsideTree() || !_session.Playing)
        {
            return;
        }

        bool me = e.Player == _session.MySeat;
        bool targetMe = e.Target == _session.MySeat;
        string who = _session.NameOf(e.Player);
        PlayEventSound(e, me, targetMe);

        if (me)
        {
            RecordCollection(e);
        }

        switch (e.Type)
        {
            case GameEventType.CardPlayed when e.Card != null:
            {
                if (!me)
                {
                    _fx.FlyCard(SeatAnchor(e.Player), DiscardCenter, e.Card, 0.28f, 0f, new Vector2(96, 138));
                }

                var color = e.Card.Color == CardColor.Wild ? UiTheme.Gold : UiTheme.CardColor(e.Card.Color);
                _fx.Burst(DiscardCenter, color, e.Card.IsAction ? 26 : 12, e.Card.IsAction ? 340 : 220, 4f);
                if (e.Card.IsAction)
                {
                    _fx.Ring(DiscardCenter, color, 140, 0.4f);
                }

                break;
            }

            case GameEventType.CardDrawn:
                _fx.FlyCard(DrawPileCenter, SeatAnchor(e.Player), null, 0.26f, 0f, new Vector2(70, 100));
                if (_session.View?.PayingDebt == true)
                {
                    _fx.Shake(targetMe || me ? 5f : 2.5f, 0.15f);
                }

                break;

            case GameEventType.ForcedDrawStarted:
            {
                string text = e.Amount < 0 ? $"{e.Text}!\n나올 때까지 뽑아라!" : $"{e.Amount}장 뽑아라!";
                _fx.FloatText(SeatAnchor(e.Target) + new Vector2(0, 46), text, Color.FromHtml("#ffb3a8"), targetMe ? 40 : 26, 0.9f);
                if (targetMe)
                {
                    _fx.Flash(UiTheme.Danger, 0.22f, 0.45f);
                    _fx.Shake(10f, 0.35f);
                }

                break;
            }

            case GameEventType.Attack:
            {
                var at = SeatAnchor(e.Target);
                _fx.Flash(new Color(1f, 0.15f, 0.1f), targetMe ? 0.35f : 0.16f, 0.4f);
                _fx.Shake(Mathf.Min(6f + e.Amount * 2.5f, 26f), 0.45f);
                _fx.FloatText(at, $"+{e.Amount}", UiTheme.Danger, targetMe ? 64 : 48, 0.7f);
                _fx.Burst(at, UiTheme.Danger, 30 + e.Amount * 4, 380, 5f);
                _fx.Ring(at, UiTheme.Danger, 160, 0.5f);
                break;
            }

            case GameEventType.Skip:
                _fx.FloatText(SeatAnchor(e.Target), "건너뜀", Color.FromHtml("#ffb070"), 34, 0.6f);
                _fx.Shake(6f, 0.25f);
                break;

            case GameEventType.Reverse:
                _fx.FloatText(_ring.GetGlobalRect().GetCenter() + new Vector2(0, -90), "방향 전환", Color.FromHtml("#9fe3ff"), 32, 0.5f);
                _fx.Ring(_ring.GetGlobalRect().GetCenter(), Color.FromHtml("#9fe3ff"), 220, 0.6f);
                break;

            case GameEventType.Awaken:
                // 각성: 빛줄기 + 번쩍임 + 지진입니다. 연출이 끝난 뒤에 능력 선택 창이 뜹니다.
                _fx.Flash(Colors.White, 0.75f, 0.7f);
                _fx.Rays(DiscardCenter, UiTheme.Prism, 520, 1.6f);
                _fx.Rays(DiscardCenter, UiTheme.Gold, 380, 1.3f);
                _fx.Ring(DiscardCenter, UiTheme.Gold, 420, 0.8f);
                _fx.Burst(DiscardCenter, UiTheme.Prism, 70, 560, 6f, 120f);
                _fx.Burst(DiscardCenter, UiTheme.Gold, 50, 420, 5f, 120f);
                _fx.Shake(22f, 1.0f);
                _fx.FloatText(DiscardCenter + new Vector2(0, -120), me ? "각성" : $"{who} 각성", UiTheme.Gold, 58, 0.9f);
                HoldOverlays(1.1f);
                break;

            case GameEventType.AbilityUsed:
                ShowToast($"{who} 각성\n{e.Text}", UiTheme.Prism);
                _fx.Flash(UiTheme.Prism, 0.3f, 0.5f);
                _fx.Shake(12f, 0.5f);
                _fx.Burst(SeatAnchor(e.Player), UiTheme.Prism, 40, 420, 5f);
                if (e.Target >= 0)
                {
                    _fx.Ring(SeatAnchor(e.Target), UiTheme.Prism, 200, 0.6f);
                }

                break;

            case GameEventType.AugmentOffered:
                if (me)
                {
                    // 선택 창 제목과 겹치지 않도록 글자 없이 짧은 번쩍임만 줍니다.
                    _fx.Flash(UiTheme.Prism, 0.2f, 0.4f);
                    _fx.Shake(4f, 0.3f);
                    HoldOverlays(0.45f);
                }
                else
                {
                    _fx.FloatText(SeatAnchor(e.Player), "증강 선택 중", UiTheme.TextDim, 18, 0.8f);
                }

                break;

            case GameEventType.AugmentGained:
            {
                var tier = SpecialAugments.All.Where(id => SpecialAugments.NameOf(id) == e.Text).Select(SpecialAugments.TierOf).FirstOrDefault();
                var color = UiTheme.TierColor(tier);
                // 남의 증강은 알림 창 대신 자리 위 글자로만 보여 줍니다. (선택 창을 가리지 않게)
                if (me)
                {
                    ShowToast($"특수 증강 획득\n{e.Text}", color);
                }
                else
                {
                    _fx.FloatText(SeatAnchor(e.Player) + new Vector2(0, 40), $"{e.Text}", color, 24, 0.9f);
                }

                _fx.Burst(SeatAnchor(e.Player), color, 50, 460, 5f, 150f);
                _fx.Ring(SeatAnchor(e.Player), color, 220, 0.7f);
                break;
            }

            case GameEventType.HandsShuffled:
                ShowToast(e.Text, UiTheme.Prism);
                _fx.Shake(16f, 0.7f);
                _fx.Flash(Color.FromHtml("#8a5cff"), 0.35f, 0.6f);
                int count = Math.Max(2, _session?.Names.Length ?? SeatCount);
                foreach (var seat in Enumerable.Range(0, count))
                {
                    _fx.FlyCard(SeatAnchor(seat), SeatAnchor((seat + 1) % count), null, 0.5f, 0.05f * seat, new Vector2(70, 100));
                }

                break;

            case GameEventType.Immune:
                _fx.FloatText(SeatAnchor(e.Target), "무효", Color.FromHtml("#9fe3ff"), 34, 0.6f);
                _fx.Ring(SeatAnchor(e.Target), Color.FromHtml("#9fe3ff"), 150, 0.5f);
                break;

            case GameEventType.Placed:
            {
                _placeReasons[e.Player] = e.Text;
                bool eliminated = e.Text.StartsWith("탈락");
                var color = eliminated ? UiTheme.Danger : UiTheme.Silver;
                ShowToast(eliminated ? $"{who} 탈락\n{e.Amount}등" : $"{who}\n{e.Amount}등 확정", color);
                _fx.FloatText(SeatAnchor(e.Player), eliminated ? "탈락" : $"{e.Amount}등", color, me ? 56 : 40, 0.9f);
                if (eliminated)
                {
                    _fx.Flash(UiTheme.Danger, me ? 0.4f : 0.2f, 0.6f);
                    _fx.Shake(me ? 16f : 8f, 0.5f);
                }
                else
                {
                    _fx.Burst(SeatAnchor(e.Player), color, 50, 420, 5f);
                }

                break;
            }

            case GameEventType.Win:
                _winReason = e.Text;
                _placeReasons[e.Player] = e.Text;
                ShowToast(me ? "우승" : $"{who}\n1등", UiTheme.Gold);
                _fx.Confetti(me ? 260 : 140);
                _fx.Flash(UiTheme.Gold, me ? 0.6f : 0.3f, 0.9f);
                _fx.Shake(me ? 14f : 6f, 0.6f);
                _fx.Burst(SeatAnchor(e.Player), UiTheme.Gold, 80, 520, 6f);
                break;
        }
    }

    /// <summary>사건에 맞는 효과음을 냅니다. 나와 관련된 소리는 조금 더 크게 냅니다.</summary>
    private static void PlayEventSound(GameEvent e, bool me, bool targetMe)
    {
        switch (e.Type)
        {
            case GameEventType.CardPlayed when e.Card != null:
                // 카드를 내려놓는 실제 녹음 소리에, 액션 카드는 합성 타격음을 살짝 겹칩니다.
                Audio.Sfx.Play("play", me ? 0f : -3f);
                if (e.Card.IsAction)
                {
                    Audio.Sfx.Play("play_action", me ? -5f : -8f);
                }
                break;
            case GameEventType.CardDrawn:
                Audio.Sfx.Play("draw", me ? -2f : -8f, 1f, 0.08f);
                break;
            case GameEventType.ForcedDrawStarted when targetMe:
                Audio.Sfx.Play("warn", -2f);
                break;
            case GameEventType.Attack:
                // 장수가 많을수록 낮고 묵직하게 들립니다.
                Audio.Sfx.Play("attack", targetMe ? 1f : -3f, Mathf.Clamp(1.15f - e.Amount * 0.03f, 0.8f, 1.15f));
                break;
            case GameEventType.Skip:
                Audio.Sfx.Play("skip", targetMe ? 0f : -4f);
                break;
            case GameEventType.Reverse:
                Audio.Sfx.Play("reverse", -3f);
                break;
            case GameEventType.Awaken:
                Audio.Sfx.Play("awaken", 1f, 1f, 0f);
                break;
            case GameEventType.AbilityUsed:
                Audio.Sfx.Play("ability", -1f);
                break;
            case GameEventType.AugmentOffered:
                Audio.Sfx.Play("augment_offer", me ? 0f : -8f, 1f, 0f);
                break;
            case GameEventType.AugmentGained:
                Audio.Sfx.Play("augment_gain", me ? 0f : -5f, 1f, 0f);
                break;
            case GameEventType.HandsShuffled:
                Audio.Sfx.Play("shuffle", -1f);
                break;
            case GameEventType.Immune:
                Audio.Sfx.Play("shield", -3f);
                break;
            case GameEventType.Win:
                Audio.Sfx.Play(me ? "win" : "augment_gain", me ? 0f : -6f, 1f, 0f);
                break;
            case GameEventType.Placed:
                Audio.Sfx.Play(e.Text.StartsWith("탈락") ? "lose" : me ? "augment_gain" : "shield", me ? 0f : -5f, 1f, 0f);
                break;
        }
    }

    /// <summary>화면 가운데에 잠깐 떴다가 사라지는 알림을 띄웁니다.</summary>
    private void ShowToast(string text, Color color)
    {
        _toastLabel.Text = text;
        _toastLabel.AddThemeColorOverride("font_color", color);
        _toast.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.06f, 0.08f, 0.94f), new Color(color, 0.7f), 1, 8, 18));
        _toast.Visible = true;
        _toast.Modulate = Colors.White;
        _toast.Size = _toast.GetCombinedMinimumSize();
        _toast.Position = _table.GetGlobalRect().GetCenter() - _toast.Size / 2;
        _toast.PivotOffset = _toast.Size / 2;

        // 튀어나왔다가 사라지는 연출은 AnimationPlayer(ToastAnim의 "show")가 합니다.
        _toastAnim.Stop();
        _toastAnim.Play("show");
    }

    /// <summary>
    /// 방금 덮인 카드를 버린 더미 아래로 밀어 넣습니다. 새 판이 시작되면(card가 null) 더미를 비웁니다.
    /// 가장 오래된 카드가 맨 아래에 있고, 장마다 위치와 각도를 무작위로 어긋나게 둡니다.
    /// </summary>
    private void PushDiscardPile(Card? card)
    {
        if (card == null)
        {
            foreach (var pile in _discardPile)
            {
                pile.Visible = false;
                pile.Card = null;
            }

            return;
        }

        // 한 칸씩 아래로 내립니다. (0번이 맨 아래)
        for (int i = 0; i < _discardPile.Length - 1; i++)
        {
            _discardPile[i].Card = _discardPile[i + 1].Card;
            _discardPile[i].Visible = _discardPile[i + 1].Visible;
            _pileJitter[i] = _pileJitter[i + 1];
        }

        int top = _discardPile.Length - 1;
        _discardPile[top].Card = card;
        _discardPile[top].Visible = true;
        _pileJitter[top] = (
            new Vector2((float)GD.RandRange(-18.0, 18.0), (float)GD.RandRange(-12.0, 12.0)),
            (float)GD.RandRange(-0.45, 0.45));
    }

    private void PlayDiscardPop()
    {
        _discard.PivotOffset = _discard.Size / 2;
        _discard.Scale = new Vector2(1.25f, 1.25f);
        _discard.Rotation = (float)GD.RandRange(-0.14, 0.14);
        var tween = CreateTween();
        tween.TweenProperty(_discard, "scale", Vector2.One, 0.22)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private void AppendLog(string message)
    {
        if (message.StartsWith("  ") && !message.StartsWith("    "))
        {
            _lastAction = message.Trim();
        }

        // 언어를 바꿨을 때 다시 그릴 수 있게 원문(한국어)을 모아 둡니다.
        _logHistory.Add(message);
        if (_logHistory.Count > 600)
        {
            _logHistory.RemoveRange(0, 100);
        }

        RenderLog(message);
    }

    /// <summary>기록 한 줄을 현재 언어로 바꿔 기록 창에 붙입니다.</summary>
    private void RenderLog(string original)
    {
        string message = Loc.Tr(original);
        string trimmed = message.TrimStart();
        string escaped = Escape(trimmed);
        int indent = message.Length - trimmed.Length;
        string prefix = indent >= 4 ? "      " : indent >= 2 ? "  " : "";
        string myName = Loc.Tr(_session?.NameOf(_session.MySeat) ?? "");

        // 엔진 로그의 ★·✦ 표시는 색으로만 강조하고 기호는 지웁니다.
        bool highlight = message.Contains('★');
        escaped = escaped.Replace("★", "").Replace("✦", "").Trim();
        if (highlight)
        {
            escaped = $"[color=#f0b849]{escaped}[/color]";
        }
        else if (!string.IsNullOrEmpty(myName) && trimmed.StartsWith(myName + ":"))
        {
            escaped = $"[color=#9cc4ff]{escaped}[/color]";
        }
        else if (indent >= 4)
        {
            escaped = $"[color=#8b93a1]{escaped}[/color]";
        }

        _logLabel.AppendText(prefix + escaped + "\n");
    }

    /// <summary>카드 이름의 대괄호가 BBCode 태그로 해석되지 않도록 바꿉니다.</summary>
    private static string Escape(string text) => text.Replace("[", "[lb]");

    // ───────────── 씬 연결 ─────────────

    /// <summary>
    /// Game.tscn(과 그 안의 화면 씬)에 있는 노드를 찾아 필드에 넣고 버튼 이벤트를 연결합니다.
    /// 화면 배치와 모양은 모두 씬 파일에 있고, 여기서는 동작만 붙입니다. (%이름 = 씬 고유 이름)
    /// </summary>
    private void BindUi()
    {
        // 게임 화면 (Game.tscn)
        _shakeRoot = GetNode<Control>("%Layout");
        _infoLabel = GetNode<Label>("%InfoLabel");
        GetNode<Button>("%SettingsButton").Pressed += ToggleSettings;
        GetNode<Button>("%GuideButton").Pressed += () => _guide.Open();
        GetNode<Button>("%LogButton").Pressed += () => _logPanel.Visible = !_logPanel.Visible;
        _newGameButton = GetNode<Button>("%NewGameButton");
        _newGameButton.Pressed += () =>
        {
            if (_session?.IsSolo == true)
            {
                _session.Restart();
            }
            else
            {
                _session?.ReturnToRoom();
            }
        };
        GetNode<Button>("%LeaveButton").Pressed += LeavePressed;

        for (int k = 1; k < SeatCount; k++)
        {
            _seats[k] = GetNode<SeatView>($"%Seat{k}");
            _seats[k].Assign(k, $"P{k}");
            _seats[k].Clicked += OnSeatClicked;
        }

        // 테이블 (배치는 LayoutTable에서 매 프레임 가운데 기준으로 잡습니다)
        _table = GetNode<Control>("%Table");
        _ring = GetNode<DirectionRing>("%Ring");
        for (int i = 0; i < _drawStack.Length; i++)
        {
            _drawStack[i] = GetNode<CardView>($"%Draw{i}");
            _drawStack[i].FaceDown = true;
            _drawStack[i].MouseFilter = MouseFilterEnum.Ignore;
        }

        // 맨 위 카드만 클릭을 받습니다.
        _drawStack[^1].MouseFilter = MouseFilterEnum.Stop;
        _drawStack[^1].Clicked += OnDrawPileClicked;
        _drawLabel = GetNode<Label>("%DrawLabel");
        for (int i = 0; i < _discardPile.Length; i++)
        {
            _discardPile[i] = GetNode<CardView>($"%Pile{i}");
        }

        _discard = GetNode<CardView>("%Discard");
        _colorPill = GetNode<PanelContainer>("%ColorPill");
        _colorPillLabel = GetNode<Label>("%ColorPillLabel");
        _penaltyBadge = GetNode<PanelContainer>("%PenaltyBadge");
        _penaltyLabel = GetNode<Label>("%PenaltyLabel");
        _promptPanel = GetNode<PanelContainer>("%PromptPanel");
        _promptLabel = GetNode<Label>("%PromptLabel");

        // 내 정보 줄
        _myAvatar = GetNode<AvatarView>("%MyAvatar");
        _myAvatar.Letter = "나";
        _myNameLabel = GetNode<Label>("%MyName");
        _myCountLabel = GetNode<Label>("%MyCount");
        _myAugments = GetNode<HBoxContainer>("%MyAugments");
        _myProgress = GetNode<Label>("%MyProgress");
        _cancelButton = GetNode<Button>("%CancelButton");
        _cancelButton.Pressed += () => { ClearPending(); Refresh(); };
        _drawButton = GetNode<Button>("%DrawButton");
        _drawButton.Pressed += DrawPressed;
        _passButton = GetNode<Button>("%PassButton");
        _passButton.Pressed += () => Submit(PlayerAction.Pass());
        _hand = GetNode<HandView>("%Hand");
        _logPanel = GetNode<PanelContainer>("%LogPanel");
        _logLabel = GetNode<RichTextLabel>("%LogLabel");

        // 선택 창들
        _colorOverlay = GetNode<Control>("%ColorPicker");
        var suitRow = _colorOverlay.GetNode<HBoxContainer>("%SuitRow");
        foreach (var color in Deck.Colors)
        {
            var c = color;
            var button = new SuitButton(c) { CustomMinimumSize = new Vector2(116, 130) };
            button.Pressed += () => OnColorPicked(c);
            suitRow.AddChild(button);
        }

        _colorOverlay.GetNode<Button>("%CancelButton").Pressed += () => { ClearPending(); Refresh(); };

        var ability = GetNode<ChoiceOverlay>("%AbilityOverlay");
        _abilityOverlay = ability;
        _abilityRow = ability.Row;
        var augment = GetNode<ChoiceOverlay>("%AugmentOverlay");
        _augmentOverlay = augment;
        _augmentRow = augment.Row;
        augment.SubtitleLabel.Text = $"이번 판 동안 계속 적용됩니다  ·  최대 {SpecialAugments.MaxPerPlayer}개";
        var gamble = GetNode<ChoiceOverlay>("%GambleOverlay");
        _gambleOverlay = gamble;
        _gambleRow = gamble.Row;

        // 결과 화면 (screens/GameOver.tscn)
        _gameOverOverlay = GetNode<Control>("%GameOver");
        _gameOverPanel = _gameOverOverlay.GetNode<PanelContainer>("%Panel");
        _rankEmblem = _gameOverOverlay.GetNode<RankEmblem>("%RankEmblem");
        _gameOverLabel = _gameOverOverlay.GetNode<Label>("%ResultTitle");
        _rankList = _gameOverOverlay.GetNode<VBoxContainer>("%RankList");
        _roomButton = _gameOverOverlay.GetNode<Button>("%RoomButton");
        _roomButton.Pressed += () =>
        {
            if (_session?.IsHost == true)
            {
                _session.ReturnToRoom();
            }
            else
            {
                _session?.LeaveGame();
            }
        };
        _againButton = _gameOverOverlay.GetNode<Button>("%AgainButton");
        _againButton.Pressed += () => _session?.Restart();
        _mainMenuButton = _gameOverOverlay.GetNode<Button>("%MainMenuButton");
        _mainMenuButton.Pressed += () => BackToLobby("");

        _toast = GetNode<PanelContainer>("%Toast");
        _toastLabel = GetNode<Label>("%ToastLabel");
        _toastAnim = GetNode<AnimationPlayer>("%ToastAnim");

        _guide = GetNode<GuideView>("%Guide");
        BindLobby();
        BindSettings();

        // 초대 알림 (ui/InviteBanner.tscn)
        _inviteBanner = GetNode<PanelContainer>("%InviteBanner");
        _inviteLabel = _inviteBanner.GetNode<Label>("%InviteLabel");
        _inviteBanner.GetNode<Button>("%InviteJoinButton").Pressed += () =>
        {
            _inviteBanner.Visible = false;
            JoinSteamLobby(_inviteLobby);
        };
        _inviteBanner.GetNode<Button>("%InviteCloseButton").Pressed += () => _inviteBanner.Visible = false;

        // 연출은 게임 화면과 선택 창보다 위(CanvasLayer 5)에 그립니다.
        _fx = GetNode<FxLayer>("%Fx");
        _fx.SetShakeTarget(_shakeRoot);
    }

    /// <summary>메인 메뉴와 대기방(screens/MainMenu.tscn)을 연결합니다.</summary>
    private void BindLobby()
    {
        var menu = GetNode<Control>("%MainMenu");
        _lobbyOverlay = menu;
        _lobbyMenu = menu.GetNode<Control>("%MenuView");
        _lobbyRoom = menu.GetNode<Control>("%RoomView");
        _lobbyStatus = menu.GetNode<Label>("%LobbyStatus");
        menu.GetNode<Label>("%Version").Text = $"v{GameVersion.Current}";

        _nameEdit = menu.GetNode<LineEdit>("%NameEdit");
        _nameEdit.Text = SteamRuntime.IsReady && SteamRuntime.PersonaName.Length > 0
            ? SteamRuntime.PersonaName
            : $"플레이어{GD.Randi() % 90 + 10}";

        menu.GetNode<Button>("%SoloButton").Pressed += StartSolo;
        _soloAugmentToggle = menu.GetNode<CheckButton>("%SoloAugmentToggle");

        // 혼자 하기 상대 봇 수(1~3명)입니다. 고른 값은 설정 파일에 저장합니다.
        var soloBots = menu.GetNode<OptionButton>("%SoloBotsOption");
        soloBots.Selected = Math.Clamp(GameSettings.SoloBots, 1, HostSession.SeatCount - 1) - 1;
        soloBots.ItemSelected += index =>
        {
            GameSettings.SoloBots = (int)index + 1;
            GameSettings.Save();
        };
        _steamHostButton = menu.GetNode<Button>("%SteamHostButton");
        _steamHostButton.Pressed += StartSteamHosting;
        _codeEdit = menu.GetNode<LineEdit>("%CodeEdit");
        _steamJoinButton = menu.GetNode<Button>("%SteamJoinButton");
        _steamJoinButton.Pressed += JoinSteamByCode;
        _steamStatus = menu.GetNode<Label>("%SteamStatus");
        _addressEdit = menu.GetNode<LineEdit>("%AddressEdit");
        _portEdit = menu.GetNode<LineEdit>("%PortEdit");
        _portEdit.Text = NetBridge.DefaultPort.ToString();
        menu.GetNode<Button>("%IpHostButton").Pressed += StartHosting;
        menu.GetNode<Button>("%IpJoinButton").Pressed += StartJoining;
        menu.GetNode<Button>("%MenuSettingsButton").Pressed += ToggleSettings;
        menu.GetNode<Button>("%GuideMenuButton").Pressed += () => _guide.Open();

        // 봇 난이도: 혼자 하기(메인 화면)와 멀티 대기방(방장만) 두 곳에서 고르고, 마지막 값을 저장합니다.
        var soloLevel = menu.GetNode<OptionButton>("%SoloLevelOption");
        soloLevel.Selected = Math.Clamp(GameSettings.BotLevel, 0, 2);
        soloLevel.ItemSelected += index =>
        {
            GameSettings.BotLevel = (int)index;
            GameSettings.Save();
        };
        _botLevelOption = menu.GetNode<OptionButton>("%BotLevelOption");
        _botLevelOption.ItemSelected += index =>
        {
            if (_updatingToggle || _session is not HostSession host)
            {
                return;
            }

            GameSettings.BotLevel = (int)index;
            GameSettings.Save();
            host.SetBotLevel((BotLevel)index);
        };

        // 대기방입니다. 방은 게임이 끝나도 그대로 남아서 같은 코드로 계속 모일 수 있습니다.
        _steamRoomBox = menu.GetNode<Control>("%SteamRoomBox");
        _roomCodeLabel = menu.GetNode<Label>("%RoomCodeLabel");
        menu.GetNode<Button>("%CopyCodeButton").Pressed += () =>
        {
            DisplayServer.ClipboardSet(_roomCode);
            _lobbyStatus.Text = "방 코드를 복사했습니다.";
        };
        menu.GetNode<Button>("%OverlayInviteButton").Pressed += SteamRuntime.OpenInviteDialog;
        _roomPlayers = menu.GetNode<VBoxContainer>("%RoomPlayers");
        _augmentToggle = menu.GetNode<CheckButton>("%AugmentToggle");
        _augmentToggle.Toggled += pressed =>
        {
            if (!_updatingToggle && _session is HostSession host)
            {
                host.SetOptions(host.RoomOptions with { SpecialAugments = pressed });
            }
        };
        _friendBox = menu.GetNode<Control>("%FriendBox");
        _friendList = menu.GetNode<VBoxContainer>("%FriendList");
        menu.GetNode<Button>("%RefreshFriendsButton").Pressed += RefreshFriends;
        _startButton = menu.GetNode<Button>("%StartButton");
        _startButton.Pressed += () => (_session as HostSession)?.StartGame();
        _endGameButton = menu.GetNode<Button>("%EndGameButton");
        _endGameButton.Pressed += () => _session?.ReturnToRoom();
        menu.GetNode<Button>("%LeaveRoomButton").Pressed += () => BackToLobby("");
    }

    /// <summary>설정 창(screens/Settings.tscn)을 연결합니다.</summary>
    private void BindSettings()
    {
        var settings = GetNode<Control>("%Settings");
        _settingsOverlay = settings;

        _languageOption = settings.GetNode<OptionButton>("%LanguageOption");
        _languageOption.ItemSelected += index =>
        {
            if (_syncingSettings)
            {
                return;
            }

            GameSettings.Language = Localization.Languages[(int)index];
            GameSettings.Save();
            Localization.Apply(GameSettings.Language);
        };

        _windowModeOption = settings.GetNode<OptionButton>("%WindowModeOption");
        _windowModeOption.ItemSelected += index =>
        {
            if (_syncingSettings)
            {
                return;
            }

            GameSettings.WindowMode = (int)index;
            _resolutionOption.Disabled = index != 0;
            GameSettings.ApplyDisplay();
            GameSettings.Save();
        };

        // 해상도 목록은 GameSettings에 있어서 코드로 채웁니다.
        _resolutionOption = settings.GetNode<OptionButton>("%ResolutionOption");
        for (int i = 0; i < GameSettings.Resolutions.Length; i++)
        {
            _resolutionOption.AddItem(GameSettings.ResolutionName(i));
        }

        _resolutionOption.ItemSelected += index =>
        {
            if (_syncingSettings)
            {
                return;
            }

            GameSettings.ResolutionIndex = (int)index;
            GameSettings.ApplyDisplay();
            GameSettings.Save();
        };

        _masterSlider = settings.GetNode<HSlider>("%MasterSlider");
        _masterValue = settings.GetNode<Label>("%MasterValue");
        WireVolumeSlider(_masterSlider, _masterValue, value =>
        {
            GameSettings.MasterVolume = value;
            GameSettings.ApplyAudio();
        });
        _sfxSlider = settings.GetNode<HSlider>("%SfxSlider");
        _sfxValue = settings.GetNode<Label>("%SfxValue");
        WireVolumeSlider(_sfxSlider, _sfxValue, value =>
        {
            GameSettings.SfxVolume = value;
            GameSettings.ApplyAudio();
            Audio.Sfx.Play("play", -2f, 1f, 0.04f, 120);
        });
        _musicSlider = settings.GetNode<HSlider>("%MusicSlider");
        _musicValue = settings.GetNode<Label>("%MusicValue");
        WireVolumeSlider(_musicSlider, _musicValue, value =>
        {
            GameSettings.MusicVolume = value;
            GameSettings.ApplyAudio();
        });

        _muteToggle = settings.GetNode<CheckButton>("%MuteToggle");
        _muteToggle.Toggled += pressed =>
        {
            if (_syncingSettings)
            {
                return;
            }

            GameSettings.Muted = pressed;
            GameSettings.ApplyAudio();
        };
        settings.GetNode<Button>("%SfxTestButton").Pressed += () => Audio.Sfx.Play("awaken", 0f, 1f, 0f);
        settings.GetNode<Button>("%CloseButton").Pressed += ToggleSettings;
        settings.GetNode<Label>("%Credit").Text = Audio.Music.Credit
            + "\n글꼴: Pretendard, Black Han Sans (SIL OFL 1.1)  ·  효과음·테두리: Kenney (CC0)  ·  천 텍스처: ambientCG (CC0)";
    }

    // ───────────── 레이아웃 ─────────────

    /// <summary>
    /// 테이블 영역 안의 링, 버린 더미, 덱, 문양 표시 위치를 매 프레임 가운데 기준으로 잡습니다.
    /// </summary>
    private void LayoutTable()
    {
        var size = _table.Size;
        var center = size / 2;

        float ringSize = Mathf.Min(size.Y + 20, 240);
        _ring.Size = new Vector2(ringSize, ringSize);
        _ring.Position = center - _ring.Size / 2;

        _discard.Size = new Vector2(112, 162);
        _discard.Position = center - _discard.Size / 2;
        for (int i = 0; i < _discardPile.Length; i++)
        {
            var pile = _discardPile[i];
            pile.Size = _discard.Size;
            pile.PivotOffset = pile.Size / 2;
            pile.Position = center - pile.Size / 2 + _pileJitter[i].Offset;
            pile.Rotation = _pileJitter[i].Rotation;
        }

        // 덱도 반듯하게 쌓지 않고 장마다 조금씩 어긋나게 둡니다.
        var drawBase = center + new Vector2(-250, 0);
        for (int i = 0; i < _drawStack.Length; i++)
        {
            _drawStack[i].Size = new Vector2(96, 138);
            _drawStack[i].PivotOffset = _drawStack[i].Size / 2;
            _drawStack[i].Rotation = Mathf.Sin(i * 2.3f + 0.7f) * 0.045f;
            _drawStack[i].Position = drawBase - _drawStack[i].Size / 2 + new Vector2(-i * 3 + Mathf.Sin(i * 1.7f) * 2.5f, -i * 4);
        }

        _drawLabel.Position = drawBase + new Vector2(-_drawLabel.Size.X / 2, 78);
        _colorPill.Position = center + new Vector2(170, -_colorPill.Size.Y / 2);
        _penaltyBadge.Size = _penaltyBadge.GetCombinedMinimumSize();
        float pulse = 1f + 0.06f * Mathf.Sin(Time.GetTicksMsec() / 120f);
        _penaltyBadge.PivotOffset = _penaltyBadge.Size / 2;
        _penaltyBadge.Scale = new Vector2(pulse, pulse);
        _penaltyBadge.Position = center + new Vector2(-_penaltyBadge.Size.X / 2, -81 - _penaltyBadge.Size.Y - 6);
    }

    // ───────────── UI 생성 ─────────────

    // ───────────── 도박사 선택 창 ─────────────

    private Control _gambleOverlay = null!;
    private HBoxContainer _gambleRow = null!;
    private string _gambleSignature = "";

    private void RefreshGambleOverlay(PlayerView view)
    {
        bool show = view.DrawChoices.Count > 0 && !view.Winner.HasValue;
        string signature = string.Join(",", view.DrawChoices.Select(c => c.Id));
        if (show && signature != _gambleSignature)
        {
            _gambleSignature = signature;
            foreach (var child in _gambleRow.GetChildren())
            {
                child.QueueFree();
            }

            for (int i = 0; i < view.DrawChoices.Count; i++)
            {
                int index = i;
                var card = new CardView
                {
                    Card = view.DrawChoices[i],
                    CustomMinimumSize = new Vector2(150, 216),
                    Playable = view.DrawChoicePlayable.Length > i && view.DrawChoicePlayable[i],
                    LiftOnHover = false,
                };
                card.Clicked += _ => Submit(PlayerAction.ChooseDraw(index));
                _gambleRow.AddChild(card);
            }
        }

        if (!show)
        {
            _gambleSignature = "";
        }

        _gambleOverlay.Visible = show;
    }

    // ───────────── 설정 창 ─────────────

    private Control _settingsOverlay = null!;
    private OptionButton _windowModeOption = null!;
    private OptionButton _resolutionOption = null!;
    private OptionButton _languageOption = null!;
    private HSlider _masterSlider = null!;
    private HSlider _sfxSlider = null!;
    private HSlider _musicSlider = null!;
    private Label _musicValue = null!;
    private Label _masterValue = null!;
    private Label _sfxValue = null!;
    private CheckButton _muteToggle = null!;
    private bool _syncingSettings;

    private void ToggleSettings()
    {
        if (_settingsOverlay.Visible)
        {
            _settingsOverlay.Visible = false;
            GameSettings.Save();
            return;
        }

        SyncSettingsUi();
        _settingsOverlay.Visible = true;
    }

    /// <summary>저장된 설정 값을 설정 창 컨트롤에 옮깁니다.</summary>
    private void SyncSettingsUi()
    {
        _syncingSettings = true;
        _languageOption.Selected = Math.Max(0, Array.IndexOf(Localization.Languages, GameSettings.Language));
        _windowModeOption.Selected = GameSettings.WindowMode;
        _resolutionOption.Selected = GameSettings.ResolutionIndex;
        _resolutionOption.Disabled = GameSettings.WindowMode != 0;
        _masterSlider.Value = GameSettings.MasterVolume * 100;
        _sfxSlider.Value = GameSettings.SfxVolume * 100;
        _musicSlider.Value = GameSettings.MusicVolume * 100;
        _musicValue.Text = $"{(int)_musicSlider.Value}";
        _muteToggle.ButtonPressed = GameSettings.Muted;
        _masterValue.Text = $"{(int)_masterSlider.Value}";
        _sfxValue.Text = $"{(int)_sfxSlider.Value}";
        _syncingSettings = false;
    }

    /// <summary>볼륨 슬라이더를 옆 숫자 라벨과 설정 값에 연결합니다.</summary>
    private void WireVolumeSlider(HSlider slider, Label value, Action<float> changed)
    {
        slider.ValueChanged += v =>
        {
            value.Text = $"{(int)v}";
            if (!_syncingSettings)
            {
                changed((float)v / 100f);
            }
        };
        slider.DragEnded += _ => GameSettings.Save();
    }

    /// <summary>게임 안에 친구의 초대 알림을 띄웁니다.</summary>
    private void ShowInvite(string from, Steamworks.CSteamID lobby)
    {
        _inviteLobby = lobby;
        _inviteLabel.Text = $"{from}님의 초대";
        _inviteBanner.Visible = true;
        _inviteBanner.Size = _inviteBanner.GetCombinedMinimumSize();
        _inviteBanner.Position = new Vector2(GetViewportRect().Size.X - _inviteBanner.Size.X - 16, 16);
        _inviteBanner.PivotOffset = new Vector2(_inviteBanner.Size.X, 0);
        _inviteBanner.GetNode<AnimationPlayer>("%AppearAnim").Play("appear");
    }

    // ───────────── 로비 ─────────────

    private void ShowLobbyMenu(string status)
    {
        _fx.ClearTransient();
        _toast.Visible = false;
        Audio.Music.SetInGame(false);
        bool steam = SteamRuntime.IsReady;
        _steamHostButton.Disabled = !steam;
        _steamJoinButton.Disabled = !steam;
        _steamStatus.Text = steam
            ? $"{SteamRuntime.PersonaName} 계정으로 로그인됨"
            : SteamRuntime.InitError;
        _steamStatus.AddThemeColorOverride("font_color", steam ? UiTheme.TextDim : UiTheme.Danger);
        _lobbyOverlay.Visible = true;
        _lobbyMenu.Visible = true;
        _lobbyRoom.Visible = false;
        _nameEdit.Editable = true;
        _lobbyStatus.Text = status;
        _gameOverOverlay.Visible = false;
        _colorOverlay.Visible = false;
        _abilityOverlay.Visible = false;
        _augmentOverlay.Visible = false;
    }

    private void ShowLobbyRoom(string status)
    {
        _fx.ClearTransient();
        _toast.Visible = false;
        Audio.Music.SetInGame(false);
        _lobbyOverlay.Visible = true;
        _lobbyMenu.Visible = false;
        _lobbyRoom.Visible = true;
        _nameEdit.Editable = false;
        _startButton.Visible = _session is HostSession;
        bool steamRoom = !string.IsNullOrEmpty(_roomCode);
        _steamRoomBox.Visible = steamRoom;
        _friendBox.Visible = steamRoom && SteamRuntime.IsReady;
        _roomCodeLabel.Text = $"방 코드   {_roomCode}";
        _lobbyStatus.Text = status;
        _gameOverOverlay.Visible = false;
        RefreshLobby();
        if (_friendBox.Visible)
        {
            RefreshFriends();
        }
    }

}
