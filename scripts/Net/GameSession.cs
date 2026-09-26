using System;
using SpCardgame.Core;

namespace SpCardgame.Net;

/// <summary>
/// 화면(GameController)이 보는 게임 세션입니다. 혼자 하기, 호스트, 클라이언트가 모두 이 형태로 보입니다.
/// 화면은 View만 그리고, 행동은 Submit으로만 보냅니다. 규칙 판정은 항상 호스트가 합니다.
/// 멀티에서는 대기방 → 게임 → 대기방 순서로 돌고, 방(코드)은 게임이 끝나도 그대로 유지됩니다.
/// </summary>
public abstract class GameSession
{
    /// <summary>내 자리 번호입니다.</summary>
    public int MySeat { get; protected set; }

    /// <summary>자리 순서대로의 플레이어 이름입니다.</summary>
    public string[] Names { get; protected set; } = Array.Empty<string>();

    /// <summary>내 시점의 최신 게임 상태입니다. 대기방에 있을 때는 null입니다.</summary>
    public PlayerView? View { get; protected set; }

    /// <summary>대기방에 모인 사람 이름입니다. (0번은 방장)</summary>
    public string[] LobbyNames { get; protected set; } = Array.Empty<string>();

    /// <summary>대기방 사람마다의 Steam ID입니다. (LobbyNames와 같은 순서, 모르면 0)</summary>
    public ulong[] LobbySteamIds { get; protected set; } = Array.Empty<ulong>();

    /// <summary>게임 자리마다의 Steam ID입니다. (Names와 같은 순서, 봇이나 IP 접속은 0)</summary>
    public ulong[] SeatSteamIds { get; protected set; } = Array.Empty<ulong>();

    /// <summary>자리의 Steam ID를 돌려줍니다. 모르면 0입니다.</summary>
    public ulong SteamIdOf(int seat) => seat >= 0 && seat < SeatSteamIds.Length ? SeatSteamIds[seat] : 0;

    /// <summary>방장이 고른 판 설정입니다.</summary>
    public GameOptions RoomOptions { get; protected set; } = new();

    /// <summary>내가 지금 게임 화면에 있는지 알려 줍니다. (false면 대기방)</summary>
    public bool Playing { get; protected set; }

    /// <summary>방에서 판이 진행 중인지 알려 줍니다. 내가 대기방으로 나와 있어도 다른 사람들은 계속 플레이할 수 있습니다.</summary>
    public bool GameRunning { get; protected set; }

    /// <summary>대기방 사람마다 지금 게임에 앉아 있는지 표시합니다. (LobbyNames와 같은 순서)</summary>
    public bool[] LobbyBusy { get; protected set; } = Array.Empty<bool>();

    public abstract bool IsHost { get; }

    /// <summary>혼자 하기인지 알려 줍니다. (대기방 없이 바로 시작합니다)</summary>
    public virtual bool IsSolo => false;

    public event Action? Changed;
    public event Action? LobbyChanged;
    public event Action? GameStarted;
    public event Action? ReturnedToRoom;
    public event Action<string>? LogLine;
    public event Action<GameEvent>? Fx;
    public event Action<string>? ErrorMessage;
    public event Action<string>? Disconnected;

    public abstract void Submit(PlayerAction action);

    /// <summary>매 프레임 호출합니다. 호스트는 여기서 봇을 움직입니다.</summary>
    public virtual void Tick(double delta) { }

    /// <summary>같은 사람들로 바로 한 판 더 합니다. (방장만 가능)</summary>
    public virtual void Restart() { }

    /// <summary>모두를 대기방으로 돌려보냅니다. (방장만 가능)</summary>
    public virtual void ReturnToRoom() { }

    /// <summary>나만 게임에서 빠져 대기방으로 갑니다. 내 자리는 봇이 이어받습니다.</summary>
    public virtual void LeaveGame() { }

    public string NameOf(int seat) => seat >= 0 && seat < Names.Length ? Names[seat] : $"P{seat}";

    protected void RaiseChanged() => Changed?.Invoke();
    protected void RaiseLobbyChanged() => LobbyChanged?.Invoke();
    protected void RaiseGameStarted() => GameStarted?.Invoke();
    protected void RaiseReturnedToRoom() => ReturnedToRoom?.Invoke();
    protected void RaiseLog(string line) => LogLine?.Invoke(line);
    protected void RaiseFx(GameEvent gameEvent) => Fx?.Invoke(gameEvent);
    protected void RaiseError(string message) => ErrorMessage?.Invoke(message);
    protected void RaiseDisconnected(string reason) => Disconnected?.Invoke(reason);
}
