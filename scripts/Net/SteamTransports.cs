using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Steamworks;

namespace SpCardgame.Net;

/// <summary>
/// 매 프레임 받은 메시지를 꺼내야 하는 전송 계층입니다. (Steam 소켓은 콜백이 아니라 직접 꺼내는 방식입니다.)
/// </summary>
public interface IPollingTransport
{
    void Poll();
}

/// <summary>
/// Steam P2P 소켓으로 문자열을 주고받는 공통 기능입니다.
/// Steam 중계망(SDR)을 거치므로 포트포워딩 없이 연결됩니다.
/// </summary>
internal static class SteamMessaging
{
    public static void Send(HSteamNetConnection connection, string json)
    {
        byte[] data = Encoding.UTF8.GetBytes(json);
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            SteamNetworkingSockets.SendMessageToConnection(connection, handle.AddrOfPinnedObject(), (uint)data.Length,
                Constants.k_nSteamNetworkingSend_Reliable, out _);
        }
        finally
        {
            handle.Free();
        }
    }

    public static List<string> Receive(HSteamNetConnection connection)
    {
        var messages = new List<string>();
        var pointers = new IntPtr[32];
        int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection, pointers, pointers.Length);

        for (int i = 0; i < count; i++)
        {
            var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pointers[i]);
            var bytes = new byte[message.m_cbSize];
            Marshal.Copy(message.m_pData, bytes, 0, message.m_cbSize);
            SteamNetworkingMessage_t.Release(pointers[i]);
            messages.Add(Encoding.UTF8.GetString(bytes));
        }

        return messages;
    }

    public static bool IsClosed(ESteamNetworkingConnectionState state) =>
        state is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
            or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally;
}

/// <summary>
/// 호스트 쪽 Steam 전송 계층입니다. P2P 리슨 소켓을 열고 들어오는 연결을 받습니다.
/// </summary>
public sealed class SteamServerTransport : IServerTransport, IPollingTransport, IDisposable
{
    private readonly HSteamListenSocket _listen;
    private readonly List<HSteamNetConnection> _connections = new();

    public event Action<long>? PeerConnected;
    public event Action<long>? PeerDisconnected;
    public event Action<long, string>? Received;

    public SteamServerTransport()
    {
        _listen = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
        SteamRuntime.ConnectionStatusChanged += OnStatusChanged;
        SteamRuntime.LobbyMemberLeft += OnLobbyMemberLeft;
    }

    /// <summary>
    /// Steam 로비에서 나간 사람의 연결을 바로 정리합니다.
    /// P2P 연결 종료 알림은 늦거나 오지 않을 때가 있어서(방장 화면에 계속 남는 문제), 로비 알림을 함께 씁니다.
    /// </summary>
    private void OnLobbyMemberLeft(ulong steamId)
    {
        foreach (var connection in _connections.ToArray())
        {
            if (SteamNetworkingSockets.GetConnectionInfo(connection, out var info)
                && info.m_identityRemote.GetSteamID().m_SteamID == steamId)
            {
                _connections.Remove(connection);
                SteamNetworkingSockets.CloseConnection(connection, 0, "", false);
                PeerDisconnected?.Invoke(connection.m_HSteamNetConnection);
            }
        }
    }

    private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
    {
        if (data.m_info.m_hListenSocket != _listen)
        {
            return;
        }

        var connection = data.m_hConn;
        switch (data.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                SteamNetworkingSockets.AcceptConnection(connection);
                break;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                _connections.Add(connection);
                PeerConnected?.Invoke(connection.m_HSteamNetConnection);
                break;

            default:
                if (SteamMessaging.IsClosed(data.m_info.m_eState))
                {
                    if (_connections.Remove(connection))
                    {
                        PeerDisconnected?.Invoke(connection.m_HSteamNetConnection);
                    }

                    SteamNetworkingSockets.CloseConnection(connection, 0, "", false);
                }

                break;
        }
    }

    public void Send(long peer, string json)
    {
        foreach (var connection in _connections)
        {
            if (connection.m_HSteamNetConnection == (uint)peer)
            {
                SteamMessaging.Send(connection, json);
                return;
            }
        }
    }

    public void Kick(long peer)
    {
        var connection = _connections.FirstOrDefault(c => c.m_HSteamNetConnection == (uint)peer);
        if (connection.m_HSteamNetConnection == 0)
        {
            return;
        }

        _connections.Remove(connection);
        SteamNetworkingSockets.CloseConnection(connection, 0, "방장이 강퇴했어요.", true);
        PeerDisconnected?.Invoke(peer);
    }

    public string IdentityOf(long peer)
    {
        foreach (var connection in _connections)
        {
            if (connection.m_HSteamNetConnection == (uint)peer &&
                SteamNetworkingSockets.GetConnectionInfo(connection, out var info))
            {
                return info.m_identityRemote.GetSteamID().m_SteamID.ToString();
            }
        }

        return "";
    }

    public void Poll()
    {
        foreach (var connection in _connections.ToArray())
        {
            foreach (string json in SteamMessaging.Receive(connection))
            {
                Received?.Invoke(connection.m_HSteamNetConnection, json);
            }
        }
    }

    public void Dispose()
    {
        SteamRuntime.ConnectionStatusChanged -= OnStatusChanged;
        SteamRuntime.LobbyMemberLeft -= OnLobbyMemberLeft;
        foreach (var connection in _connections)
        {
            SteamNetworkingSockets.CloseConnection(connection, 0, "호스트가 방을 닫았어요.", false);
        }

        _connections.Clear();
        SteamNetworkingSockets.CloseListenSocket(_listen);
    }
}

/// <summary>
/// 참가자 쪽 Steam 전송 계층입니다. 방장의 Steam ID로 P2P 연결을 겁니다.
/// </summary>
public sealed class SteamClientTransport : IClientTransport, IPollingTransport, IDisposable
{
    private readonly HSteamNetConnection _connection;
    private bool _connected;

    public event Action? Connected;
    public event Action<string>? Disconnected;
    public event Action<string>? Received;

    public SteamClientTransport(CSteamID host)
    {
        SteamRuntime.ConnectionStatusChanged += OnStatusChanged;
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID(host);
        _connection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, null);
    }

    private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
    {
        if (data.m_hConn != _connection)
        {
            return;
        }

        if (data.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
        {
            _connected = true;
            Connected?.Invoke();
        }
        else if (SteamMessaging.IsClosed(data.m_info.m_eState))
        {
            string reason = _connected ? "호스트와의 연결이 끊겼어요." : "호스트에 연결하지 못했어요.";
            _connected = false;
            SteamNetworkingSockets.CloseConnection(_connection, 0, "", false);
            Disconnected?.Invoke($"{reason} ({data.m_info.m_szEndDebug})");
        }
    }

    public void Send(string json)
    {
        if (_connected)
        {
            SteamMessaging.Send(_connection, json);
        }
    }

    public void Poll()
    {
        if (!_connected)
        {
            return;
        }

        foreach (string json in SteamMessaging.Receive(_connection))
        {
            Received?.Invoke(json);
        }
    }

    public void Dispose()
    {
        SteamRuntime.ConnectionStatusChanged -= OnStatusChanged;
        // linger = true: 방금 보낸 작별 인사(bye)가 방장에게 도착한 뒤에 연결을 닫습니다.
        SteamNetworkingSockets.CloseConnection(_connection, 0, "", true);
    }
}
