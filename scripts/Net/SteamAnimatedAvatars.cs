using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using SpCardgame.Util;

namespace SpCardgame.Net;

/// <summary>
/// Steam 포인트 상점의 "움직이는 아바타"(GIF)를 받아서 프레임 텍스처로 만들어 둡니다.
/// Steamworks SDK는 정지 사진만 주기 때문에, 공개 웹 API(IPlayerService/GetAnimatedAvatar)로 주소를 받고
/// Steam CDN에서 GIF를 내려받아 GifDecoder로 풉니다. 실패하거나 없으면 정지 사진을 그대로 씁니다.
/// </summary>
public static class SteamAnimatedAvatars
{
    private const string ApiUrl = "https://api.steampowered.com/IPlayerService/GetAnimatedAvatar/v1/?steamid=";
    private const string CdnUrl = "https://cdn.akamai.steamstatic.com/steamcommunity/public/images/";

    /// <summary>재생할 수 있게 준비된 움직이는 아바타입니다. 모든 화면이 같은 시계로 재생해서 서로 맞춰 움직입니다.</summary>
    public sealed class Animation
    {
        public Texture2D[] Frames { get; }

        /// <summary>각 프레임이 끝나는 시각(ms, 누적)입니다.</summary>
        private readonly int[] _ends;

        public int TotalMs { get; }

        public Animation(Texture2D[] frames, int[] delays)
        {
            Frames = frames;
            _ends = new int[delays.Length];
            int sum = 0;
            for (int i = 0; i < delays.Length; i++)
            {
                sum += Math.Max(20, delays[i]);
                _ends[i] = sum;
            }

            TotalMs = Math.Max(1, sum);
        }

        /// <summary>지금 시각(ms)에 보여 줄 프레임 번호입니다.</summary>
        public int FrameAt(ulong timeMs)
        {
            int t = (int)(timeMs % (ulong)TotalMs);
            int index = Array.BinarySearch(_ends, t + 1);
            if (index < 0)
            {
                index = ~index;
            }

            return Math.Min(index, Frames.Length - 1);
        }
    }

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Steam ID별 결과입니다. 값이 null이면 받는 중이거나 움직이는 아바타가 없는 것입니다.</summary>
    private static readonly Dictionary<ulong, Animation?> Cache = new();

    /// <summary>
    /// 움직이는 아바타를 돌려줍니다. 처음 부르면 백그라운드로 받기 시작하고 null을 돌려주며,
    /// 준비가 끝나면 SteamRuntime.AvatarLoaded로 알려 줍니다.
    /// </summary>
    public static Animation? Get(ulong steamId)
    {
        if (!SteamRuntime.IsReady || steamId == 0)
        {
            return null;
        }

        if (Cache.TryGetValue(steamId, out var cached))
        {
            return cached;
        }

        Cache[steamId] = null;
        _ = Task.Run(() => FetchAsync(steamId));
        return null;
    }

    private static async Task FetchAsync(ulong steamId)
    {
        try
        {
            string json = await Http.GetStringAsync(ApiUrl + steamId);
            string? path = ReadGifPath(json);
            if (path == null)
            {
                return;
            }

            byte[] bytes = await Http.GetByteArrayAsync(CdnUrl + path);
            var gif = GifDecoder.Decode(bytes, maxFrames: 150);
            if (gif == null || gif.Frames.Count < 2)
            {
                return;
            }

            // 이미지까지는 백그라운드에서 만들고, 텍스처는 메인 스레드에서 만듭니다.
            var images = new Image[gif.Frames.Count];
            var delays = new int[gif.Frames.Count];
            for (int i = 0; i < images.Length; i++)
            {
                images[i] = Image.CreateFromData(gif.Width, gif.Height, false, Image.Format.Rgba8, gif.Frames[i].Rgba);
                delays[i] = gif.Frames[i].DelayMs;
            }

            Callable.From(() => Finish(steamId, images, delays)).CallDeferred();
        }
        catch (Exception e)
        {
            GD.Print($"[Avatar] 움직이는 아바타를 받지 못해 정지 사진을 씁니다: {e.Message}");
        }
    }

    private static void Finish(ulong steamId, Image[] images, int[] delays)
    {
        var textures = new Texture2D[images.Length];
        for (int i = 0; i < images.Length; i++)
        {
            textures[i] = ImageTexture.CreateFromImage(images[i]);
        }

        Cache[steamId] = new Animation(textures, delays);
        SteamRuntime.NotifyAvatarLoaded(steamId);
    }

    /// <summary>API 응답에서 GIF 경로(image_small)를 꺼냅니다. 움직이는 아바타가 없으면 null입니다.</summary>
    private static string? ReadGifPath(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out var response)
            || !response.TryGetProperty("avatar", out var avatar))
        {
            return null;
        }

        foreach (var key in new[] { "image_small", "image_large" })
        {
            if (avatar.TryGetProperty(key, out var value) && value.GetString() is { } path
                && path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return null;
    }
}
