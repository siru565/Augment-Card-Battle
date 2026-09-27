using System;
using Godot;

namespace SpCardgame.Net;

/// <summary>
/// Godot 고수준 멀티플레이어(ENet)로 JSON 메시지를 주고받는 노드입니다.
/// 모든 피어에서 같은 경로(/root/Game/Net)에 있어야 RPC가 서로 연결됩니다.
/// 게임 규칙은 전혀 모르고, 문자열을 옮기는 일만 합니다.
/// </summary>
public partial class NetBridge : Node
{
    public const int DefaultPort = 24680;
    public const int MaxClients = 3;

    public event Action<long>? PeerJoined;
    public event Action<long>? PeerLeft;
    public event Action<long, string>? ServerMessage;
    public event Action? ConnectedOk;
    public event Action<string>? ConnectionLost;
    public event Action<string>? ClientMessage;

    public bool IsOnline => Multiplayer.MultiplayerPeer is ENetMultiplayerPeer;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += id => PeerJoined?.Invoke(id);
        Multiplayer.PeerDisconnected += id => PeerLeft?.Invoke(id);
        Multiplayer.ConnectedToServer += () => ConnectedOk?.Invoke();
        Multiplayer.ConnectionFailed += () => ConnectionLost?.Invoke("접속에 실패했어요. 주소와 포트를 확인해 주세요.");
        Multiplayer.ServerDisconnected += () => ConnectionLost?.Invoke("호스트와의 연결이 끊겼어요.");
    }

    /// <summary>방을 만듭니다. 성공하면 Error.Ok를 반환합니다.</summary>
    public Error Host(int port)
    {
        Close();
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(port, MaxClients);
        if (error == Error.Ok)
        {
            Multiplayer.MultiplayerPeer = peer;
        }

        return error;
    }

    /// <summary>다른 사람의 방에 접속을 시작합니다. 결과는 ConnectedOk 또는 ConnectionLost로 옵니다.</summary>
    public Error Join(string address, int port)
    {
        Close();
        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(address, port);
        if (error == Error.Ok)
        {
            Multiplayer.MultiplayerPeer = peer;
        }

        return error;
    }

    public void Close()
    {
        // 게임을 끌 때는 트리에서 빠지면서 Multiplayer가 이미 없을 수 있습니다.
        if (!IsInsideTree() || Multiplayer == null)
        {
            return;
        }

        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer peer)
        {
            // 바로 끊지 않고 상대에게 "연결 종료"를 알린 뒤 닫습니다.
            // 그냥 닫으면 상대는 시간 초과(수십 초)가 될 때까지 내가 아직 있다고 생각합니다.
            if (peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
            {
                foreach (int id in Multiplayer.GetPeers())
                {
                    peer.GetPeer(id)?.PeerDisconnect();
                }

                peer.Host?.Flush();
            }

            peer.Close();
        }

        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
    }

    public void SendToPeer(long peer, string json) => RpcId(peer, "ToClient", json);

    /// <summary>참가자 한 명의 연결을 끊습니다. (강퇴)</summary>
    public void KickPeer(long peer)
    {
        if (IsInsideTree() && Multiplayer?.MultiplayerPeer is ENetMultiplayerPeer enet)
        {
            enet.DisconnectPeer((int)peer);
        }
    }

    public void SendToServer(string json) => RpcId(1, "ToServer", json);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ToServer(string json)
    {
        if (Multiplayer.IsServer())
        {
            ServerMessage?.Invoke(Multiplayer.GetRemoteSenderId(), json);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ToClient(string json) => ClientMessage?.Invoke(json);
}

/// <summary>NetBridge를 호스트 세션용 전송 계층으로 감쌉니다. 방을 나갈 때 Dispose로 연결을 풉니다.</summary>
public sealed class ServerTransport : IServerTransport, IDisposable
{
    private readonly NetBridge _bridge;

    public event Action<long>? PeerConnected;
    public event Action<long>? PeerDisconnected;
    public event Action<long, string>? Received;

    public ServerTransport(NetBridge bridge)
    {
        _bridge = bridge;
        _bridge.PeerJoined += OnJoined;
        _bridge.PeerLeft += OnLeft;
        _bridge.ServerMessage += OnMessage;
    }

    private void OnJoined(long id) => PeerConnected?.Invoke(id);
    private void OnLeft(long id) => PeerDisconnected?.Invoke(id);
    private void OnMessage(long id, string json) => Received?.Invoke(id, json);

    public void Send(long peer, string json) => _bridge.SendToPeer(peer, json);

    public void Kick(long peer) => _bridge.KickPeer(peer);

    /// <summary>IP 접속은 같은 PC에서 여러 창으로 테스트하는 경우가 많아서 차단용 식별값을 쓰지 않습니다.</summary>
    public string IdentityOf(long peer) => "";

    public void Dispose()
    {
        _bridge.PeerJoined -= OnJoined;
        _bridge.PeerLeft -= OnLeft;
        _bridge.ServerMessage -= OnMessage;
    }
}

/// <summary>NetBridge를 클라이언트 세션용 전송 계층으로 감쌉니다. 방을 나갈 때 Dispose로 연결을 풉니다.</summary>
public sealed class ClientTransport : IClientTransport, IDisposable
{
    private readonly NetBridge _bridge;

    public event Action? Connected;
    public event Action<string>? Disconnected;
    public event Action<string>? Received;

    public ClientTransport(NetBridge bridge)
    {
        _bridge = bridge;
        _bridge.ConnectedOk += OnConnected;
        _bridge.ConnectionLost += OnLost;
        _bridge.ClientMessage += OnMessage;
    }

    private void OnConnected() => Connected?.Invoke();
    private void OnLost(string reason) => Disconnected?.Invoke(reason);
    private void OnMessage(string json) => Received?.Invoke(json);

    public void Send(string json) => _bridge.SendToServer(json);

    public void Dispose()
    {
        _bridge.ConnectedOk -= OnConnected;
        _bridge.ConnectionLost -= OnLost;
        _bridge.ClientMessage -= OnMessage;
    }
}
