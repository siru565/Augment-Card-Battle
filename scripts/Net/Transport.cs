using System;

namespace SpCardgame.Net;

/// <summary>
/// 호스트 쪽 전송 계층입니다. 실제 게임에서는 ENet(NetBridge), 테스트에서는 메모리 전송을 씁니다.
/// </summary>
public interface IServerTransport
{
    event Action<long>? PeerConnected;
    event Action<long>? PeerDisconnected;
    event Action<long, string>? Received;

    void Send(long peer, string json);

    /// <summary>해당 피어의 연결을 끊습니다. (강퇴)</summary>
    void Kick(long peer);

    /// <summary>
    /// 다시 접속해도 같은 사람임을 알 수 있는 값입니다. (Steam ID) 알 수 없으면 빈 문자열입니다.
    /// 강퇴한 사람이 다시 들어오지 못하게 막을 때 씁니다.
    /// </summary>
    string IdentityOf(long peer);
}

/// <summary>
/// 클라이언트 쪽 전송 계층입니다.
/// </summary>
public interface IClientTransport
{
    event Action? Connected;
    event Action<string>? Disconnected;
    event Action<string>? Received;

    void Send(string json);
}
