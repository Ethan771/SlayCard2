using System.Collections.Generic;
using Godot;

namespace SlayCard;

// 全局音频管理器：BGM + SFX 对象池（纯代码构建）。
public partial class AudioManager : Node
{
    public static AudioManager? Instance { get; private set; }

    private readonly List<AudioStreamPlayer> _sfxPool = new();
    private readonly Dictionary<string, AudioStream?> _streamCache = new();
    private readonly HashSet<string> _missingLogged = new();
    private AudioStreamPlayer _bgmPlayer = null!;
    private string _currentBgmPath = string.Empty;

    public override void _Ready()
    {
        Instance = this;
        BuildPlayers();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void PlayBGM(string path)
    {
        if (!TryGetStream(path, out AudioStream? stream))
        {
            return;
        }

        if (_currentBgmPath == path && _bgmPlayer.Playing)
        {
            return;
        }

        _currentBgmPath = path;
        _bgmPlayer.Stream = stream;
        _bgmPlayer.Play();
    }

    public void PlaySFX(string path)
    {
        if (!TryGetStream(path, out AudioStream? stream))
        {
            return;
        }

        foreach (AudioStreamPlayer player in _sfxPool)
        {
            if (player.Playing)
            {
                continue;
            }

            player.Stream = stream;
            player.Play();
            return;
        }

        // 池已满时，抢占第一个，避免关键反馈丢失。
        if (_sfxPool.Count > 0)
        {
            _sfxPool[0].Stream = stream;
            _sfxPool[0].Play();
        }
    }

    private void BuildPlayers()
    {
        _bgmPlayer = new AudioStreamPlayer
        {
            Name = "BgmPlayer",
            Bus = "Master"
        };
        AddChild(_bgmPlayer);

        const int poolSize = 6;
        for (int i = 0; i < poolSize; i++)
        {
            var player = new AudioStreamPlayer
            {
                Name = $"SfxPlayer_{i}",
                Bus = "Master"
            };
            AddChild(player);
            _sfxPool.Add(player);
        }
    }

    private bool TryGetStream(string path, out AudioStream? stream)
    {
        stream = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (_streamCache.TryGetValue(path, out AudioStream? cached))
        {
            stream = cached;
            return stream is not null;
        }

        stream = GD.Load<AudioStream>(path);
        _streamCache[path] = stream;
        if (stream is null)
        {
            if (_missingLogged.Add(path))
            {
                GD.PrintErr($"Audio load failed: {path}");
            }
            return false;
        }

        return true;
    }
}
