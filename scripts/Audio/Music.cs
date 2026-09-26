using Godot;

namespace SpCardgame.Audio;

/// <summary>
/// 배경음악을 재생하는 노드입니다. 곡이 끝나면 처음부터 다시 틀고, 시작할 때 천천히 커집니다.
/// 게임 중에는 효과음이 잘 들리도록 메인 화면보다 조금 작게 틉니다.
/// </summary>
public partial class Music : Node
{
    public const string BusName = "Music";
    public const string TrackPath = "res://audio/music/backbay_lounge.ogg";

    /// <summary>화면에 보여 줄 음악 저작권 표기입니다. (CC BY 4.0은 출처 표기가 조건입니다)</summary>
    public const string Credit = "배경음악: \"Backbay Lounge\" Kevin MacLeod (incompetech.com) · CC BY 4.0";

    private const float MenuDb = -4f;
    private const float GameDb = -9f;

    private static Music? _instance;
    private AudioStreamPlayer _player = null!;
    private Tween? _fade;

    public override void _EnterTree()
    {
        _instance = this;
        EnsureBus();
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    public override void _Ready()
    {
        _player = new AudioStreamPlayer { Bus = BusName, VolumeDb = -40f };
        AddChild(_player);

        if (!ResourceLoader.Exists(TrackPath))
        {
            GD.PrintErr($"[음악] {TrackPath} 파일이 없어요.");
            return;
        }

        var stream = GD.Load<AudioStream>(TrackPath);
        if (stream is AudioStreamOggVorbis ogg)
        {
            ogg.Loop = true;
        }

        _player.Stream = stream;
        _player.Play();
        FadeTo(MenuDb, 2.5f);
    }

    /// <summary>음악 버스가 없으면 만들어서 Master로 보냅니다.</summary>
    public static void EnsureBus()
    {
        if (AudioServer.GetBusIndex(BusName) >= 0)
        {
            return;
        }

        AudioServer.AddBus();
        int index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, BusName);
        AudioServer.SetBusSend(index, "Master");
    }

    /// <summary>게임 화면인지 메인 화면인지에 맞춰 음악 크기를 부드럽게 바꿉니다.</summary>
    public static void SetInGame(bool inGame) => _instance?.FadeTo(inGame ? GameDb : MenuDb, 1.2f);

    private void FadeTo(float db, float seconds)
    {
        if (_player == null || !IsInsideTree())
        {
            return;
        }

        _fade?.Kill();
        _fade = CreateTween();
        _fade.TweenProperty(_player, "volume_db", db, seconds).SetTrans(Tween.TransitionType.Sine);
    }
}
