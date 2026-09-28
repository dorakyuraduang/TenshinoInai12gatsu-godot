using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace Tenshi;

public partial class GameMain
{
    private sealed class PlayerPreferences
    {
        public int TextSpeed { get; set; } = 5;
        public int AutoSpeed { get; set; } = 5;
        public bool InstantText { get; set; }
        public bool SkipUnread { get; set; }
        public bool ReadSkip { get; set; }
        public bool Effects { get; set; } = true;
        public bool Particles { get; set; } = true;
        public bool VSync { get; set; } = true;
        public bool SmoothText { get; set; } = true;
        public bool SmoothFullText { get; set; } = true;
        public string PrimaryFont { get; set; } = "黑体";
        public string FullFont { get; set; } = "黑体";
        public bool Clock { get; set; }
        public bool InterruptVoice { get; set; } = true;
        public float MasterVolume { get; set; } = 1;
        public float MusicVolume { get; set; } = 1;
        public float VoiceVolume { get; set; } = 1;
        public float SoundVolume { get; set; } = 1;
    }

    private sealed record HistoryEntry(string Speaker, string Text, string Voice, int EventIndex, bool FullPage = false,
        int Background = -1, int[]? Characters = null, int CharacterCount = 0);
    // Use the bundled original UI font on Android and desktop alike. The
    // system names remain the fallback if the resource is unavailable.
    private readonly Font _uiFont = (Font?)GD.Load<FontFile>("res://fonts/simhei.ttf") ??
        new SystemFont { FontNames = ["SimHei", "黑体", "Noto Sans SC", "sans"] };
    private PlayerPreferences _preferences = new();
    private HashSet<string> _readMessages = new();
    private readonly List<HistoryEntry> _history = new();
    private SceneEvent? _activeMessage;
    private int _activeMessageIndex = -1;
    private int _textMode = 1;
    private bool _messageWaiting, _clearAfterMessage, _newMessage = true, _messageWasRead;
    private bool _autoRunning, _skipRunning, _userHidden, _storyEnded, _readDirty;
    private double _messageTimer, _skipTimer, _readSaveTimer, _cursorTime;
    private double _scriptPause;
    private TextureRect _waitCursor = null!;
    private TextureRect _fullPagePanel = null!;
    // !SysDisp: 0x6BA2A1AF selects the alpha-filled rectangle operation; it
    // is not a color. The next operand is 0x3F002246 (A=63/128, RGB=0,34,70).
    // Native dispatch 0x100039D0 -> 0x1000EDD0 blends a 676x600 rectangle
    // at (56,0), after common.px frame 106 supplies its translucent edges.
    private ColorRect _fullPageTone = null!;
    private static readonly Color FullPageToneColor = new(0, 34 / 255.0f, 70 / 255.0f, 63 / 128.0f);

    private static ColorRect CreateFullPageTone() => new()
    {
        Position = new Vector2(56, 0),
        Size = new Vector2(676, 600),
        Color = FullPageToneColor,
        MouseFilter = MouseFilterEnum.Ignore
    };
    private Texture2D[] _waitFrames = [];
    private float MusicVolumeDb => LinearDb(_preferences.MasterVolume * _preferences.MusicVolume);
    private static float LinearDb(float value) => value <= 0 ? -80 : Mathf.LinearToDb(value);

    private void LoadPreferences()
    {
        try
        {
            if (File.Exists(DataPath("preferences.json")))
                _preferences = JsonSerializer.Deserialize<PlayerPreferences>(File.ReadAllText(DataPath("preferences.json"))) ?? new();
            if (File.Exists(DataPath("read_messages.json")))
                _readMessages = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(DataPath("read_messages.json"))) ?? new();
        }
        catch (JsonException error) { GD.PushWarning($"Settings could not be read: {error.Message}"); }
        _preferences.TextSpeed = Math.Clamp(_preferences.TextSpeed, 0, 9);
        _preferences.AutoSpeed = Math.Clamp(_preferences.AutoSpeed, 0, 9);
        _dialogue.Smooth = _preferences.SmoothText;
        _dialogue.FullSmooth = _preferences.SmoothFullText;
        _dialogue.SetFonts(_preferences.PrimaryFont, _preferences.FullFont);
        ApplyVolumes();
        ApplyWeatherPreferences();
    }

    private void PersistPreferences()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath("preferences.json"))!);
        File.WriteAllText(DataPath("preferences.json"), JsonSerializer.Serialize(_preferences));
        _dialogue.Smooth = _preferences.SmoothText;
        _dialogue.FullSmooth = _preferences.SmoothFullText;
        _dialogue.SetFonts(_preferences.PrimaryFont, _preferences.FullFont);
        ApplyVolumes();
        ApplyWeatherPreferences();
    }

    private void ApplyVolumes()
    {
        _menuMusic.VolumeDb = MusicVolumeDb;
        if (_storyTrack > 0) _storyMusic.VolumeDb = MusicVolumeDb;
        _voicePlayer.VolumeDb = LinearDb(_preferences.MasterVolume * _preferences.VoiceVolume);
        foreach (AudioStreamPlayer player in _soundPlayers)
            player.VolumeDb = LinearDb(_preferences.MasterVolume * _preferences.SoundVolume);
    }

    private void SaveReadMessages()
    {
        if (!_readDirty) return;
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath("read_messages.json"))!);
        File.WriteAllText(DataPath("read_messages.json"), JsonSerializer.Serialize(_readMessages));
        _readDirty = false;
    }

    private void ResetMessageState()
    {
        StopMessageModes();
        _activeMessage = null;
        _activeMessageIndex = -1;
        _textMode = 1;
        _storyEnded = false;
        _scriptPause = 0;
        EndSceneTransition();
        _weather.Stop();
        SetSceneTone(0);
        _history.Clear();
        SetMessageLayout(false);
        SetMessageWindow(true);
    }

    private void ClearMessage()
    {
        _dialogue.Text = "";
        _newMessage = true;
        _clearAfterMessage = false;
        _messageWaiting = false;
        _waitCursor.Visible = false;
    }

    private void BeginMessage(SceneEvent item)
    {
        string text = FormatMessageText(item.Text);
        SetMessageWindow(true);
        _activeMessage = item;
        _activeMessageIndex = _eventIndex;
        _messageWasRead = _readMessages.Contains($"{item.Source}:{item.Value}");
        if (_preferences.ReadSkip && _messageWasRead) _skipRunning = true;
        if (!_preferences.SkipUnread && !_messageWasRead) _skipRunning = false;
        if (_newMessage) _dialogue.SetMessage(text.TrimStart('\u007f'));
        else _dialogue.AppendMessage(text);
        var entry = new HistoryEntry(_speaker.Text, _dialogue.Text, item.Voice, _eventIndex, _dialogue.FullPage,
            _currentBackground, (int[])_characterCodes.Clone(), _characterSlotCount);
        if (!_newMessage && _history.Count > 0) _history[^1] = entry;
        else _history.Add(entry);
        if (_history.Count > 100) _history.RemoveAt(0); // !SysMsg keeps a 100-entry ring.
        _newMessage = false;
        _messageWaiting = (item.Flags & 1) != 0;
        _clearAfterMessage = (item.Flags & 2) != 0;
        _messageTimer = 0;
        _waitCursor.Visible = false;
        if (_textMode == 0 || _preferences.InstantText || _skipRunning) _dialogue.RevealAll();
    }

    private string FormatMessageText(string text) => text.Replace("\ue000", _givenName).Replace("\ue001", _surname);

    // A click during printing completes the current page; a second click advances.
    private void Advance()
    {
        if (!_game.Visible || _systemLayer.Visible || _storyEnded) return;
        if (_choicesLayer.Visible) return;
        if (_scriptPause > 0) { _scriptPause = 0; EndSceneTransition(); AdvanceScene(); return; }
        if (_userHidden) { SetMessageWindow(true); return; }
        if (_autoRunning || _skipRunning) { StopMessageModes(); return; }
        if (!_dialogue.Complete) { _dialogue.RevealAll(); MarkMessageRead(); return; }
        ContinueMessage();
    }

    private void ContinueMessage()
    {
        if (_dialogue.NextPage()) { _messageTimer = 0; return; }
        MarkMessageRead();
        if (_clearAfterMessage) ClearMessage();
        AdvanceScene();
    }

    private void MarkMessageRead()
    {
        if (_activeMessage == null || !_dialogue.Complete || !_dialogue.LastPage) return;
        _readDirty |= _readMessages.Add($"{_activeMessage.Source}:{_activeMessage.Value}");
    }

    private void ProcessMessage(double delta)
    {
        _clock.Visible = _preferences.Clock && _game.Visible && !_userHidden;
        if (_clock.Visible) _clock.QueueRedraw();
        _readSaveTimer += delta;
        if (_readSaveTimer > 5) { _readSaveTimer = 0; SaveReadMessages(); SaveUnlocks(); }
        if (!_game.Visible || _systemLayer.Visible || _storyEnded) return;
        if (_choicesLayer.Visible) return;
        if (_scriptPause > 0)
        {
            _scriptPause -= delta;
            UpdateSceneTransition();
            if (_scriptPause <= 0 || _skipRunning || Input.IsKeyPressed(Key.Ctrl))
            { _scriptPause = 0; EndSceneTransition(); AdvanceScene(); }
            return;
        }
        if (_userHidden || _activeMessage == null) return;
        bool skipping = _skipRunning || Input.IsKeyPressed(Key.Ctrl);
        // Scripted instant lines remain instant even with the gradual preference.
        _dialogue.Tick(delta, _textMode == 3 ? 0 : _preferences.TextSpeed,
            _textMode == 0 || _preferences.InstantText || skipping);
        if (!_dialogue.Complete) { _messageTimer = 0; _waitCursor.Visible = false; return; }
        MarkMessageRead();
        _cursorTime += delta;
        _waitCursor.Visible = _messageWaiting && !_autoRunning && !skipping;
        if (_waitFrames.Length > 0)
            _waitCursor.Texture = _waitFrames[Math.Min((int)(_cursorTime * 60) % 152, _waitFrames.Length - 1)];
        Vector2 end = _dialogue.EndPosition;
        _waitCursor.Position = _dialogue.Position + new Vector2(Math.Min(end.X + 4, _dialogue.WrapWidth - 16), end.Y - 2);
        if (skipping)
        {
            _skipTimer += delta;
            if (_skipTimer >= 0.06) { _skipTimer = 0; ContinueMessage(); }
            return;
        }
        if (!_messageWaiting && _dialogue.LastPage) { ContinueMessage(); return; }
        if (!_autoRunning) return;
        if (_voicePlayer.Playing) { _messageTimer = 0; return; }
        _messageTimer += delta;
        int bytes = Encoding.GetEncoding("GB18030").GetByteCount(_activeMessage.Text);
        double frames = _activeMessage.Voice.Length > 0 ? 5 :
            5 + (10 - _preferences.AutoSpeed) * 6 + (10 - _preferences.AutoSpeed) * bytes * 5 / (_dialogue.FullPage ? 10 : 12);
        if (_messageTimer >= frames / 60.0) ContinueMessage();
    }

    private void StopMessageModes()
    {
        _autoRunning = _skipRunning = false;
        _messageTimer = _skipTimer = 0;
    }

    private void ToggleAuto()
    {
        _autoRunning = !_autoRunning;
        _skipRunning = false;
        _messageTimer = 0;
    }

    private void ToggleSkip()
    {
        _skipRunning = !_skipRunning && (_preferences.SkipUnread || _messageWasRead);
        _autoRunning = false;
    }

    private void SetMessageWindow(bool visible)
    {
        _userHidden = !visible;
        _dialogue.Visible = visible;
        _dialoguePanel.Visible = visible && !_dialogue.FullPage;
        _fullPageTone.Visible = visible && _dialogue.FullPage;
        _fullPagePanel.Visible = visible && _dialogue.FullPage;
        _speaker.Visible = visible && !_dialogue.FullPage;
        _speakerPanel.Visible = visible && !_dialogue.FullPage && _speaker.Text.Length > 0;
        foreach (TextureButton button in _gameButtons) button.Visible = visible;
        if (!visible) _waitCursor.Visible = false;
    }

    private void SetMessageLayout(bool fullPage)
    {
        ClearMessage();
        _speakerId = 255;
        _speaker.Text = "";
        _dialogue.ConfigureLayout(fullPage);
        _dialogue.Position = fullPage ? new Vector2(74, 20) : new Vector2(154, 504);
        foreach (TextureRect character in _characters)
            character.Visible = !fullPage;
        SetMessageWindow(fullPage);
    }

    private void ToggleMessageWindow() => SetMessageWindow(_userHidden);

    private void ReplayVoice(string? voice = null)
    {
        voice ??= _activeMessage?.Voice;
        if (string.IsNullOrEmpty(voice) || _voiceArchive == null) return;
        _voicePlayer.Stop();
        _voicePlayer.Stream = VoiceDecoder.Decode(_voiceArchive.Read(voice).Data);
        _voicePlayer.Play();
        _messageTimer = 0;
    }

    private bool HandleSystemInput(InputEvent input)
    {
        if (_systemLayer.Visible)
        {
            if (IsEscapeInput(input))
            {
                CloseSystemUi();
                return true;
            }
            if (input is InputEventKey { Pressed: true, Echo: false } key)
            {
                if (_historyShowing && key.Keycode is Key.Up or Key.Pageup) ChangeHistory(-1);
                else if (_historyShowing && key.Keycode is Key.Down or Key.Pagedown) ChangeHistory(1);
                return true;
            }
            return false;
        }
        if (!_game.Visible || input is not InputEventKey { Pressed: true, Echo: false } shortcut) return false;
        switch (shortcut.Keycode)
        {
            case Key.A: ToggleAuto(); return true;
            case Key.S: ToggleSkip(); return true;
            case Key.H: ToggleMessageWindow(); return true;
            case Key.V: ReplayVoice(); return true;
            case Key.Pageup: case Key.Up: OpenHistory(); return true;
            case Key.F5: OpenSaveSlots(false); return true;
            case Key.F9: OpenSaveSlots(true); return true;
            default: return false;
        }
    }
}
