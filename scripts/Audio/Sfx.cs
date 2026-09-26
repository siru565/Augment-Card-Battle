using System.Collections.Generic;
using Godot;

namespace SpCardgame.Audio;

/// <summary>
/// 효과음을 재생하는 노드입니다. 처음 쓸 때 소리를 합성해 두고, 여러 소리가 겹쳐도 되도록 플레이어를 여러 개 돌려 씁니다.
/// res://audio/sfx/이름.wav(또는 .ogg)가 있으면 합성음 대신 그 파일을 씁니다. (나중에 산 에셋으로 바로 교체할 수 있습니다)
/// </summary>
public partial class Sfx : Node
{
    public const string BusName = "SFX";
    private const int Voices = 12;

    private static Sfx? _instance;
    private readonly Dictionary<string, AudioStream> _streams = new();
    private readonly List<AudioStreamPlayer> _players = new();
    private readonly RandomNumberGenerator _rng = new();
    private int _next;

    /// <summary>같은 소리가 너무 빽빽하게 겹치지 않도록 마지막 재생 시각(ms)을 기억합니다.</summary>
    private readonly Dictionary<string, ulong> _lastPlayed = new();

    public override void _EnterTree()
    {
        _instance = this;
        EnsureBus();
        for (int i = 0; i < Voices; i++)
        {
            var player = new AudioStreamPlayer { Bus = BusName };
            AddChild(player);
            _players.Add(player);
        }
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    /// <summary>효과음 버스가 없으면 만들어서 Master로 보냅니다.</summary>
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

    /// <summary>
    /// 효과음을 재생합니다. pitchJitter만큼 음높이를 살짝 흔들어서 같은 소리가 반복돼도 덜 기계적으로 들리게 합니다.
    /// </summary>
    public static void Play(string name, float volumeDb = 0f, float pitch = 1f, float pitchJitter = 0.04f, int minGapMs = 25)
    {
        var self = _instance;
        if (self == null || !self.IsInsideTree())
        {
            return;
        }

        ulong now = Time.GetTicksMsec();
        if (self._lastPlayed.TryGetValue(name, out ulong last) && now - last < (ulong)minGapMs)
        {
            return;
        }

        self._lastPlayed[name] = now;
        var player = self._players[self._next];
        self._next = (self._next + 1) % self._players.Count;

        player.Stream = self.GetStream(name);
        player.VolumeDb = volumeDb;
        player.PitchScale = pitch * (1f + self._rng.RandfRange(-pitchJitter, pitchJitter));
        player.Play();
    }

    private AudioStream GetStream(string name)
    {
        if (_streams.TryGetValue(name, out var cached))
        {
            return cached;
        }

        AudioStream? stream = null;
        foreach (string ext in new[] { "wav", "ogg", "mp3" })
        {
            string path = $"res://audio/sfx/{name}.{ext}";
            if (ResourceLoader.Exists(path))
            {
                stream = GD.Load<AudioStream>(path);
                break;
            }
        }

        stream ??= new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SfxSynth.SampleRate,
            Stereo = false,
            Data = SfxSynth.ToPcm16(SfxSynth.Make(name)),
        };

        _streams[name] = stream;
        return stream;
    }
}
