using System;
using Godot;

namespace Tenshi;

/// <summary>Game flow adapter for the reusable native OriginalVideoPlayer extension.</summary>
public partial class OpeningMoviePlayer : Control
{
    private TextureRect? _player;
    private Action? _finished;
    private Callable _onFinished;
    private Callable _onError;
    private int _generation;
    private double _startupSeconds;

    public bool HasFrame => _player?.Texture != null && VideoTime >= 0;
    public bool HasAudio => _player != null && _player.Call("has_audio").AsBool()
        && _player.Call("get_playback_state").AsInt32() == 2;
    public double VideoTime => _player?.Call("get_video_position").AsDouble() ?? -1;
    public double AudioTime => _player?.Call("get_stream_position").AsDouble() ?? 0;
    public event Action<string>? Failed;

    public OpeningMoviePlayer()
    {
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var ground = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
        ground.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(ground);
        GuiInput += input =>
        {
            if (input is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
                Skip();
        };
    }

    public void Play(string path, Action finished)
    {
        Stop();
        int generation = _generation;
        _finished = finished;
        _startupSeconds = 0;
        Visible = true;
        try
        {
            if (_player == null)
            {
                if (!ClassDB.ClassExists("OriginalVideoPlayer"))
                    throw new InvalidOperationException("原生视频插件未加载，请检查 Original Video 对应平台的动态库。");
                _player = ClassDB.Instantiate("OriginalVideoPlayer").AsGodotObject() as TextureRect
                    ?? throw new InvalidOperationException("无法创建原生视频播放器。");
                _player.Name = "OriginalVideo";
                _player.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                _player.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                _player.MouseFilter = MouseFilterEnum.Ignore;
                _player.SetAnchorsPreset(LayoutPreset.FullRect);
                AddChild(_player);
            }
            // Defer until native processing returns; old callbacks cannot complete a newer OP.
            _onFinished = Callable.From(() =>
            {
                if (generation == _generation) Complete();
            });
            _onError = Callable.From<string>(reason =>
            {
                if (generation == _generation) Fail(reason);
            });
            _player.Connect("playback_finished", _onFinished, (uint)ConnectFlags.Deferred);
            _player.Connect("playback_error", _onError, (uint)ConnectFlags.Deferred);
            _player.Call("set_source", path);
            _player.Call("play_from_position", 0.0);
            GD.Print("OP: native MPEG/ASF decoder started");
        }
        catch (Exception error) { Fail(error.Message); }
    }

    public override void _Process(double delta)
    {
        if (!Visible || HasFrame || (_player?.Call("is_paused").AsBool() ?? false)) return;
        if ((_startupSeconds += delta) > 15)
            Fail("OP startup timed out before the first video frame");
    }

    public void Skip() => Complete();
    public override void _ExitTree() => Stop();

    public void Stop()
    {
        _generation++;
        Visible = false;
        _finished = null;
        if (_player == null || !IsInstanceValid(_player)) return;
        if (_player.IsConnected("playback_finished", _onFinished))
            _player.Disconnect("playback_finished", _onFinished);
        if (_player.IsConnected("playback_error", _onError))
            _player.Disconnect("playback_error", _onError);
        _player.Call("stop");
    }

    private void Complete()
    {
        if (!Visible) return;
        GD.Print("OP: completed");
        Action? finished = _finished;
        Stop();
        finished?.Invoke();
    }

    private void Fail(string reason)
    {
        GD.PushError($"OP: {reason}");
        Stop();
        Failed?.Invoke(reason);
    }
}
