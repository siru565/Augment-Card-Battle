using System.Collections.Generic;
using Godot;

namespace SpCardgame.UI;

/// <summary>
/// 화면 부품 씬(.tscn)의 경로를 모아 두고, 불러온 PackedScene을 재사용해서 인스턴스를 만듭니다.
/// 부품의 모양(노드 구성, 크기, 테마 변형)은 씬 파일에서 에디터로 고치고, 코드는 값만 채웁니다.
/// </summary>
public static class Scenes
{
    public const string Seat = "res://scenes/ui/Seat.tscn";
    public const string AugmentChip = "res://scenes/ui/AugmentChip.tscn";
    public const string AbilityCard = "res://scenes/ui/AbilityCard.tscn";
    public const string RankRow = "res://scenes/ui/RankRow.tscn";
    public const string RoomRow = "res://scenes/ui/RoomRow.tscn";
    public const string FriendRow = "res://scenes/ui/FriendRow.tscn";

    private static readonly Dictionary<string, PackedScene> Cache = new();

    /// <summary>씬을 인스턴스로 만듭니다. 같은 씬은 한 번만 불러옵니다.</summary>
    public static T Create<T>(string path) where T : Node
    {
        if (!Cache.TryGetValue(path, out var scene))
        {
            scene = GD.Load<PackedScene>(path);
            Cache[path] = scene;
        }

        return scene.Instantiate<T>();
    }
}
