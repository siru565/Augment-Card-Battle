using System;
using System.Collections.Generic;
using System.Linq;
using SpCardgame.AI;
using SpCardgame.Core;

namespace SpCardgame.Net;

/// <summary>
/// 게임을 실제로 진행하는 쪽(방장)입니다. GameEngine을 가지고 규칙을 판정하고, 봇을 움직이며,
/// 각 클라이언트에게 그 사람 시점의 PlayerView만 보냅니다. (남의 손패는 보내지 않습니다.)
/// 대기방을 관리하며(설정, 강퇴), 게임이 끝나도 방은 그대로 유지됩니다.
/// 전송 계층이 없으면 혼자 하기(나 + 봇)로 동작합니다.
/// </summary>
public sealed class HostSession : GameSession
{
    /// <summary>한 판에 앉을 수 있는 최대 인원입니다. (방장 포함)</summary>
    public const int SeatCount = 4;

    /// <summary>한 판을 시작하려면 필요한 최소 인원입니다. (사람 + 봇)</summary>
    public const int MinPlayers = 2;

    /// <summary>강퇴 메시지가 도착할 시간을 준 뒤 연결을 끊기까지 기다리는 시간(초)입니다.</summary>
    private const float KickDelay = 0.6f;

    private enum SeatKind
    {
        Local,
        Remote,
        Bot,
    }

    private sealed record Member(long Peer, string Name, string Identity);

    private readonly IServerTransport? _transport;
    private readonly string _hostName;
    private readonly string _version;
    private readonly List<Member> _lobby = new();
    private readonly HashSet<string> _banned = new();
    private readonly List<(long Peer, float Time)> _pendingKicks = new();
    private readonly Random _botRng = new();

    private GameEngine? _engine;
    private SeatKind[] _seatKinds = Array.Empty<SeatKind>();
    private long[] _seatPeers = Array.Empty<long>();
    private IBot[] _bots = Array.Empty<IBot>();
    private float _botTimer;

    /// <summary>봇 난이도입니다. 새로 앉는 봇과 나간 사람 자리를 이어받는 봇 모두 이 난이도를 씁니다.</summary>
    public BotLevel BotLevel { get; private set; } = BotLevel.Normal;

    /// <summary>방장이 넣어 둔 봇 수입니다. 사람이 들어와서 자리가 모자라면 실제로는 그만큼 줄어듭니다.</summary>
    private int _wantedBots = SeatCount - 1;
    private float _draftTimer;

    /// <summary>봇이 한 번 행동하기까지 기다리는 시간(초)입니다.</summary>
    public float BotDelay { get; set; } = 0.9f;

    public override bool IsHost => true;

    public override bool IsSolo => _transport == null;

    public bool InGame => _engine != null;

    /// <summary>방장 자신의 Steam ID입니다. (Steam이 없으면 0) 화면 쪽에서 넣어 줍니다.</summary>
    public ulong HostSteamId
    {
        get => _hostSteamId;
        set
        {
            _hostSteamId = value;
            UpdateLobbyNames();
        }
    }

    private ulong _hostSteamId;

    private static ulong ParseSteamId(string identity) => ulong.TryParse(identity, out ulong id) ? id : 0;

    public HostSession(string hostName, IServerTransport? transport, string version = "", GameOptions? options = null)
    {
        _hostName = hostName;
        _version = version;
        _transport = transport;
        RoomOptions = options ?? new GameOptions();
        MySeat = 0;
        UpdateLobbyNames();

        if (_transport != null)
        {
            _transport.PeerDisconnected += OnPeerDisconnected;
            _transport.Received += OnReceived;
        }
    }

    // ───────────── 대기방 ─────────────

    /// <summary>다음 판에 실제로 앉을 봇 수입니다. 사람이 먼저 자리를 차지하고, 남는 자리만큼만 봇이 앉습니다.</summary>
    public int BotCount => Math.Clamp(_wantedBots, 0, SeatCount - 1 - _lobby.Count);

    /// <summary>다음 판 인원(방장 + 참가자 + 봇)입니다.</summary>
    public int PlannedPlayers => 1 + _lobby.Count + BotCount;

    /// <summary>봇 수를 정합니다. 판이 진행 중일 때는 바꿀 수 없습니다. (혼자 하기는 판 사이에 언제든 바꿀 수 있습니다)</summary>
    public void SetBotCount(int count)
    {
        if (GameRunning && _transport != null)
        {
            return;
        }

        _wantedBots = Math.Clamp(count, 0, SeatCount - 1);
        BroadcastLobby();
    }

    /// <summary>봇 난이도를 정합니다. 판이 진행 중일 때는 바꿀 수 없습니다.</summary>
    public void SetBotLevel(BotLevel level)
    {
        if (GameRunning && _transport != null)
        {
            return;
        }

        BotLevel = level;
        BroadcastLobby();
    }

    /// <summary>봇을 한 명 넣습니다. 자리가 가득 찼으면 아무 일도 하지 않습니다.</summary>
    public void AddBot()
    {
        if (PlannedPlayers < SeatCount)
        {
            SetBotCount(BotCount + 1);
        }
    }

    /// <summary>봇을 한 명 뺍니다.</summary>
    public void RemoveBot()
    {
        if (BotCount > 0)
        {
            SetBotCount(BotCount - 1);
        }
    }

    private void UpdateLobbyNames()
    {
        LobbyNames = new[] { _hostName }.Concat(_lobby.Select(p => p.Name)).ToArray();
        LobbySteamIds = new[] { HostSteamId }.Concat(_lobby.Select(p => ParseSteamId(p.Identity))).ToArray();
        LobbyBots = BotCount;
        LobbyBotLevel = BotLevel;
    }

    private void BroadcastLobby()
    {
        UpdateLobbyNames();
        GameRunning = _engine != null && !_engine.State.IsFinished;
        LobbyBusy = new[] { GameRunning && Playing }
            .Concat(_lobby.Select(m => GameRunning && IsSeated(m.Peer)))
            .ToArray();
        SendAll(new NetMessage
        {
            T = NetMessage.Lobby, Names = LobbyNames, Options = RoomOptions, Playing = GameRunning, Busy = LobbyBusy,
            SteamIds = LobbySteamIds, Bots = LobbyBots, BotLevel = (int)BotLevel,
        });
        RaiseLobbyChanged();
    }

    private bool IsSeated(long peer)
    {
        for (int seat = 0; seat < _seatKinds.Length; seat++)
        {
            if (_seatKinds[seat] == SeatKind.Remote && _seatPeers[seat] == peer)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>판 설정을 바꿉니다. 판이 진행 중이 아닐 때만 바꿀 수 있습니다.</summary>
    public void SetOptions(GameOptions options)
    {
        if (GameRunning)
        {
            return;
        }

        RoomOptions = options;
        BroadcastLobby();
    }

    /// <summary>
    /// 대기방의 index번째 사람(1부터, 0번은 방장)을 내보냅니다. 같은 Steam 계정은 이 방에 다시 들어올 수 없습니다.
    /// </summary>
    public void Kick(int lobbyIndex)
    {
        if (lobbyIndex < 1 || lobbyIndex > _lobby.Count || _transport == null)
        {
            return;
        }

        var member = _lobby[lobbyIndex - 1];
        _lobby.RemoveAt(lobbyIndex - 1);
        if (!string.IsNullOrEmpty(member.Identity))
        {
            _banned.Add(member.Identity);
        }

        _transport.Send(member.Peer, new NetMessage { T = NetMessage.Kicked, Text = "방장이 방에서 내보냈어요." }.ToJson());
        _pendingKicks.Add((member.Peer, KickDelay));

        if (_engine != null)
        {
            HandOverToBot(member.Peer, $"{member.Name}님이 강퇴되어 봇이 이어받습니다.");
        }

        EmitRoomLog($"{member.Name}님을 내보냈어요.");
        BroadcastLobby();
    }

    private void OnPeerDisconnected(long peer)
    {
        _pendingKicks.RemoveAll(k => k.Peer == peer);
        int index = _lobby.FindIndex(p => p.Peer == peer);
        if (index < 0)
        {
            return;
        }

        string name = _lobby[index].Name;
        _lobby.RemoveAt(index);

        if (_engine != null)
        {
            HandOverToBot(peer, $"{name}의 연결이 끊겨서 봇이 이어받습니다.");
        }

        BroadcastLobby();
    }

    /// <summary>게임 중에 나간 사람 자리는 봇이 이어받습니다.</summary>
    private void HandOverToBot(long peer, string message)
    {
        for (int seat = 0; seat < _seatKinds.Length; seat++)
        {
            if (_seatKinds[seat] == SeatKind.Remote && _seatPeers[seat] == peer)
            {
                _seatKinds[seat] = SeatKind.Bot;
                _seatPeers[seat] = 0;
                _bots[seat] = BotFactory.Create(BotLevel);
                EmitLog(message);
            }
        }

        Broadcast();
        EndIfNoHumans();
    }

    /// <summary>게임에 사람이 한 명도 남지 않으면 판을 끝냅니다. (봇끼리만 두는 판은 의미가 없습니다)</summary>
    private void EndIfNoHumans()
    {
        if (_engine == null || _seatKinds.Any(k => k != SeatKind.Bot))
        {
            return;
        }

        if (!_engine.State.IsFinished)
        {
            EmitRoomLog("게임에 사람이 모두 나가서 판을 끝냈어요.");
        }

        _engine = null;
        BroadcastLobby();
    }

    /// <summary>
    /// 방장만 게임에서 빠져 대기방으로 갑니다. 방장 자리는 봇이 이어받고, 남은 사람들은 계속 플레이합니다.
    /// 혼자 하기에서는 판을 그냥 끝냅니다.
    /// </summary>
    public override void LeaveGame()
    {
        if (_engine == null || !Playing)
        {
            return;
        }

        Playing = false;
        View = null;
        if (_seatKinds.Length > MySeat && _seatKinds[MySeat] == SeatKind.Local)
        {
            _seatKinds[MySeat] = SeatKind.Bot;
            _bots[MySeat] = BotFactory.Create(BotLevel);
            EmitLog($"{_hostName}님이 대기방으로 나가서 봇이 이어받습니다.");
        }

        RaiseReturnedToRoom();
        EndIfNoHumans();
        BroadcastLobby();
    }

    // ───────────── 게임 진행 ─────────────

    /// <summary>
    /// 대기방 인원과 방장이 넣어 둔 봇으로 게임을 시작합니다. (2~4명)
    /// 인원이 모자라면(방장 혼자, 봇 없음) 시작하지 않고 false를 돌려줍니다.
    /// </summary>
    public bool StartGame()
    {
        if (GameRunning && _transport != null)
        {
            return false;
        }

        int players = PlannedPlayers;
        if (players < MinPlayers)
        {
            RaiseError($"최소 {MinPlayers}명이 있어야 시작할 수 있어요. 봇을 넣어 주세요.");
            return false;
        }

        _seatKinds = new SeatKind[players];
        _seatPeers = new long[players];
        _bots = new IBot[players];
        var names = new string[players];
        var steamIds = new ulong[players];

        _seatKinds[0] = SeatKind.Local;
        names[0] = _hostName;
        steamIds[0] = HostSteamId;

        int botLetter = 0;
        for (int seat = 1; seat < players; seat++)
        {
            if (seat - 1 < _lobby.Count)
            {
                _seatKinds[seat] = SeatKind.Remote;
                _seatPeers[seat] = _lobby[seat - 1].Peer;
                names[seat] = _lobby[seat - 1].Name;
                steamIds[seat] = ParseSteamId(_lobby[seat - 1].Identity);
            }
            else
            {
                _seatKinds[seat] = SeatKind.Bot;
                _bots[seat] = BotFactory.Create(BotLevel);
                names[seat] = $"봇 {(char)('A' + botLetter++)}";
            }
        }

        Names = names;
        SeatSteamIds = steamIds;
        BeginRound();
        return true;
    }

    /// <summary>
    /// 바로 한 판 더 합니다. 멀티에서는 대기방에 있는 사람까지 모두 모아서 새로 시작합니다.
    /// </summary>
    public override void Restart()
    {
        if (_transport != null)
        {
            if (_engine == null || _engine.State.IsFinished)
            {
                StartGame();
            }

            return;
        }

        if (_engine != null)
        {
            BeginRound();
        }
    }

    /// <summary>게임을 끝내고 모두를 대기방으로 돌려보냅니다. 방과 방 코드는 그대로입니다.</summary>
    public override void ReturnToRoom()
    {
        if (_engine == null)
        {
            return;
        }

        _engine = null;
        Playing = false;
        View = null;
        SendAll(new NetMessage { T = NetMessage.Room });
        BroadcastLobby();
        RaiseReturnedToRoom();
    }

    private void BeginRound()
    {
        int seed = Environment.TickCount & int.MaxValue;
        _engine = new GameEngine(_seatKinds.Length, seed, RoomOptions) { Names = Names };
        _engine.Log = EmitLog;
        _engine.Event += OnEngineEvent;
        Playing = true;

        for (int seat = 0; seat < _seatKinds.Length; seat++)
        {
            if (_seatKinds[seat] == SeatKind.Remote)
            {
                _transport?.Send(_seatPeers[seat], new NetMessage { T = NetMessage.Start, Seat = seat, Names = Names, SteamIds = SeatSteamIds }.ToJson());
            }
        }

        RaiseGameStarted();
        _engine.Start();
        EmitLog($"(시드 {seed})");
        _botTimer = 0f;
        Broadcast();
        BroadcastLobby();
    }

    public override void Submit(PlayerAction action) => ApplyFrom(MySeat, action, null);

    /// <summary>개발용: 내 차례일 때 각성 선택지를 강제로 띄웁니다. (UI 스크린샷 확인용)</summary>
    public void DebugForceAbilityChoices()
    {
        if (_engine == null || _engine.State.CurrentPlayer != MySeat)
        {
            return;
        }

        _engine.State.PendingAbilities.Clear();
        _engine.State.PendingAbilities.AddRange(AbilityPool.RollChoices(_botRng));
        Broadcast();
    }

    /// <summary>개발용: 내 차례일 때 특수 증강 선택지를 강제로 띄웁니다. (UI 스크린샷 확인용)</summary>
    public void DebugForceAugmentChoices()
    {
        if (_engine == null || _engine.State.CurrentPlayer != MySeat)
        {
            return;
        }

        _engine.State.PendingAugments.Clear();
        _engine.State.PendingAugments.AddRange(SpecialAugments.RollChoices(_engine.State.Players[MySeat].Augments, _botRng));
        Broadcast();
    }

    /// <summary>개발용: 나에게 억지 뽑기를 걸어 둡니다. (UI 스크린샷 확인용)</summary>
    public void DebugForceDebt(int cards)
    {
        if (_engine == null)
        {
            return;
        }

        _engine.State.DrawQueue.Add(new DrawDebt(MySeat, cards, null, "+2 공격"));
        _engine.State.DeferredSteps ??= 0;
        Broadcast();
    }

    /// <summary>개발용: 내 차례에 쌓인 공격을 걸어 둡니다. (UI 스크린샷 확인용)</summary>
    public void DebugPenalty(int amount)
    {
        if (_engine != null)
        {
            _engine.State.PendingPenalty = amount;
            Broadcast();
        }
    }

    /// <summary>개발용: 내 손에 특수 증강을 바로 넣습니다.</summary>
    public void DebugGiveAugment(SpecialAugmentId id)
    {
        if (_engine != null && !_engine.State.Players[MySeat].Has(id))
        {
            _engine.State.Players[MySeat].Augments.Add(id);
            Broadcast();
        }
    }

    public override void Tick(double delta)
    {
        ProcessPendingKicks((float)delta);

        if (_engine == null || _engine.State.IsFinished)
        {
            return;
        }

        // 게임 시작 직후 첫 증강 고르기: 봇들은 사람을 기다리게 하지 않도록 잠깐 뒤 한꺼번에 고릅니다.
        if (_engine.State.Drafting)
        {
            _draftTimer += (float)delta;
            if (_draftTimer < 0.4f)
            {
                return;
            }

            bool changed = false;
            for (int seat = 0; seat < _seatKinds.Length; seat++)
            {
                if (_seatKinds[seat] == SeatKind.Bot && _engine.State.Players[seat].DraftChoices.Count > 0)
                {
                    var choice = _bots[seat].Decide(_engine.State.ViewFor(seat), _botRng);
                    changed |= _engine.Apply(seat, choice).Ok || _engine.Apply(seat, PlayerAction.ChooseAugment(0)).Ok;
                }
            }

            if (changed)
            {
                _botTimer = 0f;
                Broadcast();
            }

            return;
        }

        _draftTimer = 0f;

        // 억지 뽑기 중이면 뽑는 사람이, 아니면 차례인 사람이 행동합니다.
        int actor = _engine.State.InputPlayer;
        if (_seatKinds[actor] != SeatKind.Bot)
        {
            _botTimer = 0f;
            return;
        }

        // 봇이 억지로 뽑을 때는 한 장씩 빠르게 뽑아서 흐름을 끊지 않습니다.
        // 사람이 모두 순위가 정해져 관전만 하고 있으면 남은 봇들의 판을 빠르게 진행합니다.
        bool humansPlaying = Enumerable.Range(0, _seatKinds.Length)
            .Any(seat => _seatKinds[seat] != SeatKind.Bot && _engine.State.IsActive(seat));
        float speed = humansPlaying ? 1f : 0.25f;

        // 증강·능력 고르기도 조금 빠르게 합니다.
        float delay = speed * (_engine.State.PayingDebt ? BotDelay * 0.35f
            : _engine.State.ChoosingAugment || _engine.State.ChoosingAbility ? BotDelay * 0.5f
            : BotDelay);
        _botTimer += (float)delta;
        if (_botTimer < delay)
        {
            return;
        }

        _botTimer = 0f;
        var action = _bots[actor].Decide(_engine.State.ViewFor(actor), _botRng);
        var result = _engine.Apply(actor, action);
        if (!result.Ok)
        {
            // 봇이 잘못된 행동을 하면 게임이 멈추지 않도록 기본 행동으로 대신합니다.
            var view = _engine.State.ViewFor(actor);
            var fallback = view.MustDraw ? PlayerAction.ForcedDraw()
                : view.DrawChoices.Count > 0 ? PlayerAction.ChooseDraw(0)
                : view.AugmentChoices.Count > 0 ? PlayerAction.ChooseAugment(0)
                : view.CanPass ? PlayerAction.Pass()
                : PlayerAction.Draw();
            _engine.Apply(actor, fallback);
        }

        Broadcast();
    }

    private void ProcessPendingKicks(float delta)
    {
        for (int i = _pendingKicks.Count - 1; i >= 0; i--)
        {
            var (peer, time) = _pendingKicks[i];
            time -= delta;
            if (time > 0)
            {
                _pendingKicks[i] = (peer, time);
                continue;
            }

            _pendingKicks.RemoveAt(i);
            _transport?.Kick(peer);
        }
    }

    private void ApplyFrom(int seat, PlayerAction action, long? peer)
    {
        if (_engine == null)
        {
            return;
        }

        var result = _engine.Apply(seat, action);
        if (!result.Ok)
        {
            string message = result.Error ?? "알 수 없는 오류";
            if (peer.HasValue)
            {
                _transport?.Send(peer.Value, new NetMessage { T = NetMessage.Error, Text = message }.ToJson());
            }
            else
            {
                RaiseError(message);
            }

            return;
        }

        _botTimer = 0f;
        Broadcast();
    }

    /// <summary>
    /// 모든 사람에게 각자 시점의 상태를 보냅니다. 호스트 화면도 함께 갱신합니다.
    /// </summary>
    private void Broadcast()
    {
        if (_engine == null)
        {
            return;
        }

        for (int seat = 0; seat < _seatKinds.Length; seat++)
        {
            if (_seatKinds[seat] == SeatKind.Remote)
            {
                var message = new NetMessage { T = NetMessage.View, State = _engine.State.ViewFor(seat) };
                _transport?.Send(_seatPeers[seat], message.ToJson());
            }
        }

        if (Playing)
        {
            View = _engine.State.ViewFor(MySeat);
            RaiseChanged();
        }

        // 게임이 끝나면 대기방에 있는 사람들도 새 게임을 시작할 수 있게 알려 줍니다.
        if (_engine.State.IsFinished && GameRunning)
        {
            BroadcastLobby();
        }
    }

    private void EmitLog(string line)
    {
        if (Playing)
        {
            RaiseLog(line);
        }

        if (_engine != null)
        {
            SendToPlayers(new NetMessage { T = NetMessage.Log, Text = line });
        }
    }

    /// <summary>대기방에 있는 모두에게 안내 문구를 보냅니다.</summary>
    private void EmitRoomLog(string line)
    {
        RaiseLog(line);
        SendAll(new NetMessage { T = NetMessage.Log, Text = line });
    }

    /// <summary>엔진의 연출용 사건을 모두에게 그대로 전달합니다.</summary>
    private void OnEngineEvent(GameEvent gameEvent)
    {
        if (Playing)
        {
            RaiseFx(gameEvent);
        }

        SendToPlayers(new NetMessage { T = NetMessage.Fx, Event = gameEvent });
    }

    // ───────────── 수신 ─────────────

    private void OnReceived(long peer, string json)
    {
        var message = NetMessage.FromJson(json);
        if (message == null)
        {
            return;
        }

        switch (message.T)
        {
            case NetMessage.Hello:
                HandleHello(peer, message.Text, message.Version);
                break;

            case NetMessage.Action when message.Move.HasValue:
                int seat = Array.IndexOf(_seatPeers, peer);
                if (seat > 0 && _seatKinds[seat] == SeatKind.Remote)
                {
                    ApplyFrom(seat, message.Move.Value, peer);
                }

                break;

            case NetMessage.Leave:
                HandleLeave(peer);
                break;
        }
    }

    /// <summary>참가자가 게임에서 빠져 대기방으로 갔습니다. 그 자리는 봇이 이어받습니다.</summary>
    private void HandleLeave(long peer)
    {
        var member = _lobby.FirstOrDefault(m => m.Peer == peer);
        if (member == null || _engine == null || !IsSeated(peer))
        {
            return;
        }

        HandOverToBot(peer, $"{member.Name}님이 대기방으로 나가서 봇이 이어받습니다.");
        BroadcastLobby();
    }

    /// <summary>들어오려는 사람을 거절합니다. 이유를 먼저 보내고 잠시 뒤 연결을 끊습니다.</summary>
    private void Reject(long peer, string reason)
    {
        _transport?.Send(peer, new NetMessage { T = NetMessage.Kicked, Text = reason }.ToJson());
        _pendingKicks.Add((peer, KickDelay));
    }

    private void HandleHello(long peer, string? rawName, string? version)
    {
        // 버전이 다르면 규칙이 달라서 게임이 꼬일 수 있으므로 받지 않습니다.
        if (!string.IsNullOrEmpty(_version) && version != _version)
        {
            string theirs = string.IsNullOrEmpty(version) ? "알 수 없음" : $"v{version}";
            Reject(peer, $"게임 버전이 달라요. (방장 v{_version}, 내 게임 {theirs}) itch 앱에서 업데이트한 뒤 다시 접속해 주세요.");
            return;
        }

        string identity = _transport?.IdentityOf(peer) ?? "";
        if (!string.IsNullOrEmpty(identity) && _banned.Contains(identity))
        {
            Reject(peer, "방장이 내보낸 방이라 다시 들어갈 수 없어요.");
            return;
        }

        if (_lobby.Count >= SeatCount - 1)
        {
            Reject(peer, "방이 가득 찼어요.");
            return;
        }

        string name = string.IsNullOrWhiteSpace(rawName) ? $"플레이어{_lobby.Count + 2}" : rawName.Trim();
        if (name.Length > 12)
        {
            name = name[..12];
        }

        _lobby.RemoveAll(p => p.Peer == peer);
        _lobby.Add(new Member(peer, name, identity));
        BroadcastLobby();
        EmitRoomLog(GameRunning ? $"{name}님이 들어왔어요. (다음 판부터 함께해요)" : $"{name}님이 들어왔어요.");
    }

    // ───────────── 전송 헬퍼 ─────────────

    private void SendAll(NetMessage message)
    {
        if (_transport == null)
        {
            return;
        }

        string json = message.ToJson();
        foreach (var member in _lobby)
        {
            _transport.Send(member.Peer, json);
        }
    }

    private void SendToPlayers(NetMessage message)
    {
        if (_transport == null)
        {
            return;
        }

        string json = message.ToJson();
        for (int seat = 0; seat < _seatKinds.Length; seat++)
        {
            if (_seatKinds[seat] == SeatKind.Remote)
            {
                _transport.Send(_seatPeers[seat], json);
            }
        }
    }
}
