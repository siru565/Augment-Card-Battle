using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;

namespace SpCardgame.Net;

/// <summary>
/// Steam API 초기화, 콜백 처리, 로비 만들기·참가를 담당합니다. 프로세스당 한 번만 초기화합니다.
/// 개발 중에는 Valve의 테스트용 앱 ID 480(Spacewar)을 씁니다. 출시할 때 내 앱 ID로 바꾸면 됩니다.
/// </summary>
public static class SteamRuntime
{
    public const uint AppId = 480;

    /// <summary>로비 데이터에 넣는 게임 식별자입니다. 480을 쓰는 다른 게임과 섞이지 않게 합니다.</summary>
    private const string GameTag = "augment-card-battle";

    private static bool _initTried;
    private static bool _resolverInstalled;
    private static Callback<GameLobbyJoinRequested_t>? _joinRequested;
    private static Callback<SteamNetConnectionStatusChangedCallback_t>? _connectionStatus;
    private static Callback<LobbyInvite_t>? _lobbyInvite;
    private static Callback<LobbyChatUpdate_t>? _lobbyChatUpdate;
    private static Callback<AvatarImageLoaded_t>? _avatarLoaded;
    private static readonly Dictionary<ulong, ImageTexture?> AvatarCache = new();
    private static Callback<GameRichPresenceJoinRequested_t>? _presenceJoin;
    private static CallResult<LobbyCreated_t>? _lobbyCreated;
    private static CallResult<LobbyEnter_t>? _lobbyEntered;
    private static Action<CSteamID>? _onCreated;
    private static Action<CSteamID, CSteamID>? _onEntered;
    private static Action<string>? _onFailed;

    public static bool IsReady { get; private set; }

    public static string InitError { get; private set; } = "";

    /// <summary>현재 들어가 있는 로비입니다. 없으면 Nil입니다.</summary>
    public static CSteamID CurrentLobby { get; private set; } = CSteamID.Nil;

    /// <summary>Steam 친구 목록이나 초대로 "게임 참가"를 눌렀을 때 호출합니다.</summary>
    public static event Action<CSteamID>? JoinRequested;

    /// <summary>늦게 도착한 프로필 사진이 준비됐을 때 호출합니다. (Steam ID)</summary>
    public static event Action<ulong>? AvatarLoaded;

    /// <summary>게임 안에서 친구의 방 초대를 받았을 때 호출합니다. (보낸 사람 이름, 로비)</summary>
    public static event Action<string, CSteamID>? InviteReceived;

    /// <summary>
    /// 지금 들어가 있는 Steam 로비에서 누군가 나갔을 때 호출합니다. (Steam ID)
    /// 나간 사람의 게임이 연결을 제대로 닫지 못해도(강제 종료 등) Steam이 로비에서 빼 주므로, 방장이 빨리 알아챌 수 있습니다.
    /// </summary>
    public static event Action<ulong>? LobbyMemberLeft;

    /// <summary>P2P 연결 상태가 바뀔 때 호출합니다. 전송 계층이 구독합니다.</summary>
    public static event Action<SteamNetConnectionStatusChangedCallback_t>? ConnectionStatusChanged;

    public static string PersonaName => IsReady ? SteamFriends.GetPersonaName() : "";

    /// <summary>내 Steam ID입니다. Steam이 없으면 0입니다.</summary>
    public static ulong MySteamId => IsReady ? SteamUser.GetSteamID().m_SteamID : 0;

    // ───────────── 프로필 사진 ─────────────

    /// <summary>
    /// Steam 프로필 사진(184×184)을 텍스처로 돌려줍니다. 아직 받는 중이거나 없으면 null이고,
    /// 받는 중이었다면 도착했을 때 AvatarLoaded가 호출됩니다.
    /// </summary>
    public static Texture2D? GetAvatar(ulong steamId)
    {
        if (!IsReady || steamId == 0)
        {
            return null;
        }

        if (AvatarCache.TryGetValue(steamId, out var cached))
        {
            return cached;
        }

        var id = new CSteamID(steamId);
        int handle = SteamFriends.GetLargeFriendAvatar(id);

        // -1: 아직 받는 중 (도착하면 콜백이 옵니다), 0: 사용자 정보가 없어서 먼저 요청해야 합니다.
        if (handle == -1)
        {
            return null;
        }

        if (handle == 0)
        {
            SteamFriends.RequestUserInformation(id, false);
            return null;
        }

        var texture = ToTexture(handle);
        AvatarCache[steamId] = texture;
        return texture;
    }

    /// <summary>움직이는 아바타처럼 다른 곳에서 준비한 사진이 도착했음을 화면들에 알립니다.</summary>
    internal static void NotifyAvatarLoaded(ulong steamId) => AvatarLoaded?.Invoke(steamId);

    /// <summary>Steam 이미지 핸들의 RGBA 픽셀을 Godot 텍스처로 바꿉니다.</summary>
    private static ImageTexture? ToTexture(int handle)
    {
        if (!SteamUtils.GetImageSize(handle, out uint width, out uint height) || width == 0 || height == 0)
        {
            return null;
        }

        var pixels = new byte[width * height * 4];
        if (!SteamUtils.GetImageRGBA(handle, pixels, pixels.Length))
        {
            return null;
        }

        var image = Image.CreateFromData((int)width, (int)height, false, Image.Format.Rgba8, pixels);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Steam을 초기화하고, 매 프레임 콜백을 돌릴 노드를 트리에 붙입니다. 여러 번 불러도 한 번만 동작합니다.
    /// </summary>
    public static void EnsureInitialized(Node anyNode)
    {
        if (_initTried)
        {
            return;
        }

        _initTried = true;

        try
        {
            InstallDllResolver();

            // steam_appid.txt가 없어도 초기화되도록 환경 변수로도 앱 ID를 알려 줍니다.
            System.Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
            System.Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());

            var result = SteamAPI.InitEx(out string message);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                InitError = result == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient
                    ? "Steam이 실행 중이 아니에요. Steam에 로그인한 뒤 게임을 다시 켜 주세요."
                    : $"Steam 초기화 실패: {message}";
                return;
            }

            SteamNetworkingUtils.InitRelayNetworkAccess();
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(data => JoinRequested?.Invoke(data.m_steamIDLobby));
            _connectionStatus = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(data => ConnectionStatusChanged?.Invoke(data));
            _lobbyInvite = Callback<LobbyInvite_t>.Create(OnLobbyInvite);
            _lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _avatarLoaded = Callback<AvatarImageLoaded_t>.Create(data =>
            {
                ulong id = data.m_steamID.m_SteamID;
                AvatarCache.Remove(id);
                AvatarLoaded?.Invoke(id);
            });
            _presenceJoin = Callback<GameRichPresenceJoinRequested_t>.Create(data =>
            {
                if (TryParseConnect(data.m_rgchConnect, out var lobby))
                {
                    JoinRequested?.Invoke(lobby);
                }
            });
            _lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            IsReady = true;

            anyNode.GetTree().Root.CallDeferred(Node.MethodName.AddChild, new SteamPump());
        }
        catch (Exception e)
        {
            InitError = $"Steam 라이브러리를 불러오지 못했어요: {e.Message}";
        }
    }

    // ───────────── 로비 ─────────────

    /// <summary>로비를 만듭니다. 성공하면 onCreated(로비 ID)를 부릅니다.</summary>
    public static void CreateLobby(int maxMembers, Action<CSteamID> onCreated, Action<string> onFailed)
    {
        if (!IsReady)
        {
            onFailed(InitError);
            return;
        }

        LeaveLobby();
        _onCreated = onCreated;
        _onFailed = onFailed;
        _lobbyCreated!.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, maxMembers));
    }

    /// <summary>로비에 들어갑니다. 성공하면 onEntered(로비 ID, 방장 ID)를 부릅니다.</summary>
    public static void JoinLobby(CSteamID lobby, Action<CSteamID, CSteamID> onEntered, Action<string> onFailed)
    {
        if (!IsReady)
        {
            onFailed(InitError);
            return;
        }

        LeaveLobby();
        _onEntered = onEntered;
        _onFailed = onFailed;
        _lobbyEntered!.Set(SteamMatchmaking.JoinLobby(lobby));
    }

    public static void LeaveLobby()
    {
        if (IsReady && CurrentLobby != CSteamID.Nil)
        {
            SteamMatchmaking.LeaveLobby(CurrentLobby);
            SteamFriends.ClearRichPresence();
        }

        CurrentLobby = CSteamID.Nil;
    }

    // ───────────── 친구 초대 ─────────────

    /// <summary>초대 목록에 보여 줄 친구 한 명입니다.</summary>
    public sealed record FriendEntry(CSteamID Id, string Name, bool Online, bool InThisGame);

    /// <summary>
    /// 친구 목록을 가져옵니다. 이 게임을 켜 둔 친구 → 온라인 친구 → 오프라인 친구 순서입니다.
    /// </summary>
    public static List<FriendEntry> GetFriends()
    {
        var result = new List<FriendEntry>();
        if (!IsReady)
        {
            return result;
        }

        int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        for (int i = 0; i < count; i++)
        {
            var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            bool online = SteamFriends.GetFriendPersonaState(id) != EPersonaState.k_EPersonaStateOffline;
            bool inGame = SteamFriends.GetFriendGamePlayed(id, out var played) && played.m_gameID.AppID().m_AppId == AppId;
            result.Add(new FriendEntry(id, SteamFriends.GetFriendPersonaName(id), online, inGame));
        }

        return result
            .OrderByDescending(f => f.InThisGame)
            .ThenByDescending(f => f.Online)
            .ThenBy(f => f.Name)
            .ToList();
    }

    /// <summary>
    /// 친구에게 지금 방의 초대를 보냅니다. 친구가 이 게임을 켜 두었다면 게임 안에 초대 알림이 뜹니다.
    /// (오버레이가 없어도 동작합니다)
    /// </summary>
    public static bool InviteFriend(CSteamID friend) =>
        IsReady && CurrentLobby != CSteamID.Nil && SteamMatchmaking.InviteUserToLobby(CurrentLobby, friend);

    /// <summary>나감(2) · 연결 끊김(4) · 추방(8) · 차단(16) 중 하나면 로비를 떠난 것입니다.</summary>
    private const uint MemberGoneFlags = 2 | 4 | 8 | 16;

    private static void OnLobbyChatUpdate(LobbyChatUpdate_t data)
    {
        if (data.m_ulSteamIDLobby == CurrentLobby.m_SteamID && (data.m_rgfChatMemberStateChange & MemberGoneFlags) != 0)
        {
            LobbyMemberLeft?.Invoke(data.m_ulSteamIDUserChanged);
        }
    }

    private static void OnLobbyInvite(LobbyInvite_t data)
    {
        var from = new CSteamID(data.m_ulSteamIDUser);
        var lobby = new CSteamID(data.m_ulSteamIDLobby);
        if (lobby == CurrentLobby)
        {
            return;
        }

        InviteReceived?.Invoke(SteamFriends.GetFriendPersonaName(from), lobby);
    }

    /// <summary>
    /// 친구 목록의 "게임 참가"로 들어올 수 있도록 접속 정보를 Steam 상태에 올려 둡니다.
    /// </summary>
    private static void PublishPresence()
    {
        if (IsReady && CurrentLobby != CSteamID.Nil)
        {
            SteamFriends.SetRichPresence("connect", $"+connect_lobby {CurrentLobby.m_SteamID}");
        }
    }

    /// <summary>"+connect_lobby 숫자" 형식의 접속 문자열에서 로비를 꺼냅니다. (명령줄 인자도 같은 형식입니다)</summary>
    public static bool TryParseConnect(string text, out CSteamID lobby)
    {
        lobby = CSteamID.Nil;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int index = Array.IndexOf(parts, "+connect_lobby");
        if (index < 0 || index + 1 >= parts.Length || !ulong.TryParse(parts[index + 1], out ulong value))
        {
            return false;
        }

        lobby = new CSteamID(value);
        return true;
    }

    /// <summary>Steam 오버레이의 친구 초대 창을 엽니다. (Shift+Tab 오버레이가 켜져 있어야 합니다.)</summary>
    public static void OpenInviteDialog()
    {
        if (IsReady && CurrentLobby != CSteamID.Nil)
        {
            SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobby);
        }
    }

    public static void SetLobbyJoinable(bool joinable)
    {
        if (IsReady && CurrentLobby != CSteamID.Nil)
        {
            SteamMatchmaking.SetLobbyJoinable(CurrentLobby, joinable);
        }
    }

    private static void OnLobbyCreated(LobbyCreated_t data, bool ioFailure)
    {
        if (ioFailure || data.m_eResult != EResult.k_EResultOK)
        {
            _onFailed?.Invoke($"Steam 로비를 만들지 못했어요. ({data.m_eResult})");
            return;
        }

        CurrentLobby = new CSteamID(data.m_ulSteamIDLobby);
        SteamMatchmaking.SetLobbyData(CurrentLobby, "game", GameTag);
        SteamMatchmaking.SetLobbyData(CurrentLobby, "host", PersonaName);
        PublishPresence();
        _onCreated?.Invoke(CurrentLobby);
    }

    private static void OnLobbyEntered(LobbyEnter_t data, bool ioFailure)
    {
        // 1은 k_EChatRoomEnterResponseSuccess입니다.
        if (ioFailure || data.m_EChatRoomEnterResponse != 1)
        {
            _onFailed?.Invoke("방에 들어가지 못했어요. 코드가 맞는지, 방이 아직 열려 있는지 확인해 주세요.");
            return;
        }

        CurrentLobby = new CSteamID(data.m_ulSteamIDLobby);
        if (SteamMatchmaking.GetLobbyData(CurrentLobby, "game") != GameTag)
        {
            LeaveLobby();
            _onFailed?.Invoke("이 게임의 방이 아니에요.");
            return;
        }

        PublishPresence();
        _onEntered?.Invoke(CurrentLobby, SteamMatchmaking.GetLobbyOwner(CurrentLobby));
    }

    // ───────────── 방 코드 ─────────────

    /// <summary>로비 ID(64비트 숫자)를 친구에게 알려 주기 쉬운 36진수 코드로 바꿉니다.</summary>
    public static string ToRoomCode(CSteamID lobby)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        ulong value = lobby.m_SteamID;
        var chars = new System.Text.StringBuilder();
        while (value > 0)
        {
            chars.Insert(0, digits[(int)(value % 36)]);
            value /= 36;
        }

        return chars.ToString();
    }

    public static bool TryParseRoomCode(string code, out CSteamID lobby)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        lobby = CSteamID.Nil;
        ulong value = 0;
        foreach (char c in code.Trim().ToUpperInvariant())
        {
            if (c == '-' || c == ' ')
            {
                continue;
            }

            int digit = digits.IndexOf(c);
            if (digit < 0)
            {
                return false;
            }

            value = value * 36 + (ulong)digit;
        }

        lobby = new CSteamID(value);
        return value != 0;
    }

    // ───────────── 네이티브 DLL 찾기 ─────────────

    /// <summary>
    /// Godot C#에서는 steam_api64.dll을 기본 경로에서 못 찾는 경우가 있어서, 직접 후보 폴더를 뒤져서 불러옵니다.
    /// (프로젝트 폴더, 빌드 출력 폴더, 내보낸 게임의 실행 파일 폴더)
    /// </summary>
    private static void InstallDllResolver()
    {
        if (_resolverInstalled)
        {
            return;
        }

        _resolverInstalled = true;
        NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly, ResolveSteamLibrary);
    }

    private static IntPtr ResolveSteamLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.StartsWith("steam_api"))
        {
            return IntPtr.Zero;
        }

        string fileName = OperatingSystem.IsWindows() ? "steam_api64.dll"
            : OperatingSystem.IsMacOS() ? "libsteam_api.dylib"
            : "libsteam_api.so";

        string[] folders =
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(assembly.Location) ?? "",
            ProjectSettings.GlobalizePath("res://"),
            Path.GetDirectoryName(OS.GetExecutablePath()) ?? "",
        };

        foreach (string folder in folders)
        {
            if (string.IsNullOrEmpty(folder))
            {
                continue;
            }

            string path = Path.Combine(folder, fileName);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }
}

/// <summary>
/// 매 프레임 Steam 콜백을 처리하는 노드입니다. SteamRuntime이 자동으로 트리에 붙입니다.
/// </summary>
public partial class SteamPump : Node
{
    public override void _Process(double delta)
    {
        if (SteamRuntime.IsReady)
        {
            SteamAPI.RunCallbacks();
        }
    }

    public override void _ExitTree()
    {
        if (SteamRuntime.IsReady)
        {
            SteamRuntime.LeaveLobby();
            SteamAPI.Shutdown();
        }
    }
}
