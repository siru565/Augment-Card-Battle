using System.Text.Json;
using System.Text.Json.Serialization;
using SpCardgame.Core;

namespace SpCardgame.Net;

/// <summary>
/// 호스트와 클라이언트가 주고받는 메시지입니다. JSON 문자열 한 줄로 보냅니다.
/// T 값으로 종류를 구분하고, 종류마다 필요한 필드만 채웁니다.
/// </summary>
public sealed record NetMessage
{
    // 클라이언트 → 호스트
    public const string Hello = "hello";
    public const string Action = "action";
    public const string Leave = "leave";

    // 호스트 → 클라이언트
    public const string Lobby = "lobby";
    public const string Start = "start";
    public const string View = "view";
    public const string Log = "log";
    public const string Fx = "fx";
    public const string Room = "room";
    public const string Kicked = "kicked";
    public const string Error = "error";

    public string T { get; init; } = "";
    public string? Text { get; init; }

    /// <summary>게임 버전입니다. 접속할 때 호스트와 같은지 확인합니다.</summary>
    public string? Version { get; init; }
    public string[]? Names { get; init; }
    public int Seat { get; init; } = -1;
    public PlayerView? State { get; init; }
    public PlayerAction? Move { get; init; }

    /// <summary>방장이 고른 판 설정입니다. (대기방 정보에 함께 보냅니다)</summary>
    public GameOptions? Options { get; init; }

    /// <summary>화면 연출용 사건입니다.</summary>
    public GameEvent? Event { get; init; }

    /// <summary>대기방 정보를 보낼 때, 지금 게임이 진행 중인지 알려 줍니다.</summary>
    public bool Playing { get; init; }

    /// <summary>대기방 사람마다 지금 게임에 앉아 있는지 표시합니다. (0번은 방장)</summary>
    public bool[]? Busy { get; init; }

    /// <summary>
    /// 대기방 사람(또는 게임 자리)마다의 Steam ID입니다. 프로필 사진을 가져올 때 씁니다.
    /// IP 접속이나 봇은 0입니다.
    /// </summary>
    public ulong[]? SteamIds { get; init; }

    /// <summary>대기방 정보를 보낼 때, 다음 판에 앉을 봇 수를 알려 줍니다.</summary>
    public int Bots { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static NetMessage? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<NetMessage>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
