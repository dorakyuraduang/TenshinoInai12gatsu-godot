using System;
using System.Collections.Generic;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private readonly List<AudioStreamPlayer> _soundPlayers = new();
    private readonly int[] _soundIds = new int[8];
    private readonly Tween?[] _soundTweens = new Tween?[8];
    private int _soundChannel;

    private void PlaySound(int number, bool loop)
    {
        if (_soundArchive == null || _skipRunning) return;
        if (_soundPlayers.Count == 0)
        {
            for (int i = 0; i < 8; i++)
            {
                var player = new AudioStreamPlayer();
                AddChild(player);
                _soundPlayers.Add(player);
            }
        }
        string name = $"se{number:000}.w";
        // !SysSound:4 returns when the original optional sound file is absent.
        if (!_soundArchive.Contains(name)) { GD.PushWarning($"Original optional sound {name} is absent"); return; }
        AudioStreamWav stream = AudioDecoder.Decode(_soundArchive.Read(name).Data);
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = stream.Data.Length / ((stream.Stereo ? 2 : 1) *
                (stream.Format == AudioStreamWav.FormatEnum.Format16Bits ? 2 : 1));
        }
        AudioStreamPlayer current = _soundPlayers[_soundChannel];
        _soundTweens[_soundChannel]?.Kill();
        current.Stop();
        current.Stream = stream;
        current.VolumeDb = LinearDb(_preferences.MasterVolume * _preferences.SoundVolume);
        current.Play();
        _soundIds[_soundChannel] = number;
        _soundChannel = (_soundChannel + 1) % 8;
    }

    private void StopSound(int number, int fadeMilliseconds = -1, int volume = 0)
    {
        for (int i = 0; i < _soundPlayers.Count; i++)
        {
            if (_soundIds[i] != number) continue;
            _soundTweens[i]?.Kill();
            AudioStreamPlayer player = _soundPlayers[i];
            if (fadeMilliseconds < 0 || (fadeMilliseconds == 0 && volume == 0)) { player.Stop(); continue; }
            float target = LinearDb(_preferences.MasterVolume * _preferences.SoundVolume * Math.Clamp(volume, 0, 256) / 256f);
            Tween tween = _soundTweens[i] = CreateTween();
            tween.TweenProperty(player, "volume_db", target, Math.Max(0, fadeMilliseconds) / 1000.0);
            if (volume == 0) tween.TweenCallback(Callable.From(player.Stop));
        }
    }
}

public partial class OriginalClock : Control
{
    public Texture2D[] Frames { get; set; } = [];
    public OriginalClock() { Position = new Vector2(751, 577); Size = new Vector2(49, 23); MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        if (Frames.Length < 65) return;
        DrawTexture(Frames[64], Vector2.Zero);
        DateTime time = DateTime.Now;
        DrawDigits(time.Hour, 11, 3);
        DrawDigits(time.Minute, 29, 3);
        DrawDigits(time.Month, 11, 12);
        DrawDigits(time.Day, 29, 12);
    }
    private void DrawDigits(int value, int x, int y)
    {
        DrawTexture(Frames[45 + value / 10], new Vector2(x, y));
        DrawTexture(Frames[45 + value % 10], new Vector2(x + 5, y));
    }
}
