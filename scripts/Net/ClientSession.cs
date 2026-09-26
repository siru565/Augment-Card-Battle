using System;
using SpCardgame.Core;

namespace SpCardgame.Net;

/// <summary>
/// 호스트에 접속한 쪽입니다. 규칙 판정은 하지 않고, 받은 상태를 그대로 보여 주며 행동만 보냅니다.
/// </summary>
public sealed class ClientSession : GameSession
{
    private readonly IClientTransport _transport;
    private readonly string _name;
    private readonly string _version;

    /// <summary>끊김 알림을 한 번만 보내기 위한 표시입니다. (강퇴 메시지 뒤에 연결 끊김이 또 오기 때문입니다)</summary>
    private bool _closed;

    public override bool IsHost => false;

    public ClientSession(string name, IClientTransport transport, string version = "")
    {
        _name = name;
        _version = version;
        _transport = transport;
        _transport.Connected += OnConnected;
        _transport.Received += OnReceived;
        _transport.Disconnected += Close;
    }

    private void OnConnected() =>
        _transport.Send(new NetMessage { T = NetMessage.Hello, Text = _name, Version = _version }.ToJson());

    public override void LeaveGame()
    {
        if (!Playing)
        {
            return;
        }

        _transport.Send(new NetMessage { T = NetMessage.Leave }.ToJson());
        Playing = false;
        View = null;
        RaiseReturnedToRoom();
    }

    public override void Submit(PlayerAction action) =>
        _transport.Send(new NetMessage { T = NetMessage.Action, Move = action }.ToJson());

    private void Close(string reason)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        RaiseDisconnected(reason);
    }

    private void OnReceived(string json)
    {
        var message = NetMessage.FromJson(json);
        if (message == null)
        {
            return;
        }

        switch (message.T)
        {
            case NetMessage.Lobby:
                LobbyNames = message.Names ?? Array.Empty<string>();
                RoomOptions = message.Options ?? new GameOptions();
                GameRunning = message.Playing;
                LobbyBusy = message.Busy ?? Array.Empty<bool>();
                RaiseLobbyChanged();
                break;

            case NetMessage.Start:
                MySeat = message.Seat;
                Names = message.Names ?? Array.Empty<string>();
                View = null;
                Playing = true;
                RaiseGameStarted();
                break;

            case NetMessage.Room:
                Playing = false;
                View = null;
                RaiseReturnedToRoom();
                break;

            case NetMessage.View when message.State != null && Playing:
                View = message.State;
                RaiseChanged();
                break;

            case NetMessage.Log when message.Text != null && (Playing || !GameRunning):
                RaiseLog(message.Text);
                break;

            case NetMessage.Fx when message.Event != null && Playing:
                RaiseFx(message.Event);
                break;

            case NetMessage.Kicked:
                Close(message.Text ?? "방장이 방에서 내보냈어요.");
                break;

            case NetMessage.Error when message.Text != null:
                RaiseError(message.Text);
                break;
        }
    }
}
